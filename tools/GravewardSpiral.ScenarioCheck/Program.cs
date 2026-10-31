using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Save;

// シナリオを作る人(とそのエージェント)向けの検査の道具。インストール先の ScenarioCheck.exe。
// エディタの「検査」のタブと同じ検査を、画面を開かずに流す。ほかに、呪文の表・何が指しているか・絵の一覧・部品の一覧を出す。
// 使い方は Usage() と scenario-kit/README.md。終了コード: 0 = 問題なし、1 = 問題あり、2 = 使い方の間違いや読めないとき。

Console.OutputEncoding = Encoding.UTF8;
try
{
    return Run(args);
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage());
    return 2;
}

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(Usage());
        return args.Length == 0 ? 2 : 0;
    }
    var opts = Options.Parse(args);
    if (opts.Help)
    {
        Console.WriteLine(UsageFor(opts.Command));
        return 0;
    }
    return opts.Command switch
    {
        "check" => Check(opts),
        "spells" => Spells(opts),
        "uses" => Uses(opts),
        "art" => Art(opts),
        "reference" => Reference(),
        "fill" => Fill(opts),
        "new" => New(opts),
        "list" => List(opts),
        "map" => Map(opts),
        "finish" => Finish(opts),
        _ => throw new UsageException($"知らないコマンド / unknown command: {opts.Command}"),
    };
}

static string Usage() => """
    ScenarioCheck <command> ...   (ScenarioCheck <command> --help で、そのコマンドだけ)

      new <folder> [--floors 3] [--size 12x12] [--levels 1-5] [--seed S] [--rooms few|normal|many] [--doors few|normal|many] [--gimmicks none|few|normal|many] [--prefabs standard|none]
          新しいシナリオの雛形を作る(迷宮はエディタの「新しく作る」と同じ自動生成。id はフォルダの名前)。
          つまみで形をテーマに寄せる(修道院なら部屋と扉を多く、洞窟なら部屋と扉を少なく、など)。作ったあと、地図・敵・イベント・文を直していく。
          --prefabs standard(既定)は、土台のはめ込む部屋(レバーの部屋・双子のスイッチ・印の祠など。標準イベントの印つき)を入れる。none なら入れない
          (あとで fill を流すと、標準のイベントは散らして置く)。レベル帯は --levels で決める(省くと 1-5)。
          worlds.json(文の雛形。TODO: を書き換える)・bosses.json(最下層の主の形)・text/ja.json・en.json(空の文言)も作る。
          Creates a new scenario skeleton with a generated maze (id = folder name). The knobs shape the maze for your theme.

      map <folder|zip> [--floor N]
          地図を座標つきで出す(列の上に x、行の左に y。印のあるマスは *、下に印・階段や仕掛けのマス・start・部屋の一覧)。座標を数えずに済む。
          Prints the maps with coordinates (x across the top, y down the left; * marks cells with a mark, listed below).

      finish <folder|zip>
          仕上げの点検。できたものの一覧と、このシナリオで確かめること(レベル帯、階の名前と入ったときの文、ボスの文、門、
          標準の部屋の印を組ごとに、使っていない地図、文の英日)を出す。最後に流して、出力を報告に貼る。
          finish を流しただけでは、ゲームで確かめたことにならない(報告では、データの照合・ゲームで確かめた・まだ、を分ける)。
          Final review: a summary of what was made and scenario-specific things to confirm. Run it last and paste the output.

      list monsters|items|conditions|skills|abilities|damage|classes|races|sounds [--scenario <folder|zip>] [--cr 1-5] [--type undead,construct]
          使える id の一覧(モンスターは CR と種類、品は種類も。sounds はイベントの手順 sound の効果音)。モンスターは --cr(1/4-2 の形も)と --type(種類。カンマで区切る)で絞れる。
          Lists usable ids (monsters with CR and type, items with kind).

      check <folder|zip> [--bots N] [--party fighter,cleric,...] [--level L] [--lang ja|en]
          エディタの「検査」と同じ検査(読み込み・迷宮・行けるか・歩くボット・イベント・印・品・整合性)。
          --bots N を付けると、ボットに N 回通しで遊ばせる(時間がかかる)。
          The same checks as the editor's Check tab. --bots N lets bots play the scenario N times.

      spells <class> [--scenario <folder|zip>] [--race human] [--max 20]
          そのクラスのレベルごとのスロット・初級呪文と覚える数・新しく使える呪文。
          Per level: spell slots, cantrips/spells known, and newly available spells.

      uses <id> [--scenario <folder|zip>]
          その id を指しているデータ(このクラスや能力を消すと何が壊れるか)。
          Everything that refers to the id (what breaks if you remove it).

      art [--scenario <folder|zip>]
          モンスターの id・名前・使う絵の一覧(絵の借り先の間違いを見つける)。
          Monsters with id, names and the picture they use.

      fill <folder> [--seed S]
          決めていない中身(各階に出る敵・名のある敵・散らすイベント)を選んで、フォルダに書き込む(エディタと同じ)。
          エディタの保存と同じく、ファイル全体を書き直す(dungeons.json の名前を manifest の名前にそろえる、省いてよい既定の値も書く、
          印の行の並びが変わる)。フォルダを書き換えるので、写しで試してから使うこと。
          Fills in what the author left undecided (enemies per floor, named monsters, scattered events). Rewrites the folder.

      reference
          今のゲームが知っている部品の一覧(イベントの手順と条件、効果、能力の効果と on)。
          The part types this build knows (event steps/conditions, effects, passive effects).

    終了コード / exit code: 0 = OK, 1 = problems found, 2 = usage error
    """;

/// <summary>1つのコマンドの説明(Usage() のそのコマンドの段落)。`ScenarioCheck new --help` など。</summary>
static string UsageFor(string command)
{
    var lines = Usage().Replace("\r\n", "\n").Split('\n');
    int start = Array.FindIndex(lines, l => l.TrimStart().StartsWith(command + " ") || l.Trim() == command);
    if (start < 0) return Usage();
    var block = lines.Skip(start).TakeWhile(l => l.Trim().Length > 0).Select(l => l.Length >= 2 ? l[2..] : l);
    return "ScenarioCheck " + string.Join("\n", block).TrimStart() + "\n\n" + "終了コード / exit code: 0 = OK, 1 = problems found, 2 = usage error";
}

// ---------------- check ----------------

