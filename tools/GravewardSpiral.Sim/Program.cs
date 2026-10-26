// 戦闘を自動で何回も回して、勝率を出す(調整用)。
//
//   dotnet run --project tools/GravewardSpiral.Sim -- [--level L] [--party fighter,fighter,cleric,...] [--fights N] [--seed S]
//                                                    [--monster ID[xN]] [--depth D | --floors F]
//
//   --level    パーティのレベル(省略すると 1)
//   --party    クラスを並べる(省略すると ファイター2・パラディン・ローグ・レンジャー・クレリック)
//   --monster  決まった敵と戦う(goblinx3 のように数も書ける。何度でも書ける)
//   --depth    深さの割合(0〜1)の遭遇表で戦う。--floors F なら全階(B1〜BF)を順に試す
//   --boss ID  bosses.json のボスと戦う(強化の倍率つき)
//   --at-level パーティのレベルを、その階の目安のレベル(切り捨て、最低 1)にする(--level より優先)
//   --gear N   装備を強くする: 武器・鎧・盾を +N にし、守りの指輪と守りの外套を付ける(同調済み)
//   --affixes K  武器・鎧・盾に、パーティのレベルで追加効果を K 個ずつ付ける(--gear と一緒に使う)
//   毎回、新しいパーティで戦う(HP は減ったまま持ち越さない)
//
//   --check-events F [F ...]  下書きのイベントの JSON(配列)を検査する(部品、知らない項目名、id の重なり)。問題があれば 1 で終わる
//   --allow-existing          --check-events で、パックに既にある id を重なりとしない(置き換える下書き)
//
//   --campaign         ボットのパーティにシナリオを最初から最後まで遊ばせる(迷宮はシードごとに作る)
//                      --level L を付けると L のパーティで始め、世界のレベル帯もその強さに合わせる(周回)
//   --runs N           編成ごとに何回遊ぶか(シード 1〜N。省略すると 20)
//   --parties A;B;...  編成を並べる(1つは fighter,cleric,... の形)。省略すると、いろいろな編成を試す
//   --verbose          1回ごとの流れも出す
//   --simple           単純なボット(逃げない・水薬なし・装備を替えない)で遊ばせる(比べる用)
//   --event-census     新しいゲームと同じ手順で世界を --runs N 個(シード 1〜N)作り、置かれたイベントを数える(--floors F で階の数、--level L で強いクランの世界、--heard K で伝説の噂を K 回聞いたクラン)
//   --ai-world         本物の AI(資格情報マネージャーか ANTHROPIC_API_KEY のキー)に、自由な迷宮の世界の文を書かせて出す
//                      (--seed S、--floors F、--model M で AI のモデルを替える。お金がかかるので確かめるときだけ。--scenario none と一緒に使う)
//   --fill-scenario    --scenario のシナリオ(フォルダ)の、決めていない中身(出る敵・名のある敵・散らすイベント)を選んで書き込む
//                      (シナリオエディタの「決めていないところを全部埋める」と同じ。--seed S で選び方を変える。フォルダを書き換える)
//   --convert-elevators  --scenario のシナリオ(フォルダ)の、前の形のエレベーター(階ごとの elevator:組 の印)を縦穴(dungeons.json の elevators)に直す
//                      (マップの e と印の行を消し、組の範囲で止まる階が一番多い座標を選ぶ。--seed S で選び方を変える。フォルダを書き換える)
//   --scenario ID      重ねて読むシナリオ(Content/scenarios/ID か、シナリオのフォルダ)。省略すると第1部 gravekeeper、none なら自由な迷宮(標準のパックだけ)
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Monsters;

Console.OutputEncoding = System.Text.Encoding.UTF8;

