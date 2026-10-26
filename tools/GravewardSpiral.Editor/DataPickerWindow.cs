using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 標準のパックの品・モンスター・呪文を選ぶ窓(データのタブの「写す」の元)。言葉と2つの分け方で絞り込み、右に中身の要点を出す。
/// 品は種類と等級、モンスターは種類と脅威度、呪文は呪文のレベルと系統で絞る。
/// </summary>
public sealed class DataPickerWindow : Window
{
    /// <summary>一覧の1つ: id、名前、分け方1と2(の名前)、右に出す要点。</summary>
    private sealed record Entry(string Id, string Name, string A, string B, string Detail, double Sort = 0);

    private readonly ListBox _list = new() { Height = 440 };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0) };
    private readonly List<Entry> _all;

    public string? Picked { get; private set; }

    public DataPickerWindow(string kind, GameData data, ContentLibrary content)
    {
        Title = T("標準のものから選ぶ", "Pick a standard one");
        Width = 860;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);
        var texts = content.Texts(Lang);
        string Text(string key, string fallback) => texts.GetValueOrDefault(key) ?? fallback;
        _all = kind switch
        {
            "items" => data.Items.Values.Where(i => i.OriginGuid == null).Select(i => new Entry(i.Id, Loc.Name(i.Name) ?? i.Id, KindName(i.Kind), Text("rarity." + i.Rarity, i.Rarity),
                string.Join("\n", new[]
                {
                    $"{KindName(i.Kind)}・{Text("rarity." + i.Rarity, i.Rarity)}・{i.Price:0.##} G",
                    i.Damage != null ? T($"ダメージ {i.Damage} {i.DamageType}", $"Damage {i.Damage} {i.DamageType}") : null,
                    i.ArmorClass > 0 ? $"AC {i.ArmorClass}" : null,
                    i.Bonus != 0 ? $"+{i.Bonus}" : null,
                    Loc.Name(i.Description),
                }.OfType<string>()))).ToList(),
            "monsters" => data.Monsters.Values.Where(m => m.Usable).Select(m => new Entry(m.Id, Loc.Name(m.Name) ?? m.Id, PartNames.Name(PartNames.CreatureTypes, m.CreatureType), CrBand(m.Cr),
                T($"{PartNames.Name(PartNames.CreatureTypes, m.CreatureType)}・脅威度 {m.Cr:0.##}・経験値 {m.Xp}\nHP {m.HpAverage}({m.Hp})・AC {m.Ac}", $"{PartNames.Name(PartNames.CreatureTypes, m.CreatureType)}, CR {m.Cr:0.##}, XP {m.Xp}\nHP {m.HpAverage} ({m.Hp}), AC {m.Ac}"), m.Cr)).ToList(),
            _ => data.Spells.Values.Select(s => new Entry(s.Id, Loc.Name(s.Name) ?? s.Id, s.Level == 0 ? T("初級呪文", "Cantrip") : T($"{s.Level} レベル", $"Level {s.Level}"), Text("school." + s.School, s.School),
                T($"{(s.Level == 0 ? "初級呪文" : $"{s.Level} レベル")}・{Text("school." + s.School, s.School)}\n使える: {string.Join("・", s.Classes)}", $"{(s.Level == 0 ? "Cantrip" : $"Level {s.Level}")}, {Text("school." + s.School, s.School)}\nClasses: {string.Join(", ", s.Classes)}"))).ToList(),
        };
        _all = [.. _all.OrderBy(e => e.A, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.CurrentCulture)];

        var search = new TextBox { Width = 260, ToolTip = T("名前か id で探す", "Search by name or id") };
        var a = Filter(_all.Select(e => e.A), kind == "monsters" ? T("すべての種類", "All kinds") : kind == "items" ? T("すべての種類", "All kinds") : T("すべてのレベル", "All levels"));
        var b = Filter(_all.OrderBy(e => e.Sort).Select(e => e.B), kind == "monsters", kind == "monsters" ? T("すべての脅威度", "All CR") : kind == "items" ? T("すべての等級", "All rarities") : T("すべての系統", "All schools"));
        var count = new TextBlock { Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        void Fill()
        {
            string q = search.Text.Trim();
            string fa = a.SelectedIndex > 0 ? (string)a.SelectedItem : "", fb = b.SelectedIndex > 0 ? (string)b.SelectedItem : "";
            _list.Items.Clear();
            foreach (var e in _all.Where(e => (fa.Length == 0 || e.A == fa) && (fb.Length == 0 || e.B == fb)
                         && (q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Id.Contains(q, StringComparison.OrdinalIgnoreCase))))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };
                panel.Children.Add(new TextBlock { Text = e.Name, FontWeight = FontWeights.SemiBold });
                panel.Children.Add(new TextBlock { Text = $"{e.Id}  {e.A}・{e.B}", Foreground = Ui.Dim, FontSize = 11.5 });
                _list.Items.Add(new ListBoxItem { Content = panel, Tag = e });
            }
            count.Text = T($"{_list.Items.Count} 件", $"{_list.Items.Count}");
        }
        search.TextChanged += (_, _) => Fill();
        a.SelectionChanged += (_, _) => Fill();
        b.SelectionChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) =>
        {
            _detail.Inlines.Clear();
            if ((_list.SelectedItem as ListBoxItem)?.Tag is not Entry e) return;
            _detail.Inlines.Add(new System.Windows.Documents.Run(e.Name + "\n") { FontWeight = FontWeights.SemiBold, FontSize = 14 });
            _detail.Inlines.Add(new System.Windows.Documents.Run(e.Id + "\n\n") { Foreground = Ui.Dim });
            _detail.Inlines.Add(new System.Windows.Documents.Run(e.Detail));
        };
        _list.MouseDoubleClick += (_, _) => Pick();

        var panel = new StackPanel { Margin = new Thickness(16) };
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bar.Children.Add(search);
        bar.Children.Add(a);
        bar.Children.Add(b);
        bar.Children.Add(count);
        panel.Children.Add(bar);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        var detail = new ScrollViewer { Content = _detail, Height = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(detail, 1);
        body.Children.Add(_list);
        body.Children.Add(detail);
        panel.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = Ui.Button(T("これにする", "Use this"), Pick);
        ok.Style = (Style)Application.Current.FindResource("AccentButton");
        ok.IsDefault = true;
        var cancel = Ui.Button(T("やめる", "Cancel"), Close);
        cancel.IsCancel = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Fill();
        Loaded += (_, _) => search.Focus();
    }

    private static ComboBox Filter(IEnumerable<string> values, string all) => Filter(values, false, all);

    /// <summary>絞り込みの欄(はじめは「すべて」)。keepOrder なら並びはそのまま(脅威度の順など)。</summary>
    private static ComboBox Filter(IEnumerable<string> values, bool keepOrder, string all)
    {
        var list = new List<string> { all };
        list.AddRange(keepOrder ? values.Distinct() : values.Distinct().Order(StringComparer.Ordinal));
        return new ComboBox { ItemsSource = list, SelectedIndex = 0, Width = 150, Margin = new Thickness(8, 0, 0, 0) };
    }

    private static string KindName(Core.Items.ItemKind k) => k switch
    {
        Core.Items.ItemKind.Weapon => T("武器", "Weapon"), Core.Items.ItemKind.Armor => T("鎧", "Armor"), Core.Items.ItemKind.Shield => T("盾", "Shield"),
        Core.Items.ItemKind.Backpack => T("鞄", "Backpack"), Core.Items.ItemKind.Consumable => T("消耗品", "Consumable"), Core.Items.ItemKind.Gear => T("道具", "Gear"),
        Core.Items.ItemKind.Accessory => T("装身具", "Accessory"), Core.Items.ItemKind.Key => T("鍵", "Key"), _ => T("矢弾", "Ammo"),
    };

    /// <summary>脅威度の帯。</summary>
    private static string CrBand(double cr) => cr switch
    {
        < 1 => T("脅威度 1 未満", "CR under 1"), < 3 => T("脅威度 1〜2", "CR 1-2"), < 5 => T("脅威度 3〜4", "CR 3-4"),
        < 9 => T("脅威度 5〜8", "CR 5-8"), < 13 => T("脅威度 9〜12", "CR 9-12"), _ => T("脅威度 13 以上", "CR 13+"),
    };

    private void Pick()
    {
        if ((_list.SelectedItem as ListBoxItem)?.Tag is not Entry e) return;
        Picked = e.Id;
        DialogResult = true;
    }
}
