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
        leftTop.Children.Add(Ui.Button(T("AI で新しく作る…", "New with AI…"), () => AskAi(fresh: true), T("元にするイベントなしで、こうしたいことだけを書いて AI に下書きさせる", "Have the AI draft a brand-new event from your request alone")));
        leftTop.Children.Add(Ui.Button(T("AI で直す…", "Rework with AI…"), () => AskAi(), T("選んでいるイベントを元に(無ければ新しく)、こうしたいことを書いて AI に下書きさせる", "Have the AI draft an event from the selected one (or a new one) and your request")));
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
        // 足すのは、木で枝や部品を選ぶと右に出るボタンで(その場所に足せるものだけを出す)
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        tools.Children.Add(new TextBlock { Text = T("木で選ぶと、そこに足せるものが右に出る", "Select in the tree to see what can be added there, on the right"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
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

    /// <summary>確かめ用(--event): そのイベントを選び、木を上から数えて index 番目(0 が根)を選ぶ。</summary>
    public void SelectForCheck(string eventId, int index)
    {
        if (_context.Pack?.Events.FirstOrDefault(e => e.Id == eventId) is { } e) RefreshList(e);
        var flat = new List<TreeViewItem>();
        void Walk(ItemCollection items)
        {
            foreach (TreeViewItem i in items)
            {
                flat.Add(i);
                Walk(i.Items);
            }
        }
        Walk(_tree.Items);
        if (index >= 0 && index < flat.Count) flat[index].IsSelected = true;
    }

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
            var root = Item(T($"イベント {e.Id}", $"Event {e.Id}") + (HowItHappens(e) is { Length: > 0 } how ? "  —  " + how : ""), e);
            root.Items.Add(Conds(T("起こる条件", "Conditions"), e.When));
            root.Items.Add(Steps(T("イベントの流れ(上から順に)", "Event flow (in order)"), e.Steps));
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
        int n = 0;
        foreach (var s in steps)
        {
            var child = Step(s);
            child.Header = $"{++n}. {child.Header}";
            item.Items.Add(child);
        }
        return item;
    }

    private TreeViewItem Conds(string label, List<EventCondition> conds)
    {
        var item = Item(label + (conds.Count == 0 ? T(" なし", " (none)") : ""), new CondList(label, conds));
        foreach (var c in conds) item.Items.Add(Item(_words.Condition(c), c));
        return item;
    }

    private TreeViewItem Step(EventStep s)
    {
        var item = Item(Describe(s), s);
        switch (s.Type)
        {
            case "if":
                item.Items.Add(Conds(T("条件", "Conditions"), s.When));
                item.Items.Add(Steps(T("条件を満たすとき", "When met"), s.Then));
                item.Items.Add(Steps(T("それ以外の場合", "Otherwise"), s.Else));
                break;
            case "check":
                item.Items.Add(Steps(T("成功したら", "On success"), s.Success));
                item.Items.Add(Steps(T("失敗したら", "On failure"), s.Fail));
                break;
            case "combat":
                item.Items.Add(Steps(T("勝ったら", "If won"), s.Win));
                item.Items.Add(Steps(T("逃げたら", "If fled"), s.Lose));
                break;
            case "world_doc":
                item.Items.Add(Steps(T("書き付けが無い世界では", "In a world without notes"), s.Else));
                break;
            case "pick":
                item.Items.Add(Conds(T("選べる人の条件", "Who can be chosen"), s.When));
                item.Items.Add(Steps(T("選んだら", "Once chosen"), s.Then));
                item.Items.Add(Steps(T("選べる人がいない・やめたら", "If nobody / cancelled"), s.Else));
                break;
            case "spend_slot":
                item.Items.Add(Steps(T("使えたら", "If spent"), s.Then));
                item.Items.Add(Steps(T("残っていなければ", "If none left"), s.Else));
                break;
            case "choice" or "random":
                foreach (var o in s.Options)
                {
                    var oi = Item(s.Type == "choice" ? T($"選択肢「{Loc.Name(o.Label)}」", $"Choice \"{Loc.Name(o.Label)}\"") : T($"候補(重み {o.Weight:0.##})", $"Option (weight {o.Weight:0.##})"), o);
                    if (s.Type == "choice") oi.Items.Add(Conds(T("この選択肢を出す条件", "Show if"), o.When));
                    oi.Items.Add(Steps(s.Type == "choice" ? T("選んだら", "If picked") : T("この候補なら", "For this option"), o.Steps));
                    item.Items.Add(oi);
                }
                break;
        }
        return item;
    }

    private string Describe(EventStep s) => s.Type switch
    {
        "text" => (Loc.Name(s.Speaker) is { Length: > 0 } who ? T($"文章({who}): ", $"Text ({who}): ") : T("文章: ", "Text: ")) + Short(Loc.Name(s.Text) ?? s.Key),
        "choice" => T($"選択肢: {Short(Loc.Name(s.Text))}", $"Choice: {Short(Loc.Name(s.Text))}"),
        "if" => T("条件分岐", "Conditional branch"),
        "check" => T($"判定: {SkillOrAbility(s)} DC{s.Dc}", $"Check: {SkillOrAbility(s)} DC{s.Dc}"),
        "random" => T("ランダム分岐", "Random branch"),
        "combat" => T("戦闘: ", "Combat: ") + (s.Boss != null ? T($"ボス {s.Boss}", $"boss {s.Boss}") : s.Groups.Count == 0 ? T("この階の敵", "this floor's enemies")
            : string.Join("・", s.Groups.Select(g => $"{MonsterName(g.Monster)}×{g.Count}"))),
        "pick" => T($"一人を選ぶ{(s.Value == 1 ? "(くじ)" : "")}: {Short(Loc.Name(s.Text))}", $"Choose one{(s.Value == 1 ? " (by lot)" : "")}: {Short(Loc.Name(s.Text))}"),
        "spend_slot" => T($"呪文スロットを1つ捧げる(レベル {Math.Max(1, s.Value)} 以上)", $"Give up a spell slot (level {Math.Max(1, s.Value)}+)"),
        "world_doc" => T("書き付けを読む", "Read a note"),
        "if" or "random" or "end" or "again" or "town" or "ending" => PartNames.Name(PartNames.Steps, s.Type),
        _ => _words.Step(s),   // 入れ子の無い手順は、文で(「全員に 1d4 のダメージ」など)
    };

    /// <summary>このイベントがどこで・いつ・何回起こるか(木のいちばん上に出す)。</summary>
    private string HowItHappens(EventDef e)
    {
        string where;
        if (e.Place == "town") where = T("町で選んだとき", "when chosen in town");
        else if (e.Scatter != null) where = T("迷宮に散らしたマス", "scattered cells");
        else
        {
            var cells = (_context.Pack?.Floors ?? []).SelectMany((f, i) => f.AllCells().Where(c => f[c].Mark == EventPlacer.MarkPrefix + e.Id).Select(c => $"B{i + 1} ({c.X},{c.Y})")).ToList();
            where = cells.Count == 0 ? T("迷宮のどこにも置いていない(迷宮のタブで印を置く)", "not placed in the maze (place a mark on the Maze tab)")
                : cells.Count <= 2 ? string.Join(T("と", ", "), cells) : T($"{cells.Count} か所のマス", $"{cells.Count} cells");
        }
        string when = e.Place == "town" ? "" : string.Join(T("と", " or "), new[]
        {
            e.OnEnter ? T("着いたとき", "on entering") : null, e.OnTurn ? T("向きを変えたとき", "on turning") : null,
            e.OnCast ? T("呪文を唱えたとき", "on casting") : null, e.OnRest ? T("休んだとき", "on resting") : null,
        }.OfType<string>());
        string times = e.Once switch { "game" => T("世界で一度", "once per world"), "place" => T("そのマスで一度", "once per cell"), _ => T("何度でも", "any number of times") };
        return string.Join(T("・", ", "), new[] { when.Length > 0 ? $"{where}: {when}" : where, times }.Where(x => x.Length > 0));
    }

    private string SkillOrAbility(EventStep s) => s.Skill != null ? Loc.Name(_context.BaseData.Skills.GetValueOrDefault(s.Skill)?.Name) ?? s.Skill
        : PartNames.Name(PartNames.Abilities, s.Ability ?? "");

    private string MonsterName(string id) => Loc.Name(_context.BaseData.Monsters.GetValueOrDefault(id)?.Name) ?? id;

    /// <summary>木の行の文(品やモンスターは名前で)。</summary>
    private DraftSummary _words => _summary ??= new DraftSummary(_context);
    private DraftSummary? _summary;


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

    private void AddStep(string type)
    {
        var (list, at) = StepTarget();
        if (list == null) return;
        var s = new EventStep { Type = type };
        if (s.Type is "text" or "choice") s.Text = LocalizedText.Of("", "");
        if (s.Type == "choice") s.Options.Add(new EventOption { Label = LocalizedText.Of("Yes", "はい") });
        if (s.Type == "random") s.Options.AddRange([new EventOption(), new EventOption()]);
        if (s.Type == "pick") s.Text = LocalizedText.Of("Who?", "誰が?");
        list.Insert(at, s);
        _selected = s;
        Changed();
    }

    private void AddCondition(string type)
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
        var cond = new EventCondition { Type = type };
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
        // 選択肢を選んでいれば、その後ろに足す
        int at = _selected is EventOption sel && step.Options.IndexOf(sel) is >= 0 and var i ? i + 1 : step.Options.Count;
        step.Options.Insert(at, opt);
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
            case EventStep s:
                StepProps(s);
                if (s.Type is "choice" or "random") _props.Children.Add(OptionButton(s.Type == "choice"));
                _props.Children.Add(Palette(T("この後ろに足す", "Add after this"), StepButtons(), open: false));
                break;
            case EventCondition c:
                CondProps(c);
                _props.Children.Add(Palette(T("条件をもう1つ足す(すべて合うときだけ)", "Add another condition (all must match)"), CondButtons(), open: false));
                break;
            case EventOption o:
                OptionProps(o);
                _props.Children.Add(OptionButton(o.Label != null));
                _props.Children.Add(Ui.Note(T("この選択肢の中身は、木で下の「出す条件」「手順」を選んで足す", "To fill this choice, select its \"Show if\" or \"Steps\" below it in the tree")));
                break;
            case StepList sl:
                _props.Children.Add(Ui.Heading(sl.Label));
                _props.Children.Add(Palette(T("ここに足す", "Add here"), StepButtons(), open: true));
                break;
            case CondList cl:
                _props.Children.Add(Ui.Heading(cl.Label));
                _props.Children.Add(Ui.Note(T("条件がすべて合うときだけ起こる(条件なしなら、いつでも)", "Happens only when all conditions match (always, if there are none)")));
                _props.Children.Add(Palette(T("ここに条件を足す", "Add a condition here"), CondButtons(), open: true));
                break;
            default:
                if (SelectedStandard is { } std) StandardProps(std);
                else _props.Children.Add(Ui.Note(T("イベントを選ぶか、「足す」で作る", "Select an event, or create one with \"Add\"")));
                break;
        }
    }

    /// <summary>足せる部品のボタンの束(open なら最初から開く。部品を選んでいるときは、たたんで出す)。</summary>
    private static UIElement Palette(string title, UIElement buttons, bool open)
    {
        var box = new Expander { Header = title, IsExpanded = open, Content = buttons, Margin = new Thickness(0, 12, 0, 0), FontWeight = FontWeights.SemiBold };
        if (buttons is FrameworkElement fe) fe.SetValue(Control.FontWeightProperty, FontWeights.Normal);
        return box;
    }

    /// <summary>手順を足す: まず組を選び、その組の部品を選ぶと説明が出て、「足す」で選んでいるところ(枝なら最後、部品ならその後ろ)に足す。</summary>
    private UIElement StepButtons()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var parts = new ComboBox();
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Dim, Margin = new Thickness(0, 4, 0, 4) };
        var groups = new ComboBox { ItemsSource = PartNames.StepGroups.Select(g => T(g.Ja, g.En)).ToList(), SelectedIndex = _stepGroup };
        groups.SelectionChanged += (_, _) =>
        {
            if (groups.SelectedIndex < 0) return;
            _stepGroup = groups.SelectedIndex;
            parts.ItemsSource = PartNames.Choices(PartNames.StepGroups[_stepGroup].Keys, PartNames.Steps);
            parts.SelectedIndex = 0;
        };
        parts.ItemsSource = PartNames.Choices(PartNames.StepGroups[_stepGroup].Keys, PartNames.Steps);
        parts.SelectionChanged += (_, _) => help.Text = parts.SelectedItem is Choice c ? StepHelp().GetValueOrDefault(c.Key, "") : "";
        parts.SelectedIndex = 0;
        panel.Children.Add(Ui.Row(T("組", "Group"), groups, 80));
        panel.Children.Add(Ui.Row(T("部品", "Part"), parts, 80));
        panel.Children.Add(Ui.Row("", help, 80));
        panel.Children.Add(Ui.Row("", Ui.Button(T("足す", "Add"), () => { if (parts.SelectedItem is Choice c) AddStep(c.Key); }), 80));
        return panel;
    }

    /// <summary>手順を足すときに選んでいる組(足したあとも覚えておく)。</summary>
    private int _stepGroup;

    /// <summary>条件を足す: 種類を選んで「足す」。足した条件を選ぶと、その種類に合った入力が出る。</summary>
    private UIElement CondButtons()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        string type = "flag";
        panel.Children.Add(Ui.Row(T("種類", "Type"), Combo(PartNames.Conditions.Keys, type, v => type = v, PartNames.Conditions), 80));
        panel.Children.Add(Ui.Row("", Ui.Button(T("足す", "Add"), () => AddCondition(type)), 80));
        return panel;
    }

    private UIElement OptionButton(bool choice) =>
        new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { Ui.Button(choice ? T("選択肢をもう1つ足す", "Add another choice") : T("候補をもう1つ足す", "Add another option"), AddOption) } };

    private void EventProps(EventDef e)
    {
        _props.Children.Add(Ui.Heading(T("イベント", "Event")));
        var id = Ui.Text(e.Id, _ => { });
        id.LostFocus += (_, _) => RenameEvent(e, id.Text.Trim());
        _props.Children.Add(Ui.Row("id", id, 90));
        // 名前(一覧に出す。町のイベントでは、町で選ぶときの題にもなる)。空なら、メモか最初の文を名前の代わりに出す
        _props.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(e.Title, v => { e.Title = v; Changed(false); RefreshNames(); }), 90));
        _props.Children.Add(Ui.Row("", Ui.Note(T("一覧に出す名前。町のイベントでは、町で選ぶときの題にもなる。空ならメモか最初の文を出す",
            "The name shown in the list. For town events it is also the title chosen in town. Empty: the note or first line is shown")), 90));
        _props.Children.Add(Ui.Row(T("起こる場所", "Place"), Combo(EventCheck.Places, e.Place, v => { e.Place = v; Changed(); RefreshNames(); }, PartNames.Places), 90));
        _props.Children.Add(Ui.Row(T("一度きり", "Once only"), Combo(["", .. EventCheck.OnceKinds], e.Once ?? "", v => { e.Once = v.Length == 0 ? null : v; Changed(false); }, PartNames.Once), 90));
        if (e.Place == "maze")
            _props.Children.Add(Ui.Row(T("起こるとき", "Trigger"), Combo(EventCheck.Triggers, e.Trigger, v => { e.Trigger = v; Changed(false); }, PartNames.Triggers), 90));
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

    private void StepProps(EventStep s)
    {
        _props.Children.Add(Ui.Heading(T("流れ: ", "Flow: ") + PartNames.Name(PartNames.Steps, s.Type)));
        _props.Children.Add(Ui.Row(T("種類", "Type"), Combo(EventStep.KnownTypes, s.Type, v => { s.Type = v; Changed(); ShowProps(); }, PartNames.Steps), 110));
        if (StepHelp().GetValueOrDefault(s.Type) is { Length: > 0 } help) _props.Children.Add(Ui.Note(help));
        foreach (var (label, editor) in StepFieldsFor(s)) _props.Children.Add(Ui.Row(label, editor, 110));
    }

    /// <summary>
    /// 手順の種類ごとの入力(名前と欄)。選んだ種類に関係する欄だけを出す。数や名前の多いもの(品・呪文・判定)は、
    /// 上の欄で絞ってから選ぶ(種類 → 品、レベル → 呪文、技能か能力値か → どれ)。選び方で欄が変わるもの(払う・もらう、ボスか決めた敵か)は、
    /// まず選び方を出す。
    /// </summary>
    private IEnumerable<(string Label, UIElement Editor)> StepFieldsFor(EventStep s)
    {
        UIElement Num(int value, Action<int> set) => Number(value, v => set((int)v));
        UIElement Id(IEnumerable<Choice> choices) => IdCombo(choices, s.Id, v => s.Id = v);
        UIElement Text(LocalizedText? value, Action<LocalizedText?> set, bool multi = true) =>
            Ui.Localized(value, v => { set(v); Changed(); }, multiline: multi, height: multi ? 54 : 0);
        switch (s.Type)
        {
            case "text":
                yield return (T("文", "Text"), Text(s.Text, v => s.Text = v));
                yield return (T("話す人", "Speaker"), Text(s.Speaker, v => s.Speaker = v, multi: false));
                yield return (T("絵", "Picture"), IdCombo(MonsterChoices(), s.Art, v => s.Art = v));
                yield return (T("文言のキー", "Text key"), Ui.Text(s.Key, v => { s.Key = Null(v); Changed(); }));
                break;
            case "choice":
                yield return (T("問い", "Question"), Text(s.Text, v => s.Text = v));
                break;
            case "pick":
                yield return (T("問い", "Question"), Text(s.Text, v => s.Text = v, multi: false));
                yield return (T("選び方", "How"), Radio([("0", T("自分で選ぶ", "Player chooses")), ("1", T("くじ(画面を出さない)", "By lot (no menu)"))],
                    s.Value == 1 ? "1" : "0", v => s.Value = int.Parse(v)));
                break;
            case "random":
                yield return ("", Check(T("同じマスでは毎回同じものを選ぶ(壁の文字など)", "Same pick every time on the same cell (wall writing etc.)"), s.PerPlace, v => s.PerPlace = v));
                break;
            case "check":
            {
                bool bySkill = s.Ability == null;
                yield return (T("何で判定", "Check with"), Radio([("skill", T("技能", "Skill")), ("ability", T("能力値", "Ability"))], bySkill ? "skill" : "ability", v =>
                {
                    if (v == "skill") { s.Ability = null; s.Skill ??= "perception"; }
                    else { s.Skill = null; s.Ability ??= "wis"; }
                }, rebuild: true));
                if (bySkill) yield return (T("技能", "Skill"), IdCombo(_context.BaseData.Skills.Values.Select(x => new Choice(x.Id, Loc.Name(x.Name) ?? x.Id)), s.Skill, v => s.Skill = v));
                else yield return (T("能力値", "Ability"), Combo(PartNames.Abilities.Keys, s.Ability ?? "wis", v => { s.Ability = v; Changed(); }, PartNames.Abilities));
                yield return (T("難しさ(DC)", "Difficulty (DC)"), Num(s.Dc, v => s.Dc = v));
                break;
            }
            case "flag" or "unflag":
                yield return (T("フラグ", "Flag"), Id(Known("flag")));
                break;
            case "counter" or "counter_set":
                yield return (T("変数", "Variable"), Id(Known("counter")));
                yield return (s.Type == "counter" ? T("増やす数(負なら減らす)", "Add (negative subtracts)") : T("代入する値", "Value"), Num(s.Value, v => s.Value = v));
                break;
            case "give_item" or "take_item":
                yield return (T("品", "Item"), ItemPicker(s.Id, v => s.Id = v));
                yield return (T("いくつ", "How many"), Num(Math.Max(1, s.Count), v => s.Count = v));
                break;
            case "treasure":
                yield return (T("いくつ", "How many"), Num(Math.Max(1, s.Count), v => s.Count = v));
                break;
            case "gold":
                yield return (T("向き", "Direction"), Radio([("get", T("もらう", "Receive")), ("pay", T("払う", "Pay"))], s.Value < 0 ? "pay" : "get",
                    v => s.Value = v == "pay" ? -Math.Abs(s.Value) : Math.Abs(s.Value)));
                yield return (T("金貨", "Gold"), Num(Math.Abs(s.Value), v => s.Value = s.Value < 0 ? -Math.Abs(v) : Math.Abs(v)));
                break;
            case "xp":
                yield return (T("経験値", "XP"), Num(s.Value, v => s.Value = v));
                break;
            case "heal":
                yield return (T("どれだけ", "How much"), Radio([("full", T("全回復", "Fully")), ("dice", T("ダイス", "Dice"))], s.Dice == null ? "full" : "dice",
                    v => s.Dice = v == "full" ? null : s.Dice ?? "2d8", rebuild: true));
                if (s.Dice != null) yield return (T("ダイス", "Dice"), Ui.Text(s.Dice, v => { s.Dice = Null(v); Changed(); }));
                break;
            case "damage":
                yield return (T("ダイス", "Dice"), Ui.Text(s.Dice ?? "1d6", v => { s.Dice = Null(v); Changed(); }));
                yield return (T("ダメージの種類", "Damage type"), Combo(["", .. Core.Effects.DamageTypes.All], s.DamageType ?? "", v => { s.DamageType = Null(v); Changed(); }, DamageNames()));
                foreach (var row in SaveRows(s, T("セーヴで半分", "Save for half"))) yield return row;
                break;
            case "condition":
                yield return (T("状態", "Condition"), Id(ConditionChoices()));
                yield return (T("続く分", "Minutes"), Num(s.Value, v => s.Value = v));
                foreach (var row in SaveRows(s, T("セーヴで防げる", "Save to resist"))) yield return row;
                break;
            case "cure":
                yield return (T("治す状態", "Condition"), Id(ConditionChoices()));
                break;
            case "light":
                yield return (T("明かり", "Light"), Radio([("on", T("つける", "Light")), ("off", T("消す", "Put out"))], s.Value > 0 ? "on" : "off",
                    v => s.Value = v == "on" ? Math.Max(1, s.Value > 0 ? s.Value : 60) : 0, rebuild: true));
                if (s.Value > 0) yield return (T("何分", "Minutes"), Num(s.Value, v => s.Value = v));
                break;
            case "spend_slot":
                yield return (T("レベル以上", "Level at least"), Num(Math.Max(1, s.Value), v => s.Value = v));
                break;
            case "combat":
            {
                string mode = s.Boss != null ? "boss" : s.Groups.Count > 0 ? "groups" : "floor";
                yield return (T("相手", "Enemies"), Radio([("floor", T("この階の敵", "This floor's")), ("groups", T("決めた敵", "Chosen")), ("boss", T("ボス", "Boss"))], mode, v =>
                {
                    s.Boss = v == "boss" ? s.Boss ?? BossChoices().FirstOrDefault()?.Key ?? "final" : null;
                    if (v != "groups") s.Groups.Clear();
                    else if (s.Groups.Count == 0) s.Groups.Add(new Core.Monsters.BossDef.BossGroup { Monster = "goblin", Count = 2 });
                }, rebuild: true));
                if (mode == "groups") yield return (T("敵の組", "Groups"), GroupsBox(s.Groups));
                if (mode == "boss") yield return (T("ボス", "Boss"), IdCombo(BossChoices(), s.Boss, v => s.Boss = v));
                break;
            }
            case "open" or "close":
                yield return (T("門", "Gate"), Id(MarkChoices("gate:")));
                break;
            case "warp":
                yield return (T("着地点", "Landing point"), Id(MarkChoices("dest:")));
                break;
            case "sound":
                yield return (T("効果音", "Sound"), Id(SoundChoices()));
                break;
            case "run":
                yield return (T("イベント", "Event"), Id(EventChoices(maze: false).Concat(_context.BaseData.Events.Values.Select(e => new Choice(e.Id, EventList.Name(e) ?? ""))).DistinctBy(c => c.Key)));
                break;
            case "timer":
                yield return (T("起こすイベント", "Event to run"), Id(EventChoices(maze: true)));
                yield return (T("何歩あと", "After how many steps"), Num(Math.Max(1, s.Value), v => s.Value = v));
                break;
            case "timer_stop":
                yield return (T("タイマー", "Timer"), Id(EventChoices(maze: true)));
                break;
            case "world_doc":
                yield return (T("どれを見せる", "Which"), Radio([("0", T("場所ごとに抽選", "Drawn per place")), ("n", T("番号で", "By number"))], s.Value > 0 ? "n" : "0",
                    v => s.Value = v == "n" ? Math.Max(1, s.Value) : 0, rebuild: true));
                if (s.Value > 0) yield return (T("何番目", "Number"), Num(s.Value, v => s.Value = v));
                break;
        }
    }

    /// <summary>セーヴの欄(セーヴがあるか → 能力値と難しさ)。</summary>
    private IEnumerable<(string, UIElement)> SaveRows(EventStep s, string label)
    {
        yield return ("", Check(label, s.Ability != null, v => s.Ability = v ? s.Ability ?? "dex" : null, rebuild: true));
        if (s.Ability == null) yield break;
        yield return (T("能力値", "Ability"), Combo(PartNames.Abilities.Keys, s.Ability, v => { s.Ability = v; Changed(); }, PartNames.Abilities));
        yield return (T("難しさ(DC)", "Difficulty (DC)"), Number(s.Dc, v => s.Dc = (int)v));
    }

    /// <summary>いくつかから1つを選ぶ(ラジオボタン)。rebuild なら、選んだあとで欄を出し直す(選び方で出る欄が変わるとき)。</summary>
    private UIElement Radio(IEnumerable<(string Key, string Label)> options, string value, Action<string> set, bool rebuild = false)
    {
        var row = new WrapPanel();
        string group = Guid.NewGuid().ToString("N");
        foreach (var (key, label) in options)
        {
            var b = new RadioButton { Content = label, GroupName = group, IsChecked = key == value, Margin = new Thickness(0, 2, 14, 2) };
            b.Checked += (_, _) =>
            {
                set(key);
                Changed();
                if (rebuild) ShowProps();
            };
            row.Children.Add(b);
        }
        return row;
    }

    /// <summary>品を選ぶ: 種類で絞ってから品(名前で)。</summary>
    private UIElement ItemPicker(string? value, Action<string?> set)
    {
        var panel = new StackPanel();
        var kinds = new List<Choice> { new("", T("すべて", "All")) };
        kinds.AddRange(Enum.GetValues<Core.Items.ItemKind>().Select(k => new Choice(k.ToString(), ItemKindName(k))));
        var items = new ComboBox();
        var current = value != null && _context.BaseData.Items.TryGetValue(value, out var def) ? def.Kind.ToString() : "";
        var kind = new ComboBox { ItemsSource = kinds, SelectedItem = kinds.FirstOrDefault(k => k.Key == current) ?? kinds[0], DisplayMemberPath = "Label", Margin = new Thickness(0, 0, 0, 2) };
        void Fill()
        {
            string k = (kind.SelectedItem as Choice)?.Key ?? "";
            var list = _context.BaseData.Items.Values.Where(i => k.Length == 0 || i.Kind.ToString() == k)
                .Select(i => new Choice(i.Id, Loc.Name(i.Name) ?? i.Id)).OrderBy(c => c.Label, StringComparer.CurrentCulture).ToList();
            items.ItemsSource = list;
            items.SelectedItem = list.FirstOrDefault(c => c.Key == value);
        }
        kind.SelectionChanged += (_, _) => Fill();
        items.SelectionChanged += (_, _) => { if (items.SelectedItem is Choice c && c.Key != value) { set(c.Key); value = c.Key; Changed(); } };
        Fill();
        panel.Children.Add(kind);
        panel.Children.Add(items);
        return panel;
    }

    private static string ItemKindName(Core.Items.ItemKind k) => k switch
    {
        Core.Items.ItemKind.Weapon => T("武器", "Weapons"),
        Core.Items.ItemKind.Armor => T("鎧", "Armor"),
        Core.Items.ItemKind.Shield => T("盾", "Shields"),
        Core.Items.ItemKind.Backpack => T("背負い袋", "Packs"),
        Core.Items.ItemKind.Consumable => T("消耗品", "Consumables"),
        Core.Items.ItemKind.Gear => T("道具", "Gear"),
        Core.Items.ItemKind.Accessory => T("装身具", "Accessories"),
        Core.Items.ItemKind.Key => T("大事な品", "Key items"),
        Core.Items.ItemKind.Ammo => T("矢弾", "Ammo"),
        _ => k.ToString(),
    };

    private IEnumerable<Choice> MonsterChoices() => _context.BaseData.Monsters.Values.Select(m => new Choice(m.Id, Loc.Name(m.Name) ?? m.Id));

    private IEnumerable<Choice> ConditionChoices() => Enum.GetValues<GravewardSpiral.Core.Effects.Condition>()
        .Select(x => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(x.ToString())).Select(id => new Choice(id, ConditionWord(id)));

    private IReadOnlyDictionary<string, string> DamageNames()
    {
        var summary = new DraftSummary(_context);
        return new PartTable<string>([("", "なし", "none"), .. Core.Effects.DamageTypes.All.Select(t => (t, summary.DamageType(t), summary.DamageType(t)))]);
    }

    /// <summary>このシナリオのボス(最下層の主 final と、足したボス)。</summary>
    private IEnumerable<Choice> BossChoices()
    {
        yield return new Choice("final", T("最下層の主", "Final boss"));
        foreach (var b in _context.Pack?.Bosses ?? [])
            if ((string?)b["id"] is { } id && id != "final") yield return new Choice(id, (string?)b["name"]?[Loc.En ? "en" : "ja"] ?? "");
    }

    /// <summary>迷宮に置いた印(門 gate: / 着地点 dest:)の名前。</summary>
    private IEnumerable<Choice> MarkChoices(string prefix) => (_context.Pack?.Floors ?? []).SelectMany((f, i) => f.AllCells().Select(c => (f[c].Mark, i)))
        .Where(m => m.Mark?.StartsWith(prefix) == true).GroupBy(m => m.Mark![prefix.Length..])
        .Select(g => new Choice(g.Key, T($"B{g.First().i + 1}", $"B{g.First().i + 1}")));

    /// <summary>効果音(標準のパックとこのシナリオの audio/se)。</summary>
    private IEnumerable<Choice> SoundChoices()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var dir in new[] { Path.Combine(_context.ContentRoot, "srd", "audio", "se"), _context.Pack is { } p ? Path.Combine(p.Directory, "audio", "se") : "" })
            if (dir.Length > 0 && Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*.wav")) names.Add(Path.GetFileNameWithoutExtension(f));
        return names.Select(n => new Choice(n, ""));
    }

    private static Dictionary<string, string> StepHelp() => new()
    {
        ["text"] = T("プレイヤーの画面に文章を1つ出す(1画面)。決定を押すと、流れの次へ進む。続けて2つ置けば、2画面に分けて出る。話す人を入れると「話す人: 文」の形になる(空なら地の文)。絵はモンスターを選ぶ。文言のキーは、文の代わりに文言のタブの文を使うとき", "Shows text. With a speaker it reads \"Speaker: line\" (empty: narration). Picture is a monster. A text key uses a line from the Text tab instead"),
        ["choice"] = T("プレイヤーの画面に問いと選択肢を出し、選んだものの「選んだら」の流れへ進む。選択肢を選ぶと「選択肢をもう1つ足す」。出す条件の合わない選択肢は出さない", "A question and choices. Select a choice to add another. Choices whose show-condition fails are hidden"),
        ["world_doc"] = T("この階の書き付け(AI か世界の文)を1つ見せる。番号でなら、同じ階の2か所に別々のものを出せる。書き付けの無い世界では「でなければ」の手順", "Shows one of this floor's notes. By number, two places on a floor can show different ones. In a world without notes, the else steps run"),
        ["if"] = T("条件に当てはまれば「条件を満たすとき」、満たさなければ「それ以外の場合」の流れへ。条件は木の「条件」を選んで足す", "Runs then if the conditions match, else otherwise. Add conditions by selecting \"If\" in the tree"),
        ["check"] = T("パーティで一番得意な人が判定する(一人を選んだあとは、その人)。成功と失敗の手順は木で足す", "The member best at it rolls (or the chosen one). Add success and failure steps in the tree"),
        ["random"] = T("候補のどれか1つ(重みで)。候補を選ぶと重みを変えられる", "Picks one of the options by weight. Select an option to change its weight"),
        ["run"] = T("ほかのイベントの流れを、続けて起こす", "Then runs another event's flow"),
        ["timer"] = T("何歩かあとに、選んだ迷宮のイベントを、そのとき迷宮のどこにいても起こす。もう一度置くと数え直す。休むと進み、町では止まる", "After some steps, runs the chosen maze event wherever the party is. Setting it again restarts it. Resting advances it; it pauses in town"),
        ["timer_stop"] = T("タイマーを止める(時が来ても何も起こらない)", "Stops the timer (nothing happens when it would have run out)"),
        ["end"] = T("ここで終わる", "Ends here"),
        ["again"] = T("ここで終わり、一度きりのイベントでも済んだことにしない(立ち去る、など。また来れば起こる)", "Ends here without marking a once-only event as done (walking away, etc.)"),
        ["town"] = T("町へ戻る", "Returns to town"),
        ["ending"] = T("この世界をクリアにして町へ戻り、エンディングを流す(クランにクリアの印が付く)", "Clears this world: returns to town and plays the ending (the clan gets the clear mark)"),
        ["flag"] = T("フラグを立てる。ほかのイベントの条件「フラグが立っている」で見る", "Sets a flag, checked by the \"Flag is set\" condition"),
        ["unflag"] = T("フラグを消す", "Clears a flag"),
        ["counter"] = T("変数に足す(負なら引く)。条件「変数を比べる」で見る", "Adds to a counter (negative subtracts), checked by \"Compare counter\""),
        ["counter_set"] = T("変数に値を代入する", "Sets a counter to a value"),
        ["give_item"] = T("品を渡す(動ける人の持ち物へ。持ちきれない大事な品はクランハウスへ)", "Gives an item (to an able member; key items that do not fit go to the clan house)"),
        ["take_item"] = T("品を取る(足りなければ、あるだけ。装備している分も取る)", "Takes an item (as many as there are, equipped ones included)"),
        ["treasure"] = T("この階の宝の表から、いくつか渡す", "Gives some treasure from this floor's table"),
        ["gold"] = T("金貨をもらう・払う。払う選択肢には、出す条件「お金がある」を付けると、足りないときに出ない", "Receive or pay gold. Give a paying choice the \"Has gold\" show-condition so it is hidden when short"),
        ["xp"] = T("動ける人それぞれに経験値", "XP to each able member"),
        ["pick"] = T("一人を選ぶ。選べる人の条件は木の「選べる人」で(一人ずつ見る)。くじなら画面を出さない。選んだら「選んだら」、いないか「やめておく」なら「いない・やめた」(手順があるときだけ「やめておく」を出す)。選んだあとは、回復・ダメージ・状態・品・判定と、人を見る条件がその人だけになる", "Choose one member. Who can be chosen is set under it in the tree. By lot shows no menu. After choosing, heal, damage, conditions, items, checks and per-member conditions apply to that person only"),
        ["heal"] = T("動ける人全員の HP を戻す(一人を選んだあとは、その人)", "Heals every able member (or the chosen one)"),
        ["damage"] = T("動ける人全員にダメージ(一人を選んだあとは、その人)。HP は 1 より下がらない。その種類に抵抗があれば半分", "Damages every able member (or the chosen one). HP never drops below 1. Resistance halves it"),
        ["condition"] = T("状態にする。続く分が 0 なら治すまで", "Inflicts a condition. 0 minutes lasts until cured"),
        ["cure"] = T("状態を治す(選ばなければ全部)", "Cures a condition (all, if none is chosen)"),
        ["spend_slot"] = T("決めたレベル以上の呪文スロットを1つ使う(残っている一番低いもの)。使えたら「使えた」、無ければ「残っていない」", "Spends one spell slot of the level or higher (the lowest left). then if spent, else if none"),
        ["light"] = T("明かりをつける・消す(消すと、明かりが消えているときだけ読める文字などに使える)", "Lights or puts out the light (for writing seen only in the dark, etc.)"),
        ["combat"] = T("戦う。勝ったら「勝った」、逃げたら「逃げた」の手順(逃げた手順が無ければ、また来ると起こる)", "Fights. Win and fled steps follow (without fled steps, the event happens again next time)"),
        ["open"] = T("迷宮のタブで置いた門の錠を開ける(この階)", "Unlocks a gate placed on the Maze tab (this floor)"),
        ["close"] = T("開けた門の錠を、また閉める(この階)", "Locks an opened gate again (this floor)"),
        ["warp"] = T("迷宮のタブで置いた着地点へパーティを移す(ほかの階へも)。向きはそのまま。イベントはここで終わり、着いたマスのイベントが起こる", "Moves the party to a landing point placed on the Maze tab (other floors too). Facing is kept. The event ends, and the landing cell's event fires"),
        ["sound"] = T("効果音を鳴らす", "Plays a sound effect"),
    };

    /// <summary>戦闘の敵の組(1行に1組。上で種類と脅威度を絞る)。</summary>
    private UIElement GroupsBox(List<BossDef.BossGroup> groups) => GroupsEditor.Create(_context, groups, () => Changed(), boss: false);

    private void CondProps(EventCondition c)
    {
        _props.Children.Add(Ui.Heading(T("条件: ", "Condition: ") + PartNames.Name(PartNames.Conditions, c.Type)));
        _props.Children.Add(Ui.Row(T("種類", "Type"), Combo(PartNames.Conditions.Keys, c.Type, v => { c.Type = v; Changed(); ShowProps(); }, PartNames.Conditions), 110));
        foreach (var (label, editor) in CondFields(c)) _props.Children.Add(Ui.Row(label, editor, 110));
        _props.Children.Add(Ui.Row("", Check(c.Type switch
        {
            "flag" => T("立っていないとき", "When it is not set"),
            _ => T("合わないとき(〜でない)", "When it does not match (not)"),
        }, c.Not, v => c.Not = v), 110));
        if (CondHelp(c.Type) is { Length: > 0 } help) _props.Children.Add(Ui.Note(help));
    }

    /// <summary>条件の種類ごとの入力(名前と欄)。種類を選ぶと、それに合ったものだけが出る。</summary>
    private IEnumerable<(string Label, UIElement Editor)> CondFields(EventCondition c)
    {
        UIElement Id(IEnumerable<Choice> choices) => IdCombo(choices, c.Id, v => c.Id = v);
        UIElement Num(int value, Action<int> set) => Number(value, v => set((int)v));
        UIElement Op() => Combo([">=", "<=", "==", "!=", ">", "<"], c.Op, v => { c.Op = v; Changed(); }, PartNames.Compare);
        switch (c.Type)
        {
            case "flag": yield return (T("フラグ", "Flag"), Id(Known("flag"))); break;
            case "done": yield return (T("イベント", "Event"), Id(EventChoices(maze: false))); break;
            case "item":
                yield return (T("品", "Item"), Id(ItemChoices()));
                yield return (T("いくつ以上", "At least"), Num(c.Count, v => c.Count = v));
                break;
            case "equipped": yield return (T("品", "Item"), Id(ItemChoices())); break;
            case "gold": yield return (T("金貨 以上", "Gold at least"), Num(c.Value, v => c.Value = v)); break;
            case "level": yield return (T("誰かがレベル", "Someone at level"), Num(c.Value, v => c.Value = v)); break;
            case "class": yield return (T("クラス", "Class"), Id(_context.BaseData.Classes.Values.Select(x => new Choice(x.Id, Loc.Name(x.Name) ?? x.Id)))); break;
            case "race": yield return (T("種族", "Race"), Id(_context.BaseData.Races.Values.Select(x => new Choice(x.Id, Loc.Name(x.Name) ?? x.Id)))); break;
            case "counter":
                yield return (T("変数", "Variable"), Id(Known("counter")));
                yield return (T("比べ方", "Compare"), Op());
                yield return (T("値", "Value"), Num(c.Value, v => c.Value = v));
                break;
            case "chance": yield return (T("確率(%)", "Chance (%)"), Num(c.Value, v => c.Value = v)); break;
            case "alive": yield return (T("動ける人数", "Able members"), Num(c.Value, v => c.Value = v)); break;
            case "depth": yield return (T("深さ(%)", "Depth (%)"), Num(c.Value, v => c.Value = v)); break;
            case "facing": yield return (T("方角", "Direction"), Combo(EventCheck.Facings, c.Id ?? "", v => { c.Id = v; Changed(); }, PartNames.Facings)); break;
            case "entered": yield return (T("入り方", "Entered by"), Combo(EventCheck.Entries, c.Id ?? "", v => { c.Id = v; Changed(); }, PartNames.Entries)); break;
            case "zone": yield return (T("区域", "Zone"), Combo(EventCheck.Zones, c.Id ?? "", v => { c.Id = v; Changed(); }, PartNames.Zones)); break;
            case "slots": yield return (T("レベル以上", "Level at least"), Num(c.Value, v => c.Value = v)); break;
            case "status":
                yield return (T("状態", "Condition"), Id(Enum.GetValues<GravewardSpiral.Core.Effects.Condition>()
                    .Select(x => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(x.ToString())).Select(id => new Choice(id, ConditionWord(id)))));
                break;
            case "hp":
                yield return (T("比べ方", "Compare"), Op());
                yield return (T("HP(%)", "HP (%)"), Num(c.Value, v => c.Value = v));
                break;
            case "cast": yield return (T("呪文", "Spell"), Id(_context.BaseData.Spells.Values.Select(x => new Choice(x.Id, Loc.Name(x.Name) ?? x.Id)))); break;
            case "timer":
                yield return (T("タイマー", "Timer"), Id(EventChoices(maze: true)));
                yield return (T("比べ方", "Compare"), Op());
                yield return (T("残りの歩数", "Steps left"), Num(c.Value, v => c.Value = v));
                break;
        }
    }

    private static string CondHelp(string type) => type switch
    {
        "flag" => T("フラグは、流れの「フラグを立てる」で立つ。一覧はこのシナリオで使っているもの(新しい名前も打てる)", "Flags are set by the \"Set flag\" step. The list shows the ones this scenario uses (you can type a new one)"),
        "key" => T("この階の鍵を拾っている", "The party has picked up this floor's key"),
        "turned" => T("そのマスで向きを変えて起きた(「起こるとき」が両方のイベントで、着いたときと分ける)", "Fired by turning in the cell (tells it from entering when the trigger is both)"),
        "entered" => T("階段やテレポーターで来たときは、どれでもない", "Arriving by stairs or teleporter counts as none"),
        "armed" => T("武器を手に持っている人がいる。「合わないとき」にすると、誰も持っていない", "Someone holds a weapon. With \"not\", nobody does"),
        "lit" => T("明かり(ライトなど)がついている", "A light (such as Light) is on"),
        "cast" => T("イベントの「起こるとき」に呪文を入れる(キャンプからそのマスで唱えると起こる)", "Set the event's trigger to casting (fires when a spell is cast in the cell from camp)"),
        "rested" => T("イベントの「起こるとき」に休むを入れる", "Set the event's trigger to resting"),
        "timer" => T("比べ方が 1 以上のままなら「動いている」", "With \">= 1\" it means the timer is running"),
        "slots" or "status" or "hp" or "item" or "equipped" or "level" or "class" or "race"
            => T("「一人を選ぶ」のあとは、選んだ人だけを見る", "After \"Choose one member\", only the chosen member is checked"),
        _ => "",
    };

    private string ConditionWord(string id) => new DraftSummary(_context).ConditionName(id);

    /// <summary>名前を見せて選べ、新しい id も打てる欄。</summary>
    private ComboBox IdCombo(IEnumerable<Choice> choices, string? value, Action<string?> set)
    {
        // 名前のあるものは名前の順に、「名前 (id)」で見せる。打った文字は、一覧のどれか(名前か id)に合えばその id、合わなければ新しい id
        var list = choices.OrderBy(c => c.Label.Length == 0 ? c.Key : c.Label, StringComparer.CurrentCulture).ToList();
        var box = new ComboBox { IsEditable = true, ItemsSource = list, IsTextSearchEnabled = true };
        box.SetValue(TextSearch.TextPathProperty, nameof(Choice.Display));
        box.Text = list.FirstOrDefault(c => c.Key == value)?.Display ?? value ?? "";
        string? KeyOf(string text) =>
            list.FirstOrDefault(c => c.Display == text || c.Key == text || c.Label.Length > 0 && c.Label == text)?.Key ?? Null(text);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice c && c.Key != value) { value = c.Key; set(c.Key); Changed(); } };
        box.LostFocus += (_, _) => { var v = KeyOf(box.Text); if (v != value) { value = v; set(v); Changed(); } };
        return box;
    }

    private IEnumerable<Choice> ItemChoices() => _context.BaseData.Items.Values.Select(x => new Choice(x.Id, Loc.Name(x.Name) ?? x.Id));

    private IEnumerable<Choice> EventChoices(bool maze) =>
        (_context.Pack?.Events ?? []).Where(e => !maze || e.Place == "maze").Select(e => new Choice(e.Id, EventList.Name(e) ?? ""));

    /// <summary>このシナリオのイベントで使っているフラグか数の名前(立てる・足す手順と、見る条件から)。</summary>
    private IEnumerable<Choice> Known(string kind)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        void Conds(IEnumerable<EventCondition> cs)
        {
            foreach (var c in cs) if (c.Type == kind && c.Id != null) names.Add(c.Id);
        }
        void Walk(IEnumerable<EventStep> steps)
        {
            foreach (var st in steps)
            {
                if (st.Id != null && (kind == "flag" ? st.Type is "flag" or "unflag" : st.Type is "counter" or "counter_set")) names.Add(st.Id);
                Conds(st.When);
                foreach (var o in st.Options) Conds(o.When);
                foreach (var child in st.Children()) Walk(child);
            }
        }
        foreach (var e in _context.Pack?.Events ?? [])
        {
            Conds(e.When);
            Walk(e.Steps);
        }
        return names.Select(n => new Choice(n, ""));
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

    private CheckBox Check(string label, bool value, Action<bool> set, bool rebuild = false)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            Changed();
            if (rebuild) ShowProps();
        };
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
    /// fresh なら、選んでいるものがあっても元にしない(新しく作る。選んでいるものはそのまま)。
    /// </summary>
    public AiDraftWindow? AskAi(string? baseId = null, bool fresh = false)
    {
        if (_context.Pack is not { } p) return null;
        if (baseId != null) _list.SelectedIndex = -1;
        var current = fresh ? null : Current;
        var source = fresh ? null : baseId == null ? Current ?? SelectedStandard : _context.BaseData.Events.GetValueOrDefault(baseId);
        string id = current?.Id ?? (source != null && p.Events.All(x => x.Id != source.Id) ? source.Id : NewEventId(p));
        var baseJson = source == null ? null : JsonNode.Parse(ScenarioPack.CleanJson(source)) as JsonObject;
        string label = source == null ? T("なし(新しく作る)", "None (write a new one)")
            : $"{EventList.Name(source) ?? source.Id}({source.Id})" + (current == null ? T("(標準のイベント。写して足す)", " (standard event; added as a copy)") : "");
        var window = new AiDraftWindow(_context, Core.Ai.DraftWriter.Kind.Event, baseJson, label, id, null, current != null, _context.CheckEvent, (draft, items, replace) =>
        {
            EventDef parsed;
            try { parsed = draft.Deserialize<EventDef>(ContentLibrary.JsonOptions)!; }
            catch (JsonException ex)
            {
                MessageBox.Show(T("JSON を読めません: ", "Cannot read the JSON: ") + ex.Message);
                return;
            }
            if (replace && current is { } e && p.Events.IndexOf(e) is >= 0 and var i) p.Events[i] = parsed;
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