static int Check(Options o)
{
    string dir = o.ScenarioDir ?? throw new UsageException("check にはシナリオのフォルダか zip を渡す / check needs a scenario folder or zip");
    var problems = new List<string>();
    var pack = ScenarioPack.Load(dir, problems);
    string T(string ja, string en) => o.Lang == "en" ? en : ja;
    foreach (var p in problems) Console.WriteLine("× " + T("シナリオ: ", "Scenario: ") + p);
    var (content, data, baseContent) = LoadWith(dir);
    var lines = ScenarioCheck.Run(pack, content, data, baseContent, T, o.Lang);
    foreach (var l in lines)
        Console.WriteLine(l.Level switch { CheckLevel.Pass => "○ ", CheckLevel.Problem => "× ", CheckLevel.Caution => "！ ", _ => "・ " } + l.Text);
    int count = problems.Count + lines.Count(l => l.Level == CheckLevel.Problem);
    int cautions = lines.Count(l => l.Level == CheckLevel.Caution);
    Console.WriteLine();
    Console.WriteLine(count == 0
        ? T($"検査に通りました(注意 {cautions} 件)", $"Check passed ({cautions} caution(s))")
        : T($"問題 {count} 件、注意 {cautions} 件", $"{count} problem(s), {cautions} caution(s)"));

    if (o.Bots > 0)
    {
        if (data == null || !content.IsValid)
        {
            Console.WriteLine(T("× 読み込みの検査を通らないので、ボットは遊べない", "× The loading check failed, so the bots cannot play"));
            return 1;
        }
        Console.WriteLine();
        Console.WriteLine(T($"ボットが {o.Bots} 回遊んでいます({string.Join(",", o.Party)}、Lv {o.Level})…", $"Bots are playing {o.Bots} time(s) ({string.Join(",", o.Party)}, Lv {o.Level})…"));
        var options = new CampaignOptions { StartLevel = o.Level };
        var results = Enumerable.Range(1, o.Bots).Select(i => CampaignSim.Run(data, content, o.Party, (ulong)i, options)).ToList();
        int cleared = results.Count(x => x.Cleared);
        Console.WriteLine(T($"クリア {cleared}/{o.Bots}、平均 Lv {results.Average(x => x.AverageLevel):0.0}、全滅 平均 {results.Average(x => x.Wipes):0.0} 回、一番深い階 B{results.Max(x => x.DeepestFloor)}",
            $"Cleared {cleared}/{o.Bots}, average Lv {results.Average(x => x.AverageLevel):0.0}, wipes {results.Average(x => x.Wipes):0.0} per run, deepest floor B{results.Max(x => x.DeepestFloor)}"));
        foreach (var r in results.Where(x => !x.Cleared))
            Console.WriteLine(T("  クリアできなかった理由: ", "  Reason for not clearing: ") + (r.FailReason ?? "?"));
    }
    return count == 0 ? 0 : 1;
}

// ---------------- new ----------------

