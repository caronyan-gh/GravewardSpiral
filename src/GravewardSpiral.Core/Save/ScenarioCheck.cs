using System.Text.Json;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;

namespace GravewardSpiral.Core.Save;

/// <summary>検査の1行の種類: 通った、問題(遊べない・壊れている)、注意(わざとかもしれない)、知らせ(無くても遊べる)。</summary>
public enum CheckLevel { Pass, Problem, Caution, Note }

public sealed record CheckLine(CheckLevel Level, string Text);

/// <summary>
/// シナリオのまとめての検査(エディタの「検査」のタブと、配布する検査の道具 ScenarioCheck が同じものを呼ぶ)。
/// 読み込み、迷宮の辻褄、町から行けるか、ボットが歩けるか、イベント、印、品、文言、曲、絵、中身の整合性、物語。
/// 文は英日で、t(日本語, 英語) で選ぶ。
/// </summary>
public static class ScenarioCheck
{
    /// <summary>
    /// 検査する。content と data は標準のパックにシナリオを重ねて読んだもの(data が null なら読み込みの問題だけ出す)。
    /// baseContent は標準のパックだけ(連動する組の定義を見る)。eventName はイベントの見せ方(無ければ題か id)。
    /// </summary>
    public static List<CheckLine> Run(ScenarioPack pack, ContentLibrary content, GameData? data, ContentLibrary baseContent,
        Func<string, string, string> t, string lang, Func<EventDef, string?>? eventName = null)
    {
        var lines = new List<CheckLine>();
        void Pass(string text) => lines.Add(new(CheckLevel.Pass, text));
        void Problem(string text) => lines.Add(new(CheckLevel.Problem, text));
        void Warn(string text) => lines.Add(new(CheckLevel.Caution, text));
        void Note(string text) => lines.Add(new(CheckLevel.Note, text));

        // 読み込み(ゲームが起動時に見るもの)
        foreach (var p in content.Problems) Problem(t("読み込み: ", "Loading: ") + p);
        if (content.Problems.Count == 0) Pass(t("読み込みの検査を通った", "Loading check passed"));
        if (data == null) return lines;
        // 地図が1つも読めないときは、迷宮から先は調べられない(落ちずに、問題として返す)
        if (pack.Floors.Count == 0)
        {
            Problem(t("迷宮の階が1つも読めない(dungeons.json の floors と、maps の地図の書き方を確かめる)", "No maze floor could be read (check floors in dungeons.json and how the maps are written)"));
            return lines;
        }

        // 迷宮の辻褄、町から行けるか、ボットが歩けるか
        var dungeon = pack.Dungeon();
        var structure = dungeon.Validate();
        foreach (var p in structure) Problem(t("迷宮: ", "Maze: ") + p);
        var reach = ReachabilityValidator.Validate(dungeon);
        foreach (var p in reach.Problems) Problem(t("行けるか: ", "Reachability: ") + p);
        if (structure.Count == 0 && reach.Ok) Pass(t($"迷宮の辻褄と、町から行けるか(立てるマス {reach.StandingCells}、行けるマス {reach.ReachedCells})", $"Maze consistency and reachability from town (standable cells {reach.StandingCells}, reachable cells {reach.ReachedCells})"));
        var bot = MazeBot.Explore(dungeon);
        foreach (var p in bot.Problems) Problem(t("歩くボット: ", "Walking bot: ") + p);
        if (bot.Ok) Pass(t($"ボットが全部の階を歩いて、主まで行って戻れた(歩いたマス {bot.Visited} / {bot.Standing})", $"The bot walked every floor, reached the boss and returned (cells walked {bot.Visited} / {bot.Standing})"));
        else if (!bot.ReachedBoss) Problem(t("歩くボットが、最下層の主(印 boss:final)まで行けない", "The walking bot cannot reach the final boss (mark boss:final)"));

        // イベント
        var drafts = pack.Events.Select(e => ("events.json", JsonSerializer.SerializeToElement(e, ContentLibrary.JsonOptions)));
        var events = EventDraftCheck.Check(data, content, drafts, allowExisting: true, handPlaced: true);
        foreach (var p in events.Problems) Problem(t("イベント: ", "Event: ") + p);
        if (events.Problems.Count == 0) Pass(t($"イベント {pack.Events.Count} 個に問題はない", $"No problems in {pack.Events.Count} events"));

        // 印が指すイベント
        for (int i = 0; i < dungeon.Floors.Count; i++)
        {
            var f = dungeon.Floors[i];
            foreach (var c in f.AllCells())
            {
                if (f[c].Mark is { } m && m.StartsWith(EventPlacer.MarkPrefix) && !data.Events.ContainsKey(m[EventPlacer.MarkPrefix.Length..]))
                    Problem(t($"B{i + 1} {c} の印 {m} のイベントが無い", $"B{i + 1} {c}: the event for mark {m} does not exist"));
            }
        }
        // オリジナルの品: 元の武器・鎧(base)と見せかけ(looks_like)は標準の品だけを指す(シナリオの外へ持ち出すため)。呪文は標準のもの
        foreach (var o in pack.Items)
        {
            if ((string?)o["id"] is not { } id || !data.Items.TryGetValue(id, out var item)) continue;
            foreach (var (what, refId) in new[] { ("base", item.Base), ("looks_like", item.LooksLike?.Id) })
            {
                if (refId == null) continue;
                if (!data.Items.TryGetValue(refId, out var r)) Problem(t($"品 {id} の {what} の品 {refId} が無い", $"Item {id}: {what} item {refId} does not exist"));
                else if (r.OriginGuid != null) Problem(t($"品 {id} の {what} がシナリオの品 {refId} を指している(持ち出すと外で見つからないので、標準の品にする)", $"Item {id}: {what} points to scenario item {refId} (it would not be found outside once exported; use a standard item)"));
            }
            if (item.UseSpell != null && !data.Spells.ContainsKey(item.UseSpell)) Problem(t($"品 {id} の呪文 {item.UseSpell} が無い", $"Item {id}: spell {item.UseSpell} does not exist"));
        }
        // 文言の印のキー
        var texts = content.Texts("ja");
        foreach (var key in dungeon.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()
                     .Where(m => m.StartsWith("message:")).Select(m => m["message:".Length..]).Distinct())
            if (!texts.ContainsKey(key)) Problem(t($"文言の印のキー {key} の文が無い(文言のタブで書く)", $"No text for message mark key {key} (write it in the Text tab)"));
        // イベントの曲
        foreach (var e in pack.Events.Where(e => e.Bgm != null))
            if (content.FindFile(e.Bgm!) == null)
                Problem(t($"イベント {e.Id} の曲 {e.Bgm} が無い(曲のタブで足す)", $"Event {e.Id}: track {e.Bgm} does not exist (add it in the Music tab)"));
        // モンスターの絵(無くても遊べる)
        foreach (var o in pack.Monsters)
            if ((string?)o["id"] is { } mid && o["extends"] == null && o["art_from"] == null
                && !File.Exists(Path.Combine(pack.Directory, "art", "monsters", mid + ".png")))
                Note(t($"モンスター {mid} に絵が無い(データのタブで選ぶ。無くても遊べる)", $"Monster {mid} has no picture (choose one in the Data tab; playable without)"));
        int before = lines.Count;
        Consistency(pack, data, dungeon, baseContent, t, lang, eventName, Problem, Warn, Note);
        if (lines.Count == before) Pass(t("中身の整合性(ボス・名のある敵・錠と鍵・行き先・エレベーター・門・連動する組)", "Content consistency (bosses, named monsters, locks and keys, destinations, elevators, gates, linked sets)"));

        Authoring(pack, content, dungeon, t, lang, Warn, Note);

        // 物語(worlds.json はあるのに、このシナリオの id の項目が無いときは、id の違いを知らせる)
        if (pack.Text.Prologue == null)
        {
            var worldIds = WorldIds(pack.Directory);
            if (worldIds.Count > 0 && !worldIds.Contains(pack.Id))
                Warn(t($"worlds.json の id({string.Join("、", worldIds)})が、シナリオの id {pack.Id} と違うので、序文や階の名前が読まれない(id を {pack.Id} にする)",
                    $"worlds.json has id(s) {string.Join(", ", worldIds)}, not the scenario id {pack.Id}, so its prologue and floor names are not used (set the id to {pack.Id})"));
            else Note(t("序文がまだ無い(物語のタブ。無くても遊べる)", "No prologue yet (Story tab; playable without)"));
        }
        // ScenarioCheck の new が書いた雛形の文(TODO: で始まる)が残っている
        var todo = new List<string>();
        void Todo(LocalizedText? text, string ja, string en)
        {
            if (text != null && new[] { text.Get("ja"), text.Get("en") }.Any(x => x?.TrimStart().StartsWith("TODO", StringComparison.Ordinal) == true)) todo.Add(t(ja, en));
        }
        Todo(pack.Text.Prologue, "序文", "prologue");
        for (int i = 0; i < pack.Text.Floors.Count; i++)
        {
            Todo(pack.Text.Floors[i].Name, $"B{i + 1} の名前", $"B{i + 1} name");
            Todo(pack.Text.Floors[i].Enter, $"B{i + 1} の入ったときの文", $"B{i + 1} entry text");
        }
        Todo(pack.Text.Boss?.Name, "主の名前", "boss name");
        Todo(pack.Text.Boss?.Intro, "主の出会いの文", "boss intro");
        Todo(pack.Text.Boss?.Victory, "主を倒したときの文", "boss victory");
        if (todo.Count > 0)
            Warn(t($"worlds.json に雛形のままの文(TODO で始まる)が {todo.Count} 箇所ある。書き換えるか、要らない項目は消す: {string.Join("、", todo)}",
                $"worlds.json still has {todo.Count} template text(s) starting with TODO. Rewrite them or delete the entries: {string.Join(", ", todo)}"));
        return lines;
    }

