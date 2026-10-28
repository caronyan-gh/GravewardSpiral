using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Save;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>フラグの選択と管理。表示名だけ変えても保存済みのフラグ名は変わらない。</summary>
public static class EventFlagsUi
{
    private sealed record Choice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    public static List<EventDef> Events(EditorContext ctx) => ctx.Pack is { } p ? EventAuthoring.Events(p, ctx.BaseData.Events.Values).ToList() : [];
    public static HashSet<string> Private(EditorContext ctx) => Events(ctx).Select(e => e.OwnFlag).OfType<string>()
        .Concat(ctx.Pack?.EventSets.SelectMany(s => s.OwnedFlags.Append(EventPlacer.ChainFlag(s.Id))) ?? []).ToHashSet();
    public static List<string> Names(EditorContext ctx)
    {
        if (ctx.Pack is not { } p) return [];
        var available = Events(ctx).ToDictionary(e => e.Id);
        var used = p.Events.Select(e => e.Id).Concat(p.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark))
            .OfType<string>().Where(m => m.StartsWith("event:")).Select(m => m[6..])).ToHashSet();
        var pending = new Queue<string>(used);
        while (pending.TryDequeue(out var id))
            if (available.TryGetValue(id, out var e))
                foreach (var s in EventAuthoring.Steps(e).Where(s => s.Type is "run" or "timer"))
                    if (s.Id != null && used.Add(s.Id)) pending.Enqueue(s.Id);
        // このシナリオの参照を拾う。未使用の標準イベントのフラグを全部並べない。
        return used.Where(available.ContainsKey).SelectMany(id => EventAuthoring.FlagNames(available[id])).Concat(p.Flags.Select(f => f.Id))
            .Concat(p.Rumors.SelectMany(r => r.When).Where(c => c.Type == "flag").Select(c => c.Id).OfType<string>())
            .Concat(p.Elevators.Select(e => e.Power).OfType<string>())
            .Concat(p.Bosses.Select(b => (string?)b["reward_flag"]).OfType<string>()).Distinct().Order().ToList();
    }
    public static string Label(EditorContext ctx, string? id)
    {
        if (id == null) return T("(未選択)", "(not selected)");
        var e = Events(ctx).FirstOrDefault(e => e.OwnFlag == id);
        if (e != null) return EventList.Name(e) ?? e.Id;
        if (ctx.Pack?.EventSets.FirstOrDefault(s => EventPlacer.ChainFlag(s.Id) == id) is { } set)
            return (Name(set.Name) ?? set.Id) + T("が配置されている", " is placed");
        var flag = ctx.Pack?.Flags.FirstOrDefault(f => f.Id == id);
        return Name(flag?.Name) ?? id;
    }

    public static FrameworkElement Picker(EditorContext ctx, EventDef? current, string? value, Action<string?> set, Action changed)
    {
        var panel = new StackPanel();
        var privateFlags = Private(ctx);
        var modes = new List<Choice> { new("common", T("共通フラグ", "Shared flag")), new("event", T("イベントのフラグ", "Event flag")) };
        if (current?.OwnFlag != null) modes.Insert(0, new("self", T("このイベントのフラグ", "This event's flag")));
        if (ctx.Pack?.EventSets.Count > 0) modes.Add(new("set", T("セット内のフラグ", "Set flag")));
        string mode = value != null && value == current?.OwnFlag ? "self" : Events(ctx).Any(e => e.OwnFlag != null && e.OwnFlag == value) ? "event" : value != null && privateFlags.Contains(value) ? "set" : "common";
        var kind = new ComboBox { ItemsSource = modes, SelectedItem = modes.First(x => x.Key == mode), DisplayMemberPath = nameof(Choice.Label) };
        var target = new StackPanel();
        panel.Children.Add(kind);
        panel.Children.Add(target);
        void Apply(string? id) { value = id; set(id); changed(); }
        void Show(bool choose)
        {
            target.Children.Clear();
            if (mode == "self") { if (choose) Apply(current!.OwnFlag); return; }
            IEnumerable<Choice> choices = mode switch
            {
                "event" => Events(ctx).Where(e => e.OwnFlag != null).Select(e => new Choice(e.OwnFlag!, EventList.Name(e) ?? e.Id)),
                "set" => ctx.Pack!.EventSets.SelectMany(s => s.OwnedFlags.Append(EventPlacer.ChainFlag(s.Id)).Select(f => new Choice(f, $"{Name(s.Name) ?? s.Id} / {Label(ctx, f)}"))),
                _ => Names(ctx).Where(f => !privateFlags.Contains(f)).Select(f => new Choice(f, Label(ctx, f))),
            };
            var list = choices.DistinctBy(c => c.Key).OrderBy(c => c.Label).ToList();
            var box = new ComboBox { ItemsSource = list, IsEditable = mode == "common", IsTextSearchEnabled = true };
            box.SetValue(TextSearch.TextPathProperty, nameof(Choice.Label));
            box.DisplayMemberPath = nameof(Choice.Label);
            box.SelectedItem = choose ? null : list.FirstOrDefault(c => c.Key == value);
            if (box.IsEditable) box.Text = choose ? "" : list.FirstOrDefault(c => c.Key == value)?.Label ?? value ?? "";
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice c) Apply(c.Key); };
            box.LostFocus += (_, _) =>
            {
                if (!box.IsEditable) return;
                string text = box.Text.Trim();
                string? id = list.FirstOrDefault(c => c.Label == text || c.Key == text)?.Key ?? (text.Length > 0 ? text : null);
                if (id != value) Apply(id);
            };
            target.Children.Add(box);
            if (choose) target.Children.Add(Ui.Note(T("選ぶまで、元のフラグの設定を保ちます。", "The current flag is kept until you choose another.")));
        }
        kind.SelectionChanged += (_, _) => { if (kind.SelectedItem is Choice c) { mode = c.Key; Show(true); } };
        Show(false);
        return panel;
    }

    public static Window Manager(EditorContext ctx)
    {
        var window = new Window { Title = T("フラグの管理", "Flags"), Width = 900, Height = 660, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var top = new StackPanel();
        var all = new CheckBox { Content = T("イベント・セット専用も表示", "Include private flags") };
        var search = new TextBox { ToolTip = T("名前・IDで絞る", "Filter by name or ID") };
        var list = new ListBox();
        var detail = new StackPanel();
        string? selected = null;
        void Refresh()
        {
            var priv = Private(ctx);
            var choices = Names(ctx).Where(f => all.IsChecked == true || !priv.Contains(f))
                .Where(f => ($"{Label(ctx, f)} {f}").Contains(search.Text, StringComparison.OrdinalIgnoreCase))
                .Select(f => new Choice(f, Label(ctx, f))).ToList();
            list.ItemsSource = choices; list.DisplayMemberPath = nameof(Choice.Label);
            list.SelectedItem = choices.FirstOrDefault(c => c.Key == selected);
        }
        void Show()
        {
            detail.Children.Clear();
            if (selected == null || ctx.Pack is not { } p) return;
            var def = p.Flags.FirstOrDefault(f => f.Id == selected);
            EventFlagDef Ensure() { if (def == null) { def = new() { Id = selected }; p.Flags.Add(def); } return def; }
            detail.Children.Add(Ui.Heading(Label(ctx, selected)));
            detail.Children.Add(Ui.Note(T("内部ID(変更してもらう必要はありません): ", "Internal ID (no need to edit): ") + selected));
            if (Events(ctx).Any(e => e.OwnFlag == selected)) detail.Children.Add(Ui.Note(T("表示名は所有するイベントの名前です。イベントのルートで変更できます。", "Named after its owning event. Rename the event on its root panel.")));
            else detail.Children.Add(Ui.Row(T("表示名", "Name"), Ui.Localized(def?.Name, v => { Ensure().Name = v; ctx.MarkEdited(); })));
            detail.Children.Add(Ui.Row(T("説明", "Description"), Ui.Text(def?.Note, v => { Ensure().Note = v; ctx.MarkEdited(); })));
            detail.Children.Add(Ui.Heading(T("使っている場所", "Used by")));
            foreach (var e in Events(ctx).Where(e => EventAuthoring.FlagNames(e).Contains(selected)))
            {
                var actions = new List<string>();
                if (e.OwnFlag == selected) actions.Add(T("専用フラグの所有者", "owner"));
                if (EventAuthoring.Steps(e).Any(s => s.Type == "flag" && s.Id == selected)) actions.Add("ON");
                if (EventAuthoring.Steps(e).Any(s => s.Type == "unflag" && s.Id == selected)) actions.Add("OFF");
                if (EventAuthoring.Conditions(e).Any(c => c.Type == "flag" && c.Id == selected)) actions.Add(T("条件で確認", "condition"));
                detail.Children.Add(Ui.Note($"{EventList.Name(e) ?? e.Id}: {string.Join(" / ", actions)}"));
            }
            foreach (var shaft in p.Elevators.Where(x => x.Power == selected)) detail.Children.Add(Ui.Note(T($"エレベーター ({shaft.X},{shaft.Y}) の電源", $"Elevator ({shaft.X},{shaft.Y}) power")));
            foreach (var rumor in p.Rumors.Where(r => r.When.Any(c => c.Type == "flag" && c.Id == selected))) detail.Children.Add(Ui.Note(T("噂の条件: ", "Rumor condition: ") + rumor.Id));
            foreach (var boss in p.Bosses.Where(b => (string?)b["reward_flag"] == selected)) detail.Children.Add(Ui.Note(T("倒すとONになるボス: ", "Boss sets ON: ") + (string?)boss["id"]));
        }
        top.Children.Add(Ui.Note(T("定義の管理です。プレイ中のON/OFFは変更しません。", "Definitions only; this does not change a saved game's flag values.")));
        top.Children.Add(all); top.Children.Add(search);
        top.Children.Add(Ui.Button(T("共通フラグを作る", "New shared flag"), () =>
        {
            if (ctx.Pack is not { } p) return;
            var f = new EventFlagDef { Id = EventAuthoring.NewId(p, "flag", ctx.BaseData.Events.Values), Name = LocalizedText.Of("New flag", "新しいフラグ") };
            p.Flags.Add(f); selected = f.Id; ctx.MarkEdited(); Refresh(); Show();
        }));
        top.Children.Add(Ui.Button(T("一覧を更新", "Refresh list"), Refresh));
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top); left.Children.Add(list);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is Choice c) { selected = c.Key; Show(); } };
        all.Click += (_, _) => Refresh(); search.TextChanged += (_, _) => Refresh();
        grid.Children.Add(left);
        var right = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(right, 1); grid.Children.Add(right);
        window.Content = grid; Refresh();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        return window;
    }
}
