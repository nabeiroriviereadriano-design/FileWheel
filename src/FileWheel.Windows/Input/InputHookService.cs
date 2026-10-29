using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FileWheel.Core.Input;
using FileWheel.Core.Logging;
using static FileWheel.Windows.Native.NativeMethods;

namespace FileWheel.Windows.Input;

/// <summary>
/// Crochets clavier/souris de bas niveau sur un thread dedie (avec sa propre boucle de messages).
///
/// Ce que FileWheel fait ET ne fait pas :
/// - il OBSERVE souris et clavier pour reconnaitre le geste « glisser + touche » ; il ne bloque
///   jamais un clic ni un deplacement : le glisser-deposer natif de Windows continue normalement ;
/// - il n'essaie PAS de deviner les fichiers glisses a partir de la souris : ce sont les donnees
///   OLE du glisser (CF_HDROP), recues quand le curseur passe sur la roue, qui font foi ;
/// - il ne masque qu'un seul evenement : le relachement d'Alt (ou de Win) apres l'affichage de la
///   roue, remplace par un relachement precede d'une touche neutre, pour que l'Explorateur
///   n'active pas sa barre de menus (ou Windows le menu Demarrer). C'est la technique standard
///   (« touche de masquage »).
/// Les callbacks font le minimum (aucune E/S, aucun appel bloquant) : Windows retire
/// silencieusement un crochet trop lent.
/// </summary>
public sealed class InputHookService : IDisposable
{
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook, _keyboardHook;
    private HookProc? _mouseProc, _keyboardProc; // references conservees (pas de ramasse-miettes)
    private readonly ConcurrentQueue<Action> _inbox = new();
    private readonly DragGestureDetector _detector = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _timerOn;
    private long _lastRehook;
    private const uint WM_FW_WAKE = WM_APP + 1;
    // Avec une fenetre nulle, Windows attribue lui-meme l'identifiant du minuteur : on le conserve.
    private IntPtr _tickTimer, _rehookTimer;

    /// <summary>Sortie du detecteur (sur le thread des crochets : a remarshaler vers l'interface).</summary>
    public event Action<GestureOutput>? Gesture;

    /// <summary>Echap pressee alors que la roue est visible (mode clic) ; l'evenement est consomme.</summary>
    public event Action? EscapeWhileVisible;

    /// <summary>Clic (gauche ou droit) n'importe ou alors que la roue est visible : position en pixels physiques. Jamais consomme.</summary>
    public event Action<int, int>? MouseDownWhileVisible;

    /// <summary>Positionne par l'interface : la roue est-elle a l'ecran ?</summary>
    public volatile bool WheelVisible;

    public ItemProbe? Probe { get; set; }

    /// <summary>
    /// Mode diagnostic : journalise (niveau Info) chaque appui dans l'Explorateur ou sur le Bureau, la
    /// classification de la fenetre, la verification d'element et la decision. Aucun nom de fichier.
    /// </summary>
    public volatile bool Diagnostic;

    // Diagnostic automatique : si une touche est pressee pendant qu'on tient le bouton gauche (geste voulu)
    // et que la roue ne s'ouvre pas, on journalise pourquoi, meme sans le mode diagnostic.
    private bool _buttonDown, _keyDuringPress, _shownDuringPress;
    private string _pressInfo = "";
    private readonly List<string> _pressKeys = new();
    private int _autoDiagCount;

    /// <summary>Marque posee sur les touches injectees par FileWheel (masquage d'Alt) pour les reconnaitre.</summary>
    private static readonly IntPtr OwnInjectionMarker = new(0x46574C21); // « FWL! »

    public void Start(GestureOptions options)
    {
        if (_thread is not null) return;
        _detector.Options = options;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "FileWheel.Hooks", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void UpdateOptions(GestureOptions options) => Post(() => _detector.Options = options);

    public void SetEnabled(bool enabled) => Post(() =>
    {
        _detector.Enabled = enabled;
        if (!enabled) Emit(_detector.Cancel(HideReason.Disabled));
    });

    /// <summary>L'interface signale que la roue a ete fermee (depot, clic, Echap).</summary>
    public void NotifyWheelClosed() => Post(() => _detector.Cancel());

    private void Post(Action a)
    {
        _inbox.Enqueue(a);
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_FW_WAKE, IntPtr.Zero, IntPtr.Zero);
    }

