namespace FileWheel.Core.Input;

/// <summary>Type de fenetre ou le bouton gauche a ete enfonce.</summary>
public enum DragSource { Other, Explorer, Desktop }

public enum ItemCheckPolicy
{
    /// <summary>Pas de verification de l'element sous le curseur.</summary>
    None,
    /// <summary>Si la verification echoue ou tarde, on affiche quand meme (le depot confirmera).</summary>
    Lenient,
    /// <summary>La roue n'apparait que si l'element sous le curseur est un fichier/dossier confirme.</summary>
    Strict,
}

public sealed record GestureOptions
{
    public ActivationChord Chord { get; init; } = ActivationChord.Default;
    /// <summary>Distance minimale (pixels physiques) depuis le point d'appui.</summary>
    public int MinDistancePx { get; init; } = 12;
    /// <summary>Duree pendant laquelle les conditions doivent rester vraies avant l'affichage.</summary>
    public int DelayMs { get; init; } = 90;
    public bool OnDesktop { get; init; } = true;
    public bool InExplorer { get; init; } = true;
    /// <summary>Fermer la roue si la combinaison est relachee alors que la roue est affichee.</summary>
    public bool CloseOnChordRelease { get; init; }
    public ItemCheckPolicy ItemCheck { get; init; } = ItemCheckPolicy.Lenient;
    /// <summary>Delai au-dela duquel une verification d'element sans reponse est consideree inconnue.</summary>
    public int ItemCheckTimeoutMs { get; init; } = 350;
}

public enum GestureAction { None, ShowWheel, HideWheel }

public enum HideReason { None, MouseReleased, Escape, ChordReleased, Cancelled, Disabled }

public readonly record struct GestureOutput(GestureAction Action, int X = 0, int Y = 0, HideReason Reason = HideReason.None)
{
    public static readonly GestureOutput Nothing = new(GestureAction.None);
}

/// <summary>
/// Machine a etats du declenchement « glisser + touche ».
///
/// Elle ne connait ni Windows ni les fichiers : elle recoit des evenements bruts
/// (souris, clavier, horloge) et decide quand afficher ou cacher la roue. Toute la
/// logique de declenchement est donc testable sans ecran (tests/WheelTriggerTests).
///
/// Regles :
/// 1. le bouton gauche doit etre enfonce dans l'Explorateur ou sur le Bureau (selon les options) ;
/// 2. le curseur doit s'etre eloigne d'au moins MinDistancePx du point d'appui ;
/// 3. la combinaison doit etre tenue exactement (aucune autre touche parasite) ;
/// 4. ces conditions doivent rester vraies pendant DelayMs ;
/// 5. si la verification de l'element sous le curseur repond « non » (zone vide,
///    selection rectangulaire), la roue n'apparait pas pour cet appui.
///
/// Un appui isole sur la touche, un clic, ou un simple deplacement ne declenchent jamais rien.
/// </summary>
public sealed class DragGestureDetector
{
    private enum State { Idle, Pressed, Shown }

    private readonly HashSet<int> _down = new();
    private readonly HashSet<int> _maskOnRelease = new();
    private State _state = State.Idle;
    private int _startX, _startY, _lastX, _lastY;
    private long _pressTime;
    private long _conditionsSince = -1;
    private bool _spoiled;          // une touche parasite a ete pressee pendant cet appui
    private int _spoiledBy;         // code de la touche parasite (diagnostic)
    private bool? _itemCheck;        // resultat de la verification d'element (null = en attente)
    private bool _movedEnough;

    public GestureOptions Options { get; set; } = new();
    public bool Enabled { get; set; } = true;

    public bool IsWheelShown => _state == State.Shown;
    public bool IsPressed => _state != State.Idle;

    /// <summary>Touches actuellement enfoncees (normalisees gauche/droite incluses).</summary>
    public IReadOnlyCollection<int> KeysDown => _down;

    // ---------------------------------------------------------------- souris

