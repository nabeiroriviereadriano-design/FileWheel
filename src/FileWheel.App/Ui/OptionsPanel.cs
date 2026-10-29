using System.Windows;
using System.Windows.Controls;
using FileWheel.Conversion.Core;

namespace FileWheel.App.Ui;

/// <summary>
/// Panneau de reglages genere a partir des descriptions de capacite (OptionDef).
/// Les reglages « avances » sont regroupes dans un volet repliable.
/// </summary>
public sealed class OptionsPanel : StackPanel
{
    private readonly Dictionary<string, string> _values = new();
    public event Action? Changed;

    public IReadOnlyDictionary<string, string> Values => _values;

    public void Load(IReadOnlyList<OptionDef> options, IReadOnlyDictionary<string, string>? initial, bool showAdvanced)
    {
        Children.Clear();
        _values.Clear();
        var basic = options.Where(o => !o.Advanced).ToList();
        var adv = options.Where(o => o.Advanced).ToList();
        foreach (var o in basic) Children.Add(Control(o, initial));
        if (adv.Count > 0 && showAdvanced)
        {
            var inner = new StackPanel();
            foreach (var o in adv) inner.Children.Add(Control(o, initial));
            Children.Add(new Expander { Header = "Réglages avancés", Content = inner, Margin = new Thickness(0, 8, 0, 0), IsExpanded = false });
        }
        if (options.Count == 0) Children.Add(Ui.Hint("Aucun réglage pour cette conversion."));
    }

    private UIElement Control(OptionDef o, IReadOnlyDictionary<string, string>? initial)
    {
        var value = initial is not null && initial.TryGetValue(o.Key, out var iv) ? iv : o.Default;
        _values[o.Key] = value;
        UIElement ctl;
        switch (o.Kind)
        {
            case OptionKind.Choice:
                ctl = Ui.Combo(o.Choices.Select(c => (c.Value, c.Label)), value, v => Set(o.Key, v));
                break;
            case OptionKind.Bool:
                return Ui.Check(o.Label, value == "true", b => Set(o.Key, b ? "true" : "false"), o.Help.Length > 0 ? o.Help : null);
            case OptionKind.Int:
            {
                var tb = new TextBox { Text = value, Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
                tb.TextChanged += (_, _) =>
                {
                    bool ok = int.TryParse(tb.Text, out var n) && n >= o.Min && n <= o.Max;
                    tb.ToolTip = ok ? null : $"Nombre entre {o.Min} et {o.Max}";
                    if (ok) tb.ClearValue(Border.BorderBrushProperty); else tb.BorderBrush = Ui.Error;
                    Set(o.Key, tb.Text);
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(tb);
                if (o.Unit.Length > 0) row.Children.Add(new TextBlock { Text = o.Unit, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Subtle });
                ctl = row;
                break;
            }
            default:
            {
                var tb = new TextBox { Text = value, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
                if (o.Kind == OptionKind.Time) tb.ToolTip = "Exemples : 90, 1:30, 00:01:30.5";
                tb.TextChanged += (_, _) => Set(o.Key, tb.Text);
                ctl = tb;
                break;
            }
        }
        return Ui.Field(o.Label, ctl, o.Help.Length > 0 ? o.Help : null);
    }

    private void Set(string key, string value)
    {
        _values[key] = value;
        Changed?.Invoke();
    }
}
