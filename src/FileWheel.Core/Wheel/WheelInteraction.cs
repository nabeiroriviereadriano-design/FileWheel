namespace FileWheel.Core.Wheel;

/// <summary>Mode de la roue : pendant le glisser (on « depose » sur un secteur) ou apres (on clique).</summary>
public enum WheelMode { Drag, Click }

public enum WheelOutcome
{
    /// <summary>Rien a faire (la roue reste telle quelle).</summary>
    None,
    /// <summary>Executer <see cref="WheelInteraction.Selected"/>.</summary>
    Execute,
    /// <summary>Fermer la roue sans rien faire.</summary>
    Cancel,
    /// <summary>La roue reste ouverte en mode clic (sous-menu ouvert, action indisponible, retour).</summary>
    StayOpen,
}

/// <summary>
/// Navigation dans la roue, independante de l'affichage.
///
/// - Survol d'une branche pendant <c>dwellMs</c> : ouverture du sous-menu (pas besoin de cliquer
///   pendant un glisser).
/// - Depot / clic sur une feuille : execution.
/// - Depot / clic sur une branche : ouverture du sous-menu et passage en mode clic
///   (les fichiers sont deja connus, le glisser est termine).
/// - Disque central : annuler (racine) ou revenir au menu parent (sous-menu, par survol prolonge ou clic).
/// - Element desactive : jamais execute ; la raison est affichee.
/// - Echap : annulation complete.
/// </summary>
public sealed class WheelInteraction
{
    private readonly Stack<WheelItem> _path = new();
    private long _hoverSince;
    private readonly int _dwellMs;

    public WheelInteraction(WheelItem root, int dwellMs = 380)
    {
        Root = root;
        Current = root;
        _dwellMs = Math.Max(100, dwellMs);
    }

    public WheelItem Root { get; }
    public WheelItem Current { get; private set; }
    public WheelMode Mode { get; set; } = WheelMode.Drag;
    public int HoverIndex { get; private set; } = WheelLayout.Center;
    public WheelItem? Selected { get; private set; }
    /// <summary>Message a afficher au centre (raison d'indisponibilite, etc.).</summary>
    public string? Message { get; private set; }
    public int Depth => _path.Count;
    public bool InSubmenu => _path.Count > 0;

    public IReadOnlyList<WheelItem> Items => Current.Children;

    public WheelItem? HoverItem => HoverIndex >= 0 && HoverIndex < Items.Count ? Items[HoverIndex] : null;

    /// <summary>Deplacement du pointeur. Retourne vrai si l'affichage doit etre rafraichi.</summary>
    public bool OnPointer(int hitIndex, long nowMs)
    {
        bool changed = false;
        if (hitIndex != HoverIndex)
        {
            HoverIndex = hitIndex;
            _hoverSince = nowMs;
            Message = null;
            changed = true;
        }
        if (Mode == WheelMode.Drag)
        {
            var item = HoverItem;
            if (item is { IsBranch: true, Enabled: true } && nowMs - _hoverSince >= _dwellMs)
            {
                Enter(item, nowMs);
                return true;
            }
            if (HoverIndex == WheelLayout.Center && InSubmenu && nowMs - _hoverSince >= _dwellMs * 3 / 2)
            {
                Back(nowMs);
                return true;
            }
        }
        return changed;
    }

    /// <summary>Progression du survol (0..1) pour animer l'ouverture d'un sous-menu.</summary>
    public double DwellProgress(long nowMs)
    {
        if (Mode != WheelMode.Drag) return 0;
        var item = HoverItem;
        if (item is { IsBranch: true, Enabled: true }) return Math.Clamp((nowMs - _hoverSince) / (double)_dwellMs, 0, 1);
        if (HoverIndex == WheelLayout.Center && InSubmenu) return Math.Clamp((nowMs - _hoverSince) / (_dwellMs * 1.5), 0, 1);
        return 0;
    }

    /// <summary>Depot (mode glisser) ou clic (mode clic) a l'emplacement <paramref name="hitIndex"/>.</summary>
    public WheelOutcome OnRelease(int hitIndex, long nowMs)
    {
        HoverIndex = hitIndex;
        if (hitIndex == WheelLayout.Outside) return WheelOutcome.Cancel;
        if (hitIndex == WheelLayout.Center)
        {
            if (InSubmenu && Mode == WheelMode.Click) { Back(nowMs); return WheelOutcome.StayOpen; }
            if (InSubmenu && Mode == WheelMode.Drag) { Mode = WheelMode.Click; Back(nowMs); return WheelOutcome.StayOpen; }
            return WheelOutcome.Cancel;
        }
        var item = HoverItem;
        if (item is null) return WheelOutcome.Cancel;
        if (!item.Enabled)
        {
            Mode = WheelMode.Click;
            Message = item.DisabledReason ?? "Indisponible ici";
            return WheelOutcome.StayOpen;
        }
        if (item.IsBranch)
        {
            Mode = WheelMode.Click;
            Enter(item, nowMs);
            return WheelOutcome.StayOpen;
        }
        Selected = item;
        return WheelOutcome.Execute;
    }

    public WheelOutcome OnEscape() => WheelOutcome.Cancel;

    private void Enter(WheelItem branch, long nowMs)
    {
        _path.Push(Current);
        Current = branch;
        HoverIndex = WheelLayout.Center;
        _hoverSince = nowMs;
        Message = null;
    }

    public void Back(long nowMs)
    {
        if (_path.Count == 0) return;
        Current = _path.Pop();
        HoverIndex = WheelLayout.Center;
        _hoverSince = nowMs;
        Message = null;
    }

    /// <summary>Fil d'Ariane affiche au centre (« Convertir › Image »).</summary>
    public string Breadcrumb => string.Join(" › ", _path.Reverse().Skip(1).Append(Current).Where(i => i != Root).Select(i => i.Label));
}