    public GestureOutput OnMouseDown(int x, int y, DragSource source, long nowMs)
    {
        if (_state == State.Shown)
        {
            // Nouveau clic alors que la roue est encore visible (mode clic) : l'interface gere.
            return GestureOutput.Nothing;
        }
        bool allowed = Enabled && source switch
        {
            DragSource.Explorer => Options.InExplorer,
            DragSource.Desktop => Options.OnDesktop,
            _ => false,
        };
        if (!allowed)
        {
            _state = State.Idle;
            return GestureOutput.Nothing;
        }
        _state = State.Pressed;
        _startX = _lastX = x;
        _startY = _lastY = y;
        _pressTime = nowMs;
        _conditionsSince = -1;
        _spoiled = false;
        _spoiledBy = 0;
        _itemCheck = Options.ItemCheck == ItemCheckPolicy.None ? true : null;
        _movedEnough = false;
        return GestureOutput.Nothing;
    }

    public GestureOutput OnMouseMove(int x, int y, long nowMs)
    {
        _lastX = x;
        _lastY = y;
        if (_state != State.Pressed) return GestureOutput.Nothing;
        if (!_movedEnough)
        {
            long dx = x - _startX, dy = y - _startY;
            if (dx * dx + dy * dy >= (long)Options.MinDistancePx * Options.MinDistancePx) _movedEnough = true;
        }
        return Evaluate(nowMs);
    }

    public GestureOutput OnMouseUp(long nowMs)
    {
        var wasShown = _state == State.Shown;
        _state = State.Idle;
        _conditionsSince = -1;
        return wasShown ? new GestureOutput(GestureAction.HideWheel, Reason: HideReason.MouseReleased) : GestureOutput.Nothing;
    }

    /// <summary>Resultat asynchrone de la verification « l'element sous le curseur est-il un fichier ? ».</summary>
    public GestureOutput OnItemCheck(bool isFileItem, long nowMs)
    {
        if (_state != State.Pressed) return GestureOutput.Nothing;
        _itemCheck = isFileItem;
        return Evaluate(nowMs);
    }

    /// <summary>Appele periodiquement (minuterie) pour honorer le delai d'apparition.</summary>
    public GestureOutput OnTick(long nowMs) => _state == State.Pressed ? Evaluate(nowMs) : GestureOutput.Nothing;

    // ---------------------------------------------------------------- clavier

    public GestureOutput OnKeyDown(int vk, long nowMs)
    {
        if (vk == VK.Escape)
        {
            var shown = _state == State.Shown;
            _state = _state == State.Idle ? State.Idle : State.Pressed;
            _spoiled = true;
            _spoiledBy = vk;
            _down.Add(vk);
            return shown ? new GestureOutput(GestureAction.HideWheel, Reason: HideReason.Escape) : GestureOutput.Nothing;
        }
        // Codes synthetiques (emis par Windows, un pilote ou un autre logiciel) : ignores completement.
        if (!VK.IsModifier(vk) && !VK.IsShortcutKey(vk) && vk != Options.Chord.Key)
            return _state == State.Pressed ? Evaluate(nowMs) : GestureOutput.Nothing;
        bool isNew = _down.Add(vk);
        if (isNew && !VK.IsModifier(vk) && vk != Options.Chord.Key && _state != State.Idle)
        {
            // Alt+Tab, Win+D, etc. : on laisse le raccourci Windows agir et la roue s'efface.
            _spoiled = true;
            _spoiledBy = vk;
            if (_state == State.Shown)
            {
                _state = State.Pressed;
                return new GestureOutput(GestureAction.HideWheel, Reason: HideReason.Cancelled);
            }
        }
        return _state == State.Pressed ? Evaluate(nowMs) : GestureOutput.Nothing;
    }

    /// <summary>
    /// Relachement d'une touche. <paramref name="suppressMenu"/> indique a l'appelant qu'il doit
    /// masquer ce relachement (Alt ouvrirait la barre de menus de l'Explorateur, Win le menu Demarrer)
    /// parce que la roue a ete affichee pendant que la touche etait tenue.
    /// </summary>
    public GestureOutput OnKeyUp(int vk, long nowMs, out bool suppressMenu)
    {
        _down.Remove(vk);
        // Le relachement de la touche gauche/droite efface aussi l'etat generique.
        if (VK.IsAlt(vk)) { _down.Remove(VK.Menu); }
        if (VK.IsCtrl(vk)) { _down.Remove(VK.Control); }
        if (VK.IsShift(vk)) { _down.Remove(VK.Shift); }
        if (vk == VK.Escape) _down.Remove(VK.Escape);

        suppressMenu = _maskOnRelease.Remove(vk);
        if (VK.IsAlt(vk)) suppressMenu |= _maskOnRelease.Remove(VK.Menu);

        if (_state == State.Shown && Options.CloseOnChordRelease && !ChordHeld())
        {
            _state = State.Pressed;
            _spoiled = true;
            return new GestureOutput(GestureAction.HideWheel, Reason: HideReason.ChordReleased);
        }
        // Remarque : apres une touche parasite, l'appui reste « gache » meme quand elle est
        // relachee, pour eviter un affichage inattendu juste apres un raccourci Windows.
        return _state == State.Pressed ? Evaluate(nowMs) : GestureOutput.Nothing;
    }