int level = 1, fights = 500, floors = 0, gear = 0, affixes = 0;
int heardTimes = 0;
bool atLevel = false, campaign = false, verbose = false, simple = false, aiWorld = false, eventCensus = false;
int runs = 20;
string? partiesArg = null;
string? aiModel = null;
var checkFiles = new List<string>();
bool allowExisting = false, fillScenario = false, convertElevators = false;
string? bossId = null;
string scenarioId = ScenarioCatalog.Gravekeeper;
ulong seed = 1;
double? depth = null;
string[] classes = ["fighter", "fighter", "paladin", "rogue", "ranger", "cleric"];
var fixedGroups = new List<(string Id, int Count)>();

for (int i = 0; i < args.Length; i++)
{
    string next = i + 1 < args.Length ? args[i + 1] : "";
    switch (args[i])
    {
        case "--level": level = int.Parse(next); i++; break;
        case "--gear": gear = int.Parse(next); i++; break;
        case "--affixes": affixes = int.Parse(next); i++; break;
        case "--party": classes = next.Split(','); i++; break;
        case "--fights": fights = int.Parse(next); i++; break;
        case "--seed": seed = ulong.Parse(next); i++; break;
        case "--depth": depth = double.Parse(next); i++; break;
        case "--floors": floors = int.Parse(next); i++; break;
        case "--at-level": atLevel = true; break;
        case "--boss": bossId = next; i++; break;
        case "--campaign": campaign = true; break;
        case "--ai-world": aiWorld = true; break;
        case "--event-census": eventCensus = true; break;
        case "--heard": heardTimes = int.Parse(next); i++; break;
        case "--model": aiModel = next; i++; break;
        case "--scenario": scenarioId = next; i++; break;
        case "--allow-existing": allowExisting = true; break;
        case "--fill-scenario": fillScenario = true; break;
        case "--convert-elevators": convertElevators = true; break;
        case "--check-events":
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--")) checkFiles.Add(args[++i]);
            break;
        case "--runs": runs = int.Parse(next); i++; break;
        case "--parties": partiesArg = next; i++; break;
        case "--verbose": verbose = true; break;
        case "--simple": simple = true; break;
        case "--monster":
        {
            var parts = next.Split('x');
            fixedGroups.Add((parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 1));
            i++;
            break;
        }
        default:
            Console.Error.WriteLine($"知らない引数: {args[i]}");
            return 2;
    }
}

string contentRoot = FindContent();
var content = scenarioId == "none"
    ? ContentLibrary.Load(contentRoot)
    : ContentLibrary.Load(contentRoot, Directory.Exists(scenarioId) ? scenarioId : Path.Combine(contentRoot, "scenarios", scenarioId));   // フォルダを渡せば、そのシナリオのパック
if (!content.IsValid)
{
    foreach (var p in content.Problems) Console.Error.WriteLine(p);
    return 1;
}
var data = GameData.Load(content);

if (fillScenario)
{
    // シナリオの中身を書き込む(ScenarioFill。エディタと同じもの)
    string folder = Directory.Exists(scenarioId) ? scenarioId : Path.Combine(contentRoot, "scenarios", scenarioId);
    var fillProblems = new List<string>();
    var pack = GravewardSpiral.Core.Save.ScenarioPack.Load(folder, fillProblems);
    foreach (var x in fillProblems) Console.Error.WriteLine(x);
    var baseContent = ContentLibrary.Load(contentRoot);
    var done = GravewardSpiral.Core.Save.ScenarioFill.Fill(pack, data, baseContent, seed == 0 ? 1 : seed, GravewardSpiral.Core.Save.ScenarioFill.Parts.All);
    pack.Save(baseContent);
    Console.WriteLine($"埋めたもの: {done}(顔ぶれ {pack.Rosters.Count(r => r.Count > 0)} 階、名のある敵 {pack.Named?.Count}、散らすイベントの印 {pack.Floors.Sum(f => f.AllCells().Count(c => f[c].Mark?.StartsWith("event:") == true))})");
    return 0;
}