static int New(Options o)
{
    string dir = o.Arg ?? throw new UsageException("new には作るフォルダを渡す / new needs a folder to create");
    dir = Path.GetFullPath(dir);
    if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
        throw new UsageException($"{dir} は空ではない(新しいフォルダを渡す) / {dir} is not empty");
    string id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
    if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9_]+$"))
        throw new UsageException($"フォルダの名前 {id} を id にする。小文字の英字・数字・_ だけにする / the folder name {id} becomes the id: use lowercase letters, digits and _ only");
    var (_, _, baseContent) = LoadWith(null);
    var settings = GravewardSpiral.Core.MazeGen.MazeGenSettings.Load(baseContent);
    Shape(settings, o);
    var request = new GravewardSpiral.Core.MazeGen.GenerationRequest(o.Seed, o.Floors, GravewardSpiral.Core.MazeGen.FloorSizeMode.Fixed(o.Width, o.Height));
    var dungeon = new GravewardSpiral.Core.MazeGen.MazeGenerator(settings).Generate(request).Dungeon;
    var pack = ScenarioPack.New(dir, id, dungeon.Floors, "ScenarioCheck");
    // 保存で manifest の levels はレベル帯(encounters.json の first/last_level)から書くので、レベル帯のほうに入れる
    pack.FirstLevel = o.LevelMin;
    pack.LastLevel = o.LevelMax;
    // 物語の文の雛形(worlds.json)。TODO: のまま残すと check が注意する。文言(text/ja.json・en.json)は空の形だけ
    static LocalizedText Todo(string ja, string en) => LocalizedText.Of("TODO: " + en, "TODO: " + ja);
    pack.Text = new GravewardSpiral.Core.Ai.WorldText
    {
        Model = "scenario",
        Prologue = Todo("序文(なぜ迷宮があり、何をしに潜るのか)", "prologue (why the maze exists and why the party goes in)"),
        Floors = dungeon.Floors.Select((_, i) => new GravewardSpiral.Core.Ai.WorldFloorText
        {
            Name = Todo($"B{i + 1} の名前", $"name of B{i + 1}"),
            Enter = Todo($"B{i + 1} に入ったときの文", $"text on entering B{i + 1}"),
        }).ToList(),
        Boss = new GravewardSpiral.Core.Ai.WorldBossText
        {
            Name = Todo("最下層の主の名前", "name of the final boss"),
            Intro = Todo("主に出会ったときの文", "text when meeting the boss"),
            Victory = Todo("主を倒したときの文", "text after defeating the boss"),
        },
    };
    pack.Texts["ja"] = [];
    pack.Texts["en"] = [];
    pack.Save(baseContent);
    // 最下層の主(bosses.json)の形: 標準の final を写す(auto: true のまま。組を書けば決まった主になる)
    string bossesFile = Path.Combine(dir, "bosses.json");
    if (!File.Exists(bossesFile) && baseContent.Find("bosses", GameSession.FinalBossId) is { } baseFinal)
    {
        var final = System.Text.Json.Nodes.JsonNode.Parse(baseFinal.Data.GetRawText())!.AsObject();
        final["note"] = "最下層の主。auto: true は世界のレベル帯から敵を選ぶ。決めるなら auto を false にして groups に [{ \"monster\": id, \"count\": 数 }] を書く";
        File.WriteAllText(bossesFile, new System.Text.Json.Nodes.JsonArray(final).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
    Console.WriteLine($"作りました / created: {dir}");
    Console.WriteLine($"  id {id}、{dungeon.Floors.Count} 階、{o.Width}x{o.Height}、推奨レベル / levels {o.LevelMin}-{o.LevelMax}、シード / seed {o.Seed}");
    Console.WriteLine("次 / next: manifest の名前と説明 → 地図(maps。map で座標を見る)を直す → 敵(dungeons.json の rosters と bosses.json の final。fill でも埋まる) → イベント → 文(worlds.json の TODO: を書き換える) → check → finish");
    if (o.Prefabs != "none") Console.WriteLine("  地図には土台のはめ込む部屋の標準イベントの印(event:lever など)が入っている。そのまま動く。要らなければ mark 行を消す(--prefabs none で入れない)");
    return 0;
}

/// <summary>形のつまみ(部屋・扉・仕掛け)を、生成の設定の数に当てる。</summary>
static void Shape(GravewardSpiral.Core.MazeGen.MazeGenSettings s, Options o)
{
    switch (o.Rooms)
    {
        case "few": s.RoomDensity *= 0.4; s.LoopChance *= 0.5; break;
        case "many": s.RoomDensity = Math.Min(0.6, s.RoomDensity * 1.8); break;
    }
    switch (o.Doors)
    {
        case "few": s.DoorChance = 0.2; s.SecretDoorChance = 0.03; break;
        case "many": s.DoorChance = 0.95; s.SecretDoorChance = 0.2; break;
    }
    double mult = o.Gimmicks switch { "none" => 0, "few" => 0.5, "many" => 2, _ => 1 };
    if (o.Prefabs == "none") s.Prefabs.Clear();   // 土台のはめ込む部屋(標準イベントの印つき)を入れない
    if (mult == 1) return;
    GravewardSpiral.Core.MazeGen.IntRange Scale(GravewardSpiral.Core.MazeGen.IntRange r) => new((int)Math.Floor(r.Min * mult), (int)Math.Ceiling(r.Max * mult));
    foreach (var rule in new[] { s.OneWay, s.Spinners, s.Teleporters, s.Pits, s.Chutes, s.Locks }) rule.Count = Scale(rule.Count);
    foreach (var rule in new[] { s.Dark, s.Fog, s.AntiMagic }) rule.Count = Scale(rule.Count);
    if (mult == 0) s.WrapChance = 0;
}

// ---------------- map ----------------

static int Map(Options o)
{
    string dir = o.ScenarioDir ?? throw new UsageException("map にはシナリオのフォルダか zip を渡す / map needs a scenario folder or zip");
    var pack = ScenarioPack.Load(dir, []);
    for (int i = 0; i < pack.Floors.Count; i++)
    {
        if (o.Floor is int only && only != i + 1) continue;
        PrintMap(pack.Floors[i], i, pack.MapFile(i));
        Console.WriteLine();
    }
    return 0;
}

/// <summary>地図を座標つきで出す。マスの文字の位置(列 2x+1・行 2y+1)に合わせて、上に x、左に y を振る。印のあるマスで空いていれば * にする。</summary>
static void PrintMap(MazeFloor f, int index, string file)
{
    Console.WriteLine($"B{index + 1}  {f.Width}x{f.Height}  ({file})");
    var text = MazeTextFormat.Write(f).Replace("\r\n", "\n").Split('\n');
    int start = Array.IndexOf(text, "walls") + 1;
    var walls = text.Skip(start).Take(2 * f.Height + 1).Select(l => l.ToCharArray()).ToList();
    foreach (var c in f.AllCells())
        if (f[c].Mark != null && walls[2 * c.Y + 1][2 * c.X + 1] is ' ' or '.') walls[2 * c.Y + 1][2 * c.X + 1] = '*';
    string tens = "    " + string.Concat(Enumerable.Range(0, 2 * f.Width + 1).Select(col => col % 2 == 1 && (col / 2) >= 10 ? (char)('0' + (col / 2) / 10 % 10) : ' '));
    string ones = "    " + string.Concat(Enumerable.Range(0, 2 * f.Width + 1).Select(col => col % 2 == 1 ? (char)('0' + (col / 2) % 10) : ' '));
    if (f.Width > 10) Console.WriteLine(tens);
    Console.WriteLine(ones);
    for (int r = 0; r < walls.Count; r++)
        Console.WriteLine((r % 2 == 1 ? $"{r / 2,3} " : "    ") + new string(walls[r]));
    Console.WriteLine("  文字 / cells: u 上り  d 下り  s 回転床  t テレポーター  p 落とし穴  c シュート  e エレベーター  # 岩盤  * 印  /  辺 / edges: D 扉  S 隠し扉  L 錠  ^ v < > 一方通行");
    foreach (var c in f.AllCells().Where(c => f[c].Mark != null)) Console.WriteLine($"  ({c.X},{c.Y}) {f[c].Mark}");
    // 階段や仕掛けのマスも座標で(図の文字を数えずに済むように)
    foreach (var c in f.AllCells().Where(c => f[c].Feature is not (CellFeature.None or CellFeature.Solid)))
        Console.WriteLine($"  ({c.X},{c.Y}) {f[c].Feature.ToString().ToLowerInvariant()}" + (f[c].Target is { } t ? $" -> {(t.Floor is int tf ? $"B{tf + 1} " : "")}({t.Cell.X},{t.Cell.Y})" : ""));
    Console.WriteLine($"  start ({f.Start.X},{f.Start.Y}) {f.StartFacing.Letter()}");
    foreach (var room in f.Rooms) Console.WriteLine($"  room {room.X} {room.Y} {room.W} {room.H}");
}

// ---------------- finish ----------------

static int Finish(Options o)
{
    string dir = o.ScenarioDir ?? throw new UsageException("finish にはシナリオのフォルダか zip を渡す / finish needs a scenario folder or zip");
    var problems = new List<string>();
    var pack = ScenarioPack.Load(dir, problems);
    var (content, data, baseContent) = LoadWith(dir);
    string T(string ja, string en) => o.Lang == "en" ? en : ja;
    if (data == null || pack.Floors.Count == 0)
    {
        Console.WriteLine(T("× 読み込めないので、仕上げの点検はできない。先に check で直す", "× Cannot load; fix it with check first"));
        return 1;
    }
    static IEnumerable<EventStep> All(IEnumerable<EventStep> steps) => steps.SelectMany(s => s.Children().SelectMany(All).Prepend(s));
    static IEnumerable<EventCondition> Conds(EventDef e) => e.When.Concat(All(e.Steps).SelectMany(s => s.When.Concat(s.Options.SelectMany(op => op.When)).Concat(s.Cases.SelectMany(c => c.When))));
    var dungeon = pack.Dungeon();

    Console.WriteLine(T("## できたもの", "## What was made"));
    Console.WriteLine($"- id {pack.Id}  {pack.Manifest.Name?.Get("ja")} / {pack.Manifest.Name?.Get("en")}");
    Console.WriteLine(T($"- 推奨レベル {pack.FirstLevel:0}〜{pack.LastLevel:0}(manifest の levels と encounters.json のレベル帯。保存のたびに同じ値にそろう)",
        $"- Levels {pack.FirstLevel:0}-{pack.LastLevel:0} (manifest levels and the encounters.json band)"));
    for (int i = 0; i < pack.Floors.Count; i++)
    {
        var f = pack.Floors[i];
        string features = string.Join(" ", f.AllCells().GroupBy(c => f[c].Feature).Where(g => g.Key is not (CellFeature.None or CellFeature.Solid)).Select(g => $"{g.Key.ToString().ToLowerInvariant()}x{g.Count()}"));
        string roster = i < pack.Rosters.Count && pack.Rosters[i].Count > 0 ? string.Join(",", pack.Rosters[i]) : T("(fill かゲームが選ぶ)", "(chosen by fill or the game)");
        Console.WriteLine($"- B{i + 1} {f.Width}x{f.Height}  {features}  " + T("敵: ", "enemies: ") + roster);
    }
    Console.WriteLine(T($"- イベント {pack.Events.Count} 個", $"- {pack.Events.Count} event(s)"));
    var stepTypes = pack.Events.SelectMany(e => All(e.Steps)).Select(s => s.Type).Distinct().Order();
    var condTypes = pack.Events.SelectMany(Conds).Select(c => c.Type).Distinct().Order();
    Console.WriteLine(T("  使った手順: ", "  steps used: ") + string.Join(", ", stepTypes));
    Console.WriteLine(T("  使った条件: ", "  conditions used: ") + string.Join(", ", condTypes));
    if (data.Bosses.TryGetValue(GameSession.FinalBossId, out var final))
        Console.WriteLine(T("- 最下層の主: ", "- Final boss: ") + string.Join(", ", final.Groups.Select(g => $"{g.Monster}x{g.Count}")));

    Console.WriteLine();
    Console.WriteLine(T("## このシナリオで確かめること", "## Things to confirm for this scenario"));
    int n = 0;
    void Item(string ja, string en) => Console.WriteLine($"{++n}. " + T(ja, en));
    Item($"推奨レベルは {pack.FirstLevel:0}〜{pack.LastLevel:0}。頼まれたレベルと合っているか(new は --levels を省くと 1-5)。",
        $"Levels are {pack.FirstLevel:0}-{pack.LastLevel:0}. Do they match the request? (new defaults to 1-5 without --levels)");

    // 階の名前と入ったときの文
    for (int i = 0; i < pack.Floors.Count; i++)
    {
        var f = pack.Floors[i];
        var w = pack.Text.Floor(i);
        string shown = w?.Name is { } wn ? $"{wn.Get("ja")} / {wn.Get("en")}(worlds.json)" : f.NameJa != null || f.NameEn != null ? $"{f.NameJa} / {f.NameEn}" + T("(地図の name 行)", " (map name lines)") : T("(名前なし。B 何階だけ)", "(no name; just the floor number)");
        string enter = w?.Enter != null ? T("入ったときの文あり", "entry text present") : T("入ったときの文なし", "no entry text");
        Item($"B{i + 1} の名前: {shown}。{enter}。worlds.json の名前と文が、地図の name 行より先に使われる。",
            $"B{i + 1} name: {shown}; {enter}. worlds.json names and texts take priority over map name lines.");
    }

    // ボスの文
    var bossMarks = dungeon.Floors.SelectMany((f, i) => f.AllCells().Where(c => f[c].Mark?.StartsWith("boss:") == true).Select(c => (i, c, f[c].Mark!["boss:".Length..])));
    foreach (var (fi, c, id) in bossMarks)
    {
        string where = id == GameSession.FinalBossId && pack.Text.Boss?.Intro != null
            ? T("worlds.json の boss.intro / victory(最下層の主は、こちらが先に使われる)", "worlds.json boss.intro / victory (used first for the final boss)")
            : data.Bosses.TryGetValue(id, out var b) && b.Intro != null ? T($"bosses.json の intro / victory の文言のキー({b.Intro})", $"text keys from bosses.json intro / victory ({b.Intro})") : T("無し(戦いの前後に文は出ない)", "none (no text before or after the fight)");
        Item($"B{fi + 1} ({c.X},{c.Y}) のボス {id} の出る文: {where}。", $"Boss {id} at B{fi + 1} ({c.X},{c.Y}) speaks with: {where}.");
    }

    // 門
    foreach (var (fi, f) in dungeon.Floors.Select((f, i) => (i, f)))
        foreach (var c in f.AllCells().Where(c => f[c].Mark?.StartsWith("gate:") == true))
        {
            var locked = DirectionExtensions.All.Where(d => f[c][d] == EdgeType.Locked).ToList();
            string gate = f[c].Mark!["gate:".Length..];
            if (locked.Count == 0) Item($"B{fi + 1} ({c.X},{c.Y}) の門 {gate} のマスに錠の辺(L)が無い。門は、印のマスの周りの L を開ける(L の両側どちらのマスに印を置いてもよい)。",
                $"Gate {gate} at B{fi + 1} ({c.X},{c.Y}) has no locked edge (L). A gate opens the L edges around its marked cell (either side of the L is fine).");
            else Item($"B{fi + 1} ({c.X},{c.Y}) の門 {gate}: {string.Join("・", locked.Select(d => DirName(d, "ja")))}の L を、手順 open {gate} が開ける。開けるイベントが、門の手前から行ける所にあるか。",
                $"Gate {gate} at B{fi + 1} ({c.X},{c.Y}): open {gate} unlocks its {string.Join("/", locked.Select(d => DirName(d, "en")))} L. Is the opening event reachable without passing the gate?");
        }

    // 土台の標準イベントの印(new のはめ込む部屋や fill が置いたもの)。はめ込む部屋ごとに組にして出す
    var own = pack.Events.Select(e => e.Id).ToHashSet();
    bool IsStandard(string? mark) => mark != null && (mark.StartsWith("gate:") || mark.StartsWith(EventPlacer.MarkPrefix) && !own.Contains(mark[EventPlacer.MarkPrefix.Length..]));
    var sets = PrefabSets(dungeon, baseContent, IsStandard, o.Lang);
    var inSet = sets.SelectMany(x => x.Cells.Select(c => (x.Floor, c.Cell))).ToHashSet();
    foreach (var set in sets)
    {
        string marks = string.Join("、", set.Cells.Select(c => $"({c.Cell.X},{c.Cell.Y}) {c.Mark}"));
        string missing = set.Missing.Count > 0 ? T($"欠けている印: {string.Join("、", set.Missing)}。一部だけ外すと、仕掛けが解けない。", $" Missing marks: {string.Join(", ", set.Missing)}. Removing only part of a set breaks it.") : "";
        string locks = set.Locks.Count > 0 ? T($"錠の辺(L): {string.Join("、", set.Locks)}。", $" Locked edges (L): {string.Join(", ", set.Locks)}.") : "";
        string group = set.Group != null ? T($"組 {set.Group} の部屋は、階をまたいで1つの仕掛け(どれかを外すなら全部外す)。", $" Rooms of group {set.Group} form one puzzle across floors (remove all of them or none).") : "";
        Item($"B{set.Floor + 1} 標準の部屋 {set.Name}: {marks}。{missing}{locks}{group}{(set.Missing.Count == 0 ? "そのまま動く。" : "")}外すなら組ごと(印の mark 行を全部消し、L を壁 | - か通路にする)。自分のイベントに作り替えるのもよい。",
            $"B{set.Floor + 1} standard room {set.Name}: {marks}.{missing}{locks}{group}{(set.Missing.Count == 0 ? " It works as is." : "")} To remove it, remove the whole set (delete all its mark lines and turn its L into a wall or an opening). Adapting it to your own events is fine too.");
    }
    var loose = dungeon.Floors.SelectMany((f, i) => f.AllCells().Where(c => IsStandard(f[c].Mark) && f[c].Mark!.StartsWith(EventPlacer.MarkPrefix) && !inSet.Contains((i, c))).Select(c => $"B{i + 1}({c.X},{c.Y}) {f[c].Mark}" + (data.Events.TryGetValue(f[c].Mark![EventPlacer.MarkPrefix.Length..], out var ev) && EventLabel(ev, o.Lang) is { } label ? (o.Lang == "en" ? $" \"{label}\"" : $"「{label}」") : ""))).ToList();
    if (loose.Count > 0)
        Item($"部屋に入っていない標準イベントの印 {loose.Count} 個: {string.Join("、", loose)}。fill が散らしたものか、組の残り。そのまま動く。使わないなら mark 行を消す(new --prefabs none は、はめ込む部屋を入れないだけで、あとの fill は散らす)。",
            $"{loose.Count} standard event mark(s) outside prefab rooms: {string.Join(", ", loose)}. Scattered by fill, or leftovers of a set. They work as is; delete the mark lines if unwanted (new --prefabs none only leaves out the prefab rooms; a later fill still scatters events).");

    // 散らすイベントの組み方
    Item(pack.EventsPlaced
        ? T("events_placed: true。散らすイベントは、もう地図に置いた分だけ(ゲームは新しく散らさない)。", "events_placed: true. Scattered events are only the ones already on the maps (the game will not scatter more).")
        : T("events_placed: false。新しいゲームのたびに、散らすイベントを置く(standard_events が true なら標準のものも)。手で置いた印だけにするなら true にする。", "events_placed: false. Scattered events are placed each new game (standard ones too if standard_events is true). Set true to keep only hand-placed marks."),
        "");

    // 歩くボットが踏まないマス
    var special = dungeon.Floors.SelectMany((f, i) => f.AllCells().Where(c => f[c].Feature is CellFeature.Teleporter or CellFeature.Chute).Select(c => $"B{i + 1}({c.X},{c.Y})")).ToList();
    if (special.Count > 0)
        Item($"check の「歩いたマス x / y」の差は、テレポーターとシュートのマス {special.Count} 個(乗ると移されるので、歩くボットは立たない): {string.Join("、", special)}。",
            $"The gap in check's \"cells walked x / y\" is the {special.Count} teleporter/chute cell(s) (stepping on them moves you): {string.Join(", ", special)}.");

    // 使っていない地図のファイル
    var used = Enumerable.Range(0, pack.Floors.Count).Select(i => Path.GetFullPath(Path.Combine(dir, pack.MapFile(i)))).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var mapsDir = Path.Combine(dir, "maps");
    var unused = Directory.Exists(mapsDir) ? Directory.EnumerateFiles(mapsDir, "*.txt").Where(f => !used.Contains(Path.GetFullPath(f))).Select(Path.GetFileName).ToList() : [];
    if (unused.Count > 0)
        Item($"dungeons.json が指していない地図のファイル: {string.Join("、", unused)}。控えなら残してよいが、zip に入る。", $"Map files not used by dungeons.json: {string.Join(", ", unused)}. Fine to keep as backups, but they go into the zip.");

    // 文の英日
    var oneLang = pack.Events.SelectMany(e => All(e.Steps).Where(s => s.Text != null && (string.IsNullOrWhiteSpace(s.Text.Get("ja")) || string.IsNullOrWhiteSpace(s.Text.Get("en")) || s.Text.Get("ja") == s.Text.Get("en"))).Select(_ => e.Id)).Distinct().ToList();
    if (oneLang.Count > 0)
        Item($"日本語か英語の片方しか無い(か同じ)文のあるイベント: {string.Join("、", oneLang)}。", $"Events with text missing in Japanese or English (or identical): {string.Join(", ", oneLang)}.");
    Item("仕掛けは、検査もボットも解き方までは確かめない。ゲームを --saves 付きで起こして、順に解けるか、取り逃がしが無いかを見る。",
        "Neither check nor the bots prove puzzles can be solved. Launch the game with --saves and confirm the order works and nothing can be missed.");
    Item("報告では、確かめ方を分けて書く。「データの照合」(check・finish・ファイルを読んで合わせたこと)、「ゲームで確かめた」(実際に遊んで見たこと)、「まだ確かめていない」。finish を流しただけで、確かめたことにはしない。",
        "In the report, keep three kinds apart: \"checked against the data\" (check, finish, reading the files), \"confirmed in the game\" (actually played), and \"not yet confirmed\". Running finish alone does not confirm anything.");
    return 0;
}

/// <summary>イベントの短い説明(エディタの一覧と同じ: 題、無ければ注記の最初の文、無ければ最初の文)。</summary>
static string? EventLabel(EventDef e, string lang)
{
    static string Short(string s) => s.Length > 30 ? s[..30] + "…" : s;
    static IEnumerable<EventStep> All(IEnumerable<EventStep> steps) => steps.SelectMany(s => s.Children().SelectMany(All).Prepend(s));
    if (e.Title?.Get(lang) is { Length: > 0 } title) return title;
    if (lang == "ja" && e.Note is { Length: > 0 } note) return Short(note.Split('。', '.')[0]);
    return All(e.Steps).Select(s => s.Text?.Get(lang)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) is { } text ? Short(text.Replace('\n', ' ')) : null;
}

static string DirName(Direction d, string lang) => lang == "en" ? d.ToString() : d.JaName();

/// <summary>
/// 地図に置かれた、土台のはめ込む部屋(mazegen.json の prefabs)を探す。部屋の印のどれかを手がかりに、部屋の左上を逆算し、
/// ほかの印がそろっているか、門のマスの錠の辺(L)を見る。同じ印(event:vault など)が2つの部屋にあるので、1つしか残っていない印は、その印を持つ部屋が1つのときだけ数える。
/// </summary>
static List<PrefabSet> PrefabSets(Dungeon dungeon, ContentLibrary baseContent, Func<string?, bool> isStandard, string lang)
{
    var settings = GravewardSpiral.Core.MazeGen.MazeGenSettings.Load(baseContent);
    var prefabs = settings.Prefabs.Where(r => File.Exists(r.ResolvedPath))
        .Select(r => (Rule: r, Map: MazeTextFormat.Parse(File.ReadAllText(r.ResolvedPath), r.File)))
        .Select(p => (p.Rule, Name: Path.GetFileNameWithoutExtension(p.Rule.File), Marks: p.Map.AllCells().Where(c => p.Map[c].Mark != null && !p.Map[c].Mark!.StartsWith("boss:")).Select(c => (Pos: c, Mark: p.Map[c].Mark!)).ToList()))
        .Where(p => p.Marks.Count > 0).ToList();
    var owners = prefabs.SelectMany(p => p.Marks.Select(m => (m.Mark, p.Name))).GroupBy(x => x.Mark).ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().Count());
    var result = new List<PrefabSet>();
    for (int fi = 0; fi < dungeon.Floors.Count; fi++)
    {
        var f = dungeon.Floors[fi];
        foreach (var p in prefabs)
        {
            var origins = new HashSet<GridPos>();
            foreach (var c in f.AllCells().Where(c => isStandard(f[c].Mark)))
                foreach (var m in p.Marks.Where(m => m.Mark == f[c].Mark))
                    origins.Add(new GridPos(c.X - m.Pos.X, c.Y - m.Pos.Y));
            foreach (var o in origins)
            {
                var found = new List<(GridPos Cell, string Mark)>();
                var missing = new List<string>();
                var locks = new List<string>();
                foreach (var m in p.Marks)
                {
                    var at = new GridPos(o.X + m.Pos.X, o.Y + m.Pos.Y);
                    bool inside = at.X >= 0 && at.Y >= 0 && at.X < f.Width && at.Y < f.Height;
                    if (inside && f[at].Mark == m.Mark) found.Add((at, m.Mark));
                    else missing.Add($"({at.X},{at.Y}) {m.Mark}");
                    if (inside && m.Mark.StartsWith("gate:"))
                        foreach (var d in DirectionExtensions.All.Where(d => f[at][d] == EdgeType.Locked)) locks.Add($"({at.X},{at.Y}) {DirName(d, lang)}");
                }
                if (found.Count == 0 || found.Count == 1 && p.Marks.Count > 1 && owners[found[0].Mark] > 1) continue;
                result.Add(new PrefabSet(fi, p.Name, p.Rule.Group, found, missing, locks));
            }
        }
    }
    return result;
}