    /// <summary>Annulation externe (perte de focus, desactivation, verrouillage de session...).</summary>
    public GestureOutput Cancel(HideReason reason = HideReason.Cancelled)
    {
        var shown = _state == State.Shown;
        _state = State.Idle;
        _conditionsSince = -1;
        return shown ? new GestureOutput(GestureAction.HideWheel, Reason: reason) : GestureOutput.Nothing;
    }

    /// <summary>A appeler si le hook a pu rater des relachements (verrouillage, UAC) : on repart de zero.</summary>
    public void ResetKeys()
    {
        _down.Clear();
        _maskOnRelease.Clear();
    }

    /// <summary>
    /// Resynchronise l'etat des touches avec l'etat physique reel (GetAsyncKeyState) : un relachement
    /// manque (ecran UAC, verrouillage de session, Ctrl+Alt+Suppr) ne laisse jamais une touche « collee ».
    /// </summary>
    public void SyncKeys(Func<int, bool> isPhysicallyDown)
    {
        foreach (var k in _down.ToList())
            if (!isPhysicallyDown(k)) { _down.Remove(k); _maskOnRelease.Remove(k); }
    }

    /// <summary>Etat lisible pour le journal de diagnostic (aucun nom de fichier).</summary>
    public string Describe(long nowMs) =>
        $"état={_state}, déplacé={_movedEnough} ({Math.Abs(_lastX - _startX)},{Math.Abs(_lastY - _startY)} px), " +
        $"combinaison={(ChordHeld() ? "OK" : "non")} [touches : {(_down.Count == 0 ? "aucune" : string.Join(" ", _down.Select(k => "0x" + k.ToString("X2"))))}], " +
        $"vérif. élément={(_itemCheck is null ? "sans réponse" : _itemCheck.Value ? "fichier" : "zone vide")}, " +
        $"touche parasite={(_spoiled ? "0x" + _spoiledBy.ToString("X2") : "non")}, durée={nowMs - _pressTime} ms";

    // ---------------------------------------------------------------- logique

    public bool ChordHeld()
    {
        var c = Options.Chord;
        if (!c.IsValid) return false;
        bool ctrl = _down.Any(VK.IsCtrl), alt = _down.Any(VK.IsAlt), shift = _down.Any(VK.IsShift), win = _down.Any(VK.IsWin);
        if (ctrl != c.Ctrl || alt != c.Alt || shift != c.Shift || win != c.Win) return false;
        if (c.Key != 0 && !_down.Contains(c.Key)) return false;
        // Aucune autre touche non modificatrice enfoncee.
        foreach (var k in _down)
            if (!VK.IsModifier(k) && k != c.Key) return false;
        return true;
    }

    private GestureOutput Evaluate(long nowMs)
    {
        if (!Enabled || _state != State.Pressed) return GestureOutput.Nothing;

        bool itemOk = _itemCheck switch
        {
            true => true,
            false => false,
            null => Options.ItemCheck == ItemCheckPolicy.Lenient && nowMs - _pressTime >= Options.ItemCheckTimeoutMs,
        };

        // Le delai court des que geste et combinaison sont reunis ; la verification d'element
        // se fait en parallele et n'ajoute pas de latence.
        bool baseOk = _movedEnough && !_spoiled && ChordHeld();
        if (!baseOk)
        {
            _conditionsSince = -1;
            return GestureOutput.Nothing;
        }
        if (_conditionsSince < 0) _conditionsSince = nowMs;
        if (!itemOk || nowMs - _conditionsSince < Options.DelayMs) return GestureOutput.Nothing;

        _state = State.Shown;
        // Tant que la touche reste tenue, son relachement devra etre masque.
        foreach (var k in _down)
        {
            if (VK.IsAlt(k) || VK.IsWin(k)) _maskOnRelease.Add(k);
        }
        return new GestureOutput(GestureAction.ShowWheel, _lastX, _lastY);
    }
}