if (convertElevators)
{
    // 前の形のエレベーターを縦穴に直す。マップは文字で直す(e を空きに、elevator: の印の行を消す)ので、ほかの書き方は変わらない
    string folder = Directory.Exists(scenarioId) ? scenarioId : Path.Combine(contentRoot, "scenarios", scenarioId);
    string dungeonsPath = Path.Combine(folder, "dungeons.json");
    var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(dungeonsPath))!.AsArray();
    foreach (var defNode in root.OfType<System.Text.Json.Nodes.JsonObject>())
    {
        var paths = defNode["floors"]!.AsArray().Select(x => (string)x!).ToList();
        var floorsList = paths.Select(p => GravewardSpiral.Core.Maze.MazeTextFormat.Parse(File.ReadAllText(Path.Combine(folder, p)), p)).ToList();
        var old = floorsList.SelectMany((f, i) => f.AllCells().Where(c => f[c].Feature == GravewardSpiral.Core.Maze.CellFeature.Elevator).Select(c => (Floor: i, Cell: c))).ToList();
        if (old.Count == 0) continue;
        var existing = defNode["elevators"] is { } shaftsNode
            ? System.Text.Json.JsonSerializer.Deserialize<List<GravewardSpiral.Core.Maze.ElevatorShaft>>(shaftsNode, ContentLibrary.JsonOptions) ?? []
            : [];
        var made = GravewardSpiral.Core.Maze.Elevators.FromLegacy(floorsList, new GravewardSpiral.Core.Rng.Pcg32(seed == 0 ? 1 : seed, 7), existing);
        // 縦穴にできた組の、前のマスだけを消す(できなかった組はマップに残す)
        var kept = floorsList.SelectMany((f, i) => f.AllCells().Where(c => f[c].Feature == GravewardSpiral.Core.Maze.CellFeature.Elevator
            && f[c].Mark is { } m && !made.Any(s => s.Mark == m)).Select(c => (Floor: i, Cell: c))).ToHashSet();
        foreach (var group in old.Where(o => !kept.Contains(o)).GroupBy(o => o.Floor))
        {
            string file = Path.Combine(folder, paths[group.Key]);
            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n').ToList();
            int walls = lines.IndexOf("walls");
            foreach (var (_, cell) in group)
            {
                var row = lines[walls + 1 + 2 * cell.Y + 1].ToCharArray();
                if (row[2 * cell.X + 1] == 'e') row[2 * cell.X + 1] = ' ';
                lines[walls + 1 + 2 * cell.Y + 1] = new string(row);
                lines.RemoveAll(l => l.StartsWith($"mark {cell.X} {cell.Y} elevator:"));
            }
            File.WriteAllText(file, string.Join("\n", lines));
        }
        existing.AddRange(made);
        defNode["elevators"] = System.Text.Json.JsonSerializer.SerializeToNode(existing, ContentLibrary.JsonOptions);
        foreach (var s in made)
            Console.WriteLine($"{defNode["id"]}: 縦穴 ({s.X},{s.Y}) B{s.From}〜B{s.To}、止まる階 {string.Join("・", GravewardSpiral.Core.Maze.Elevators.Stops(floorsList, s).Select(f => $"B{f + 1}"))}");
        if (kept.Count > 0) Console.WriteLine($"{defNode["id"]}: 縦穴にできなかったエレベーター {kept.Count} マスは、そのまま残した");
    }
    File.WriteAllText(dungeonsPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    return 0;
}

if (checkFiles.Count > 0)
{
    // 下書きの検査(中身は Core の EventDraftCheck。シナリオエディタも同じものを使う)
    var problems = new List<string>();
    var drafts = checkFiles.SelectMany(f => GravewardSpiral.Core.Events.EventDraftCheck.ReadFile(f, problems)).ToList();
    var result = GravewardSpiral.Core.Events.EventDraftCheck.Check(data, content, drafts, allowExisting);
    problems.AddRange(result.Problems);
    foreach (var x in problems) Console.WriteLine(x);
    Console.WriteLine(problems.Count == 0 ? $"OK: {result.Count} 個のイベントに問題はありません" : $"問題 {problems.Count} 件({result.Count} 個のイベント)");
    return problems.Count == 0 ? 0 : 1;
}
var settings = EncounterSettings.Load(content);
foreach (var (id, _) in fixedGroups.Where(g => !data.Monsters.ContainsKey(g.Id)))
{
    Console.Error.WriteLine($"モンスター '{id}' がありません");
    return 1;
}