// ---------------- list ----------------

static int List(Options o)
{
    string what = o.Arg ?? throw new UsageException("list には monsters / items / conditions / skills / abilities / damage / classes / races / sounds を渡す");
    var (content, data, _) = LoadWith(o.ScenarioDir);
    if (data == null) return Fail("読み込めない / cannot load");
    string N(GravewardSpiral.Core.Content.LocalizedText t) => $"{t.Get("ja")} / {t.Get("en")}";
    switch (what)
    {
        case "monsters":
            Console.WriteLine("id | 名前 / name | CR | 種類 / type");
            foreach (var m in data.Monsters.Values.Where(m => m.Cr >= o.CrMin && m.Cr <= o.CrMax && (o.Types.Length == 0 || o.Types.Contains(m.CreatureType)))
                .OrderBy(m => m.Cr).ThenBy(m => m.Id, StringComparer.Ordinal))
                Console.WriteLine($"{m.Id} | {N(m.Name)} | {m.Cr} | {m.CreatureType}");
            break;
        case "items":
            Console.WriteLine("id | 名前 / name | 種類 / kind");
            foreach (var i in data.Items.Values.OrderBy(i => i.Kind).ThenBy(i => i.Id, StringComparer.Ordinal))
                Console.WriteLine($"{i.Id} | {N(i.Name)} | {i.Kind.ToString().ToLowerInvariant()}");
            break;
        case "conditions":
            foreach (var c in Enum.GetValues<Condition>()) Console.WriteLine(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(c.ToString()));
            break;
        case "skills":
            foreach (var sk in content.All("skills").OrderBy(x => x.Id, StringComparer.Ordinal)) Console.WriteLine(sk.Id);
            break;
        case "abilities":
            foreach (var a in new[] { "str", "dex", "con", "int", "wis", "cha" }) Console.WriteLine(a);
            break;
        case "damage":
            foreach (var d in new[] { "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder" }) Console.WriteLine(d);
            break;
        case "classes":
            foreach (var c in data.Classes.Values.OrderBy(c => c.Id, StringComparer.Ordinal)) Console.WriteLine($"{c.Id} | {N(c.Name)}");
            break;
        case "races":
            foreach (var r in data.Races.Values.OrderBy(r => r.Id, StringComparer.Ordinal)) Console.WriteLine($"{r.Id} | {N(r.Name)}");
            break;
        case "sounds":
            // イベントの手順 sound の id(効果音のファイル名。シナリオの audio/se に同じ名前で置くと差し替わる)
            var sounds = new[] { Path.Combine(ContentRoot(), "srd", "audio", "se") }.Concat(o.ScenarioDir is { } sd ? [Path.Combine(sd, "audio", "se")] : [])
                .Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d)).Select(Path.GetFileNameWithoutExtension).Distinct().Order(StringComparer.Ordinal);
            foreach (var id in sounds) Console.WriteLine(id);
            break;
        default:
            throw new UsageException($"list で知らない種類 / unknown list: {what}");
    }
    return 0;
}

