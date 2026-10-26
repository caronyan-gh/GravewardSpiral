using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GravewardSpiral.Core.Content;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// データ: シナリオの品・モンスター・呪文の名前。
/// 品とモンスターは、標準のものを写して一部だけ変える(extends)か、全部写してから直す。項目は名前の欄と、全体の JSON で直す。
/// モンスターの絵は art/monsters/&lt;id&gt;.png に写す。呪文は名前だけ変えられる(新しい呪文は作れない)。
/// </summary>
public sealed class DataTab : IEditorTab
{
    private static (string Key, string Label)[] Kinds() => [("items", T("品", "Items")), ("monsters", T("モンスター", "Monsters")), (ContentLibrary.SpellCollection, T("呪文の名前", "Spell names"))];

    private readonly EditorContext _context;
    private readonly Grid _view = new();
    private readonly ListBox _list = new();
    /// <summary>写す元(標準のパックの id)。窓で選ぶ。</summary>
    private string? _baseId;
    private readonly TextBlock _baseName = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
    private readonly StackPanel _props = new();
    private readonly TextBlock _error = new() { Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap };
    private string _kind = "items";
    private bool _loading;

    public DataTab(EditorContext context)
    {
        _context = context;
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel();
        var kinds = new WrapPanel();
        foreach (var (key, label) in Kinds())
        {
            var radio = new RadioButton { Content = label, GroupName = "kind", Margin = new Thickness(0, 0, 12, 4), IsChecked = key == _kind, Tag = key };
            radio.Checked += (_, _) => { _kind = (string)radio.Tag; Reload(); };
            kinds.Children.Add(radio);
        }
        top.Children.Add(kinds);
        top.Children.Add(Ui.Button(T("写す元を選ぶ…", "Choose the original…"), PickBase, T("標準のパックのものを、名前と絞り込みで選ぶ", "Pick from the standard pack by name and filters")));
        top.Children.Add(_baseName);
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        buttons.Children.Add(Ui.Button(T("写して一部を変える", "Copy and change parts"), () => Add(extend: true), T("extends で元を写す。書いた項目だけが変わる(元が直されると、一緒に変わる)", "Copies the original with extends. Only the fields you write differ (if the original is fixed, this changes with it)")));
        buttons.Children.Add(Ui.Button(T("全部写して作る", "Copy everything"), () => Add(extend: false), T("元の中身を全部書き写す(元が直されても変わらない)", "Copies the whole original (stays as is if the original is fixed)")));
        buttons.Children.Add(Ui.Button(T("消す", "Delete"), Remove));
        top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        _list.SelectionChanged += (_, _) => { if (!_loading) ShowProps(); };
        left.Children.Add(_list);
        _view.Children.Add(left);

        var right = new ScrollViewer { Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
        Grid.SetColumn(right, 1);
        _view.Children.Add(right);
    }

    public string Key => "data";
    public string Header => T("データ", "Data");
    public UIElement View => _view;

    private List<JsonObject>? List => _context.Pack?.DataLists().FirstOrDefault(x => x.Collection == _kind).List;

    private JsonObject? Current => List is { } l && _list.SelectedIndex >= 0 && _list.SelectedIndex < l.Count ? l[_list.SelectedIndex] : null;

    /// <summary>標準のパックにある、その種類の id。</summary>
    private IEnumerable<string> BaseIds() => _context.BaseContent.All(_kind).Select(x => x.Id).Order();

    public void Reload()
    {
        _baseId = null;
        _baseName.Text = T("(まだ選んでいない)", "(not chosen yet)");
        Refresh(0);
    }

    /// <summary>写す元を、絞り込みのある窓で選ぶ。</summary>
    private void PickBase()
    {
        var dialog = new DataPickerWindow(_kind, _context.BaseData, _context.BaseContent) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Picked is not { } id) return;
        _baseId = id;
        _baseName.Text = T($"写す元: {BaseName(id)}({id})", $"Original: {BaseName(id)} ({id})");
    }