var gearRng = new GravewardSpiral.Core.Rng.Pcg32(seed, 99);
List<Character> PartyAt(int lv) => classes.Select((c, i) => Equip(CharacterFactory.QuickCreate(data, $"#{i + 1}", "human", c, lv), lv)).ToList();

// --gear / --affixes: 持っている武器・鎧・盾を +N にし、追加効果を付け、守りの指輪と外套を付ける
Character Equip(Character c, int lv)
{
    if (gear <= 0 && affixes <= 0) return c;
    foreach (var slot in new[] { GravewardSpiral.Core.Items.EquipSlot.MainHand, GravewardSpiral.Core.Items.EquipSlot.OffHand,
                 GravewardSpiral.Core.Items.EquipSlot.Ranged, GravewardSpiral.Core.Items.EquipSlot.Armor })
    {
        if (c.Inventory[slot] is not { } it || !data.Items.TryGetValue(it.ItemId, out var def)
            || def.Kind is not (GravewardSpiral.Core.Items.ItemKind.Weapon or GravewardSpiral.Core.Items.ItemKind.Armor or GravewardSpiral.Core.Items.ItemKind.Shield)) continue;
        it.Bonus = Math.Max(it.Bonus, gear);
        it.Identified = true;
        if (affixes > 0) GravewardSpiral.Core.Items.Loot.AddAffixes(data, it, def, affixes, lv, gearRng);
    }
    if (gear > 0)
    {
        c.Inventory.Equipped[GravewardSpiral.Core.Items.EquipSlot.Ring1] = new() { ItemId = "ring_of_protection", Attuned = true };
        c.Inventory.Equipped[GravewardSpiral.Core.Items.EquipSlot.Cloak] = new() { ItemId = "cloak_of_protection", Attuned = true };
    }
    return c;
}
List<Character> Party(GravewardSpiral.Core.Rng.IRandom _) => PartyAt(level);

void Report(string label, SimulationResult r) =>
    Console.WriteLine($"{label,-24} 勝ち {r.WinRate,6:P1}  全滅 {(double)r.Defeats / r.Fights,6:P1}  平均 {r.AverageRounds,4:F1} ラウンド  死者 {r.AverageDeaths,4:F2}  残り HP {r.AverageHpLeft,6:P0}");