// ---------------- fill ----------------

static int Fill(Options o)
{
    string dir = o.ScenarioDir ?? throw new UsageException("fill にはシナリオのフォルダを渡す / fill needs a scenario folder");
    if (dir.StartsWith(Path.Combine(Path.GetTempPath(), "ScenarioCheck"), StringComparison.OrdinalIgnoreCase))
        throw new UsageException("fill は zip には使えない(フォルダを渡す) / fill needs a folder, not a zip");
    var problems = new List<string>();
    var pack = ScenarioPack.Load(dir, problems);
    foreach (var p in problems) Console.Error.WriteLine(p);
    var (_, data, baseContent) = LoadWith(dir);
    if (data == null) return Fail("読み込めない(先に check で直す) / cannot load (run check first)");
    var done = ScenarioFill.Fill(pack, data, baseContent, o.Seed, ScenarioFill.Parts.All);
    pack.Save(baseContent);
    Console.WriteLine($"埋めたもの / filled: {done}(顔ぶれ / rosters {pack.Rosters.Count(r => r.Count > 0)}、名のある敵 / named {pack.Named?.Count ?? 0}、" +
        $"散らすイベントの印 / scattered event marks {pack.Floors.Sum(f => f.AllCells().Count(c => f[c].Mark?.StartsWith("event:") == true))})");
    return 0;
}

