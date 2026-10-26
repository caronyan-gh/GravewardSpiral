using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Events;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// イベントの一覧の見せ方と絞り込み(イベントのタブと、標準のイベントから写す窓で使う)。
/// 名前は、題(町のイベント) → メモ(最初の文) → 最初の文の手順 の順で探す。2行目に id・場所・散らすか・レア度。
/// 絞り込みは、打った言葉(id・名前・メモ・文のどれかに入っていれば)と、場所と、置き方と、中身の種類(使っている部品から決める。
/// 戦闘・宝やお金・罠やダメージ・判定・回復・状態・連動する組・伝説の噂)と、レア度。
/// </summary>
public sealed class EventList
{
    private readonly TextBox _search = new() { Margin = new Thickness(0, 2, 0, 2), ToolTip = T("id・名前・メモ・文で探す", "Search by id, name, note or text") };
    private readonly ComboBox _place = new() { Width = 96, Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _how = new() { Width = 110 };
    private readonly ComboBox _kind = new() { Width = 120, Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _rarity = new() { Width = 96 };

    /// <summary>中身の種類(キー、名前、合うか)。使っている部品で決める。</summary>
    private static readonly (string Key, string Ja, string En, Func<EventDef, bool> Has)[] Kinds =
    [
        ("combat", "戦闘", "Combat", e => Uses(e, "combat")),
        ("loot", "宝・お金", "Loot & gold", e => Uses(e, "treasure", "give_item") || Uses(e, s => s.Type == "gold" && s.Value > 0)),
        ("trap", "罠・ダメージ", "Traps & damage", e => Uses(e, "damage")),
        ("check", "判定", "Checks", e => Uses(e, "check")),
        ("heal", "回復", "Healing", e => Uses(e, "heal", "cure")),
        ("condition", "状態", "Conditions", e => Uses(e, "condition")),
        ("choice", "選択肢", "Choices", e => Uses(e, "choice")),
        ("chain", "連動する組", "Chains", e => e.Scatter?.Chain != null),
        ("legend", "伝説の噂", "Legends", e => e.Rumor != null),
    ];

    /// <summary>絞り込みを変えた。</summary>
    public event Action? Changed;

    public FrameworkElement Bar { get; }

    public EventList()
    {
        _place.ItemsSource = new[] { new Choice("", T("どこでも", "Anywhere")), new Choice("maze", T("迷宮", "Maze")), new Choice("town", T("町", "Town")) };
        _place.SelectedIndex = 0;
        _how.ItemsSource = new[] { new Choice("", T("置き方すべて", "Any placing")), new Choice("scatter", T("散らす", "Scattered")), new Choice("placed", T("手で置く", "Placed by hand")) };
        _how.SelectedIndex = 0;
        _kind.ItemsSource = new[] { new Choice("", T("中身すべて", "Any content")) }.Concat(Kinds.Select(k => new Choice(k.Key, T(k.Ja, k.En)))).ToList();
        _kind.SelectedIndex = 0;
        _rarity.ItemsSource = new[] { new Choice("", T("レア度すべて", "Any rarity")) }.Concat(PartNames.Choices(EventScatter.RarityWeights.Keys, PartNames.Rarity).Select(c => new Choice(c.Key, PartNames.Name(PartNames.Rarity, c.Key)))).ToList();
        _rarity.SelectedIndex = 0;
        _kind.SelectionChanged += (_, _) => Changed?.Invoke();
        _rarity.SelectionChanged += (_, _) => Changed?.Invoke();
        _search.TextChanged += (_, _) => Changed?.Invoke();
        _place.SelectionChanged += (_, _) => Changed?.Invoke();
        _how.SelectionChanged += (_, _) => Changed?.Invoke();
        var bar = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var searchRow = new DockPanel();
        searchRow.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        searchRow.Children.Add(_search);
        bar.Children.Add(searchRow);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(_place);
        row.Children.Add(_how);
        bar.Children.Add(row);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        row2.Children.Add(_kind);
        row2.Children.Add(_rarity);
        bar.Children.Add(row2);
        Bar = bar;
    }

    /// <summary>探す言葉。</summary>
    public string Search
    {
        get => _search.Text;
        set => _search.Text = value;
    }

    /// <summary>絞り込みを全部外す(足したイベントが隠れないように)。</summary>
    public void Clear()
    {
        _search.Text = "";
        _place.SelectedIndex = 0;
        _how.SelectedIndex = 0;
        _kind.SelectedIndex = 0;
        _rarity.SelectedIndex = 0;
    }

    public bool Matches(EventDef e)
    {
        string place = (_place.SelectedItem as Choice)?.Key ?? "", how = (_how.SelectedItem as Choice)?.Key ?? "";
        if (place.Length > 0 && e.Place != place) return false;
        if (how == "scatter" && e.Scatter == null || how == "placed" && e.Scatter != null) return false;
        string kind = (_kind.SelectedItem as Choice)?.Key ?? "", rarity = (_rarity.SelectedItem as Choice)?.Key ?? "";
        if (kind.Length > 0 && !Kinds.First(k => k.Key == kind).Has(e)) return false;
        if (rarity.Length > 0 && e.Scatter?.Rarity != rarity) return false;
        string q = _search.Text.Trim();
        if (q.Length == 0) return true;
        bool Has(string? s) => s != null && s.Contains(q, StringComparison.OrdinalIgnoreCase);
        return Has(e.Id) || Has(Name(e)) || Has(e.Note) || Texts(e.Steps).Any(Has);
    }

    /// <summary>一覧の1行(名前を太く、下に id と場所など)。tag にイベントを持つ。</summary>
    public static ListBoxItem Item(EventDef e) => new() { Content = Label(e), Tag = e, ToolTip = e.Note };

    /// <summary>一覧の1行の中身(作り直すときは、毎回新しく作る。WPF の部品は2か所に置けない)。</summary>
    public static StackPanel Label(EventDef e)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };
        panel.Children.Add(new TextBlock { Text = Name(e) ?? e.Id, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string place = PartNames.Name(PartNames.Places, e.Place);
        string how = e.Scatter != null ? T($"・散らす・{PartNames.Name(PartNames.Rarity, e.Scatter.Rarity)}", $", scattered, {PartNames.Name(PartNames.Rarity, e.Scatter.Rarity)}") : "";
        panel.Children.Add(new TextBlock { Text = $"{e.Id}  {place}{how}", Foreground = Ui.Dim, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        return panel;
    }

    /// <summary>イベントの名前: 題 → メモの最初の文 → 最初の文の手順(長ければ切る)。無ければ null。</summary>
    public static string? Name(EventDef e)
    {
        if (Loc.Name(e.Title) is { Length: > 0 } title) return title;
        if (e.Note is { Length: > 0 } note) return Short(note.Split('。', '.')[0]);
        return Texts(e.Steps).FirstOrDefault() is { } text ? Short(text) : null;
    }

    private static string Short(string s) => s.Length > 30 ? s[..30] + "…" : s;

    /// <summary>その種類の部品を使っているか(入れ子の手順も)。</summary>
    private static bool Uses(EventDef e, params string[] types) => Uses(e, s => types.Contains(s.Type));

    private static bool Uses(EventDef e, Func<EventStep, bool> match)
    {
        bool Walk(IEnumerable<EventStep> steps) => steps.Any(s => match(s) || s.Children().Any(Walk));
        return Walk(e.Steps);
    }

    /// <summary>手順の中の文(今の言語。入れ子の手順も)。</summary>
    private static IEnumerable<string> Texts(IEnumerable<EventStep> steps)
    {
        foreach (var s in steps)
        {
            if (Loc.Name(s.Text) is { Length: > 0 } t) yield return t;
            foreach (var child in s.Children())
                foreach (var x in Texts(child)) yield return x;
        }
    }
}

/// <summary>標準のパックのイベントを選んで写す窓(絞り込みと、名前の付いた一覧)。</summary>
public sealed class EventPickerWindow : Window
{
    private readonly ListBox _list = new() { Height = 440 };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0) };

    public EventDef? Picked { get; private set; }

    /// <param name="search">はじめに入れておく探す言葉(確かめ用)。最初の行を選んでおく。</param>
    /// <param name="okLabel">決めるボタンの文字(省くと「写して足す」)。</param>
    public EventPickerWindow(IEnumerable<EventDef> events, string? search = null, string? okLabel = null)
    {
        Title = T("標準のイベントから写す", "Copy a standard event");
        Width = 900;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);
        var all = events.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var filter = new EventList();
        var count = new TextBlock { Foreground = Ui.Dim };
        void Fill()
        {
            _list.Items.Clear();
            foreach (var e in all.Where(filter.Matches)) _list.Items.Add(EventList.Item(e));
            count.Text = T($"{_list.Items.Count} 件", $"{_list.Items.Count} events");
        }
        filter.Changed += Fill;
        _list.MouseDoubleClick += (_, _) => Pick();
        _list.SelectionChanged += (_, _) => Preview((_list.SelectedItem as ListBoxItem)?.Tag as EventDef);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(filter.Bar);
        panel.Children.Add(count);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        var preview = new ScrollViewer { Content = _preview, Height = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(preview, 1);
        body.Children.Add(_list);
        body.Children.Add(preview);
        panel.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = Ui.Button(okLabel ?? T("写して足す", "Copy and add"), Pick);
        ok.Style = (Style)Application.Current.FindResource("AccentButton");
        ok.IsDefault = true;
        var cancel = Ui.Button(T("やめる", "Cancel"), Close);
        cancel.IsCancel = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Fill();
        if (search is { Length: > 0 })
        {
            filter.Search = search;
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }
    }

    private void Preview(EventDef? e) => ShowPreview(_preview, e);

    /// <summary>イベントの下見(名前とメモと、手順の文。入れ子も、順に)を、文字の欄に書く。</summary>
    public static void ShowPreview(TextBlock _preview, EventDef? e)
    {
        _preview.Inlines.Clear();
        if (e == null) return;
        _preview.Inlines.Add(new System.Windows.Documents.Run(EventList.Name(e) ?? e.Id) { FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (e.Note != null) _preview.Inlines.Add(new System.Windows.Documents.Run("\n" + e.Note) { Foreground = Ui.Dim });
        void Walk(IEnumerable<EventStep> steps, string indent)
        {
            foreach (var s in steps)
            {
                string line = Loc.Name(s.Text) is { Length: > 0 } t ? t : PartNames.Name(PartNames.Steps, s.Type) + (s.Id != null ? " " + s.Id : "");
                _preview.Inlines.Add(new System.Windows.Documents.Run($"\n{indent}・{line}"));
                foreach (var o in s.Options)
                {
                    if (Loc.Name(o.Label) is { Length: > 0 } label)
                        _preview.Inlines.Add(new System.Windows.Documents.Run($"\n{indent}  「{label}」") { Foreground = Theme.Brush(Theme.Palette.Accent) });
                    Walk(o.Steps, indent + "    ");
                }
                foreach (var child in new[] { s.Then, s.Else, s.Success, s.Fail, s.Win, s.Lose }) Walk(child, indent + "  ");
            }
        }
        _preview.Inlines.Add(new System.Windows.Documents.Run("\n"));
        Walk(e.Steps, "");
    }

    private void Pick()
    {
        if ((_list.SelectedItem as ListBoxItem)?.Tag is not EventDef e) return;
        Picked = e;
        DialogResult = true;
    }
}