if (eventCensus)
{
    // 置かれたイベントを数える: 迷宮に組み込まれたもの(生成した直後の印)と、散らしたもの(PlaceEvents のあとに増えた印)に分ける
    var gen = GravewardSpiral.Core.MazeGen.MazeGenSettings.Load(content);
    var enc = EncounterSettings.Load(content);
    var scenarioDungeon = GravewardSpiral.Core.Maze.DungeonLoader.ScenarioDungeon(content);
    var pack = GravewardSpiral.Core.Save.WorldPackDef.Of(content);
    const string mark = GravewardSpiral.Core.Events.EventPlacer.MarkPrefix;
    var built = new List<int>();
    var scattered = new List<int>();
    var perFloor = new Dictionary<int, List<int>>();
    var used = new Dictionary<string, int>(StringComparer.Ordinal);
    var byRarity = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int run = 1; run <= runs; run++)
    {
        ulong sd = (ulong)run;
        var dungeon = scenarioDungeon != null ? GravewardSpiral.Core.Maze.DungeonLoader.ScenarioDungeon(content)! : null;
        var request = dungeon != null ? GravewardSpiral.Core.Maze.DungeonLoader.RequestFor(dungeon, sd)
            : floors > 0 ? GravewardSpiral.Core.MazeGen.GenerationRequest.Default(gen, sd) with { Floors = floors }
            : GravewardSpiral.Core.MazeGen.GenerationRequest.Default(gen, sd);
        dungeon ??= new GravewardSpiral.Core.MazeGen.MazeGenerator(gen).Generate(request).Dungeon;
        List<string> Marks() => dungeon.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>().Where(m => m.StartsWith(mark)).ToList();
        int before = Marks().Count;
        var session = GravewardSpiral.Core.Save.GameSession.New(0, request, dungeon);
        var worldEnc = enc;
        if (level > 1)
        {
            // 強いクランで始めた世界(--level L のクランの強さに、世界のレベル帯を合わせる)
            session.SetWorldLevel(enc, level);
            worldEnc = session.WorldSettings(enc);
        }
        if (content.Scenario == null) session.Origin = GravewardSpiral.Core.Ai.WorldBuilder.Pick(data, sd);
        else pack?.ApplyTo(session);
        // 伝説の噂を、どれも heardTimes 個の世界で聞いたクランにする
        if (heardTimes > 0)
            foreach (var e in data.Events.Values.Where(e => e.Rumor != null)) session.LegendsHeard[e.Id] = heardTimes;
        session.BuildRosters(data, worldEnc);
        session.ApplyScenario(data, content, worldEnc, gen);
        var marks = Marks();
        built.Add(before);
        scattered.Add(marks.Count - before);
        for (int f = 0; f < dungeon.Floors.Count; f++)
        {
            var fl = dungeon.Floors[f];
            int n = fl.AllCells().Count(c => fl[c].Mark?.StartsWith(mark) == true);
            if (!perFloor.TryGetValue(f, out var list)) perFloor[f] = list = [];
            list.Add(n);
        }
        foreach (var m in marks)
        {
            string id = m[mark.Length..];
            used[id] = used.GetValueOrDefault(id) + 1;
            string rarity = data.Events.TryGetValue(id, out var ev) ? ev.Scatter?.Rarity ?? "-" : "?";
            byRarity[rarity] = byRarity.GetValueOrDefault(rarity) + 1;
        }
    }
    string Stat(List<int> xs) => $"平均 {xs.Average():0.0}(最少 {xs.Min()}、最多 {xs.Max()})";
    Console.WriteLine($"{(content.Scenario?.Id ?? "自由な迷宮")}: 世界 {runs} 個、{built.Count} 個の世界を数えた");
    Console.WriteLine($"1つの世界のイベント: {Stat(built.Zip(scattered, (a, b) => a + b).ToList())}");
    Console.WriteLine($"  迷宮に組み込まれたもの: {Stat(built)}");
    Console.WriteLine($"  散らしたもの: {Stat(scattered)}");
    foreach (var (f, list) in perFloor.OrderBy(x => x.Key)) Console.WriteLine($"  B{f + 1}: {Stat(list)}");
    int mazeEvents = data.Events.Values.Count(e => e.Place == "maze");
    Console.WriteLine($"迷宮のイベント {mazeEvents} 種のうち、一度でも置かれたもの {used.Count} 種({100.0 * used.Count / Math.Max(1, mazeEvents):0}%)");
    Console.WriteLine($"レア度ごと(置かれた数): {string.Join("、", byRarity.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
    Console.WriteLine($"よく置かれたもの: {string.Join("、", used.OrderByDescending(x => x.Value).Take(10).Select(x => $"{x.Key} {x.Value}"))}");
    return 0;
}