    private void Loop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        InstallHooks();
        _rehookTimer = SetTimer(IntPtr.Zero, IntPtr.Zero, 60_000, IntPtr.Zero);
        ready.Set();
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_FW_WAKE || msg.message == WM_TIMER)
            {
                while (_inbox.TryDequeue(out var a))
                {
                    try { a(); } catch (Exception ex) { Log.Error("Crochets : action", ex); }
                }
                if (msg.message == WM_TIMER && msg.wParam == _tickTimer && _timerOn) OnTick();
                if (msg.message == WM_TIMER && msg.wParam == _rehookTimer) MaybeRehook();
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        RemoveHooks();
    }

    private void InstallHooks()
    {
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        var mod = GetModuleHandleW(null);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, mod, 0);
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, mod, 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            Log.Error($"Installation des crochets impossible (erreur {Marshal.GetLastWin32Error()}).");
        else Log.Info("Crochets clavier/souris installés.");
        _lastRehook = _clock.ElapsedMilliseconds;
    }

    private void RemoveHooks()
    {
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero;
    }

    /// <summary>
    /// Windows peut retirer un crochet sans prevenir (machine tres chargee). Toutes les 10 minutes,
    /// au repos, on le reinstalle par precaution.
    /// </summary>
    private void MaybeRehook()
    {
        if (_detector.IsPressed || WheelVisible) return;
        if (_clock.ElapsedMilliseconds - _lastRehook < 600_000) return;
        RemoveHooks();
        InstallHooks();
    }

    private void Emit(GestureOutput o)
    {
        if (o.Action == GestureAction.None) return;
        if (o.Action == GestureAction.ShowWheel) _shownDuringPress = true;
        if (Diagnostic || o.Action == GestureAction.ShowWheel) Log.Info($"Geste : {o.Action} en {o.X},{o.Y} ({o.Reason})");
        try { Gesture?.Invoke(o); } catch (Exception ex) { Log.Error("Crochets : notification", ex); }
        if (o.Action == GestureAction.HideWheel) StopTimer();
    }

    private void StartTimer()
    {
        if (_timerOn) return;
        _tickTimer = SetTimer(IntPtr.Zero, IntPtr.Zero, 25, IntPtr.Zero);
        _timerOn = _tickTimer != IntPtr.Zero;
    }

    private void StopTimer()
    {
        if (!_timerOn) return;
        KillTimer(IntPtr.Zero, _tickTimer);
        _timerOn = false;
        _tickTimer = IntPtr.Zero;
    }

    private void OnTick()
    {
        if (!_detector.IsPressed)
        {
            StopTimer();
            return;
        }
        // Indice fort de glisser OLE en cours : la fenetre d'image de glisser du Shell est visible.
        if (!_detector.IsWheelShown)
        {
            var img = FindWindowW("SysDragImage", null);
            // IMPORTANT : le resultat doit etre emis. Cette verification peut a elle seule declencher
            // l'affichage (c'est meme le cas habituel pendant un vrai glisser) ; l'ignorer « avalait » la roue.
            if (img != IntPtr.Zero && IsWindowVisible(img))
            {
                Emit(_detector.OnItemCheck(true, _clock.ElapsedMilliseconds));
                if (!_detector.IsPressed || _detector.IsWheelShown) return;
            }
        }
        Emit(_detector.OnTick(_clock.ElapsedMilliseconds));
    }

    // ------------------------------------------------------------------ souris

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                // Les evenements souris injectes (bureau a distance, tablettes, outils d'accessibilite)
                // sont acceptes : FileWheel n'injecte jamais de souris lui-meme.
                {
                    long now = _clock.ElapsedMilliseconds;
                    switch ((int)wParam)
                    {
                        case WM_RBUTTONDOWN:
                            if (WheelVisible) RaiseMouseDown(data.pt);
                            break;
                        case WM_LBUTTONDOWN:
                        {
                            if (WheelVisible) RaiseMouseDown(data.pt);
                            _detector.SyncKeys(k => (GetAsyncKeyState(k) & 0x8000) != 0);
                            var source = Classify(data.pt, out var chain);
                            Emit(_detector.OnMouseDown(data.pt.X, data.pt.Y, source, now));
                            _buttonDown = true;
                            _keyDuringPress = _shownDuringPress = false;
                            _pressKeys.Clear();
                            _pressInfo = $"{source} (fenêtres : {chain})";
                            if (Diagnostic) Log.Info($"[diag] appui en {data.pt.X},{data.pt.Y} : {source} (fenêtres : {chain})" + (source == DragSource.Other ? " → ignoré" : ""));
                            if (_detector.IsPressed)
                            {
                                StartTimer();
                                var probe = Probe;
                                if (probe is not null && _detector.Options.ItemCheck != ItemCheckPolicy.None)
                                {
                                    var pt = data.pt;
                                    probe.CheckAsync(pt.X, pt.Y, result => Post(() =>
                                    {
                                        if (Diagnostic) Log.Info($"[diag] vérification UIA : {(result ? "fichier/dossier" : "zone vide")}");
                                        Emit(_detector.OnItemCheck(result, _clock.ElapsedMilliseconds));
                                    }));
                                }
                            }
                            break;
                        }
                        case WM_MOUSEMOVE:
                            if (_detector.IsPressed) Emit(_detector.OnMouseMove(data.pt.X, data.pt.Y, now));
                            break;
                        case WM_LBUTTONUP:
                        {
                            bool autoDiag = _buttonDown && _keyDuringPress && !_shownDuringPress && _autoDiagCount < 50;
                            if ((Diagnostic || autoDiag) && !_detector.IsWheelShown)
                            {
                                if (autoDiag) _autoDiagCount++;
                                Log.Info($"[diag] glisser avec touche sans roue — appui sur {_pressInfo} ; " + _detector.Describe(now)
                                    + $" ; touches reçues : {string.Join(" ", _pressKeys)}");
                            }
                            _buttonDown = false;
                            Emit(_detector.OnMouseUp(now));
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Crochet souris", ex); }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void RaiseMouseDown(POINT pt)
    {
        try { MouseDownWhileVisible?.Invoke(pt.X, pt.Y); } catch (Exception ex) { Log.Error("Crochets : clic", ex); }
    }

    /// <summary>
    /// Type de fenetre sous le curseur : Explorateur, Bureau ou autre. On remonte la chaine des fenetres
    /// parentes (noms de classe uniquement, rapide) : la vue des fichiers du Shell (SHELLDLL_DefView) ou le
    /// volet de navigation, dans une fenetre de l'Explorateur (CabinetWClass) ou le Bureau (Progman/WorkerW).
    /// </summary>
    private static DragSource Classify(POINT pt, out string chain)
    {
        chain = "";
        var child = WindowFromPoint(pt);
        if (child == IntPtr.Zero) return DragSource.Other;
        var names = new List<string>(8);
        bool defView = false, tree = false;
        var h = child;
        for (int depth = 0; h != IntPtr.Zero && depth < 16; depth++)
        {
            var cls = ClassName(h);
            names.Add(cls);
            if (cls == "SHELLDLL_DefView") defView = true;
            if (cls is "SysTreeView32" or "NamespaceTreeControl") tree = true;
            h = GetAncestor(h, GA_PARENT);
        }
        var rootClass = ClassName(GetAncestor(child, GA_ROOT));
        chain = string.Join(" < ", names);
        if (rootClass is "Progman" or "WorkerW" && defView) return DragSource.Desktop;
        if (rootClass is "CabinetWClass" or "ExploreWClass" && (defView || tree)) return DragSource.Explorer;
        return DragSource.Other;
    }

    // ------------------------------------------------------------------ clavier

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                // Seules NOS injections (masquage d'Alt) sont ignorees ; les touches injectees par d'autres
                // outils (remappage, bureau a distance, accessibilite) comptent comme des touches reelles.
                if (data.dwExtraInfo != OwnInjectionMarker)
                {
                    int vk = (int)data.vkCode;
                    long now = _clock.ElapsedMilliseconds;
                    int msg = (int)wParam;
                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (vk == VK.Escape && WheelVisible && !_detector.IsWheelShown)
                        {
                            // Roue en mode clic (apres depot) : Echap la ferme, sans atteindre l'Explorateur.
                            try { EscapeWhileVisible?.Invoke(); } catch { }
                            return new IntPtr(1);
                        }
                        if (_buttonDown)
                        {
                            _keyDuringPress = true;
                            if (_pressKeys.Count < 30)
                                _pressKeys.Add($"0x{vk:X2}{((data.flags & LLKHF_INJECTED) != 0 ? "(injectée)" : "")}");
                        }
                        Emit(_detector.OnKeyDown(vk, now));
                    }
                    else if (msg is WM_KEYUP or WM_SYSKEYUP)
                    {
                        var o = _detector.OnKeyUp(vk, now, out bool suppressMenu);
                        Emit(o);
                        if (suppressMenu)
                        {
                            SendMaskedRelease(vk);
                            return new IntPtr(1); // le relachement d'origine est remplace par la sequence injectee
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Crochet clavier", ex); }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>Touche neutre (0xE8) enfoncee/relachee puis relachement de la touche : pas de menu Alt / Demarrer.</summary>
    private static void SendMaskedRelease(int vk)
    {
        var inputs = new INPUT[3];
        inputs[0] = Key((ushort)VK.MaskKey, false);
        inputs[1] = Key((ushort)VK.MaskKey, true);
        inputs[2] = Key((ushort)vk, true);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static INPUT Key(ushort vk, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = OwnInjectionMarker } },
        };
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
        _thread = null;
    }
}