// ---------------- spells ----------------

static int Spells(Options o)
{
    string id = o.Arg ?? throw new UsageException("spells にはクラスの id を渡す / spells needs a class id");
    var (_, data, _) = LoadWith(o.ScenarioDir);
    if (data == null) return Fail("読み込めない / cannot load");
    if (!data.Classes.TryGetValue(id, out var cls)) return Fail($"クラス {id} が無い / no class {id}");
    if (!data.Races.ContainsKey(o.Race)) return Fail($"種族 {o.Race} が無い / no race {o.Race}");
    Console.WriteLine($"{id} ({cls.Name.Get("ja")} / {cls.Name.Get("en")})  style={Spellcasting.Kind(cls)}  ability={cls.Spellcasting?.Ability ?? "-"}");
    Console.WriteLine("Lv | slots 1-9 | cantrips | known/prepared | new spells (assigned level)");
    var seen = new HashSet<string>();
    for (int lv = 1; lv <= o.Max; lv++)
    {
        var c = CharacterFactory.QuickCreate(data, "Probe", o.Race, id, lv);
        string slots = string.Join("/", Enumerable.Range(1, 9).Select(s => Spellcasting.MaxSlots(data, c, s)));
        int pact = Spellcasting.PactSlots(data, c);
        if (pact > 0) slots += $" pact {pact}xL{Spellcasting.PactSlotLevel(data, c)}";
        int known = Spellcasting.Kind(cls) is CasterKind.Prepared or CasterKind.Book ? Spellcasting.PreparedCount(cls, c) : Spellcasting.SpellsKnown(cls, c);
        var spells = Spellcasting.ClassSpells(data, cls, c, cantrips: true).Concat(Spellcasting.ClassSpells(data, cls, c, cantrips: false))
            .Where(sp => seen.Add(sp.Id)).Select(sp => $"{sp.Id}({Spellcasting.AssignedLevel(data, cls, c, sp)})");
        Console.WriteLine($"{lv,2} | {slots} | {Spellcasting.CantripsKnown(cls, c)} | {known} | {string.Join(" ", spells)}");
    }
    return 0;
}