if (aiWorld)
{
    string? key = GravewardSpiral.Game.Ai.ApiKeyStore.Load(GravewardSpiral.Game.Ai.ApiKeyStore.Claude);
    if (key == null)
    {
        Console.Error.WriteLine("API キーがありません(資格情報マネージャーの GravewardSpiral/claude か ANTHROPIC_API_KEY)");
        return 1;
    }
    var gen = GravewardSpiral.Core.MazeGen.MazeGenSettings.Load(content);
    var request = GravewardSpiral.Core.MazeGen.GenerationRequest.Default(gen, seed);
    if (floors > 0) request = request with { Floors = floors };
    var dungeon = new GravewardSpiral.Core.MazeGen.MazeGenerator(gen).Generate(request).Dungeon;
    var session = GravewardSpiral.Core.Save.GameSession.New(0, request, dungeon);
    var enc = EncounterSettings.Load(content);
    if (content.Scenario == null) session.Origin = GravewardSpiral.Core.Ai.WorldBuilder.Pick(data, seed);
    session.BuildRosters(data, enc);
    if (session.Origin is { } origin) session.World = GravewardSpiral.Core.Ai.WorldBuilder.Build(data, origin, dungeon.Floors.Count, seed);
    var prompt = GravewardSpiral.Core.Ai.WorldWriter.Prompt(content)!;
    var client = new GravewardSpiral.Core.Ai.ClaudeClient(key, aiModel);
    Console.WriteLine($"世界 seed {seed}、{dungeon.Floors.Count} 階、モデル {client.Model} に頼んでいます…");
    if (GravewardSpiral.Core.Ai.WorldWriter.AutoBoss(data, session) is { } master)
        Console.WriteLine($"主に選んだ敵: {master.Name.Get("ja")}({master.Type}、脅威度 {master.Cr})" + (master.Escorts.Count > 0 ? $"、従う者 {string.Join("・", master.Escorts.Select(e => e.Get("ja")))}" : ""));
    var (world, error) = await GravewardSpiral.Core.Ai.WorldWriter.WriteAsync(client, data, prompt, dungeon.Floors.Count, null, CancellationToken.None, seed, session);
    if (world == null)
    {
        Console.Error.WriteLine($"失敗: {error}");
        return 1;
    }
    string T(GravewardSpiral.Core.Content.LocalizedText? t) => t == null ? "(無し)" : $"{t.Get("ja")} / {t.Get("en")}";
    Console.WriteLine();
    Console.WriteLine("序文: " + T(world.Prologue));
    for (int f = 0; f < world.Floors.Count; f++) Console.WriteLine($"B{f + 1}: {T(world.Floors[f].Name)}");
    Console.WriteLine();
    Console.WriteLine("主の名前: " + T(world.Boss?.Name));
    Console.WriteLine("出会い: " + T(world.Boss?.Intro));
    Console.WriteLine("倒したとき: " + T(world.Boss?.Victory));
    return 0;
}

