using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor.Tabs;

/// <summary>
/// 検査と書き出し: 保存してから、ゲームと同じ検査をまとめて流す(読み込み、迷宮の辻褄、町から行けるか、ボットが歩けるか、イベント、印)。
/// ボットの通し(CampaignSim)は時間がかかるので、別のボタン。
/// </summary>
public sealed class CheckTab : IEditorTab
{
    /// <summary>ボットの編成(名前、クラス)。</summary>
    private static readonly (string Ja, string En, string[] Classes)[] Parties =
    [
        ("前衛と後衛の6人", "Balanced 6", ["fighter", "fighter", "paladin", "rogue", "cleric", "wizard"]),
        ("前衛の多い6人", "Front-heavy 6", ["fighter", "barbarian", "paladin", "ranger", "monk", "fighter"]),
        ("術者だけの6人", "Casters only 6", ["cleric", "druid", "wizard", "sorcerer", "warlock", "bard"]),
        ("3人", "Party of 3", ["fighter", "cleric", "wizard"]),
    ];

    private readonly ComboBox _party = new() { Width = 170, Margin = new Thickness(6, 0, 0, 0) };

    private readonly EditorContext _context;
    private readonly Action<string, bool> _say;
    private readonly Func<bool> _save;
    private readonly DockPanel _view = new() { Margin = new Thickness(12) };
    private readonly ListBox _results = new();
    private readonly TextBox _runs = new() { Text = "5", Width = 50 };
    private bool _busy;

