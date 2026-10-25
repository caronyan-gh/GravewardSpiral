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
//   --scenario ID      重ねて読むシナリオ(Content/scenarios/ID)。省略すると第1部 gravekeeper、none なら自由な迷宮(標準のパックだけ)
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Monsters;

Console.OutputEncoding = System.Text.Encoding.UTF8;

int level = 1, fights = 500, floors = 0, gear = 0, affixes = 0;
bool atLevel = false, campaign = false, verbose = false, simple = false;
int runs = 20;
string? partiesArg = null;
var checkFiles = new List<string>();
bool allowExisting = false;
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
        case "--scenario": scenarioId = next; i++; break;
        case "--allow-existing": allowExisting = true; break;
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
    : ContentLibrary.Load(contentRoot, Path.Combine(contentRoot, "scenarios", scenarioId));
if (!content.IsValid)
{
    foreach (var p in content.Problems) Console.Error.WriteLine(p);
    return 1;
}
var data = GameData.Load(content);

if (checkFiles.Count > 0)
{
    // 下書きの検査: 知らない項目名は誤りにする(書き間違いで部品が黙って無視されないように)
    var strict = new System.Text.Json.JsonSerializerOptions(ContentLibrary.JsonOptions)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    var problems = new List<string>();
    var seen = allowExisting ? new HashSet<string>() : new HashSet<string>(data.Events.Keys);
    int count = 0;
    foreach (var file in checkFiles)
    {
        List<System.Text.Json.JsonElement> list;
        try { list = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(File.ReadAllText(file)) ?? []; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            problems.Add($"{file}: 読めません: {ex.Message}");
            continue;
        }
        foreach (var el in list)
        {
            string id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "?" : "?";
            GravewardSpiral.Core.Events.EventDef e;
            try { e = System.Text.Json.JsonSerializer.Deserialize<GravewardSpiral.Core.Events.EventDef>(el, strict)!; }
            catch (System.Text.Json.JsonException ex)
            {
                problems.Add($"{file} '{id}': {ex.Message}");
                continue;
            }
            count++;
            if (!seen.Add(e.Id)) problems.Add($"{file} '{e.Id}': id が既にあります");
            if (e.Place == "maze" && e.Scatter == null) problems.Add($"{file} '{e.Id}': 迷宮のイベントに scatter がありません");
            if (e.Place == "town" && e.Title == null) problems.Add($"{file} '{e.Id}': 町のイベントに title がありません");
            problems.AddRange(GravewardSpiral.Core.Events.EventCheck.Check(data, e, content).Select(x => $"{file} '{e.Id}': {x}"));
            // 払う選択肢(gold が負)には、所持金の条件を付ける(足りなくても払えたことになって、ただで賭けられてしまう)
            foreach (var (label, need) in PayingOptions(e.Steps).Where(x => x.Need > 0))
                problems.Add($"{file} '{e.Id}': 選択肢「{label}」は {need} G を払うのに、when に gold {need} 以上の条件がありません");
        }
    }
    // 連動する組: 下書きとパックを合わせて、部品が2つ以上、step が重ならない。町などが見る chain: のフラグの組があること
    var drafts = checkFiles.SelectMany(f =>
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<GravewardSpiral.Core.Events.EventDef>>(File.ReadAllText(f), ContentLibrary.JsonOptions) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }).ToList();
    var all = data.Events.Values.Where(e => !drafts.Any(d => d.Id == e.Id)).Concat(drafts).ToList();
    var chainGroups = all.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!).ToList();
    foreach (var g in chainGroups.Where(g => g.Any(e => drafts.Contains(e))))
    {
        if (g.Count() < 2) problems.Add($"組 '{g.Key}': 部品が1つしかありません");
        foreach (var dup in g.GroupBy(e => e.Scatter!.Step).Where(x => x.Count() > 1))
            problems.Add($"組 '{g.Key}': step {dup.Key} が重なっています({string.Join(", ", dup.Select(e => e.Id))})");
    }
    var chainNames = chainGroups.Select(g => g.Key).ToHashSet();
    static IEnumerable<GravewardSpiral.Core.Events.EventCondition> Conds(GravewardSpiral.Core.Events.EventDef e)
    {
        IEnumerable<GravewardSpiral.Core.Events.EventCondition> Walk(List<GravewardSpiral.Core.Events.EventStep> steps) =>
            steps.SelectMany(st => st.When.Concat(st.Options.SelectMany(o => o.When.Concat(Walk(o.Steps)))).Concat(st.Children().SelectMany(Walk)));
        return e.When.Concat(Walk(e.Steps));
    }
    foreach (var e in drafts)
    {
        foreach (var c in Conds(e).Where(c => c.Type == "flag" && c.Id?.StartsWith("chain:") == true && !chainNames.Contains(c.Id["chain:".Length..])))
            problems.Add($"'{e.Id}': フラグ {c.Id} の組がありません");
    }
    foreach (var x in problems) Console.WriteLine(x);
    Console.WriteLine(problems.Count == 0 ? $"OK: {count} 個のイベントに問題はありません" : $"問題 {problems.Count} 件({count} 個のイベント)");
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
static IEnumerable<(string Label, int Need)> PayingOptions(List<GravewardSpiral.Core.Events.EventStep> steps)
{
    foreach (var st in steps)
    {
        foreach (var o in st.Options)
        {
            var pay = o.Steps.FirstOrDefault(x => x.Type == "gold" && x.Value < 0);
            int have = o.When.Where(w => w.Type == "gold" && !w.Not).Select(w => w.Value).DefaultIfEmpty(0).Max();
            if (pay != null && have < -pay.Value) yield return (o.Label?.Get("ja") ?? "?", -pay.Value);
            foreach (var x in PayingOptions(o.Steps)) yield return x;
        }
        foreach (var child in st.Children().Where(c => c != null))
            foreach (var x in PayingOptions(child)) yield return x;
    }
}

static string FindContent()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        string c = Path.Combine(dir.FullName, "Content");
        if (Directory.Exists(c) && File.Exists(Path.Combine(dir.FullName, "GravewardSpiral.slnx"))) return c;
    }
    throw new DirectoryNotFoundException("Content が見つかりません(リポジトリの中で動かしてください)");
}