// ---------------- uses ----------------

static int Uses(Options o)
{
    string id = o.Arg ?? throw new UsageException("uses には id を渡す / uses needs an id");
    var (content, _, _) = LoadWith(o.ScenarioDir);
    int found = 0;
    foreach (var collection in content.Collections.OrderBy(x => x, StringComparer.Ordinal))
    {
        foreach (var item in content.All(collection))
        {
            foreach (var path in Find(item.Data, "", id))
            {
                // 自分の id の欄は数えない
                if (item.Id == id && path == "id") continue;
                Console.WriteLine($"{collection}/{item.Id}  {path}  ({item.File ?? item.Pack.Id})");
                found++;
            }
        }
    }
    Console.WriteLine(found == 0 ? $"{id} を指しているものは無い / nothing refers to {id}" : $"{found} 件 / {found} reference(s)");
    return 0;

    // 文字列の値が id そのものか、「種類:id」(event:id など)の所
    static IEnumerable<string> Find(JsonElement e, string path, string id)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var hit in Find(p.Value, path.Length == 0 ? p.Name : $"{path}.{p.Name}", id)) yield return hit;
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var x in e.EnumerateArray())
                    foreach (var hit in Find(x, $"{path}[{i++}]", id)) yield return hit;
                break;
            case JsonValueKind.String:
                string s = e.GetString()!;
                if (s == id || s.EndsWith(":" + id, StringComparison.Ordinal)) yield return path;
                break;
        }
    }
}

// ---------------- art ----------------

static int Art(Options o)
{
    var (content, data, _) = LoadWith(o.ScenarioDir);
    if (data == null) return Fail("読み込めない / cannot load");
    Console.WriteLine("id | 名前 / name | picture (from)");
    foreach (var m in data.Monsters.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
    {
        string own = $"art/monsters/{m.Id}.png";
        string pic = content.FindFile(own) != null ? m.Id
            : m.ArtFrom is { } from ? (content.FindFile($"art/monsters/{from}.png") != null ? $"{from} (art_from)" : $"{from} (art_from, missing)")
            : "-";
        Console.WriteLine($"{m.Id} | {m.Name.Get("ja")} / {m.Name.Get("en")} | {pic}");
    }
    return 0;
}

// ---------------- reference ----------------

static int Reference()
{
    Console.WriteLine("## event steps (events.json の steps[].type)");
    Console.WriteLine(string.Join(", ", EventStep.KnownTypes.Order(StringComparer.Ordinal)));
    Console.WriteLine();
    Console.WriteLine("## event conditions (when[].type)");
    Console.WriteLine(string.Join(", ", EventCondition.KnownTypes.Order(StringComparer.Ordinal)));
    Console.WriteLine();
    Console.WriteLine("## effect parts (spells / items / monster actions の effects[].type)");
    Console.WriteLine(string.Join(", ", EffectPart.KnownTypes.Order(StringComparer.Ordinal)));
    Console.WriteLine();
    Console.WriteLine("## passive effects (features.json の effects[].type と、使える on)");
    foreach (var t in PassiveRules.EffectTypes.Order(StringComparer.Ordinal))
        Console.WriteLine(PassiveRules.OnsFor.TryGetValue(t, out var ons) ? $"{t}  on: {string.Join(" / ", ons)}" : t);
    return 0;
}

// ---------------- 共通 ----------------

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

/// <summary>標準のパックに、シナリオ(あれば)を重ねて読む。zip は一時フォルダに広げる。</summary>
static (ContentLibrary Content, GameData? Data, ContentLibrary Base) LoadWith(string? scenarioDir)
{
    string root = ContentRoot();
    var baseContent = ContentLibrary.Load(root);
    var content = scenarioDir == null ? ContentLibrary.Load(root) : ContentLibrary.Load(root, scenarioDir);
    DungeonLoader.ValidateAll(content);
    GameData? data = null;
    try { data = GameData.Load(content); }
    catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException)
    {
        content.Problems.Add(ex.Message);
    }
    return (content, data, baseContent);
}