    public CheckTab(EditorContext context, Action<string, bool> say, Func<bool> save)
    {
        _context = context;
        _say = say;
        _save = save;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Ui.Button(T("保存して検査する", "Save and check"), () => RunChecks()));
        bar.Children.Add(Ui.Button(T("ボットに通しで遊ばせる", "Play through with bots"), RunBots, T("選んだ編成のボットに、このシナリオを最初から最後まで遊ばせる(何分かかかる)。クリアできるか、どこで詰まるかが分かる", "Has a bot party play this scenario from start to finish (takes a few minutes), to see whether it can clear and where it gets stuck")));
        bar.Children.Add(new TextBlock { Text = T("回数", "Runs"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        bar.Children.Add(_runs);
        _party.ItemsSource = Parties.Select(x => T(x.Ja, x.En)).ToList();
        _party.SelectedIndex = 0;
        bar.Children.Add(new TextBlock { Text = T("編成", "Party"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        bar.Children.Add(_party);
        DockPanel.SetDock(bar, Dock.Top);
        _view.Children.Add(bar);
        var note = Ui.Note(T("書き出し: 上の「zip に書き出す」で zip にする。ドキュメント\\GravewardSpiral\\Scenarios に置くと、ゲームの「新しく始める」のシナリオに出る(エディタで作ったフォルダは、もうそこにある)。",
            "Export: use \"Export zip\" above to make a zip. Put it in Documents\\GravewardSpiral\\Scenarios and it appears in the game's New Game scenario list (folders made in the editor are already there)."));
        DockPanel.SetDock(note, Dock.Bottom);
        _view.Children.Add(note);
        _view.Children.Add(_results);
    }

    public string Key => "check";
    public string Header => T("検査と書き出し", "Check & Export");
    public UIElement View => _view;

    public void Reload() => _results.Items.Clear();

    /// <summary>注意の色(問題ではないが、意図したものか確かめたいもの)。</summary>
    private static readonly Brush Caution = Theme.Brush("#B7791F");

    /// <summary>結果の1行。頭の「○ / × / ！ / ・」は、通った・問題・注意・知らせのアイコンにする。</summary>
    private void Line(string text, Brush? color = null)
    {
        (string? icon, string body) = text switch
        {
            _ when text.StartsWith("○ ") => ("", text[2..]),
            _ when text.StartsWith("× ") => ("", text[2..]),
            _ when text.StartsWith("！ ") => ("\uE7BA", text[2..]),
            _ when text.StartsWith("・ ") => ("", text[2..]),
            _ => (null, text),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 2, 3) };
        if (icon != null)
            row.Children.Add(new TextBlock { Text = icon, FontFamily = Theme.Icons, FontSize = 13, Foreground = color ?? Ui.Dim, Margin = new Thickness(0, 2, 10, 0) });
        row.Children.Add(new TextBlock
        {
            Text = body, Foreground = color ?? Theme.Brush(Theme.Palette.Text), TextWrapping = TextWrapping.Wrap, MaxWidth = 1100,
            FontWeight = icon == null ? FontWeights.SemiBold : FontWeights.Normal,
        });
        _results.Items.Add(row);
    }

    /// <summary>まとめて検査する。問題の数を返す(画面にも出す)。</summary>
    public int RunChecks()
    {
        _results.Items.Clear();
        if (_context.Pack is not { } pack || !_save()) return -1;
        int problems = 0;
        void Problem(string text)
        {
            problems++;
            Line("× " + text, Ui.Warn);
        }
        // 注意: わざとかもしれないもの(遊べるが、作者に確かめてほしい)
        int cautions = 0;
        void Warn(string text)
        {
            cautions++;
            Line("！ " + text, Caution);
        }

        // 読み込み(ゲームが起動時に見るもの)
        var (content, data) = _context.LoadScenario();
        foreach (var p in content.Problems) Problem(T("読み込み: ", "Loading: ") + p);
        if (content.Problems.Count == 0) Line(T("○ 読み込みの検査を通った", "○ Loading check passed"), Ui.Good);
        if (data == null) return problems;

        // 迷宮の辻褄、町から行けるか、ボットが歩けるか
        var dungeon = pack.Dungeon();
        var structure = dungeon.Validate();
        foreach (var p in structure) Problem(T("迷宮: ", "Maze: ") + p);
        var reach = ReachabilityValidator.Validate(dungeon);
        foreach (var p in reach.Problems) Problem(T("行けるか: ", "Reachability: ") + p);
        if (structure.Count == 0 && reach.Ok) Line(T($"○ 迷宮の辻褄と、町から行けるか(立てるマス {reach.StandingCells}、行けるマス {reach.ReachedCells})", $"○ Maze consistency and reachability from town (standable cells {reach.StandingCells}, reachable cells {reach.ReachedCells})"), Ui.Good);
        var bot = MazeBot.Explore(dungeon);
        foreach (var p in bot.Problems) Problem(T("歩くボット: ", "Walking bot: ") + p);
        if (bot.Ok) Line(T($"○ ボットが全部の階を歩いて、主まで行って戻れた(歩いたマス {bot.Visited} / {bot.Standing})", $"○ The bot walked every floor, reached the boss and returned (cells walked {bot.Visited} / {bot.Standing})"), Ui.Good);
        else if (!bot.ReachedBoss) Problem(T("歩くボットが、最下層の主(印 boss:final)まで行けない", "The walking bot cannot reach the final boss (mark boss:final)"));


        // イベント
        var drafts = pack.Events.Select(e => ("events.json", JsonSerializer.SerializeToElement(e, ContentLibrary.JsonOptions)));
        var events = EventDraftCheck.Check(data, content, drafts, allowExisting: true, handPlaced: true);
        foreach (var p in events.Problems) Problem(T("イベント: ", "Event: ") + p);
        if (events.Problems.Count == 0) Line(T($"○ イベント {pack.Events.Count} 個に問題はない", $"○ No problems in {pack.Events.Count} events"), Ui.Good);

        // 印が指すイベント
        for (int i = 0; i < dungeon.Floors.Count; i++)
        {
            var f = dungeon.Floors[i];
            foreach (var c in f.AllCells())
            {
                if (f[c].Mark is { } m && m.StartsWith(EventPlacer.MarkPrefix) && !data.Events.ContainsKey(m[EventPlacer.MarkPrefix.Length..]))
                    Problem(T($"B{i + 1} {c} の印 {m} のイベントが無い", $"B{i + 1} {c}: the event for mark {m} does not exist"));
            }
        }
        // オリジナルの品: 元の武器・鎧(base)と見せかけ(looks_like)は標準の品だけを指す(シナリオの外へ持ち出すため)。呪文は標準のもの
        foreach (var o in pack.Items)
        {
            if ((string?)o["id"] is not { } id || !data.Items.TryGetValue(id, out var item)) continue;
            foreach (var (what, refId) in new[] { ("base", item.Base), ("looks_like", item.LooksLike?.Id) })
            {
                if (refId == null) continue;
                if (!data.Items.TryGetValue(refId, out var r)) Problem(T($"品 {id} の {what} の品 {refId} が無い", $"Item {id}: {what} item {refId} does not exist"));
                else if (r.OriginGuid != null) Problem(T($"品 {id} の {what} がシナリオの品 {refId} を指している(持ち出すと外で見つからないので、標準の品にする)", $"Item {id}: {what} points to scenario item {refId} (it would not be found outside once exported; use a standard item)"));
            }
            if (item.UseSpell != null && !data.Spells.ContainsKey(item.UseSpell)) Problem(T($"品 {id} の呪文 {item.UseSpell} が無い", $"Item {id}: spell {item.UseSpell} does not exist"));
        }
        // 文言の印のキー
        var texts = content.Texts("ja");
        foreach (var key in dungeon.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()
                     .Where(m => m.StartsWith("message:")).Select(m => m["message:".Length..]).Distinct())
            if (!texts.ContainsKey(key)) Problem(T($"文言の印のキー {key} の文が無い(文言のタブで書く)", $"No text for message mark key {key} (write it in the Text tab)"));
        // イベントの曲
        foreach (var e in pack.Events.Where(e => e.Bgm != null))
            if (!File.Exists(Path.Combine(pack.Directory, e.Bgm!)) && !File.Exists(Path.Combine(_context.ContentRoot, "srd", e.Bgm!)))
                Problem(T($"イベント {e.Id} の曲 {e.Bgm} が無い(曲のタブで足す)", $"Event {e.Id}: track {e.Bgm} does not exist (add it in the Music tab)"));
        // モンスターの絵(無くても遊べる)
        foreach (var o in pack.Monsters)
            if ((string?)o["id"] is { } mid && o["extends"] == null && o["art_from"] == null
                && !File.Exists(Path.Combine(pack.Directory, "art", "monsters", mid + ".png")))
                Line(T($"・ モンスター {mid} に絵が無い(データのタブで選ぶ。無くても遊べる)", $"・ Monster {mid} has no picture (choose one in the Data tab; playable without)"), Ui.Dim);
        Consistency(pack, data, dungeon, Problem, Warn);

        // 物語
        if (pack.Text.Prologue == null) Line(T("・ 序文がまだ無い(物語のタブ。無くても遊べる)", "・ No prologue yet (Story tab; playable without)"), Ui.Dim);
        string caution = cautions == 0 ? "" : T($"(注意 {cautions} 件。わざとなら、そのままでよい)", $" ({cautions} caution(s); fine if intended)");
        Line((problems == 0 ? T("検査に通りました", "Check passed") : T($"問題 {problems} 件", $"{problems} problem(s)")) + caution, problems == 0 ? Ui.Good : Ui.Warn);
        _say((problems == 0 ? T("検査に通りました", "Check passed") : T($"検査: 問題 {problems} 件", $"Check: {problems} problem(s)")) + caution, problems > 0);
        return problems;
    }

    /// <summary>
    /// 中身の整合性: ボスの印とボス、名のある敵、錠と鍵、行き先、エレベーター、門と「門を開ける」、置いた連動する組、知らない印。
    /// 問題は problem に渡す(数は呼ぶ側が数える)。知らせは「・」の行。
    /// </summary>
    private void Consistency(ScenarioPack pack, Core.Characters.GameData data, Core.Maze.Dungeon dungeon, Action<string> problem, Action<string> warn)
    {
        int before = _results.Items.Count;
        var floors = dungeon.Floors;
        var marks = new List<(int Floor, Core.Maze.GridPos Cell, string Mark)>();
        for (int i = 0; i < floors.Count; i++)
            foreach (var c in floors[i].AllCells())
                if (floors[i][c].Mark is { } m) marks.Add((i, c, m));
        string At(int floor, Core.Maze.GridPos c) => $"B{floor + 1} ({c.X}, {c.Y})";

        // ボスの印: 無いボスは問題。数・置いていない・最下層の主の階は注意(わざとかもしれない)
        foreach (var g in marks.Where(m => m.Mark.StartsWith("boss:")).GroupBy(m => m.Mark["boss:".Length..]))
        {
            if (g.Key.Length == 0)
            {
                problem(T($"{string.Join("・", g.Select(m => At(m.Floor, m.Cell)))} のボスの印で、どのボスか選んでいない(迷宮のタブの「どのボス」)", $"{string.Join(", ", g.Select(m => At(m.Floor, m.Cell)))}: boss mark without a chosen boss (\"Which boss\" on the Maze tab)"));
                continue;
            }
            if (!data.Bosses.ContainsKey(g.Key)) problem(T($"{At(g.First().Floor, g.First().Cell)} のボスの印 {g.Key} のボスが無い(物語のタブで足す)", $"{At(g.First().Floor, g.First().Cell)}: boss mark {g.Key} has no boss (add it on the Story tab)"));
            if (g.Count() > 1) warn(T($"ボス {BossName(g.Key)} が {g.Count()} か所にいる({string.Join("・", g.Select(m => At(m.Floor, m.Cell)))})", $"Boss {BossName(g.Key)} is in {g.Count()} places ({string.Join(", ", g.Select(m => At(m.Floor, m.Cell)))})"));
        }
        // ボスの名前(最下層の主、足したボスは名前と id)
        string BossName(string id) => id == GameSession.FinalBossId ? T("最下層の主", "the final boss")
            : pack.Bosses.FirstOrDefault(b => (string?)b["id"] == id)?["name"]?[Lang]?.ToString() is { Length: > 0 } n && n != id ? $"{n}({id})" : id;
        string final = "boss:" + GameSession.FinalBossId;
        if (marks.All(m => m.Mark != final))
            warn(T("最下層の主が迷宮にいない(物語のタブの「迷宮に置く」。いないと、主を倒して終わる物語にならない)", "The final boss is not in the maze (Story tab, \"Place in the maze\"; without it the story has no final fight)"));
        foreach (var m in marks.Where(m => m.Mark == final && m.Floor != floors.Count - 1))
            warn(T($"最下層の主が最下層ではなく {At(m.Floor, m.Cell)} にいる", $"The final boss is at {At(m.Floor, m.Cell)}, not on the bottom floor"));
        foreach (var o in pack.Bosses)
            if ((string?)o["id"] is { } id && marks.All(m => m.Mark != "boss:" + id))
                warn(T($"ボス {BossName(id)} が迷宮にいない(物語のタブの「迷宮に置く」。置かなければ戦わない)", $"Boss {BossName(id)} is not in the maze (Story tab, \"Place in the maze\"; unplaced bosses are never fought)"));

        // 名のある敵: 無い敵、無い階、ねぐらのマスがおかしい
        foreach (var n in pack.Named ?? [])
        {
            if (!data.NamedMonsters.ContainsKey(n.Id)) problem(T($"名のある敵 {n.Id} が無い", $"Named monster {n.Id} does not exist"));
            if (n.Floor < 0 || n.Floor >= floors.Count) { problem(T($"名のある敵 {n.Id} の階 B{n.Floor + 1} が無い", $"Named monster {n.Id}: floor B{n.Floor + 1} does not exist")); continue; }
            var f = floors[n.Floor];
            var at = new Core.Maze.GridPos(n.X, n.Y);
            if (n.Lair && (!f.InBounds(n.X, n.Y) || f[at].IsSolid))
                problem(T($"名のある敵 {n.Id} のねぐら {At(n.Floor, at)} が迷宮の外か岩盤(敵と宝のタブで置き直す)", $"Named monster {n.Id}: lair {At(n.Floor, at)} is outside the maze or solid rock (re-place it on the Enemies & loot tab)"));
        }

        // 錠と鍵: 錠のある階に鍵が無い
        for (int i = 0; i < floors.Count; i++)
        {
            var f = floors[i];
            // 門(gate:)の扉は仕掛けで開くので、鍵は要らない
            bool Gate(Core.Maze.GridPos c) => f[c].Mark?.StartsWith("gate:") == true;
            bool locked = f.AllCells().Any(c => Core.Maze.DirectionExtensions.All.Any(d => f[c][d] == Core.Maze.EdgeType.Locked
                && !Gate(c) && !(f.Neighbor(c, d) is { } n && Gate(n))));
            if (locked && marks.All(m => !(m.Floor == i && m.Mark == "key")))
                problem(T($"B{i + 1} に錠のある扉があるのに、鍵が無い(迷宮のタブで「あるもの」を鍵にする)", $"B{i + 1} has a locked door but no key (set a cell's \"What is here\" to Key on the Maze tab)"));
        }

        // テレポーターとシュートの行き先
        for (int i = 0; i < floors.Count; i++)
        {
            var f = floors[i];
            foreach (var c in f.AllCells().Where(c => f[c].Feature is Core.Maze.CellFeature.Teleporter or Core.Maze.CellFeature.Chute))
            {
                var t = f[c].Target;
                int tf = t?.Floor ?? (f[c].Feature == Core.Maze.CellFeature.Chute ? i + 1 : i);
                if (t == null || tf < 0 || tf >= floors.Count) { problem(T($"{At(i, c)} の行き先の階が無い", $"{At(i, c)}: destination floor does not exist")); continue; }
                var to = floors[tf];
                if (!to.InBounds(t.Value.Cell.X, t.Value.Cell.Y) || to[t.Value.Cell].IsSolid)
                    problem(T($"{At(i, c)} の行き先 {At(tf, t.Value.Cell)} が迷宮の外か岩盤", $"{At(i, c)}: destination {At(tf, t.Value.Cell)} is outside the maze or solid rock"));
                else if (tf == i && t.Value.Cell == c && f[c].Feature == Core.Maze.CellFeature.Teleporter)
                    Line(T($"・ {At(i, c)} のテレポーターの行き先が同じマス(迷宮のタブで行き先を入れる)", $"・ {At(i, c)}: the teleporter leads to itself (set its destination on the Maze tab)"), Ui.Dim);
            }
        }

        // エレベーター: 同じ組が2つの階より少ない
        foreach (var g in marks.Where(m => m.Mark.StartsWith("elevator:")).GroupBy(m => m.Mark))
            if (g.Select(m => m.Floor).Distinct().Count() < 2)
                problem(T($"エレベーターの組 {g.Key["elevator:".Length..]} が {g.Select(m => m.Floor).Distinct().Count()} つの階にしか無い(2つ以上の階に置く)", $"Elevator group {g.Key["elevator:".Length..]} is on only {g.Select(m => m.Floor).Distinct().Count()} floor(s) (place it on two or more)"));

        // 門と「門を開ける」
        var gates = marks.Where(m => m.Mark.StartsWith("gate:")).Select(m => m.Mark["gate:".Length..]).ToHashSet();
        var opens = new HashSet<string>();
        var walked = new HashSet<string>();
        void Walk(IEnumerable<EventStep> steps)
        {
            foreach (var st in steps)
            {
                if (st.Type == "open" && st.Id != null) opens.Add(st.Id);
                // 「別のイベント」(run)で続くイベントの中も見る
                if (st.Type == "run" && st.Id != null && walked.Add(st.Id) && data.Events.TryGetValue(st.Id, out var next)) Walk(next.Steps);
                foreach (var child in st.Children()) Walk(child);
            }
        }
        foreach (var e in data.Events.Values.Where(e => marks.Any(m => m.Mark == EventPlacer.MarkPrefix + e.Id) || pack.Events.Contains(e))) Walk(e.Steps);
        foreach (var g in gates.Where(g => !opens.Contains(g)))
            Line(T($"・ 門 {g} を開けるイベント(手順「門を開ける」)が見当たらない", $"・ No event opens gate {g} (step \"Open gate\")"), Ui.Dim);
        foreach (var o in opens.Where(o => !gates.Contains(o)))
            Line(T($"・ 「門を開ける」の門 {o} が迷宮に無い", $"・ Gate {o} opened by an event is not in the maze"), Ui.Dim);

        // 置いた連動する組: 組の一部だけ置いてある
        var placed = marks.Where(m => m.Mark.StartsWith(EventPlacer.MarkPrefix)).Select(m => m.Mark[EventPlacer.MarkPrefix.Length..]).ToHashSet();
        foreach (var chain in data.Events.Values.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!))
        {
            int have = chain.Count(e => placed.Contains(e.Id));
            if (have > 0 && have < chain.Count())
                problem(T($"連動するイベントの組 {chain.Key} が、{chain.Count()} 個のうち {have} 個しか置いていない({string.Join("・", chain.Where(e => !placed.Contains(e.Id)).Select(e => e.Id))} が無い)",
                    $"Linked event set {chain.Key}: only {have} of {chain.Count()} placed (missing {string.Join(", ", chain.Where(e => !placed.Contains(e.Id)).Select(e => e.Id))})"));
        }

        // 使われていないイベント: 迷宮のイベントで、散らさず、どのマスにも置かず、ほかのイベントからも続かないもの
        var runs = new HashSet<string>();
        void Runs(IEnumerable<EventStep> steps)
        {
            foreach (var st in steps)
            {
                if (st.Type == "run" && st.Id != null) runs.Add(st.Id);
                foreach (var child in st.Children()) Runs(child);
            }
        }
        foreach (var e in data.Events.Values) Runs(e.Steps);
        var unused = pack.Events.Where(e => e.Place == "maze" && e.Scatter == null && !placed.Contains(e.Id) && !runs.Contains(e.Id)).Select(e => EventList.Name(e) is { } n ? $"{n}({e.Id})" : e.Id).ToList();
        if (unused.Count > 0)
            warn(T($"使われていないイベント {unused.Count} 個(散らさず、迷宮にも置かず、ほかのイベントからも続かない): {string.Join("、", unused)}",
                $"{unused.Count} unused event(s) (not scattered, not placed, not run by other events): {string.Join(", ", unused)}"));
        var unplacedScatter = pack.Events.Where(e => e.Place == "maze" && e.Scatter != null && pack.EventsPlaced && !placed.Contains(e.Id)).Select(e => e.Id).ToList();
        if (unplacedScatter.Count > 0)
            warn(T($"散らすイベントなのに迷宮に置かれていないもの {unplacedScatter.Count} 個(基本のタブの「散らし直す」で置き直せる): {string.Join("、", unplacedScatter)}",
                $"{unplacedScatter.Count} scattered event(s) not placed in the maze (re-scatter on the Basic tab): {string.Join(", ", unplacedScatter)}"));

        // 知らない印
        foreach (var m in marks.Where(m => !(m.Mark == "key" || m.Mark.StartsWith(EventPlacer.MarkPrefix) || m.Mark.StartsWith("boss:") || m.Mark.StartsWith("message:")
                     || m.Mark.StartsWith("gate:") || m.Mark.StartsWith("elevator:"))))
            Line(T($"・ {At(m.Floor, m.Cell)} の印 {m.Mark} はゲームが知らない", $"・ {At(m.Floor, m.Cell)}: the game doesn't know mark {m.Mark}"), Ui.Dim);

        if (_results.Items.Count == before) Line(T("○ 中身の整合性(ボス・名のある敵・錠と鍵・行き先・エレベーター・門・連動する組)", "○ Content consistency (bosses, named monsters, locks and keys, destinations, elevators, gates, linked sets)"), Ui.Good);
    }

    /// <summary>ボットに通しで遊ばせる(別のスレッドで。終わったら結果を出す)。</summary>
    private async void RunBots()
    {
        if (_busy || _context.Pack is null || !_save()) return;
        int runs = int.TryParse(_runs.Text, out int r) ? Math.Clamp(r, 1, 50) : 5;
        var (content, data) = _context.LoadScenario();
        if (data == null || !content.IsValid)
        {
            Line(T("× 読み込みの検査を通らないので、ボットは遊べない", "× The loading check failed, so the bots cannot play"), Ui.Warn);
            return;
        }
        _busy = true;
        Line(T($"ボットが {runs} 回遊んでいます…", $"Bots are playing {runs} time(s)…"), Ui.Dim);
        var party = Parties[Math.Max(0, _party.SelectedIndex)];
        var results = await Task.Run(() => Enumerable.Range(1, runs).Select(i => CampaignSim.Run(data, content, party.Classes, (ulong)i)).ToList());
        _busy = false;
        int cleared = results.Count(x => x.Cleared);
        Line(T($"ボット({party.Ja}): {runs} 回中 {cleared} 回クリア({100 * cleared / runs}%)、平均 Lv {results.Average(x => x.AverageLevel):0.0}、全滅 {results.Average(x => x.Wipes):0.0} 回、" +
            $"一番深い階 B{results.Max(x => x.DeepestFloor)}", $"Bots ({party.En}): cleared {cleared} of {runs} runs ({100 * cleared / runs}%), average Lv {results.Average(x => x.AverageLevel):0.0}, wipes {results.Average(x => x.Wipes):0.0} per run, " +
            $"deepest floor B{results.Max(x => x.DeepestFloor)}"), cleared * 2 >= runs ? Ui.Good : Ui.Warn);
        foreach (var why in results.Where(x => !x.Cleared).GroupBy(x => x.FailReason ?? "?"))
            Line(T($"  クリアできなかった理由: {why.Key} ×{why.Count()}", $"  Reason for not clearing: {why.Key} ×{why.Count()}"), Ui.Dim);
    }
}
