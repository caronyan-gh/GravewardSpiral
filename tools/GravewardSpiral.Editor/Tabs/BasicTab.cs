using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>基本: 名前・説明・作者・版・推奨レベル帯・クランの持ち込み。</summary>
public sealed class BasicTab(EditorContext context, Action<string, bool> say) : IEditorTab
{
    private readonly ScrollViewer _view = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };

    public string Key => "basic";
    public string Header => T("基本", "Basic");
    public UIElement View => _view;

    public void Reload()
    {
        var panel = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Width = 900 };
        _view.Content = panel;
        if (context.Pack is not { } pack)
        {
            panel.Children.Add(Ui.Heading(T("シナリオを開いていません", "No scenario open")));
            panel.Children.Add(Ui.Note(T("上の「新しく作る」か「開く」「zip を開く」から始めます。開くまで、ほかのタブは使えません。シナリオは、ゲームのパックのフォルダ(manifest.json・dungeons.json と maps・events.json など)です。", "Start with \"New\", \"Open\" or \"Open zip\" above. The other tabs are available once a scenario is open.A scenario is a game pack folder (manifest.json, dungeons.json, maps, events.json, etc.).")));
            return;
        }
        var m = pack.Manifest;
        panel.Children.Add(Ui.Heading(T("シナリオ", "Scenario")));
        panel.Children.Add(Ui.Row("id", new TextBlock { Text = m.Id }));
        panel.Children.Add(Ui.Row(T("フォルダ", "Folder"), new TextBlock { Text = pack.Directory, TextWrapping = TextWrapping.Wrap }));
        panel.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(m.Name, v => { m.Name = v; context.MarkEdited(); })));
        panel.Children.Add(Ui.Row(T("説明", "Description"), Ui.Localized(m.Description, v => { m.Description = v; context.MarkEdited(); }, multiline: true, height: 60)));
        panel.Children.Add(Ui.Row(T("作者", "Author"), Ui.Text(m.Author, v => { m.Author = v.Length == 0 ? null : v; context.MarkEdited(); })));
        panel.Children.Add(Ui.Row(T("版", "Version"), Ui.Text(m.Version, v => { m.Version = v; context.MarkEdited(); })));
        var import = new CheckBox { Content = T("クランを連れて入れる", "Allow bringing a clan in"), IsChecked = m.AllowImport };
        import.Click += (_, _) => { m.AllowImport = import.IsChecked == true; context.MarkEdited(); };
        panel.Children.Add(Ui.Row(T("持ち込み", "Import"), import));

        panel.Children.Add(Ui.Heading(T("レベル帯", "Level range")));
        panel.Children.Add(Ui.Note(T("B1 の目安のレベルと、最下層の目安のレベル。階ごとの敵の強さと宝はここから決まる(敵と宝のタブで見られる)。強いクランで入ると、帯はクランに合わせて上がる。", "The target level at B1 and at the bottom floor. Enemy strength and treasure per floor follow from these (see the Enemies & loot tab). If a strong clan enters, the range rises to match.")));
        panel.Children.Add(Ui.Row(T("B1 の目安", "B1 target"), Number(pack.FirstLevel, v => pack.FirstLevel = v)));
        panel.Children.Add(Ui.Row(T("最下層の目安", "Bottom floor target"), Number(pack.LastLevel, v => pack.LastLevel = v)));

        Fills(panel, pack);

        panel.Children.Add(Ui.Heading(T("中身", "Contents")));
        panel.Children.Add(Ui.Note(T($"迷宮 {pack.Floors.Count} 階、イベント {pack.Events.Count} 個、", $"{pack.Floors.Count} floor(s), {pack.Events.Count} event(s), ") +
            T($"最下層の主 {(pack.BossGroups is { Count: > 0 } g ? string.Join("・", g.Select(x => $"{x.Monster}×{x.Count}")) : "世界のレベル帯から選ぶ")}",
              $"bottom boss: {(pack.BossGroups is { Count: > 0 } g2 ? string.Join(", ", g2.Select(x => $"{x.Monster}×{x.Count}")) : "chosen from the world's level range")}")));
    }

    /// <summary>
    /// 中身を自動で決める: 出る敵・名のある敵・散らすイベントを、ゲームの新しい世界と同じ選び方で1回選んで、シナリオに書き込む。
    /// 書き込んだものは、どの新しいゲームでもそのまま出る(気に入らないところは、敵と宝・迷宮・イベントのタブで直す)。
    /// </summary>
    private void Fills(StackPanel panel, Core.Save.ScenarioPack pack)
    {
        panel.Children.Add(Ui.Heading(T("中身を自動で決める", "Fill in contents automatically")));
        panel.Children.Add(Ui.Note(T("ゲームの新しい世界と同じ選び方で選んで、シナリオに書き込む(先に保存する)。書き込んだものは、誰が何度遊んでも同じ。気に入らないところは、敵と宝・迷宮・イベントのタブで直す。決めていないものは、遊ぶたびに選ぶ。",
            "Picks these the same way a new game does and writes them into the scenario (saves first). What is written is the same every time anyone plays. Fix what you don't like on the Enemies & loot, Maze and Events tabs. Anything left undecided is picked on each new game.")));
        void Run(Core.Save.ScenarioFill.Parts parts, bool redo)
        {
            var (done, problem) = context.Fill(parts, redo);
            if (problem != null) say(T("シナリオを読めないので埋められません: ", "Cannot fill because the scenario does not load: ") + problem, true);
            else say(done == Core.Save.ScenarioFill.Parts.None ? T("埋めるところはありませんでした(もう決めてある)", "Nothing to fill (already decided)") : T("自動で決めて書き込みました(保存済み)", "Filled in and saved"), false);
        }
        UIElement Line(string state, params Button[] buttons)
        {
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = state, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 260 });
            foreach (var b in buttons) row.Children.Add(b);
            return row;
        }
        int floors = pack.Floors.Count, decided = Enumerable.Range(0, floors).Count(i => i < pack.Rosters.Count && pack.Rosters[i].Count > 0);
        panel.Children.Add(Ui.Row(T("出る敵", "Monsters"), Line(
            decided == 0 ? T("決めていない(遊ぶたびに選ぶ)", "Not decided (picked each game)") : T($"{decided} / {floors} 階で決めてある", $"Decided on {decided} of {floors} floors"),
            Ui.Button(T("決めていない階を埋める", "Fill undecided floors"), () => Run(Core.Save.ScenarioFill.Parts.Rosters, false)),
            Ui.Button(T("全部選び直す", "Re-pick all"), () => Run(Core.Save.ScenarioFill.Parts.Rosters, true)))));
        panel.Children.Add(Ui.Row(T("名のある敵", "Named monsters"), Line(
            pack.Named == null ? T("決めていない(遊ぶたびに選ぶ)", "Not decided (picked each game)")
                : pack.Named.Count == 0 ? T("いない(決めてある)", "None (decided)") : T($"{pack.Named.Count} 体(決めてある)", $"{pack.Named.Count} (decided)"),
            Ui.Button(pack.Named == null ? T("選んで入れる", "Pick") : T("選び直す", "Re-pick"), () => Run(Core.Save.ScenarioFill.Parts.Named, true)))));
        int marks = pack.Floors.Sum(f => f.AllCells().Count(p => f[p].Mark?.StartsWith(Core.Events.EventPlacer.MarkPrefix) == true));
        panel.Children.Add(Ui.Row(T("散らすイベント", "Scattered events"), Line(
            pack.EventsPlaced ? T($"置いてある(イベントの印 {marks} 個)", $"Placed ({marks} event marks)") : T("置いていない(遊ぶたびに散らす)", "Not placed (scattered each game)"),
            Ui.Button(pack.EventsPlaced ? T("散らし直す", "Re-scatter") : T("散らして置く", "Scatter now"), () => Run(Core.Save.ScenarioFill.Parts.Events, true)),
            Ui.Button(T("散らしたものを消す", "Remove scattered"), () =>
            {
                int n = context.ClearScattered();
                say(T($"散らしたイベントの印を {n} 個消しました(保存済み)", $"Removed {n} scattered event marks (saved)"), false);
            }))));
        var standard = new CheckBox { Content = T("散らすときに、標準のパックのイベントも使う", "Also use the standard pack's events when scattering"), IsChecked = pack.Manifest.StandardEvents, Margin = new Thickness(0, 2, 0, 0) };
        standard.Click += (_, _) => { pack.Manifest.StandardEvents = standard.IsChecked == true; context.MarkEdited(); };
        panel.Children.Add(Ui.Row("", standard));
        panel.Children.Add(Ui.Row("", Ui.Note(T("標準のパックの散らすイベントは 500 ほど(泉・罠・書き付け・連動する仕掛けなど)。外すと、このシナリオの散らすイベントだけを使う。",
            "The standard pack has about 500 scattered events (fountains, traps, notes, linked puzzles…). Uncheck to use only this scenario's scattered events."))));
        var all = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var fillAll = Ui.Button(T("決めていないところを全部埋める", "Fill everything undecided"), () => Run(Core.Save.ScenarioFill.Parts.All, false));
        fillAll.Style = (Style)Application.Current.FindResource("AccentButton");
        all.Children.Add(fillAll);
        panel.Children.Add(Ui.Row("", all));
    }

    private TextBox Number(double value, Action<double> set) =>
        Ui.Text(value.ToString(CultureInfo.InvariantCulture), v =>
        {
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= 1 && d <= 30)
            {
                set(d);
                context.MarkEdited();
            }
        });
}