if (campaign)
{
    string[][] parties = partiesArg != null ? partiesArg.Split(';').Select(p => p.Split(',')).ToArray() :
    [
        ["fighter", "fighter", "paladin", "rogue", "cleric", "wizard"],     // 王道
        ["fighter", "fighter", "cleric", "rogue", "wizard", "wizard"],      // Wizardry 風(戦戦僧盗魔魔)
        ["fighter", "barbarian", "paladin", "ranger", "monk", "fighter"],   // 脳筋
        ["cleric", "druid", "wizard", "sorcerer", "warlock", "bard"],       // 術者だらけ
        ["fighter", "barbarian", "rogue", "ranger", "wizard", "sorcerer"],  // 回復なし
        ["paladin", "cleric", "cleric", "druid", "bard", "monk"],           // 回復だらけ
        ["fighter", "cleric", "wizard"],                                    // 3人
        ["fighter"],                                                        // 1人
    ];
    Console.WriteLine($"通しで遊ぶ: 編成 {parties.Length} 種 × {runs} 回");
    Console.WriteLine($"{"編成",-52} クリア  入った回  戦闘  逃走  全滅  死亡  消失  平均Lv  イベント  落とし物(固有)  武器+  魔法の武器  稼ぎ(G)  名のある敵(いた/倒/品)  失敗の理由");
    foreach (var p in parties)
    {
        var results = Enumerable.Range(1, runs).Select(r => CampaignSim.Run(data, content, p, (ulong)r, new CampaignOptions { Verbose = verbose, Smart = !simple, StartLevel = level })).ToList();
        if (verbose)
            foreach (var (r, i) in results.Select((r, i) => (r, i)))
                Console.WriteLine($"  seed {i + 1}: {(r.Cleared ? "クリア" : r.FailReason)}  " + string.Join(" / ", r.Log));
        var fails = results.Where(r => !r.Cleared).GroupBy(r => r.FailReason!.Split('(')[0]).Select(g => $"{g.Key}×{g.Count()}");
        Console.WriteLine($"{string.Join(",", p),-52} {results.Count(r => r.Cleared) / (double)runs,5:P0}  {results.Average(r => r.Expeditions),7:F1}  {results.Average(r => r.Fights),5:F0}  {results.Average(r => r.Fled),4:F0}  {results.Average(r => r.Wipes),4:F1}  {results.Average(r => r.Deaths),4:F1}  {results.Average(r => r.Lost),4:F1}  {results.Average(r => r.AverageLevel),6:F1}  {results.Average(r => r.EventsDone),7:F1}  {results.Average(r => r.Drops),5:F1}({results.Average(r => r.Uniques),4:F2})  {results.Average(r => r.WeaponBonus),5:F1}  {results.Average(r => r.MagicArmed),9:P0}  {results.Average(r => r.MazeGold),8:F0}  {results.Sum(r => r.NamedInWorld),4}/{results.Sum(r => r.NamedKills)}/{results.Sum(r => r.NamedItems),-8}  {string.Join(" ", fails)}");
    }
    return 0;
}

Console.WriteLine($"パーティ: {string.Join(", ", classes)}(レベル {level}{(gear > 0 ? $"、装備 +{gear}" : "")}{(affixes > 0 ? $"、追加効果 {affixes}" : "")})、{fights} 戦");
if (bossId != null)
{
    var boss = data.Bosses[bossId];
    var groups = boss.Groups.Select(g => (data.Monsters[g.Monster], g.Count)).ToList();
    Report($"ボス {bossId}", CombatSimulator.Run(data, fights, seed, Party, _ => groups, boss.Scale));
}
else if (fixedGroups.Count > 0)
{
    var groups = fixedGroups.Select(g => (data.Monsters[g.Id], g.Count)).ToList();
    Report(string.Join(" + ", fixedGroups.Select(g => $"{g.Id}x{g.Count}")),
        CombatSimulator.Run(data, fights, seed, Party, _ => groups));
}
else if (floors > 0)
{
    for (int f = 0; f < floors; f++)
    {
        double d = floors == 1 ? 0 : (double)f / (floors - 1);
        int lv = atLevel ? Math.Max(1, (int)settings.TargetLevel(d)) : level;
        Report($"B{f + 1}(目安 L{settings.TargetLevel(d):F1}, L{lv})",
            CombatSimulator.Run(data, fights, seed, _ => PartyAt(lv), r => Encounters.Build(data, settings, d, classes.Length, r).Groups));
    }
}
else
{
    double d = depth ?? 0;
    Report($"深さ {d:F2}(目安 L{settings.TargetLevel(d):F1})",
        CombatSimulator.Run(data, fights, seed, Party, r => Encounters.Build(data, settings, d, classes.Length, r).Groups));
}
return 0;

// 選択肢のうち、最初の手順で金を払うのに、所持金の条件が足りないもの(ラベルと、要る金額)
static string FindContent()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        string c = Path.Combine(dir.FullName, "Content");
        if (Directory.Exists(c) && File.Exists(Path.Combine(dir.FullName, "GravewardSpiral.slnx"))) return c;
    }
    throw new DirectoryNotFoundException("Content が見つかりません(リポジトリの中で動かしてください)");
}