/// <summary>ゲームの Content(この道具の横か、開発のときはリポジトリの Content)。</summary>
static string ContentRoot()
{
    string here = Path.Combine(AppContext.BaseDirectory, "Content");
    if (Directory.Exists(here)) return here;
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        string c = Path.Combine(dir.FullName, "Content");
        if (Directory.Exists(c) && File.Exists(Path.Combine(dir.FullName, "GravewardSpiral.slnx"))) return c;
    }
    throw new UsageException("ゲームの Content フォルダが見つからない(ScenarioCheck.exe はゲームと同じフォルダに置く) / Content folder not found");
}

sealed class UsageException(string message) : Exception(message);

/// <summary>地図に置かれた、はめ込む部屋1つ分の印。</summary>
sealed record PrefabSet(int Floor, string Name, string? Group, List<(GridPos Cell, string Mark)> Cells, List<string> Missing, List<string> Locks);

/// <summary>コマンドと引数。</summary>
sealed class Options
{
    public string Command = "check";
    public string? Arg;
    public string? ScenarioDir;
    public string Lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
    public int Bots;
    public string[] Party = ["fighter", "fighter", "paladin", "rogue", "cleric", "wizard"];
    public int Level = 1;
    public string Race = "human";
    public int Max = 20;
    public ulong Seed = 1;
    public int Floors = 3;
    public int Width = 12, Height = 12;
    public int LevelMin = 1, LevelMax = 5;
    public string Rooms = "normal", Doors = "normal", Gimmicks = "normal", Prefabs = "standard";
    public int? Floor;
    public bool Help;
    public double CrMin = 0, CrMax = 99;
    public string[] Types = [];

    public static Options Parse(string[] args)
    {
        var o = new Options();
        int i = 0;
        // コマンドを省いてフォルダか zip を渡したら check
        if (args[0] is "check" or "spells" or "uses" or "art" or "reference" or "fill" or "new" or "list" or "map" or "finish") o.Command = args[i++];
        for (; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new UsageException($"{a} の値が無い / missing value for {a}");
            switch (a)
            {
                case "--scenario": o.ScenarioDir = Open(Next()); break;
                case "--lang": o.Lang = Next() == "en" ? "en" : "ja"; break;
                case "--bots": o.Bots = Math.Clamp(int.Parse(Next()), 0, 100); break;
                case "--party": o.Party = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--level": o.Level = Math.Clamp(int.Parse(Next()), 1, 20); break;
                case "--race": o.Race = Next(); break;
                case "--max": o.Max = Math.Clamp(int.Parse(Next()), 1, 30); break;
                case "--seed": o.Seed = Math.Max(1, ulong.Parse(Next())); break;
                case "--floors": o.Floors = Math.Clamp(int.Parse(Next()), 1, 99); break;
                case "--size":
                {
                    var wh = Next().Split('x', 'X');
                    o.Width = Math.Clamp(int.Parse(wh[0]), 4, 64);
                    o.Height = Math.Clamp(int.Parse(wh[^1]), 4, 64);
                    break;
                }
                case "--levels":
                {
                    var lv = Next().Split('-');
                    o.LevelMin = Math.Clamp(int.Parse(lv[0]), 1, 20);
                    o.LevelMax = Math.Clamp(int.Parse(lv[^1]), o.LevelMin, 20);
                    break;
                }
                case "--rooms": o.Rooms = Knob(Next(), "few", "normal", "many"); break;
                case "--doors": o.Doors = Knob(Next(), "few", "normal", "many"); break;
                case "--gimmicks": o.Gimmicks = Knob(Next(), "none", "few", "normal", "many"); break;
                case "--prefabs": o.Prefabs = Knob(Next(), "standard", "none"); break;
                case "--floor": o.Floor = Math.Max(1, int.Parse(Next())); break;
                case "--help" or "-h": o.Help = true; break;
                case "--cr":
                {
                    var cr = Next().Split('-');
                    o.CrMin = Cr(cr[0]);
                    o.CrMax = Cr(cr[^1]);
                    break;
                }
                case "--type": o.Types = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                default:
                    if (a.StartsWith("--")) throw new UsageException($"知らない引数 / unknown option: {a}");
                    if (o.Command is "check" or "fill" or "map" or "finish" && o.ScenarioDir == null) o.ScenarioDir = Open(a);
                    else if (o.Arg == null) o.Arg = a;
                    else throw new UsageException($"余分な引数 / extra argument: {a}");
                    break;
            }
        }
        return o;
    }

    /// <summary>CR の値(1/4 や 0.25 の形も)。</summary>
    private static double Cr(string v) => v.Split('/') is [var n, var d] ? double.Parse(n) / double.Parse(d) : double.Parse(v);

    private static string Knob(string value, params string[] allowed) =>
        allowed.Contains(value) ? value : throw new UsageException($"{value} は {string.Join(" / ", allowed)} のどれか / must be one of {string.Join(", ", allowed)}");

    /// <summary>シナリオのフォルダ(manifest.json のある所)。zip なら一時フォルダに広げて、その中の manifest.json のあるフォルダ。</summary>
    private static string Open(string path)
    {
        if (Directory.Exists(path))
        {
            if (File.Exists(Path.Combine(path, "manifest.json"))) return Path.GetFullPath(path);
            throw new UsageException($"{path} に manifest.json が無い / no manifest.json in {path}");
        }
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string to = Path.Combine(Path.GetTempPath(), "ScenarioCheck", Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N")[..8]);
            ZipFile.ExtractToDirectory(path, to);
            string? manifest = Directory.EnumerateFiles(to, "manifest.json", SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault();
            return manifest != null ? Path.GetDirectoryName(manifest)! : throw new UsageException($"{path} の中に manifest.json が無い / no manifest.json in {path}");
        }
        throw new UsageException($"{path} が見つからない / not found: {path}");
    }
}