    private string BaseName(string id) => _kind switch
    {
        "items" => Loc.Name(_context.BaseData.Items.GetValueOrDefault(id)?.Name),
        "monsters" => Loc.Name(_context.BaseData.Monsters.GetValueOrDefault(id)?.Name),
        _ => Loc.Name(_context.BaseData.Spells.GetValueOrDefault(id)?.Name),
    } ?? id;

    private void Refresh(int select)
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var o in List ?? []) _list.Items.Add(Label(o));
        _list.SelectedIndex = _list.Items.Count == 0 ? -1 : Math.Clamp(select, 0, _list.Items.Count - 1);
        _loading = false;
        ShowProps();
    }

    private static string Label(JsonObject o)
    {
        string id = (string?)o["id"] ?? "?";
        string? name = o["name"] is JsonObject n ? (string?)n[Lang] ?? (string?)n[En ? "ja" : "en"] : (string?)o["name"];
        string? ext = (string?)o["extends"];
        return $"{id}  {name}{(ext != null ? $"  (← {ext})" : "")}";
    }

    private void Add(bool extend)
    {
        if (_baseId == null) PickBase();
        if (List is not { } list || _baseId == null || _context.BaseContent.Find(_kind, _baseId) is not { } src) return;
        string id = src.Id;
        if (_kind != ContentLibrary.SpellCollection)
        {
            // 新しい id(呪文は同じ id で名前だけ変える)
            id = _context.Pack!.Id + "_" + src.Id;
            for (int n = 2; list.Any(o => (string?)o["id"] == id); n++) id = $"{_context.Pack.Id}_{src.Id}{n}";
        }
        else if (list.Any(o => (string?)o["id"] == id)) return;
        var srcObj = JsonNode.Parse(src.Data.GetRawText())!.AsObject();
        JsonObject made = _kind == ContentLibrary.SpellCollection || extend
            ? new JsonObject { ["id"] = id, ["name"] = srcObj["name"]?.DeepClone() }
            : srcObj;
        if (extend && _kind != ContentLibrary.SpellCollection) made["extends"] = src.Id;
        made["id"] = id;
        list.Add(made);
        _context.MarkEdited();
        Refresh(list.Count - 1);
    }

    private void Remove()
    {
        if (List is not { } list || Current is not { } o) return;
        if (MessageBox.Show(T($"{o["id"]} を消しますか？", $"Delete {o["id"]}?"), T("データ", "Data"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        int i = list.IndexOf(o);
        list.Remove(o);
        _context.MarkEdited();
        Refresh(i);
    }

    private void ShowProps()
    {
        _props.Children.Clear();
        _error.Text = "";
        if (Current is not { } o)
        {
            _props.Children.Add(Ui.Note(_kind == ContentLibrary.SpellCollection
                ? T("呪文は、標準の呪文の名前だけ変えられる(新しい呪文や、効果の違う呪文は作れない)。左で呪文を選んで「写して一部を変える」", "Only the names of standard spells can be changed (you cannot create new spells or spells with different effects). Pick a spell on the left and press \"Copy and change parts\"")
                : T("左の「写す元を選ぶ」で標準のものを選んで、「写して一部を変える」か「全部写して作る」。品は何でも作れる(シナリオの外へ持ち出せる)", "Pick a standard one on the left, then \"Copy and change parts\" or \"Copy everything\". Any item can be made (and taken out of the scenario)")));
            return;
        }
        string id = (string?)o["id"] ?? "";
        _props.Children.Add(Ui.Heading(Label(o)));
        if (_kind != ContentLibrary.SpellCollection)
        {
            var idBox = Ui.Text(id, _ => { });
            idBox.LostFocus += (_, _) => Rename(o, idBox.Text.Trim());
            _props.Children.Add(Ui.Row("id", idBox));
            _props.Children.Add(Ui.Row(T("元(extends)", "Base (extends)"), new TextBlock { Text = (string?)o["extends"] ?? T("(無し。全部書いてある)", "(none; everything is written out)") }));
        }
        else _props.Children.Add(Ui.Row(T("呪文", "Spell"), new TextBlock { Text = id }));
        var name = o["name"] is JsonObject n ? LocalizedText.Of((string?)n["en"] ?? "", (string?)n["ja"]) : null;
        _props.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(name, v =>
        {
            o["name"] = v == null ? null : new JsonObject { ["en"] = v.Texts.GetValueOrDefault("en") ?? "", ["ja"] = v.Texts.GetValueOrDefault("ja") ?? "" };
            _context.MarkEdited();
            RefreshLabel();
        })));
        if (_kind == ContentLibrary.SpellCollection) return;
        if (_kind == "monsters") ArtPanel(id);
        if (_kind == "items")
        {
            // おまかせで出さない(no_random。古い名前は drop_only。extends なら元の値を受け継ぐ)
            static bool? Flag(JsonObject x) => (bool?)x["no_random"] ?? (bool?)x["drop_only"];
            bool inherited = (string?)o["extends"] is { } from && _context.BaseContent.Find(_kind, from) is { } b
                && Flag(JsonNode.Parse(b.Data.GetRawText())!.AsObject()) == true;
            var check = new CheckBox
            {
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = T("おまかせでは出さない(宝の抽選・商店に出ず、イベントの「品を渡す」や落とし物の表で名指ししたときだけ手に入る)",
                        "Never at random (not in treasure rolls or shops; only when named by an event's give item or a drop table)"),
                },
                IsChecked = Flag(o) ?? inherited, VerticalAlignment = VerticalAlignment.Center,
            };
            check.Click += (_, _) =>
            {
                o.Remove("drop_only");
                o["no_random"] = check.IsChecked == true;
                _context.MarkEdited();
                ShowProps();
            };
            _props.Children.Add(Ui.Row(T("出し方", "Drops"), check));
        }

        // 全体の JSON(extends のものは、書いた項目だけ。解いた中身は下に出す)
        var json = new TextBox
        {
            Text = o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Height = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _props.Children.Add(Ui.Heading(T("JSON(書いた項目)", "JSON (written fields)")));
        _props.Children.Add(Ui.Note(_kind == "items"
            ? T("項目の名前は標準の items.json と同じ(kind・price・damage・armor_class・passive・use・use_spell など)。base(元の武器・鎧)と looks_like は標準の品だけを指す", "Field names are the same as in the standard items.json (kind, price, damage, armor_class, passive, use, use_spell, etc.). base (the original weapon/armor) and looks_like can only point to standard items")
            : T("項目の名前は標準の monsters.json と同じ(cr・hp・ac・attacks・type など)", "Field names are the same as in the standard monsters.json (cr, hp, ac, attacks, type, etc.)")));
        _props.Children.Add(json);
        var jsonButtons = new StackPanel { Orientation = Orientation.Horizontal };
        jsonButtons.Children.Add(Ui.Button(T("JSON を当てる", "Apply JSON"), () => ApplyJson(o, json.Text)));
        jsonButtons.Children.Add(Ui.Button(T("JSON を確かめる", "Check JSON"), () => CheckJson(o, json.Text),
            T("当てる前に、ゲームと同じ検査をする(書き間違えた項目名、無い呪文や元の品、ダイスの書き方など)。保存はしない", "Runs the game's checks before applying (misspelled fields, missing spells or base items, dice syntax, etc.). Does not save")));
        jsonButtons.Children.Add(Ui.Button(T("項目の一覧…", "Field list…"), () => FieldList(json),
            T("書ける項目と、その説明・書き方の例", "The fields you can write, with descriptions and examples")));
        _props.Children.Add(jsonButtons);
        _props.Children.Add(_error);
        if ((string?)o["extends"] is { } ext && _context.BaseContent.Find(_kind, ext) is { } baseItem)
        {
            var merged = JsonNode.Parse(baseItem.Data.GetRawText())!.AsObject();
            foreach (var (k, v) in o) if (k != "extends") merged[k] = v?.DeepClone();
            _props.Children.Add(Ui.Heading(T("解いた中身(元 + 書いた項目。ゲームはこれを使う)", "Resolved result (base + written fields; the game uses this)")));
            _props.Children.Add(new TextBox
            {
                Text = merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Height = 200, Foreground = Ui.Dim,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        }
    }

    /// <summary>モンスターの絵: パックの art/monsters/&lt;id&gt;.png(無ければ、元のモンスターの絵を使う)。</summary>
    private void ArtPanel(string id)
    {
        string path = Path.Combine(_context.Pack!.Directory, "art", "monsters", id + ".png");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        if (File.Exists(path))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // ファイルを掴まない(差し替えられるように)
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            row.Children.Add(new Image { Source = bmp, Height = 120, Margin = new Thickness(0, 0, 12, 0) });
        }
        else row.Children.Add(new TextBlock { Text = T("絵なし(元のモンスターの絵か、絵の無い表示)", "No art (uses the base monster's art, or none)"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        row.Children.Add(Ui.Button(T("絵を選ぶ(png)", "Choose art (png)"), () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PNG|*.png" };
            if (dialog.ShowDialog() != true) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(dialog.FileName, path, overwrite: true);
            ShowProps();
        }));
        if (File.Exists(path)) row.Children.Add(Ui.Button(T("絵を消す", "Remove art"), () => { File.Delete(path); ShowProps(); }));
        _props.Children.Add(Ui.Row(T("絵", "Art"), row));
    }

    /// <summary>当てる前に、JSON をゲームと同じ検査にかける(保存しない)。結果は JSON の下に出す。</summary>
    private void CheckJson(JsonObject o, string text)
    {
        JsonObject parsed;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject p || (string?)p["id"] is not { Length: > 0 }) throw new JsonException(T("id のあるオブジェクトにしてください", "Use an object that has an id"));
            parsed = p;
        }
        catch (JsonException ex)
        {
            _error.Foreground = Ui.Warn;
            _error.Text = T("JSON を読めません: ", "Cannot read the JSON: ") + ex.Message;
            return;
        }
        var problems = _context.CheckData(_kind, parsed, List!.IndexOf(o));
        _error.Foreground = problems.Count == 0 ? Ui.Good : Ui.Warn;
        _error.Text = problems.Count == 0
            ? T("○ 問題なし(まだ当てていません。「JSON を当てる」で入れる)", "○ No problems (not applied yet; press \"Apply JSON\")")
            : T($"× 問題 {problems.Count} 件:\n", $"× {problems.Count} problem(s):\n") + string.Join("\n", problems.Select(p => "・" + p));
    }

    /// <summary>書ける項目の一覧の窓(ダブルクリックで JSON の欄に書き込む)。</summary>
    private void FieldList(TextBox json) =>
        new FieldListWindow(_kind, _context.BaseContent, json) { Owner = Application.Current.MainWindow }.Show();

    private void ApplyJson(JsonObject o, string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject parsed || (string?)parsed["id"] is not { Length: > 0 }) throw new JsonException(T("id のあるオブジェクトにしてください", "Use an object that has an id"));
            var list = List!;
            int i = list.IndexOf(o);
            list[i] = parsed;
            _context.MarkEdited();
            Refresh(i);
        }
        catch (JsonException ex)
        {
            _error.Foreground = Ui.Warn;
            _error.Text = T("JSON を読めません: ", "Cannot read the JSON: ") + ex.Message;
        }
    }

    /// <summary>id を変える(モンスターなら、階の顔ぶれと絵も直す)。</summary>
    private void Rename(JsonObject o, string id)
    {
        string old = (string?)o["id"] ?? "";
        if (id.Length == 0 || id == old || List!.Any(x => (string?)x["id"] == id)) return;
        o["id"] = id;
        if (_kind == "monsters" && _context.Pack is { } p)
        {
            foreach (var r in p.Rosters)
                for (int k = 0; k < r.Count; k++) if (r[k] == old) r[k] = id;
            string art = Path.Combine(p.Directory, "art", "monsters");
            if (File.Exists(Path.Combine(art, old + ".png"))) File.Move(Path.Combine(art, old + ".png"), Path.Combine(art, id + ".png"), overwrite: true);
        }
        _context.MarkEdited();
        RefreshLabel();
        ShowProps();
    }

    private void RefreshLabel()
    {
        if (Current is { } o && _list.SelectedIndex >= 0)
        {
            _loading = true;
            int i = _list.SelectedIndex;
            _list.Items[i] = Label(o);
            _list.SelectedIndex = i;
            _loading = false;
        }
    }
}
