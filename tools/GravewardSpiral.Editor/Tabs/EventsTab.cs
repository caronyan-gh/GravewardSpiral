using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Save;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// イベント: 左に一覧、真ん中に条件と手順の木、右に選んだ部品の項目。下に JSON(直して「JSON を当てる」でも直せる)。
/// 部品は EventCondition.KnownTypes と EventStep.KnownTypes から選ぶ(ゲームと同じ部品)。迷宮のマスへは、迷宮のタブの「イベントの印」で置く。
/// </summary>
public sealed class EventsTab : IEditorTab
{
    private readonly EditorContext _context;
    private readonly Grid _view = new();
    private readonly ListBox _list = new();
    private readonly TreeView _tree = new();
    private readonly StackPanel _props = new();
    private readonly TextBox _json = new()
    {
        AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190,
    };
    private readonly ComboBox _stepType = new() { ItemsSource = PartNames.Choices(EventStep.KnownTypes, PartNames.Steps), SelectedIndex = 0, Width = 190 };
    private readonly ComboBox _condType = new() { ItemsSource = PartNames.Choices(EventCondition.KnownTypes, PartNames.Conditions), SelectedIndex = 0, Width = 190 };
    private readonly EventList _filter = new();
    private readonly CheckBox _showPlaced = new() { Content = T("迷宮に置いた標準のイベントも出す", "Also list standard events placed in the maze"), IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };

    /// <summary>一覧に出している、迷宮に置いた標準のイベント(このシナリオのものではない。見るだけで、写すと直せる)。</summary>
    private readonly HashSet<EventDef> _standard = [];
    private readonly TextBlock _jsonError = new() { Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap };
    private object? _selected;
    private bool _loading;

    public EventsTab(EditorContext context)
    {
        _context = context;
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

        var left = new DockPanel { Margin = new Thickness(8) };
        var leftTop = new StackPanel();
        leftTop.Children.Add(Ui.Heading(T("イベント", "Events")));
        var addRow = new WrapPanel();
        addRow.Children.Add(Ui.Button(T("足す", "Add"), AddEvent));
        addRow.Children.Add(Ui.Button(T("写す", "Copy"), CopyEvent));
        addRow.Children.Add(Ui.Button(T("消す", "Delete"), RemoveEvent));
        leftTop.Children.Add(addRow);
        leftTop.Children.Add(Ui.Button(T("標準のイベントから写す…", "Copy a standard event…"), CopyFromBase, T("標準のパックのイベントを、名前と中身を見て選んで写す", "Pick an event from the standard pack by name and content, and copy it")));
        leftTop.Children.Add(Ui.Button(T("AI に頼む…", "Ask the AI…"), () => AskAi(), T("選んでいるイベントを元に(無ければ新しく)、こうしたいことを書いて AI に下書きさせる", "Have the AI draft an event from the selected one (or a new one) and your request")));
        leftTop.Children.Add(_filter.Bar);
        leftTop.Children.Add(_showPlaced);
        _showPlaced.Click += (_, _) => RefreshList(Current);
        _filter.Changed += FilterChanged;
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        _list.SelectionChanged += (_, _) => { if (!_loading) ShowEvent(); };
        left.Children.Add(_list);
        _view.Children.Add(left);

        var center = new DockPanel { Margin = new Thickness(8) };
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        // 手順は、まず組を選んで部品を絞る
        var stepGroup = new ComboBox { Width = 130, ItemsSource = PartNames.StepGroups.Select(g => T(g.Ja, g.En)).ToList(), SelectedIndex = 0 };
        stepGroup.SelectionChanged += (_, _) =>
        {
            if (stepGroup.SelectedIndex < 0) return;
            _stepType.ItemsSource = PartNames.Choices(PartNames.StepGroups[stepGroup.SelectedIndex].Keys, PartNames.Steps);
            _stepType.SelectedIndex = 0;
        };
        _stepType.ItemsSource = PartNames.Choices(PartNames.StepGroups[0].Keys, PartNames.Steps);
        tools.Children.Add(stepGroup);
        tools.Children.Add(_stepType);
        tools.Children.Add(Ui.Button(T("手順を足す", "Add step"), AddStep));
        tools.Children.Add(_condType);
        tools.Children.Add(Ui.Button(T("条件を足す", "Add condition"), AddCondition));
        tools.Children.Add(Ui.Button(T("選択肢を足す", "Add choice"), AddOption));
        tools.Children.Add(Ui.Button(T("上へ", "Up"), () => Move(-1)));
        tools.Children.Add(Ui.Button(T("下へ", "Down"), () => Move(1)));
        tools.Children.Add(Ui.Button(T("消す", "Delete"), RemoveNode));
        DockPanel.SetDock(tools, Dock.Top);
        center.Children.Add(tools);
        var jsonPanel = new StackPanel();
        jsonPanel.Children.Add(new WrapPanel { Children = { new TextBlock { Text = "JSON", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 8, 2) }, Ui.Button(T("JSON を当てる", "Apply JSON"), ApplyJson) } });
        jsonPanel.Children.Add(_json);
        jsonPanel.Children.Add(_jsonError);
        DockPanel.SetDock(jsonPanel, Dock.Bottom);
        center.Children.Add(jsonPanel);
        _tree.SelectedItemChanged += (_, _) =>
        {
            if (_loading) return;
            _selected = (_tree.SelectedItem as TreeViewItem)?.Tag;
            ShowProps();
        };
        center.Children.Add(_tree);
        Grid.SetColumn(center, 1);
        _view.Children.Add(center);

