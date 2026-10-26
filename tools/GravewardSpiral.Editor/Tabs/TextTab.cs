using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 文言: シナリオの text/ja.json と en.json(キー → 文)。迷宮の「文言の印」(message:キー)は、ここに書いた文を出す。
/// 標準のパックと同じキーを書くと、このシナリオのあいだだけ、その文言が変わる。
/// </summary>
public sealed class TextTab(EditorContext context) : IEditorTab
{
    /// <summary>表の1行。</summary>
    public sealed class Row
    {
        public string Key { get; set; } = "";
        public string Ja { get; set; } = "";
        public string En { get; set; } = "";
    }

    private readonly DockPanel _view = new() { Margin = new Thickness(12) };
    private readonly ObservableCollection<Row> _rows = [];
    private readonly TextBlock _missing = new() { Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private DataGrid? _grid;

    public string Key => "text";
    public string Header => T("文言", "Text");
    public UIElement View => _view;

    public void Reload()
    {
        _view.Children.Clear();
        _rows.Clear();
        if (context.Pack is not { } pack) return;
        var top = new StackPanel();
        top.Children.Add(Ui.Note(T("キーと文(日本語・英語)。迷宮の「文言の印」(message:キー)は、ここの文を出す。標準のパックと同じキーを書くと、このシナリオのあいだだけ変わる。", "Keys and text (Japanese / English). A maze \"message mark\" (message:key) shows the text written here. Using the same key as the standard pack changes that text for this scenario only.")));
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        buttons.Children.Add(Ui.Button(T("行を足す", "Add row"), () =>
        {
            int n = _rows.Count + 1;
            while (_rows.Any(r => r.Key == $"{pack.Id}.text{n}")) n++;
            _rows.Add(new Row { Key = $"{pack.Id}.text{n}" });
            Sync();
        }));
        buttons.Children.Add(Ui.Button(T("選んだ行を消す", "Delete selected row"), () =>
        {
            if (_grid?.SelectedItem is Row r) _rows.Remove(r);
            Sync();
        }));
        buttons.Children.Add(Ui.Button(T("足りないキーを足す", "Add missing keys"), () =>
        {
            foreach (var key in MissingKeys()) _rows.Add(new Row { Key = key });
            Sync();
        }, T("迷宮の文言の印が使っていて、まだ無いキーを足す", "Add keys that maze message marks use but do not exist yet")));
        top.Children.Add(buttons);
        (_missing.Parent as Panel)?.Children.Remove(_missing);   // 作り直すたびに、前の置き場所から外す
        top.Children.Add(_missing);
        DockPanel.SetDock(top, Dock.Top);
        _view.Children.Add(top);

        var ja = pack.Texts.GetValueOrDefault("ja") ?? [];
        var en = pack.Texts.GetValueOrDefault("en") ?? [];
        foreach (var key in ja.Keys.Concat(en.Keys).Distinct().Order())
            _rows.Add(new Row { Key = key, Ja = ja.GetValueOrDefault(key) ?? "", En = en.GetValueOrDefault(key) ?? "" });
        _grid = new DataGrid
        {
            ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = T("キー", "Key"), Binding = new Binding(nameof(Row.Key)), Width = 220 });
        var wrap = new Style(typeof(TextBlock));
        wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        _grid.Columns.Add(new DataGridTextColumn { Header = "日本語", Binding = new Binding(nameof(Row.Ja)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = wrap });
        _grid.Columns.Add(new DataGridTextColumn { Header = "English", Binding = new Binding(nameof(Row.En)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = wrap });
        _grid.CellEditEnding += (_, _) => _grid.Dispatcher.BeginInvoke(Sync);   // 書き終わった値が入ってから
        _view.Children.Add(_grid);
        ShowMissing();
    }

    /// <summary>表をシナリオの文言に書き戻す(キーが空の行は書かない)。</summary>
    private void Sync()
    {
        if (context.Pack is not { } pack) return;
        var ja = new Dictionary<string, string>(StringComparer.Ordinal);
        var en = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in _rows.Where(r => r.Key.Trim().Length > 0))
        {
            if (r.Ja.Length > 0) ja[r.Key.Trim()] = r.Ja;
            if (r.En.Length > 0) en[r.Key.Trim()] = r.En;
        }
        pack.Texts["ja"] = ja;
        pack.Texts["en"] = en;
        context.MarkEdited();
        ShowMissing();
    }

    /// <summary>迷宮の文言の印が使っていて、このシナリオにも標準のパックにも無いキー。</summary>
    private List<string> MissingKeys()
    {
        if (context.Pack is not { } pack) return [];
        var have = _rows.Select(r => r.Key).ToHashSet();
        var baseTexts = context.BaseContent.Texts("ja");
        return pack.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()
            .Where(m => m.StartsWith("message:")).Select(m => m["message:".Length..])
            .Where(k => !have.Contains(k) && !baseTexts.ContainsKey(k)).Distinct().Order().ToList();
    }

    private void ShowMissing()
    {
        var missing = MissingKeys();
        _missing.Text = missing.Count == 0 ? "" : T("迷宮の文言の印が使っていて、まだ無いキー: ", "Keys used by maze message marks but not defined yet: ") + string.Join(T("、", ", "), missing);
    }
}
