using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Save;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>セットの一括取り込み・複製・部品の編集・配置状況。同じ画面をイベントと迷宮から開く。</summary>
public sealed class EventSetsWindow : Window
{
    private readonly EditorContext _ctx;
    private readonly ListBox _sets = new();
    private readonly StackPanel _detail = new();
    private readonly Func<(int Floor, GridPos Cell)?>? _location;
    private readonly Action? _placed;
    private EventSetDef? Current => (_sets.SelectedItem as ListBoxItem)?.Tag as EventSetDef;
    private Dictionary<string, EventDef> Events => EventFlagsUi.Events(_ctx).ToDictionary(e => e.Id);

    public EventSetsWindow(EditorContext ctx, EventDef? current = null, Func<(int, GridPos)?>? location = null, Action? placed = null)
    {
        _ctx = ctx; _location = location; _placed = placed;
        Title = T("仕掛けのセット", "Event sets"); Width = 1000; Height = 760;
        Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var top = new StackPanel();
        top.Children.Add(Ui.Heading(T("このシナリオのセット", "Scenario sets")));
        top.Children.Add(Ui.Button(T("標準のセットを追加…", "Add standard set…"), AddStandard));
        top.Children.Add(Ui.Button(T("空のセットを作る", "New empty set"), () =>
        {
            var set = new EventSetDef { Id = EventAuthoring.NewId(ctx.Pack!, "set", ctx.BaseData.Events.Values), Name = LocalizedText.Of("New set", "新しいセット") };
            ctx.Pack!.EventSets.Add(set); Changed(set);
        }));
        if (current != null) top.Children.Add(Ui.Button(T("選んでいたイベントからセットを作る", "Group the selected event"), () =>
        {
            if (ctx.Pack!.EventSets.Any(s => s.Parts.Any(p => p.Kind == "event" && p.Target == current.Id)))
            { MessageBox.Show(this, T("すでにセットに属しています。そのセットを編集してください。", "This event already belongs to a set. Edit that set.")); return; }
            var set = EventAuthoring.FromEvent(current, Events);
            set.Id = EventAuthoring.NewId(ctx.Pack!, "set", ctx.BaseData.Events.Values);
            set.Name ??= LocalizedText.Of(current.Id, EventList.Name(current) ?? current.Id);
            set.Kind ??= set.Name;
            ctx.Pack!.EventSets.Add(set); Changed(set);
        }));
        top.Children.Add(Ui.Button(T("セットを複製", "Duplicate set"), () =>
        {
            if (Current is not { } set) return;
            Try(() => Changed(EventAuthoring.ImportSet(ctx.Pack!, set, Events,
                LocalizedText.Of((set.Name?.Get("en") ?? set.Id) + " (copy)", (set.Name?.Get("ja") ?? set.Id) + "(コピー)"))));
        }));
        top.Children.Add(Ui.Button(T("一覧を更新", "Refresh"), () => Refresh(Current)));
        top.Children.Add(Ui.Button(T("セットの登録を外す", "Ungroup set"), () =>
        {
            if (Current is not { } set) return;
            if (MessageBox.Show(this, T("セットの登録だけ外します。イベントと地図の配置は残ります。続けますか？", "Remove only the set registration? Events and map placements will remain."), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            ctx.Pack!.EventSets.Remove(set); Changed(null);
        }));
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top); left.Children.Add(_sets); grid.Children.Add(left);
        _sets.SelectionChanged += (_, _) => ShowSet();
        var scroll = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
        Content = grid; Refresh(ctx.Pack?.EventSets.FirstOrDefault());
        void ChangedPack() => Close();
        ctx.PackChanged += ChangedPack;
        Closed += (_, _) => ctx.PackChanged -= ChangedPack;
    }
    private void Try(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message, Title); }
    }
    private void Changed(EventSetDef? select) { _ctx.MarkEdited(); Refresh(select); }
    private string Status(EventSetPart p)
    {
        if (!p.Place) return T("配置不要", "No placement needed");
        var places = EventAuthoring.Locations(_ctx.Pack!, p.Mark);
        return places.Count == 0 ? T("未配置", "Not placed") : string.Join(", ", places.Select(x => $"B{x.Floor + 1} ({x.Cell.X},{x.Cell.Y})"));
    }
    private void Refresh(EventSetDef? select)
    {
        _sets.Items.Clear();
        foreach (var s in _ctx.Pack!.EventSets)
        {
            int total = s.Parts.Count(p => p.Place), count = s.Parts.Count(p => p.Place && EventAuthoring.Locations(_ctx.Pack, p.Mark).Count == 1);
            _sets.Items.Add(new ListBoxItem { Content = $"{Name(s.Name) ?? s.Id}  {count}/{total}", Tag = s });
        }
        _sets.SelectedItem = _sets.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag == select) ?? _sets.Items.OfType<ListBoxItem>().FirstOrDefault();
        ShowSet();
    }
    private void ShowSet()
    {
        _detail.Children.Clear();
        if (Current is not { } s) return;
        _detail.Children.Add(Ui.Heading(Name(s.Name) ?? s.Id));
        _detail.Children.Add(Ui.Row(T("セット名", "Set name"), Ui.Localized(s.Name, v =>
        {
            s.Name = v;
            foreach (var part in s.Parts.Where(p => p.Kind == "event"))
                if (_ctx.Pack!.Events.FirstOrDefault(e => e.Id == part.Target) is { } e)
                    e.Title = LocalizedText.Of($"{v?.Get("en")} - {part.Name?.Get("en") ?? part.Target}", $"{v?.Get("ja")} - {part.Name?.Get("ja") ?? part.Target}");
            _ctx.MarkEdited();
        })));
        _detail.Children.Add(Ui.Row(T("仕掛けの種類", "Kind of mechanism"), Ui.Localized(s.Kind, v => { s.Kind = v; _ctx.MarkEdited(); })));
        _detail.Children.Add(Ui.Note(T("部品を追加すると一式として管理します。地図には「未配置」の部品を置いてください。内部処理は置きません。", "Add parts to manage them together. Place the unplaced parts on the map; internal parts need no placement.")));
        if (_location != null)
            _detail.Children.Add(Ui.Button(T("未配置の部品を、この階へまとめて置く", "Place remaining parts on this floor"), () => Try(() =>
            {
                if (_location() is not { } at) throw new InvalidOperationException(T("配置したい階でマスを選んでください", "Select a cell on the desired floor"));
                EventAuthoring.PlaceRemaining(_ctx.Pack!, s, at.Floor);
                _ctx.MarkEdited(); _placed?.Invoke(); Refresh(s);
            }), T("空いた床へ配置します。開く扉には、先に錠のある辺を描いてください。全部置けるときだけ配置します。", "Uses empty floor cells. Draw locked edges for gates first. Nothing changes unless all remaining parts fit.")));
        foreach (var part in s.Parts.ToList())
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            var fields = new StackPanel();
            row.Children.Add(Ui.Heading($"{Name(part.Name) ?? part.Target} — {Status(part)}"));
            fields.Children.Add(Ui.Row(T("部品名", "Part name"), Ui.Localized(part.Name, v =>
            {
                part.Name = v;
                if (part.Kind == "event" && _ctx.Pack!.Events.FirstOrDefault(e => e.Id == part.Target) is { } e)
                    e.Title = LocalizedText.Of($"{s.Name?.Get("en")} - {v?.Get("en")}", $"{s.Name?.Get("ja")} - {v?.Get("ja")}");
                _ctx.MarkEdited();
            })));
            var required = new CheckBox { Content = T("地図に置く部品", "Place on map"), IsChecked = part.Place, IsEnabled = part.Kind == "event" };
            required.Click += (_, _) => { part.Place = required.IsChecked == true; Changed(s); };
            fields.Children.Add(required);
            fields.Children.Add(Ui.Note(part.Mark));
            var buttons = new WrapPanel();
            if (part.Place && _location != null)
                buttons.Children.Add(Ui.Button(T("選択中のマスに置く / 移動", "Place / move to selected cell"), () => Try(() =>
                {
                    if (_location() is not { } at) throw new InvalidOperationException(T("迷宮でマスを1つ選んでください", "Select one cell in the Maze tab"));
                    EventAuthoring.Place(_ctx.Pack!, part.Mark, at.Floor, at.Cell, true, _ctx.BaseData.Events.Values);
                    _ctx.MarkEdited(); _placed?.Invoke(); Refresh(s);
                })));
            fields.Children.Add(Ui.Button(T("セットから外す", "Remove from set"), () => { s.Parts.Remove(part); Changed(s); }));
            row.Children.Add(buttons);
            row.Children.Add(new Expander { Header = T("部品の設定", "Part settings"), Content = fields });
            _detail.Children.Add(row);
        }
        _detail.Children.Add(Ui.Heading(T("部品を追加", "Add a part")));
        var kind = new ComboBox { ItemsSource = new[] { new Choice("event", T("イベント", "Event")), new Choice("gate", T("開く扉", "Gate")), new Choice("dest", T("着地点", "Landing point")) }, SelectedIndex = 0 };
        var target = new ComboBox { IsEditable = true };
        void Targets()
        {
            target.ItemsSource = (kind.SelectedItem as Choice)?.Key == "event" ? Events.Values.Select(e => new Choice(e.Id, EventList.Name(e) ?? e.Id)).ToList() : new List<Choice>();
            target.Text = "";
        }
        target.SetValue(TextSearch.TextPathProperty, nameof(Choice.Display));
        kind.SelectionChanged += (_, _) => Targets(); Targets();
        _detail.Children.Add(kind); _detail.Children.Add(target);
        _detail.Children.Add(Ui.Button(T("部品を足す", "Add part"), () =>
        {
            string k = ((Choice)kind.SelectedItem).Key;
            string id = target.SelectedItem is Choice choice ? choice.Key : target.Text.Trim();
            if (id.Length == 0 || s.Parts.Any(p => p.Kind == k && p.Target == id)) return;
            if (k == "event" && !Events.ContainsKey(id)) { MessageBox.Show(this, T("存在するイベントを選んでください", "Choose an existing event")); return; }
            if (_ctx.Pack!.EventSets.Any(other => other != s && other.Parts.Any(p => p.Kind == k && p.Target == id && p.Place)))
            { MessageBox.Show(this, T("別のセットの配置部品です。一式を複製してください。", "This part belongs to another set. Duplicate the entire set.")); return; }
            s.Parts.Add(new() { Kind = k, Target = id, Name = k == "event" ? LocalizedText.Of(EventList.Name(Events[id]) ?? id, EventList.Name(Events[id]) ?? id) : LocalizedText.Of(id, id), Place = k != "event" || Events[id].Place == "maze" });
            Changed(s);
        }));
        var owned = new StackPanel();
        var members = s.Parts.Where(p => p.Kind == "event" && Events.ContainsKey(p.Target)).Select(p => Events[p.Target]).ToList();
        foreach (var flag in members.SelectMany(EventAuthoring.FlagNames).Distinct().Where(f => members.All(e => e.OwnFlag != f)))
        {
            var check = new CheckBox { Content = EventFlagsUi.Label(_ctx, flag), IsChecked = s.OwnedFlags.Contains(flag), ToolTip = flag };
            check.Click += (_, _) => { if (check.IsChecked == true) { if (!s.OwnedFlags.Contains(flag)) s.OwnedFlags.Add(flag); } else s.OwnedFlags.Remove(flag); _ctx.MarkEdited(); };
            owned.Children.Add(check);
        }
        foreach (var counter in members.SelectMany(e => EventAuthoring.Steps(e).Where(st => st.Type is "counter" or "counter_set").Select(st => st.Id)
                     .Concat(EventAuthoring.Conditions(e).Where(c => c.Type == "counter").Select(c => c.Id))).OfType<string>().Distinct())
        {
            var check = new CheckBox { Content = T("数: ", "Counter: ") + counter, IsChecked = s.OwnedCounters.Contains(counter) };
            check.Click += (_, _) => { if (check.IsChecked == true) { if (!s.OwnedCounters.Contains(counter)) s.OwnedCounters.Add(counter); } else s.OwnedCounters.Remove(counter); _ctx.MarkEdited(); };
            owned.Children.Add(check);
        }
        _detail.Children.Add(new Expander { Header = T("セット専用の状態(複製時に分ける)", "Private state (separate when copied)"), Content = owned });
        _detail.Children.Add(Ui.Note(T("イベント専用フラグは自動で分かれます。上で選んだ状態も複製時に分け、それ以外の共通フラグは共有します。", "Event-owned flags are always separated. Selected state is also separated on copy; other shared flags remain shared.")));
    }
    private void AddStandard()
    {
        var templates = EventAuthoring.Templates(_ctx.BaseContent, _ctx.BaseData.Events);
        var dialog = new Window { Title = T("セットを追加", "Add set"), Width = 650, Height = 520, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var list = new ListBox { ItemsSource = templates.Select(s => new Choice(s.Id, Name(s.Name) ?? s.Id)).ToList() };
        var name = new TextBox();
        EventSetDef? selected = null;
        var state = new StackPanel();
        var bottom = new StackPanel();
        bottom.Children.Add(Ui.Row(T("このセットの名前", "Name for this set"), name));
        bottom.Children.Add(Ui.Note(T("全部のイベントと部品表を一度に取り込みます。配置場所は迷宮で選びます。", "Imports every event and the parts list together. Choose locations on the Maze tab.")));
        bottom.Children.Add(new Expander { Header = T("専用にする状態(共通で使うものは選ばない)", "Private state (leave shared state unchecked)"), Content = new ScrollViewer { Content = state, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        bottom.Children.Add(Ui.Button(T("一式を追加", "Add entire set"), () => Try(() =>
        {
            if (selected == null || name.Text.Trim().Length == 0) return;
            var set = EventAuthoring.ImportSet(_ctx.Pack!, selected, _ctx.BaseData.Events, LocalizedText.Of(name.Text.Trim(), name.Text.Trim()));
            Changed(set); dialog.Close();
        })));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Choice c) return;
            name.Text = c.Label;
            selected = EventAuthoring.Copy(templates.First(s => s.Id == c.Key));
            state.Children.Clear();
            var members = selected.Parts.Where(p => p.Kind == "event" && _ctx.BaseData.Events.ContainsKey(p.Target)).Select(p => _ctx.BaseData.Events[p.Target]).ToList();
            var automatic = selected.Parts.Select(p => p.OwnFlag).Concat(members.Select(e => e.OwnFlag)).OfType<string>().ToHashSet();
            state.Children.Add(Ui.Note(T("専用フラグは自動で分かれます。旧来の連動イベントは、ここで所有する状態を選べます。", "Event-owned flags are separated automatically. For legacy chains, choose the state this set owns here.")));
            foreach (var flag in members.SelectMany(EventAuthoring.FlagNames).Distinct().Where(f => !automatic.Contains(f) && !f.StartsWith("chain:")))
            {
                var check = new CheckBox { Content = flag, IsChecked = selected.OwnedFlags.Contains(flag) };
                check.Click += (_, _) => { if (check.IsChecked == true) { if (!selected.OwnedFlags.Contains(flag)) selected.OwnedFlags.Add(flag); } else selected.OwnedFlags.Remove(flag); };
                state.Children.Add(check);
            }
            foreach (var counter in members.SelectMany(e => EventAuthoring.Steps(e).Where(s => s.Type is "counter" or "counter_set").Select(s => s.Id)
                         .Concat(EventAuthoring.Conditions(e).Where(c => c.Type == "counter").Select(c => c.Id))).OfType<string>().Distinct())
            {
                var check = new CheckBox { Content = T("数: ", "Counter: ") + counter, IsChecked = selected.OwnedCounters.Contains(counter) };
                check.Click += (_, _) => { if (check.IsChecked == true) { if (!selected.OwnedCounters.Contains(counter)) selected.OwnedCounters.Add(counter); } else selected.OwnedCounters.Remove(counter); };
                state.Children.Add(check);
            }
        };
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom); panel.Children.Add(list); dialog.Content = panel; dialog.ShowDialog();
    }
}
