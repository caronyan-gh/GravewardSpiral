using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Monsters;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 敵を選ぶ欄の絞り込み: 種類(すべてか、アンデッドなど)と脅威度(CR)の範囲。Bar を欄の上に置き、Choices で絞った候補を出す。
/// 脅威度の初めの範囲は、使う場所が決める(階の主なら、その階のレベルのあたり)。シナリオのモンスターは、いつも候補に入れる。
/// </summary>
public sealed class MonsterFilter
{
    private readonly EditorContext _context;
    private string? _type;
    private double _lo, _hi;

    /// <summary>絞り込みを変えた。</summary>
    public event Action? Changed;

    public FrameworkElement Bar { get; }

    public MonsterFilter(EditorContext context, (double Lo, double Hi)? cr = null)
    {
        _context = context;
        (_lo, _hi) = cr ?? (0, 30);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var kinds = new List<Choice> { new("", T("すべての種類", "All kinds")) };
        kinds.AddRange(PartNames.Choices(context.BaseData.Monsters.Values.Where(m => m.Usable).Select(m => m.CreatureType).Distinct(), PartNames.CreatureTypes));
        var type = new ComboBox { ItemsSource = kinds, SelectedIndex = 0, Width = 170 };
        var lo = new TextBox { Text = Fmt(_lo), Width = 44, VerticalContentAlignment = VerticalAlignment.Center };
        var hi = new TextBox { Text = Fmt(_hi), Width = 44, VerticalContentAlignment = VerticalAlignment.Center };
        var count = new TextBlock { Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        void Count() => count.Text = T($"{Choices().Count} 体", $"{Choices().Count} monsters");
        type.SelectionChanged += (_, _) =>
        {
            _type = (type.SelectedItem as Choice)?.Key is { Length: > 0 } k ? k : null;
            Count();
            Changed?.Invoke();
        };
        void Range()
        {
            if (!double.TryParse(lo.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) || !double.TryParse(hi.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)) return;
            (_lo, _hi) = (Math.Min(a, b), Math.Max(a, b));
            Count();
            Changed?.Invoke();
        }
        lo.LostFocus += (_, _) => Range();
        hi.LostFocus += (_, _) => Range();
        bar.Children.Add(new TextBlock { Text = T("絞り込み", "Filter"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        bar.Children.Add(type);
        bar.Children.Add(new TextBlock { Text = T("  脅威度", "  CR"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        bar.Children.Add(lo);
        bar.Children.Add(new TextBlock { Text = "〜", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        bar.Children.Add(hi);
        bar.Children.Add(count);
        var all = new Button { Content = T("解除", "Clear"), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0) };
        all.Click += (_, _) =>
        {
            type.SelectedIndex = 0;
            lo.Text = "0";
            hi.Text = "30";
            Range();
        };
        bar.Children.Add(all);
        Bar = bar;
        Count();
    }

    private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public bool Matches(MonsterDef m) => (_type == null || m.CreatureType == _type) && m.Cr >= _lo - 1e-9 && m.Cr <= _hi + 1e-9;

    /// <summary>絞った候補(このシナリオのモンスターが先、標準のものは脅威度の順)。keep の id は、合わなくても入れる(選んである敵)。</summary>
    public List<Choice> Choices(params string?[] keep)
    {
        var list = new List<Choice>();
        if (_context.Pack is { } pack)
            foreach (var o in pack.Monsters)
                if ((string?)o["id"] is { } id)
                    list.Add(new Choice(id, T($"{(string?)(o["name"] as System.Text.Json.Nodes.JsonObject)?[Lang] ?? id}(シナリオ)", $"{(string?)(o["name"] as System.Text.Json.Nodes.JsonObject)?[Lang] ?? id} (scenario)")));
        list.AddRange(_context.BaseData.Monsters.Values.Where(m => m.Usable && list.All(c => c.Key != m.Id) && (Matches(m) || keep.Contains(m.Id)))
            .OrderBy(m => m.Cr).ThenBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => new Choice(m.Id, $"{Loc.Name(m.Name)}  CR{m.Cr:0.##}  {PartNames.Name(PartNames.CreatureTypes, m.CreatureType)}")));
        return list;
    }

    /// <summary>レベル level のあたりの主に合う脅威度(level−2〜level+2)。</summary>
    public static (double, double) AroundLevel(double level) => (Math.Max(0, Math.Floor(level) - 2), Math.Ceiling(level) + 2);
}