        var right = new ScrollViewer { Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        Grid.SetColumn(right, 2);
        _view.Children.Add(right);
    }

    public string Key => "events";
    public string Header => T("イベント", "Events");
    public UIElement View => _view;

    private EventDef? Current => (_list.SelectedItem as ListBoxItem)?.Tag is EventDef e && !_standard.Contains(e) ? e : null;

    /// <summary>選んでいる、迷宮に置いた標準のイベント。</summary>
    private EventDef? SelectedStandard => (_list.SelectedItem as ListBoxItem)?.Tag is EventDef e && _standard.Contains(e) ? e : null;

    public void Reload()
    {
        RefreshList(_context.Pack?.Events.FirstOrDefault());
    }

    /// <summary>このシナリオのイベントの一覧(絞り込みに合うもの)を作り直し、select を選ぶ(絞り込みで隠れるなら、絞り込みを外す)。</summary>
    private void RefreshList(EventDef? select)
    {
        var events = _context.Pack?.Events ?? [];
        if (select != null && !_filter.Matches(select))
        {
            _filter.Changed -= FilterChanged;
            _filter.Clear();
            _filter.Changed += FilterChanged;
        }
        _loading = true;
        _list.Items.Clear();
        foreach (var e in events.Where(_filter.Matches)) _list.Items.Add(EventList.Item(e));
        // 迷宮の印に置いてある標準のパックのイベント(このシナリオに無いもの)。灰色の札で、選ぶと中身を見て写せる
        _standard.Clear();
        if (_showPlaced.IsChecked == true && _context.Pack is { } pack)
        {
            var placed = pack.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()
                .Where(m => m.StartsWith(EventPlacer.MarkPrefix)).Select(m => m[EventPlacer.MarkPrefix.Length..])
                .Where(id => events.All(x => x.Id != id)).Distinct().Order(StringComparer.Ordinal);
            foreach (var id in placed)
                if (_context.BaseData.Events.TryGetValue(id, out var std) && _filter.Matches(std))
                {
                    _standard.Add(std);
                    var item = EventList.Item(std);
                    item.Opacity = 0.7;
                    item.ToolTip = T("標準のパックのイベント(迷宮に置いてある)。選ぶと中身を見て、このシナリオに写して直せる", "A standard pack event placed in the maze. Select it to view and copy it into this scenario to edit");
                    _list.Items.Add(item);
                }
        }
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag == select) ?? (_list.Items.Count > 0 ? _list.Items[0] : null);
        _loading = false;
        ShowEvent();
    }

    private void FilterChanged() => RefreshList(Current);

    private void ShowEvent()
    {
        _selected = Current;
        RebuildTree();
        ShowProps();
    }

    private void Changed(bool tree = true)
    {
        _context.MarkEdited();
        if (tree) RebuildTree();
        if (Current is { } e) _json.Text = ScenarioPack.CleanJson(e);
        _jsonError.Text = "";
    }

    // ---------------- 木 ----------------

    /// <summary>木の中の、手順のならび(名前と、入れ物)。</summary>
    private sealed record StepList(string Label, List<EventStep> Steps);

    /// <summary>木の中の、条件のならび(名前と、入れ物)。</summary>
    private sealed record CondList(string Label, List<EventCondition> Conds);

    private void RebuildTree()
    {
        _loading = true;
        _tree.Items.Clear();
        if (Current is { } e)
        {
            var root = Item(T($"イベント {e.Id}", $"Event {e.Id}"), e);
            root.Items.Add(Conds(T("条件 (when)", "Conditions (when)"), e.When));
            root.Items.Add(Steps(T("手順 (steps)", "Steps"), e.Steps));
            root.IsExpanded = true;
            _tree.Items.Add(root);
            _json.Text = ScenarioPack.CleanJson(e);
        }
        else _json.Text = "";
        _loading = false;
        Reselect(_tree.Items);
    }

    private bool Reselect(ItemCollection items)
    {
        foreach (TreeViewItem i in items)
        {
            if (ReferenceEquals(i.Tag, _selected) || i.Tag is StepList sl && _selected is StepList s2 && ReferenceEquals(sl.Steps, s2.Steps)
                || i.Tag is CondList cl && _selected is CondList c2 && ReferenceEquals(cl.Conds, c2.Conds))
            {
                i.IsSelected = true;
                i.BringIntoView();
                return true;
            }
            if (Reselect(i.Items)) return true;
        }
        return false;
    }

    private static TreeViewItem Item(string header, object tag) => new() { Header = header, Tag = tag, IsExpanded = true };

    private TreeViewItem Steps(string label, List<EventStep> steps)
    {
        var item = Item(label, new StepList(label, steps));
        foreach (var s in steps) item.Items.Add(Step(s));
        return item;
    }

    private TreeViewItem Conds(string label, List<EventCondition> conds)
    {
        var item = Item(label + (conds.Count == 0 ? T(" なし", " (none)") : ""), new CondList(label, conds));
        foreach (var c in conds) item.Items.Add(Item(Describe(c), c));
        return item;
    }

    private TreeViewItem Step(EventStep s)
    {
        var item = Item(Describe(s), s);
        switch (s.Type)
        {
            case "if":
                item.Items.Add(Conds(T("もし (when)", "If (when)"), s.When));
                item.Items.Add(Steps(T("なら (then)", "Then"), s.Then));
                item.Items.Add(Steps(T("でなければ (else)", "Else"), s.Else));
                break;
            case "check":
                item.Items.Add(Steps(T("成功 (success)", "Success"), s.Success));
                item.Items.Add(Steps(T("失敗 (fail)", "Fail"), s.Fail));
                break;
            case "combat":
                item.Items.Add(Steps(T("勝った (win)", "Win"), s.Win));
                item.Items.Add(Steps(T("逃げた (lose)", "Fled (lose)"), s.Lose));
                break;
            case "world_doc":
                item.Items.Add(Steps(T("AI の文が無いとき (else)", "No AI text (else)"), s.Else));
                break;
            case "choice" or "random":
                foreach (var o in s.Options)
                {
                    var oi = Item(s.Type == "choice" ? T($"選択肢「{Loc.Name(o.Label)}」", $"Choice \"{Loc.Name(o.Label)}\"") : T($"候補(重み {o.Weight:0.##})", $"Option (weight {o.Weight:0.##})"), o);
                    if (s.Type == "choice") oi.Items.Add(Conds(T("出す条件 (when)", "Show if (when)"), o.When));
                    oi.Items.Add(Steps(T("手順", "Steps"), o.Steps));
                    item.Items.Add(oi);
                }
                break;
        }
        return item;
    }

    private static string Describe(EventStep s) => s.Type switch
    {
        "text" => T($"文: {Short(Loc.Name(s.Text) ?? s.Key)}", $"Text: {Short(Loc.Name(s.Text) ?? s.Key)}"),
        "choice" => T($"選ぶ: {Short(Loc.Name(s.Text))}", $"Choose: {Short(Loc.Name(s.Text))}"),
        "if" => T("もし", "If"),
        "check" => T($"判定 {s.Skill ?? s.Ability} DC{s.Dc}", $"Check {s.Skill ?? s.Ability} DC{s.Dc}"),
        "random" => T("どれか1つ", "Random one"),
        "combat" => T("戦闘 ", "Combat ") + (s.Boss ?? string.Join("・", s.Groups.Select(g => $"{g.Monster}×{g.Count}"))),
        "gold" or "xp" or "light" or "counter" or "counter_set" => $"{PartNames.Name(PartNames.Steps, s.Type)} {s.Id} {s.Value}",
        "give_item" or "take_item" => $"{PartNames.Name(PartNames.Steps, s.Type)} {s.Id}×{s.Count}",
        _ => $"{PartNames.Name(PartNames.Steps, s.Type)} {s.Id}",
    };

    private static string Describe(EventCondition c) => $"{(c.Not ? T("でない: ", "not: ") : "")}{PartNames.Name(PartNames.Conditions, c.Type)} {(c.Type == "facing" ? PartNames.Name(PartNames.Facings, c.Id ?? "") : c.Id)} {(c.Type == "counter" ? c.Op : "")}{(c.Type is "gold" or "level" or "counter" or "chance" or "depth" ? c.Value.ToString() : "")}";

    private static string Short(string? s) => s == null ? "" : s.Length > 28 ? s[..28] + "…" : s;

    // ---------------- 足す・動かす・消す ----------------

    /// <summary>選んでいる部品が入っている手順のならびと、その中の位置(ならびそのものを選んでいれば、最後)。</summary>
    private (List<EventStep>? List, int Index) StepTarget()
    {
        if (Current is not { } e) return (null, 0);
        if (_selected is StepList sl) return (sl.Steps, sl.Steps.Count);
        if (_selected is EventStep s && FindParent(e, s) is { } parent) return (parent, parent.IndexOf(s) + 1);
        if (_selected is EventOption o) return (o.Steps, o.Steps.Count);
        return (e.Steps, e.Steps.Count);
    }

    private void AddStep()
    {
        var (list, at) = StepTarget();
        if (list == null) return;
        var s = new EventStep { Type = ((Choice)_stepType.SelectedItem).Key };
        if (s.Type is "text" or "choice") s.Text = LocalizedText.Of("", "");
        if (s.Type == "choice") s.Options.Add(new EventOption { Label = LocalizedText.Of("Yes", "はい") });
        if (s.Type == "random") s.Options.AddRange([new EventOption(), new EventOption()]);
        list.Insert(at, s);
        _selected = s;
        Changed();
    }

    private void AddCondition()
    {
        if (Current is not { } e) return;
        var list = _selected switch
        {
            CondList cl => cl.Conds,
            EventCondition c => FindParent(e, c),
            EventOption o => o.When,
            EventStep { Type: "if" } s => s.When,
            _ => e.When,
        } ?? e.When;
        var cond = new EventCondition { Type = ((Choice)_condType.SelectedItem).Key };
        list.Add(cond);
        _selected = cond;
        Changed();
    }

    private void AddOption()
    {
        if (Current is not { } e) return;
        var step = _selected as EventStep ?? (_selected is EventOption o ? FindOwner(e, o) : null);
        if (step is not { Type: "choice" or "random" }) return;
        var opt = new EventOption { Label = step.Type == "choice" ? LocalizedText.Of("", "") : null };
        step.Options.Add(opt);
        _selected = opt;
        Changed();
    }

    private void Move(int by)
    {
        if (Current is not { } e) return;
        bool moved = _selected switch
        {
            EventStep s => MoveIn(FindParent(e, s), s, by),
            EventCondition c => MoveIn(FindParent(e, c), c, by),
            EventOption o => MoveIn(FindOwner(e, o)?.Options, o, by),
            _ => false,
        };
        if (moved) Changed();
    }

    private static bool MoveIn<T>(List<T>? list, T item, int by)
    {
        if (list == null) return false;
        int i = list.IndexOf(item), to = i + by;
        if (i < 0 || to < 0 || to >= list.Count) return false;
        list.RemoveAt(i);
        list.Insert(to, item);
        return true;
    }

    private void RemoveNode()
    {
        if (Current is not { } e) return;
        bool removed = _selected switch
        {
            EventStep s => FindParent(e, s)?.Remove(s) == true,
            EventCondition c => FindParent(e, c)?.Remove(c) == true,
            EventOption o => FindOwner(e, o)?.Options.Remove(o) == true,
            _ => false,
        };
        if (!removed) return;
        _selected = e;
        Changed();
    }

    /// <summary>手順が入っているならび。</summary>
    private static List<EventStep>? FindParent(EventDef e, EventStep target)
    {
        List<EventStep>? Walk(List<EventStep> list)
        {
            if (list.Contains(target)) return list;
            foreach (var s in list)
                foreach (var child in s.Children())
                    if (Walk(child) is { } found) return found;
            return null;
        }
        return Walk(e.Steps);
    }

    /// <summary>条件が入っているならび。</summary>
    private static List<EventCondition>? FindParent(EventDef e, EventCondition target)
    {
        if (e.When.Contains(target)) return e.When;
        List<EventCondition>? Walk(List<EventStep> list)
        {
            foreach (var s in list)
            {
                if (s.When.Contains(target)) return s.When;
                foreach (var o in s.Options) if (o.When.Contains(target)) return o.When;
                foreach (var child in s.Children())
                    if (Walk(child) is { } found) return found;
            }
            return null;
        }
        return Walk(e.Steps);
    }

    /// <summary>選択肢を持つ手順。</summary>
    private static EventStep? FindOwner(EventDef e, EventOption target)
    {
        EventStep? Walk(List<EventStep> list)
        {
            foreach (var s in list)
            {
                if (s.Options.Contains(target)) return s;
                foreach (var child in s.Children())
                    if (Walk(child) is { } found) return found;
            }
            return null;
        }
        return Walk(e.Steps);
    }

    // ---------------- 項目 ----------------

    /// <summary>迷宮に置いた標準のイベント: 中身の下見と、このシナリオに写して直すボタン(同じ id で写すので、迷宮の印はそのまま、写したものが使われる)。</summary>
    private void StandardProps(EventDef e)
    {
        _props.Children.Add(Ui.Heading(EventList.Name(e) ?? e.Id));
        _props.Children.Add(Ui.Note(T($"標準のパックのイベント({e.Id})。迷宮に置いてある。このままでは直せない。", $"A standard pack event ({e.Id}) placed in the maze. It can't be edited as is.")));
        var copy = Ui.Button(T("このシナリオに写して直す", "Copy into this scenario to edit"), () =>
        {
            if (_context.Pack is not { } p) return;
            var mine = Clone(e);
            p.Events.Add(mine);
            _context.MarkEdited();
            RefreshList(mine);
        }, T("同じ id で写す。迷宮の印はそのままで、写したもの(直したもの)が使われる", "Copied with the same id; the maze marks stay and your copy is used"));
        copy.Style = (Style)Application.Current.FindResource("AccentButton");
        _props.Children.Add(copy);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        EventPickerWindow.ShowPreview(preview, e);
        _props.Children.Add(preview);
    }

    private void ShowProps()
    {
        _props.Children.Clear();
        switch (_selected)
        {
            case EventDef e: EventProps(e); break;
            case EventStep s: StepProps(s); break;
            case EventCondition c: CondProps(c); break;
            case EventOption o: OptionProps(o); break;
            case StepList: _props.Children.Add(Ui.Note(T("手順のならび。上で種類を選んで「手順を足す」", "A list of steps. Pick a type above and press \"Add step\""))); break;
            case CondList: _props.Children.Add(Ui.Note(T("条件のならび。すべて合うときだけ起こる。上で種類を選んで「条件を足す」", "A list of conditions. It happens only when all match. Pick a type above and press \"Add condition\""))); break;
            default:
                if (SelectedStandard is { } std) StandardProps(std);
                else _props.Children.Add(Ui.Note(T("イベントを選ぶか、「足す」で作る", "Select an event, or create one with \"Add\"")));
                break;
        }
    }

    private void EventProps(EventDef e)
    {
        _props.Children.Add(Ui.Heading(T("イベント", "Event")));
        var id = Ui.Text(e.Id, _ => { });
        id.LostFocus += (_, _) => RenameEvent(e, id.Text.Trim());
        _props.Children.Add(Ui.Row("id", id, 90));
        _props.Children.Add(Ui.Row(T("起こる場所", "Place"), Combo(EventCheck.Places, e.Place, v => { e.Place = v; Changed(); RefreshNames(); }, PartNames.Places), 90));
        _props.Children.Add(Ui.Row(T("一度きり", "Once only"), Combo(["", .. EventCheck.OnceKinds], e.Once ?? "", v => { e.Once = v.Length == 0 ? null : v; Changed(false); }, PartNames.Once), 90));
        if (e.Place == "maze")
            _props.Children.Add(Ui.Row(T("起こるとき", "Trigger"), Combo(EventCheck.Triggers, e.Trigger, v => { e.Trigger = v; Changed(false); }, PartNames.Triggers), 90));
                _props.Children.Add(Ui.Row(T("題(町)", "Title (town)"), Ui.Localized(e.Title, v => { e.Title = v; Changed(false); }), 90));
        _props.Children.Add(Ui.Row(T("メモ", "Note"), Ui.Text(e.Note, v => { e.Note = v.Length == 0 ? null : v; Changed(false); }), 90));
        // イベントのあいだ流す曲(このシナリオの曲と、標準の曲。空なら変えない)
        var songs = new List<string> { "" };
        foreach (var dir in new[] { Path.Combine(_context.Pack!.Directory, "audio", "bgm"), Path.Combine(_context.ContentRoot, "srd", "audio", "bgm") })
            if (Directory.Exists(dir)) songs.AddRange(Directory.EnumerateFiles(dir, "*.mp3").Select(f => "audio/bgm/" + Path.GetFileName(f)));
        songs.AddRange(_context.Pack.Manifest.Music.Keys.Select(n => $"audio/bgm/{n}.mp3"));   // 棚の曲を割り当てた名前
        var bgm = new ComboBox { IsEditable = true, ItemsSource = songs.Distinct().Order().ToList(), Text = e.Bgm ?? "" };
        bgm.LostFocus += (_, _) => { e.Bgm = Null(bgm.Text); Changed(false); };
        bgm.SelectionChanged += (_, _) => { if (bgm.SelectedItem is string v) { e.Bgm = Null(v); Changed(false); } };
        _props.Children.Add(Ui.Row(T("曲", "Music"), bgm, 90));
        _props.Children.Add(Ui.Note(T("曲を選ぶと、イベントのあいだその曲を流し、終わったら前の曲に戻す(曲のタブで足す)", "The chosen song plays during the event, then the previous song returns (add songs in the Music tab)")));
        var scatter = new CheckBox { Content = T("迷宮に散らす(合う深さの空いたマスに、新しいゲームのたびに置く)", "Scatter in the maze (placed on free cells of a matching depth, anew each game)"), IsChecked = e.Scatter != null };
        scatter.Click += (_, _) =>
        {
            e.Scatter = scatter.IsChecked == true ? new EventScatter() : null;
            Changed(false);
            RefreshNames();
            ShowProps();
        };
        _props.Children.Add(scatter);
        if (e.Scatter is { } sc)
        {
            _props.Children.Add(Ui.Row(T("深さ(最小)", "Min depth"), Number(sc.MinDepth, v => sc.MinDepth = v), 90));
            _props.Children.Add(Ui.Row(T("深さ(最大)", "Max depth"), Number(sc.MaxDepth, v => sc.MaxDepth = v), 90));
            _props.Children.Add(Ui.Row(T("レベル(最小)", "Min level"), Number(sc.MinLevel, v => sc.MinLevel = v), 90));
            _props.Children.Add(Ui.Row(T("出やすさ", "Frequency"), Number(sc.Weight, v => sc.Weight = v), 90));
            _props.Children.Add(Ui.Row(T("置く所", "Placed at"), Combo(EventCheck.ScatterWhere, sc.Where, v => { sc.Where = v; Changed(false); }, PartNames.Where), 90));
            _props.Children.Add(Ui.Row(T("レア度", "Rarity"), Combo([.. EventScatter.RarityWeights.Keys], sc.Rarity, v => { sc.Rarity = v; Changed(false); }, PartNames.Rarity), 90));
            _props.Children.Add(Ui.Row(T("上限の数", "Max count"), Number(sc.Max, v => sc.Max = (int)v), 90));
        }
        else _props.Children.Add(Ui.Note(T("散らさないイベントは、迷宮のタブの「イベントの印」でマスに置く", "Events that are not scattered are placed on cells with the \"event mark\" in the Maze tab")));
    }

    /// <summary>手順の種類ごとに使う項目(EventDef.cs の EventStep の説明と同じ)。</summary>
    private static readonly Dictionary<string, string[]> StepFields = new()
    {
        ["text"] = ["text", "key", "speaker", "art"], ["choice"] = ["text"], ["if"] = [], ["check"] = ["skill", "ability", "dc"],
        ["random"] = ["per_place"], ["flag"] = ["id"], ["unflag"] = ["id"], ["counter"] = ["id", "value"], ["counter_set"] = ["id", "value"],
        ["give_item"] = ["id", "count"], ["take_item"] = ["id", "count"], ["treasure"] = ["count"], ["gold"] = ["value"], ["xp"] = ["value"],
        ["heal"] = ["dice"], ["damage"] = ["dice", "damage_type", "ability", "dc"], ["condition"] = ["id", "value", "ability", "dc"],
        ["cure"] = ["id"], ["light"] = ["value"], ["combat"] = ["groups", "boss"], ["open"] = ["id"], ["sound"] = ["id"], ["run"] = ["id"],
        ["town"] = [], ["end"] = [], ["again"] = [], ["ending"] = [], ["world_doc"] = ["value"],
    };

    private void StepProps(EventStep s)
    {
        _props.Children.Add(Ui.Heading(T("手順: ", "Step: ") + PartNames.Name(PartNames.Steps, s.Type)));
        _props.Children.Add(Ui.Row(T("種類", "Type"), Combo(EventStep.KnownTypes, s.Type, v => { s.Type = v; Changed(); ShowProps(); }, PartNames.Steps), 90));
        foreach (var field in StepFields.GetValueOrDefault(s.Type, []))
        {
            UIElement editor = field switch
            {
                "text" => Ui.Localized(s.Text, v => { s.Text = v; Changed(); }, multiline: true, height: 54),
                "speaker" => Ui.Localized(s.Speaker, v => { s.Speaker = v; Changed(false); }),
                "key" => Ui.Text(s.Key, v => { s.Key = Null(v); Changed(); }),
                "art" => Ui.Text(s.Art, v => { s.Art = Null(v); Changed(false); }),
                "skill" => Combo(["", .. _context.BaseData.Skills.Keys.Order()], s.Skill ?? "", v => { s.Skill = Null(v); Changed(); }),
                "ability" => Combo(["", "str", "dex", "con", "int", "wis", "cha"], s.Ability ?? "", v => { s.Ability = Null(v); Changed(); }),
                "dc" => Number(s.Dc, v => s.Dc = (int)v),
                "per_place" => Check(T("同じマスでは毎回同じものを選ぶ", "Pick the same one every time on the same cell"), s.PerPlace, v => s.PerPlace = v),
                "id" => IdBox(s),
                "value" => Number(s.Value, v => s.Value = (int)v),
                "count" => Number(s.Count, v => s.Count = (int)v),
                "dice" => Ui.Text(s.Dice, v => { s.Dice = Null(v); Changed(); }),
                "damage_type" => Ui.Text(s.DamageType, v => { s.DamageType = Null(v); Changed(false); }),
                "boss" => Ui.Text(s.Boss, v => { s.Boss = Null(v); Changed(); }),
                "groups" => GroupsBox(s.Groups),
                _ => new TextBlock(),
            };
            _props.Children.Add(Ui.Row(field, editor, 90));
        }
        _props.Children.Add(Ui.Note(StepHelp().GetValueOrDefault(s.Type, "")));
    }

    private static Dictionary<string, string> StepHelp() => new()
    {
        ["text"] = T("文を見せる(文か、文言のキー)。speaker は話し手、art はモンスターの id か画像のパス", "Shows text (a line, or a text key). speaker is the speaker, art is a monster id or an image path"),
        ["choice"] = T("問いと選択肢。選択肢は「選択肢を足す」。出す条件の合わない選択肢は出さない", "A question and choices. Add choices with \"Add choice\". Choices whose show-condition fails are hidden"),
        ["if"] = T("条件が合えば then、合わなければ else", "Runs then if the conditions match, else otherwise"),
        ["check"] = T("パーティで一番得意な人が判定する(技能か能力値)", "The party member best at it rolls (a skill or an ability)"),
        ["random"] = T("候補のどれか1つ(重みで)", "Picks one of the options (by weight)"),
        ["combat"] = T("groups は「monster×数」をカンマで(× か *)。boss に bosses.json の id。どちらも無ければこの階の敵", "groups is \"monster×count\" separated by commas (× or *). boss is an id from bosses.json. With neither, the floor's monsters appear"),
        ["gold"] = T("負なら払う(所持金の条件を選択肢の when に付ける)", "A negative value pays (put a gold condition in the choice's when)"),
        ["condition"] = T("id は状態の名前、value は分(0 なら治すまで)", "id is the condition name, value is minutes (0 lasts until cured)"),
        ["run"] = T("id のイベントを続けて行う", "Then runs the event with that id"), ["open"] = T("印 gate:<id> のマスの錠を開ける(この階)", "Opens the lock on the cell marked gate:<id> (this floor)"),
        ["again"] = T("ここで終わり、一度きりでも済んだことにしない", "Ends here without marking a once-only event as done"),
        ["ending"] = T("この世界をクリアにして町へ戻り、エンディングを流す(クランにクリアの印が付く)", "Clears this world: returns to town and plays the ending (the clan gets the clear mark)"),
        ["world_doc"] = T("この階の書き付けを1つ見せる。value は何番目か(1 から。0 なら場所ごとに抽選で、同じ階の2か所が同じになることがある)。書き付けの無い世界では else", "Shows one of this floor's notes. value picks which (from 1; 0 draws one per place, so two places on a floor may show the same). In a world without notes, else runs"),
    };

    private UIElement IdBox(EventStep s)
    {
        // 品・イベント・状態などの id。品と効果音は候補を出す
        IEnumerable<string>? items = s.Type switch
        {
            "give_item" or "take_item" => _context.BaseData.Items.Keys.Order(),
            "run" => _context.Pack?.Events.Select(e => e.Id).Concat(_context.BaseData.Events.Keys.Order()),
            _ => null,
        };
        if (items == null) return Ui.Text(s.Id, v => { s.Id = Null(v); Changed(); });
        var box = new ComboBox { IsEditable = true, ItemsSource = items.ToList(), Text = s.Id ?? "" };
        box.LostFocus += (_, _) => { s.Id = Null(box.Text); Changed(); };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string v) { s.Id = v; Changed(); } };
        return box;
    }

    /// <summary>戦闘の敵の組(1行に1組。上で種類と脅威度を絞る)。</summary>
    private UIElement GroupsBox(List<BossDef.BossGroup> groups) => GroupsEditor.Create(_context, groups, () => Changed(), boss: false);

    private void CondProps(EventCondition c)
    {
        _props.Children.Add(Ui.Heading(T("条件: ", "Condition: ") + PartNames.Name(PartNames.Conditions, c.Type)));
        _props.Children.Add(Ui.Row(T("種類", "Type"), Combo(EventCondition.KnownTypes, c.Type, v => { c.Type = v; Changed(); ShowProps(); }, PartNames.Conditions), 90));
        if (c.Type == "facing")
            _props.Children.Add(Ui.Row(T("方角", "Direction"), Combo(EventCheck.Facings, c.Id ?? "", v => { c.Id = v; Changed(); }, PartNames.Facings), 90));
        else _props.Children.Add(Ui.Row("id", Ui.Text(c.Id, v => { c.Id = Null(v); Changed(); }), 90));
        _props.Children.Add(Ui.Row("value", Number(c.Value, v => c.Value = (int)v), 90));
        _props.Children.Add(Ui.Row("count", Number(c.Count, v => c.Count = (int)v), 90));
        _props.Children.Add(Ui.Row(T("比べ方", "Compare"), Combo([">=", "<=", "==", "!=", ">", "<"], c.Op, v => { c.Op = v; Changed(); }), 90));
        _props.Children.Add(Ui.Row(T("反対", "Invert"), Check(T("合わないとき", "When not matching"), c.Not, v => c.Not = v), 90));
        _props.Children.Add(Ui.Note(T("flag/done/key: id、item: id と count、gold/level/depth: value、class/race: id、counter: id と比べ方と value、chance: value(%)、facing: 方角(迷宮でパーティが向いている方角。地図の上が北。向きを変えたときに起こすのは、イベントの「起こるとき」)、turned: 向きを変えて起きた(「起こるとき」が両方のイベントで、着いたときと分ける)",
            "flag/done/key: id; item: id and count; gold/level/depth: value; class/race: id; counter: id, compare and value; chance: value (%); facing: direction the party faces in the maze (north is up on the map; to fire on turning, set the event's Trigger); turned: fired by turning (tells turning from entering when the Trigger is both)")));
    }

    private void OptionProps(EventOption o)
    {
        _props.Children.Add(Ui.Heading(T("選択肢・候補", "Choice / option")));
        _props.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(o.Label, v => { o.Label = v; Changed(); }), 90));
        _props.Children.Add(Ui.Row(T("重み(random)", "Weight (random)"), Number(o.Weight, v => o.Weight = v), 90));
    }

    // ---------------- 部品 ----------------

    /// <summary>選ぶ欄。names があれば、日本語の名前と id を見せる(その表の順に並べる)。</summary>
    private ComboBox Combo(IEnumerable<string> items, string value, Action<string> set, IReadOnlyDictionary<string, string>? names = null)
    {
        var list = names != null ? PartNames.Choices(items, names) : items.Select(k => new Choice(k, "")).ToList();
        var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(c => c.Key == value) };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice c) set(c.Key); };
        return box;
    }

    private CheckBox Check(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.Click += (_, _) => { set(box.IsChecked == true); Changed(); };
        return box;
    }

    private TextBox Number(double value, Action<double> set) =>
        Ui.Text(value.ToString(CultureInfo.InvariantCulture), v =>
        {
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return;
            set(d);
            Changed();
        });

    private static string? Null(string v) => v.Trim().Length == 0 ? null : v.Trim();

    // ---------------- イベントの一覧 ----------------

    /// <summary>新しいイベントの id(シナリオの id + _event + 番号)。</summary>
    private static string NewEventId(ScenarioPack p)
    {
        int n = p.Events.Count + 1;
        while (p.Events.Any(e => e.Id == $"{p.Id}_event{n}")) n++;
        return $"{p.Id}_event{n}";
    }

    /// <summary>
    /// AI に頼む窓を開く。元は選んでいるイベント(このシナリオのものは置き換えられる。迷宮に置いた標準のものは写して足す)。無ければ新しく作る。
    /// baseId は確かめ用(--ai-draft): "new" なら元なし、ほかは標準のイベントのその id を元にする。
    /// </summary>
    public AiDraftWindow? AskAi(string? baseId = null)
    {
        if (_context.Pack is not { } p) return null;
        if (baseId != null) _list.SelectedIndex = -1;
        var source = baseId == null ? Current ?? SelectedStandard : _context.BaseData.Events.GetValueOrDefault(baseId);
        string id = Current?.Id ?? (source != null && p.Events.All(x => x.Id != source.Id) ? source.Id : NewEventId(p));
        var baseJson = source == null ? null : JsonNode.Parse(ScenarioPack.CleanJson(source)) as JsonObject;
        string label = source == null ? T("なし(新しく作る)", "None (write a new one)")
            : $"{EventList.Name(source) ?? source.Id}({source.Id})" + (Current == null ? T("(標準のイベント。写して足す)", " (standard event; added as a copy)") : "");
        var window = new AiDraftWindow(_context, Core.Ai.DraftWriter.Kind.Event, baseJson, label, id, null, Current != null, _context.CheckEvent, (draft, items, replace) =>
        {
            EventDef parsed;
            try { parsed = draft.Deserialize<EventDef>(ContentLibrary.JsonOptions)!; }
            catch (JsonException ex)
            {
                MessageBox.Show(T("JSON を読めません: ", "Cannot read the JSON: ") + ex.Message);
                return;
            }
            if (replace && Current is { } e && p.Events.IndexOf(e) is >= 0 and var i) p.Events[i] = parsed;
            else
            {
                if (p.Events.Any(x => x.Id == parsed.Id)) parsed.Id = NewEventId(p);
                p.Events.Add(parsed);
            }
            // イベントと一緒に書いた新しい品は、データの品に入れる(同じ id があれば置き換える。イベントはその id を指している)
            if (items.Count > 0 && p.DataLists().FirstOrDefault(x => x.Collection == "items").List is { } own)
                foreach (var item in items)
                {
                    int at = own.FindIndex(o => (string?)o["id"] == (string?)item["id"]);
                    if (at >= 0) own[at] = item;
                    else own.Add(item);
                }
            _selected = parsed;
            _context.MarkEdited();
            RefreshList(parsed);
        }) { Owner = Application.Current.MainWindow };
        window.Show();
        return window;
    }

    private void AddEvent()
    {
        if (_context.Pack is not { } p) return;
        p.Events.Add(new EventDef
        {
            Id = NewEventId(p), Place = "maze", Once = "game",
            Steps = [new EventStep { Type = "text", Text = LocalizedText.Of("Something is here.", "ここに何かがある。") }],
        });
        _context.MarkEdited();
        RefreshList(p.Events[^1]);
    }

    private void CopyEvent()
    {
        if (_context.Pack is not { } p || Current is not { } e) return;
        var copy = Clone(e);
        copy.Id = e.Id + "_copy";
        p.Events.Add(copy);
        _context.MarkEdited();
        RefreshList(p.Events[^1]);
    }

    private void CopyFromBase()
    {
        if (_context.Pack is not { } p) return;
        var dialog = new EventPickerWindow(_context.BaseData.Events.Values) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Picked is not { } e) return;
        var copy = Clone(e);
        copy.Id = p.Events.Any(x => x.Id == e.Id) ? e.Id + "_copy" : e.Id;
        p.Events.Add(copy);
        _context.MarkEdited();
        RefreshList(p.Events[^1]);
    }

    private void RemoveEvent()
    {
        if (_context.Pack is not { } p || Current is not { } e) return;
        if (MessageBox.Show(T($"イベント {e.Id} を消しますか？(マスの印は残るので、検査で見つかる)", $"Delete event {e.Id}? (Marks on cells remain and will be flagged by the check)"), T("イベント", "Events"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        int i = p.Events.IndexOf(e);
        p.Events.Remove(e);
        _context.MarkEdited();
        RefreshList(p.Events.Count == 0 ? null : p.Events[Math.Min(i, p.Events.Count - 1)]);
    }

    /// <summary>id を変える(このシナリオの迷宮の印と、run の手順と、done の条件も直す)。</summary>
    private void RenameEvent(EventDef e, string id)
    {
        if (_context.Pack is not { } p || id.Length == 0 || id == e.Id) return;
        if (p.Events.Any(x => x.Id == id))
        {
            MessageBox.Show(T("その id はもうあります", "That id already exists"));
            return;
        }
        Core.Save.ScenarioEdit.RenameEvent(p, e.Id, id);   // 迷宮の印・run・done の条件も
        Changed();
        RefreshNames();
    }

    /// <summary>一覧の名前と id を直す(選んでいるイベントのまま)。</summary>
    private void RefreshNames()
    {
        _loading = true;
        foreach (var item in _list.Items.OfType<ListBoxItem>())
            if (item.Tag is EventDef e)
            {
                item.Content = EventList.Label(e);
                item.ToolTip = e.Note;
            }
        _loading = false;
    }

    private void ApplyJson()
    {
        if (_context.Pack is not { } p || Current is not { } e) return;
        try
        {
            var parsed = JsonSerializer.Deserialize<EventDef>(_json.Text, EventDraftCheck.Strict)!;
            int i = p.Events.IndexOf(e);
            p.Events[i] = parsed;
            _selected = parsed;
            _context.MarkEdited();
            RefreshList(parsed);
        }
        catch (JsonException ex)
        {
            _jsonError.Text = T("JSON を読めません: ", "Cannot read the JSON: ") + ex.Message;
        }
    }

    private static EventDef Clone(EventDef e) => JsonSerializer.Deserialize<EventDef>(ScenarioPack.CleanJson(e), ContentLibrary.JsonOptions)!;
}