    /// <summary>worlds.json に書いてある id(読めなければ空)。</summary>
    private static List<string> WorldIds(string dir)
    {
        try
        {
            string path = Path.Combine(dir, WorldPackDef.Collection + ".json");
            if (!File.Exists(path) || System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) is not System.Text.Json.Nodes.JsonArray arr) return [];
            return arr.OfType<System.Text.Json.Nodes.JsonObject>().Select(o => (string?)o["id"]).OfType<string>().ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// 作り方の注意(外から作った人がつまずいた所。わざとならそのままでよいので、注意か知らせにする。読まないフラグは記録だけのこともあるので知らせ):
    /// 立てたのにどこでも読まないフラグ、一度きりのイベントで again の無い「立ち去る」、上り・下り階段が2つ以上ある階、仕切りの無い階。
    /// </summary>
    private static void Authoring(ScenarioPack pack, ContentLibrary content, Dungeon dungeon, Func<string, string, string> t, string lang,
        Action<string> warn, Action<string> note)
    {
        static IEnumerable<EventStep> All(IEnumerable<EventStep> steps) => steps.SelectMany(s => s.Children().SelectMany(All).Prepend(s));

        // 立てたフラグを、どこかで読んでいるか: 読み込んだデータ全部(イベントの条件・噂・エレベーター・ボスなど)で、その名前が立てる手順の数より多く出てくるか
        var setters = pack.Events.SelectMany(e => All(e.Steps)).Where(s => s.Type == "flag" && s.Id != null).GroupBy(s => s.Id!).ToDictionary(g => g.Key, g => g.Count());
        if (setters.Count > 0)
        {
            var seen = setters.Keys.ToDictionary(k => k, _ => 0);
            void Count(JsonElement e)
            {
                switch (e.ValueKind)
                {
                    case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Count(p.Value); break;
                    case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Count(x); break;
                    case JsonValueKind.String when e.GetString() is { } s && seen.ContainsKey(s): seen[s]++; break;
                }
            }
            foreach (var collection in content.Collections)
                foreach (var item in content.All(collection)) Count(item.Data);
            foreach (var e in pack.Events) Count(JsonSerializer.SerializeToElement(e, ContentLibrary.JsonOptions));   // 書いたばかりで、まだ読み込みに無いとき
            foreach (var s in pack.Elevators) if (s.Power != null && seen.ContainsKey(s.Power)) seen[s.Power] += 2;
            foreach (var (flag, n) in setters)
                if (seen[flag] <= 2 * n)   // 読み込んだ分と書いた分で、立てる手順を2回数える。記録だけのフラグもあるので、知らせにする
                    note(t($"フラグ {flag} を立てているが、どの条件でも読んでいない(仕掛けに使うつもりなら、条件 flag で読む。記録だけなら、そのままでよい)",
                        $"Flag {flag} is set but never read by any condition (read it with a flag condition if it drives a puzzle; fine if it is only a record)"));
        }

        // 一度きりのイベントで、選ぶと何もせずに済んでしまう選択肢(again が無いと、もう起きない)
        foreach (var e in pack.Events.Where(e => e.Once != null))
            foreach (var choice in All(e.Steps).Where(s => s.Type == "choice"))
                foreach (var o in choice.Options.Where(o => o.Steps.All(s => s.Type is "text" or "sound") && !All(o.Steps).Any(s => s.Type == "again")))
                    warn(t($"イベント {e.Id} の選択肢「{o.Label?.Get(lang)}」は、選ぶとイベントが済んで、もう起きない(また来て選び直せるようにするなら、手順 again を足す)",
                        $"Event {e.Id}: choosing \"{o.Label?.Get(lang)}\" finishes the event for good (add an again step to let the party come back and choose again)"));

        // 門: 印のマスに錠の辺(L)が無ければ、開けても何も変わらない。手順 open は、そのイベントが起きた階の門だけを開ける
        var gateFloors = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (int i = 0; i < dungeon.Floors.Count; i++)
        {
            var f = dungeon.Floors[i];
            foreach (var c in f.AllCells().Where(c => f[c].Mark?.StartsWith("gate:") == true))
            {
                string id = f[c].Mark!["gate:".Length..];
                if (!gateFloors.TryGetValue(id, out var set)) gateFloors[id] = set = [];
                set.Add(i);
                if (!Maze.DirectionExtensions.All.Any(d => f[c][d] == Maze.EdgeType.Locked))
                    warn(t($"B{i + 1} ({c.X}, {c.Y}) の門 {id} のマスに錠の辺(L)が無い(開けても何も変わらない。L の隣のマスに印を置く)",
                        $"Gate {id} at B{i + 1} ({c.X}, {c.Y}) has no locked edge (L) on its cell (opening it changes nothing; put the mark next to an L)"));
            }
        }
        var placedOn = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (int i = 0; i < dungeon.Floors.Count; i++)
            foreach (var c in dungeon.Floors[i].AllCells())
                if (dungeon.Floors[i][c].Mark is { } m && m.StartsWith(EventPlacer.MarkPrefix))
                {
                    string ev = m[EventPlacer.MarkPrefix.Length..];
                    if (!placedOn.TryGetValue(ev, out var set)) placedOn[ev] = set = [];
                    set.Add(i);
                }
        foreach (var e in pack.Events)
            foreach (var open in All(e.Steps).Where(s => s.Type == "open" && s.Id != null))
                if (placedOn.TryGetValue(e.Id, out var floors) && gateFloors.TryGetValue(open.Id!, out var gates) && !floors.Overlaps(gates))
                    warn(t($"イベント {e.Id}(B{string.Join("・B", floors.Select(x => x + 1))})の open {open.Id} は、門のある B{string.Join("・B", gates.Select(x => x + 1))} を開けない(open は、イベントの起きた階の門だけを開ける)",
                        $"Event {e.Id} (B{string.Join("/B", floors.Select(x => x + 1))}) cannot open gate {open.Id} on B{string.Join("/B", gates.Select(x => x + 1))} (open only works on the floor where the event fires)"));

        // 階段が2つ以上ある階(着くのは見つけた1つ目だけ)と、仕切りの無い階
        for (int i = 0; i < dungeon.Floors.Count; i++)
        {
            var f = dungeon.Floors[i];
            foreach (var (feature, ja, en) in new[] { (Maze.CellFeature.StairsUp, "上り階段", "up stairs"), (Maze.CellFeature.StairsDown, "下り階段", "down stairs") })
            {
                int count = f.AllCells().Count(c => f[c].Feature == feature);
                if (count > 1)
                    warn(t($"B{i + 1} に{ja}が {count} つある(階段で着くのは、そのうちの1つだけ)", $"B{i + 1} has {count} {en} (arriving by stairs always uses only one of them)"));
            }
            bool walled = f.AllCells().Where(c => !f[c].IsSolid).Any(c => new[] { Maze.Direction.East, Maze.Direction.South }
                .Any(d => f.Neighbor(c, d) is { } n && !f[n].IsSolid && f[c][d] != Maze.EdgeType.Open));
            if (!walled && f.AllCells().Count(c => !f[c].IsSolid) >= 9)
                note(t($"B{i + 1} は仕切りの無い1部屋(迷宮にするなら、中に壁や扉を足す。ScenarioCheck new で自動生成した迷宮から始めることもできる)",
                    $"B{i + 1} is a single open room (add inner walls and doors for a maze, or start from a maze generated by ScenarioCheck new)"));
        }
    }

    /// <summary>中身の整合性: ボスの印とボス、名のある敵、錠と鍵、行き先、エレベーター、門と「門を開ける」、置いた連動する組、知らない印。</summary>
    private static void Consistency(ScenarioPack pack, GameData data, Dungeon dungeon, ContentLibrary baseContent, Func<string, string, string> t, string lang,
        Func<EventDef, string?>? eventName, Action<string> problem, Action<string> warn, Action<string> note)
    {
        var floors = dungeon.Floors;
        var marks = new List<(int Floor, GridPos Cell, string Mark)>();
        for (int i = 0; i < floors.Count; i++)
            foreach (var c in floors[i].AllCells())
                if (floors[i][c].Mark is { } m) marks.Add((i, c, m));
        string At(int floor, GridPos c) => $"B{floor + 1} ({c.X}, {c.Y})";

        // ボスの印: 無いボスは問題。数・置いていない・最下層の主の階は注意(わざとかもしれない)
        foreach (var g in marks.Where(m => m.Mark.StartsWith("boss:")).GroupBy(m => m.Mark["boss:".Length..]))
        {
            if (g.Key.Length == 0)
            {
                problem(t($"{string.Join("・", g.Select(m => At(m.Floor, m.Cell)))} のボスの印で、どのボスか選んでいない(迷宮のタブの「どのボス」)", $"{string.Join(", ", g.Select(m => At(m.Floor, m.Cell)))}: boss mark without a chosen boss (\"Which boss\" on the Maze tab)"));
                continue;
            }
            if (!data.Bosses.ContainsKey(g.Key)) problem(t($"{At(g.First().Floor, g.First().Cell)} のボスの印 {g.Key} のボスが無い(物語のタブで足す)", $"{At(g.First().Floor, g.First().Cell)}: boss mark {g.Key} has no boss (add it on the Story tab)"));
            if (g.Count() > 1) warn(t($"ボス {BossName(g.Key)} が {g.Count()} か所にいる({string.Join("・", g.Select(m => At(m.Floor, m.Cell)))})", $"Boss {BossName(g.Key)} is in {g.Count()} places ({string.Join(", ", g.Select(m => At(m.Floor, m.Cell)))})"));
        }
        // ボスの名前(最下層の主、足したボスは名前と id)
        string BossName(string id) => id == GameSession.FinalBossId ? t("最下層の主", "the final boss")
            : pack.Bosses.FirstOrDefault(b => (string?)b["id"] == id)?["name"]?[lang]?.ToString() is { Length: > 0 } n && n != id ? $"{n}({id})" : id;
        string final = "boss:" + GameSession.FinalBossId;
        // 手順 ending で終わるシナリオ(主をイベントの中で倒すなど)は、最下層の主の印が無くてよい
        bool endsByEvent = pack.Events.Any(e => AllSteps(e.Steps).Any(s => s.Type == "ending"));   // このシナリオのイベントだけ(試しの迷宮の見本は数えない)
        if (marks.All(m => m.Mark != final) && !endsByEvent)
            warn(t("最下層の主が迷宮にいない(物語のタブの「迷宮に置く」。いないと、主を倒して終わる物語にならない)", "The final boss is not in the maze (Story tab, \"Place in the maze\"; without it the story has no final fight)"));
        foreach (var m in marks.Where(m => m.Mark == final && m.Floor != floors.Count - 1))
            warn(t($"最下層の主が最下層ではなく {At(m.Floor, m.Cell)} にいる", $"The final boss is at {At(m.Floor, m.Cell)}, not on the bottom floor"));
        // イベントの戦闘(combat の boss)で出すボスは、迷宮に印が無くてよい
        var fought = data.Events.Values.SelectMany(e => AllSteps(e.Steps)).Where(s => s.Type == "combat" && s.Boss != null).Select(s => s.Boss!).ToHashSet(StringComparer.Ordinal);
        foreach (var o in pack.Bosses)
            if ((string?)o["id"] is { } id && marks.All(m => m.Mark != "boss:" + id) && !fought.Contains(id))
                warn(t($"ボス {BossName(id)} が迷宮にもイベントの戦闘にもいない(物語のタブの「迷宮に置く」か、イベントの戦闘の boss。どちらも無ければ戦わない)", $"Boss {BossName(id)} is neither in the maze nor in an event fight (Story tab, \"Place in the maze\", or an event combat's boss; otherwise it is never fought)"));

        static IEnumerable<EventStep> AllSteps(IEnumerable<EventStep> steps) =>
            steps.SelectMany(s => s.Children().SelectMany(AllSteps).Prepend(s));

        // 名のある敵: 無い敵、無い階、ねぐらのマスがおかしい
        foreach (var n in pack.Named ?? [])
        {
            if (!data.NamedMonsters.ContainsKey(n.Id)) problem(t($"名のある敵 {n.Id} が無い", $"Named monster {n.Id} does not exist"));
            if (n.Floor < 0 || n.Floor >= floors.Count) { problem(t($"名のある敵 {n.Id} の階 B{n.Floor + 1} が無い", $"Named monster {n.Id}: floor B{n.Floor + 1} does not exist")); continue; }
            var f = floors[n.Floor];
            var at = new GridPos(n.X, n.Y);
            if (n.Lair && (!f.InBounds(n.X, n.Y) || f[at].IsSolid))
                problem(t($"名のある敵 {n.Id} のねぐら {At(n.Floor, at)} が迷宮の外か岩盤(敵と宝のタブで置き直す)", $"Named monster {n.Id}: lair {At(n.Floor, at)} is outside the maze or solid rock (re-place it on the Enemies & loot tab)"));
        }

        // 錠と鍵: 錠のある階に鍵が無い
        for (int i = 0; i < floors.Count; i++)
        {
            var f = floors[i];
            // 門(gate:)の扉は仕掛けで開くので、鍵は要らない
            bool Gate(GridPos c) => f[c].Mark?.StartsWith("gate:") == true;
            bool locked = f.AllCells().Any(c => DirectionExtensions.All.Any(d => f[c][d] == EdgeType.Locked
                && !Gate(c) && !(f.Neighbor(c, d) is { } n && Gate(n))));
            if (locked && marks.All(m => !(m.Floor == i && m.Mark == "key")))
                problem(t($"B{i + 1} に錠のある扉があるのに、鍵が無い(迷宮のタブで「あるもの」を鍵にする)", $"B{i + 1} has a locked door but no key (set a cell's \"What is here\" to Key on the Maze tab)"));
        }

        // テレポーターとシュートの行き先
        for (int i = 0; i < floors.Count; i++)
        {
            var f = floors[i];
            foreach (var c in f.AllCells().Where(c => f[c].Feature is CellFeature.Teleporter or CellFeature.Chute))
            {
                var target = f[c].Target;
                int tf = target?.Floor ?? (f[c].Feature == CellFeature.Chute ? i + 1 : i);
                if (target == null || tf < 0 || tf >= floors.Count) { problem(t($"{At(i, c)} の行き先の階が無い", $"{At(i, c)}: destination floor does not exist")); continue; }
                var to = floors[tf];
                if (!to.InBounds(target.Value.Cell.X, target.Value.Cell.Y) || to[target.Value.Cell].IsSolid)
                    problem(t($"{At(i, c)} の行き先 {At(tf, target.Value.Cell)} が迷宮の外か岩盤", $"{At(i, c)}: destination {At(tf, target.Value.Cell)} is outside the maze or solid rock"));
                else if (tf == i && target.Value.Cell == c && f[c].Feature == CellFeature.Teleporter)
                    note(t($"{At(i, c)} のテレポーターの行き先が同じマス(迷宮のタブで行き先を入れる)", $"{At(i, c)}: the teleporter leads to itself (set its destination on the Maze tab)"));
            }
        }

        // エレベーター: 同じ組が2つの階より少ない
        foreach (var g in marks.Where(m => m.Mark.StartsWith("elevator:")).GroupBy(m => m.Mark))
            if (g.Select(m => m.Floor).Distinct().Count() < 2)
                problem(t($"エレベーターの組 {g.Key["elevator:".Length..]} が {g.Select(m => m.Floor).Distinct().Count()} つの階にしか無い(2つ以上の階に置く)", $"Elevator group {g.Key["elevator:".Length..]} is on only {g.Select(m => m.Floor).Distinct().Count()} floor(s) (place it on two or more)"));

        // 門と「門を開ける」
        var gates = marks.Where(m => m.Mark.StartsWith("gate:")).Select(m => m.Mark["gate:".Length..]).ToHashSet();
        var opens = new HashSet<string>();
        var warps = new HashSet<string>();
        var walked = new HashSet<string>();
        void Walk(IEnumerable<EventStep> steps)
        {
            foreach (var st in steps)
            {
                if (st.Type is "open" or "close" && st.Id != null) opens.Add(st.Id);
                if (st.Type == "warp" && st.Id != null) warps.Add(st.Id);
                // 「別のイベント」(run)とタイマー(timer)で続くイベントの中も見る
                if (st.Type is "run" or "timer" && st.Id != null && walked.Add(st.Id) && data.Events.TryGetValue(st.Id, out var next)) Walk(next.Steps);
                foreach (var child in st.Children()) Walk(child);
            }
        }
        foreach (var e in data.Events.Values.Where(e => marks.Any(m => m.Mark == EventPlacer.MarkPrefix + e.Id) || pack.Events.Contains(e))) Walk(e.Steps);
        foreach (var g in gates.Where(g => !opens.Contains(g)))
            note(t($"門 {g} を開けるイベント(手順「門を開ける」)が見当たらない", $"No event opens gate {g} (step \"Open gate\")"));
        var dests = marks.Where(m => m.Mark.StartsWith("dest:")).Select(m => m.Mark["dest:".Length..]).ToHashSet();
        foreach (var w in warps.Where(w => !dests.Contains(w)))
            problem(t($"「別のマスへ移す」の着地点 {w} が迷宮に無い(迷宮のタブで「着地点」を置く)", $"Landing point {w} used by \"Move the party\" is not in the maze (place a \"Landing point\" on the Maze tab)"));
        foreach (var o in opens.Where(o => !gates.Contains(o)))
            note(t($"「門を開ける」の門 {o} が迷宮に無い", $"Gate {o} opened by an event is not in the maze"));

        // 置いた連動する組: 組の一部だけ置いてある
        foreach (var error in EventAuthoring.Check(pack, data.Events.Values, baseContent.All("event_sets").Select(x => x.As<EventSetDef>()))) problem(error);
        var placed = marks.Where(m => m.Mark.StartsWith(EventPlacer.MarkPrefix)).Select(m => m.Mark[EventPlacer.MarkPrefix.Length..]).ToHashSet();
        foreach (var chain in data.Events.Values.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!))
        {
            int have = chain.Count(e => placed.Contains(e.Id));
            if (have > 0 && have < chain.Count())
                problem(t($"連動するイベントの組 {chain.Key} が、{chain.Count()} 個のうち {have} 個しか置いていない({string.Join("・", chain.Where(e => !placed.Contains(e.Id)).Select(e => e.Id))} が無い)",
                    $"Linked event set {chain.Key}: only {have} of {chain.Count()} placed (missing {string.Join(", ", chain.Where(e => !placed.Contains(e.Id)).Select(e => e.Id))})"));
        }

        // 使われていないイベント: 迷宮のイベントで、散らさず、どのマスにも置かず、ほかのイベントからも続かない(run・timer)もの
        var runs = new HashSet<string>();
        void Runs(IEnumerable<EventStep> steps)
        {
            foreach (var st in steps)
            {
                if (st.Type is "run" or "timer" && st.Id != null) runs.Add(st.Id);   // タイマーも、歩いたあとでそのイベントを起こす
                foreach (var child in st.Children()) Runs(child);
            }
        }
        foreach (var e in data.Events.Values) Runs(e.Steps);
        string Named(EventDef e) => (eventName?.Invoke(e) ?? e.Title?.Get(lang)) is { Length: > 0 } n ? $"{n}({e.Id})" : e.Id;
        var unused = pack.Events.Where(e => e.Place == "maze" && e.Scatter == null && !placed.Contains(e.Id) && !runs.Contains(e.Id)).Select(Named).ToList();
        if (unused.Count > 0)
            warn(t($"使われていないイベント {unused.Count} 個(散らさず、迷宮にも置かず、ほかのイベントからも続かない): {string.Join("、", unused)}",
                $"{unused.Count} unused event(s) (not scattered, not placed, not run by other events): {string.Join(", ", unused)}"));
        var unplacedScatter = pack.Events.Where(e => e.Place == "maze" && e.Scatter != null && pack.EventsPlaced && !placed.Contains(e.Id)).Select(e => e.Id).ToList();
        if (unplacedScatter.Count > 0)
            warn(t($"散らすイベントなのに迷宮に置かれていないもの {unplacedScatter.Count} 個(基本のタブの「散らし直す」で置き直せる): {string.Join("、", unplacedScatter)}",
                $"{unplacedScatter.Count} scattered event(s) not placed in the maze (re-scatter on the Basic tab): {string.Join(", ", unplacedScatter)}"));

        // スイッチ待ちのエレベーター: そのフラグを立てるイベントが、迷宮に置いてあるか
        var setFlags = new HashSet<string>();
        void Sets(IEnumerable<EventStep> steps)
        {
            foreach (var st in steps)
            {
                if (st.Type == "flag" && st.Id != null) setFlags.Add(st.Id);
                foreach (var child in st.Children()) Sets(child);
            }
        }
        foreach (var e in data.Events.Values.Where(e => placed.Contains(e.Id) || runs.Contains(e.Id))) Sets(e.Steps);
        foreach (var s in pack.Elevators.Where(s => s.Power != null && !setFlags.Contains(s.Power)))
            warn(t($"エレベーター ({s.X},{s.Y}) のスイッチ(フラグ {s.Power})を入れるイベントが迷宮に無い(このままでは動かない。標準のイベント「エレベーターのスイッチ」を置く)",
                $"No event in the maze turns on the switch (flag {s.Power}) of the elevator at ({s.X},{s.Y}); it never runs. Place the standard event \"Elevator switch\""));

        // 知らない印
        foreach (var m in marks.Where(m => !(m.Mark == "key" || m.Mark.StartsWith(EventPlacer.MarkPrefix) || m.Mark.StartsWith("boss:") || m.Mark.StartsWith("message:")
                     || m.Mark.StartsWith("gate:") || m.Mark.StartsWith("elevator:"))))
            note(t($"{At(m.Floor, m.Cell)} の印 {m.Mark} はゲームが知らない", $"{At(m.Floor, m.Cell)}: the game doesn't know mark {m.Mark}"));
    }
}
