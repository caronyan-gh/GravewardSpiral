// シードを指定して迷宮を生成し、検査の結果と地図をテキストで出す。
//
//   dotnet run --project tools/GravewardSpiral.MapCheck -- [--seed S] [--floors N] [--size WxH | --min WxH --max WxH]
//                                                        [--settings ID] [--count K] [--out DIR] [--quiet]
//
//   --seed      数か文字(文字はハッシュにする)。省略すると 1
//   --count K   シードを S から K 個続けて試す(地図は出さず、通ったかと時間だけ)
//   --out DIR   各階をマップのテキスト形式で DIR に書き出す
//   --quiet     地図を出さない
//   どの指定でも、自動で歩くボット(MazeBot)でも確かめる(鍵の順番、回転床、テレポーター、エレベーター)
using System.Diagnostics;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;

Console.OutputEncoding = System.Text.Encoding.UTF8;

string? seedText = null, settingsId = null, outDir = null;
int? floors = null, count = null;
(int W, int H)? size = null, min = null, max = null;
bool quiet = false;

for (int i = 0; i < args.Length; i++)
{
    string next = i + 1 < args.Length ? args[i + 1] : "";
    switch (args[i])
    {
        case "--seed": seedText = next; i++; break;
        case "--floors": floors = int.Parse(next); i++; break;
        case "--size": size = ParseSize(next); i++; break;
        case "--min": min = ParseSize(next); i++; break;
        case "--max": max = ParseSize(next); i++; break;
        case "--settings": settingsId = next; i++; break;
        case "--count": count = int.Parse(next); i++; break;
        case "--out": outDir = next; i++; break;
        case "--quiet": quiet = true; break;
        default:
            Console.Error.WriteLine($"引数 '{args[i]}' は読めません");
            return 2;
    }
}

string contentRoot = FindContent();
var content = ContentLibrary.Load(contentRoot);
if (!content.IsValid)
{
    foreach (var p in content.Problems) Console.Error.WriteLine(p);
    return 1;
}
var settings = MazeGenSettings.Load(content, settingsId ?? MazeGenSettings.DefaultId);
var mode = size is { } s ? FloorSizeMode.Fixed(s.W, s.H)
    : min is { } a && max is { } b ? new FloorSizeMode(a.W, b.W, a.H, b.H)
    : FloorSizeMode.Fixed(settings.DefaultWidth, settings.DefaultHeight);
ulong seed = RngStreams.ParseSeed(seedText) ?? 1;
int floorCount = floors ?? settings.DefaultFloors;
var generator = new MazeGenerator(settings);

if (count is int k)
{
    int failed = 0;
    var total = Stopwatch.StartNew();
    for (int n = 0; n < k; n++)
    {
        ulong sd = seed + (ulong)n;
        try
        {
            var outp = generator.Generate(new GenerationRequest(sd, floorCount, mode));
            var r = ReachabilityValidator.Validate(outp.Dungeon);
            var bot = MazeBot.Explore(outp.Dungeon);   // ゲームと同じ動きで歩いて、鍵の順番などで詰まないか
            if (!r.Ok || !bot.Ok)
            {
                failed++;
                Console.WriteLine($"seed {sd}: NG {string.Join(" / ", r.Problems.Concat(bot.Problems))}");
            }
        }
        catch (InvalidOperationException ex)
        {
            failed++;
            Console.WriteLine($"seed {sd}: 失敗 {ex.Message}");
        }
    }
    Console.WriteLine($"{k} 個のシード中 {k - failed} 個が検査を通った({total.ElapsedMilliseconds} ms、1つあたり {total.ElapsedMilliseconds / Math.Max(1, k)} ms)");
    return failed == 0 ? 0 : 1;
}

var sw = Stopwatch.StartNew();
var output = generator.Generate(new GenerationRequest(seed, floorCount, mode));
sw.Stop();
var dungeon = output.Dungeon;
var result = ReachabilityValidator.Validate(dungeon);

Console.WriteLine($"シード {seed}、{dungeon.Floors.Count} 階、生成 {sw.ElapsedMilliseconds} ms");
Console.WriteLine(result.Ok ? $"検査: 通った(立てるマス {result.StandingCells}、町から行けるマス {result.ReachedCells})" : "検査: 通らない");
foreach (var p in result.Problems) Console.WriteLine("  " + p);
var walk = MazeBot.Explore(dungeon);
Console.WriteLine(walk.Ok ? $"ボット: 全部歩けた({walk.Visited} マス)" : "ボット: 詰まる所がある");
foreach (var p in walk.Problems) Console.WriteLine("  " + p);
foreach (var line in output.Log) Console.WriteLine("  " + line);

for (int f = 0; f < dungeon.Floors.Count; f++)
{
    var fl = dungeon.Floors[f];
    var cells = fl.AllCells().Select(p => fl[p]).ToList();
    int Count(CellFeature x) => cells.Count(c => c.Feature == x);
    int Zone_(Zone z) => cells.Count(c => c.Zone.HasFlag(z));
    string wrap = (fl.WrapX ? "東西" : "") + (fl.WrapY ? "南北" : "");
    Console.WriteLine();
    Console.WriteLine($"B{f + 1} {fl.Width}x{fl.Height}{(wrap.Length > 0 ? $" ループ:{wrap}" : "")}  岩盤 {Count(CellFeature.Solid)}  " +
                      $"扉 {Edges(fl, EdgeType.Door)} 隠し扉 {Edges(fl, EdgeType.SecretDoor)} 一方通行 {Edges(fl, EdgeType.OneWayOut)}  " +
                      $"暗闇 {Zone_(Zone.Dark)} 霧 {Zone_(Zone.Fog)} 魔法禁止 {Zone_(Zone.AntiMagic)}  " +
                      $"回転床 {Count(CellFeature.Spinner)} 落とし穴 {Count(CellFeature.Pit)} テレポーター {Count(CellFeature.Teleporter)} シュート {Count(CellFeature.Chute)}");
    if (!quiet) Console.Write(MazeTextFormat.Write(fl));
    if (outDir != null)
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, $"{fl.Id}.txt"), MazeTextFormat.Write(fl));
    }
}
return result.Ok ? 0 : 1;

static int Edges(MazeFloor f, EdgeType t) =>
    f.AllCells().Sum(p => new[] { Direction.East, Direction.South }.Count(d => f[p][d] == t || (t == EdgeType.OneWayOut && f[p][d] == EdgeType.OneWayIn)));

static (int, int) ParseSize(string s)
{
    var parts = s.Split('x');
    return (int.Parse(parts[0]), int.Parse(parts[1]));
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
