using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Maze;

/// <summary>
/// エレベーターの縦穴。座標 (X, Y) と、上の階 From・下の階 To(1 が B1)だけを決める。
/// 範囲の階のうち、そのマスが空いている階(<see cref="Elevators.CanStop"/>)には自動で止まり、岩や壁や仕掛けのある階は素通りする。
/// 止まる階のマスは、エレベーターの仕掛けと印 elevator:x_y になる(乗るときは、同じ印のある階へ移る)。
/// </summary>
public sealed record ElevatorShaft(int X, int Y, int From, int To)
{
    /// <summary>
    /// 動かすのに要るフラグ(スイッチ)。書けば、そのフラグが立つまでエレベーターは動かない(乗っても「動かない」と出るだけ)。
    /// 標準のイベント elevator_switch は <see cref="Elevators.DefaultPower"/> を立てる。null なら、はじめから動く。
    /// </summary>
    public string? Power { get; init; }

    /// <summary>止まる階のマスの印(縦穴ごとに1つ)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Mark => $"elevator:{X}_{Y}";

    [System.Text.Json.Serialization.JsonIgnore]
    public GridPos Cell => new(X, Y);

    /// <summary>その階(0 が B1)が範囲に入るか。</summary>
    public bool Covers(int floor) => floor + 1 >= Math.Min(From, To) && floor + 1 <= Math.Max(From, To);
}

/// <summary>エレベーターの縦穴の扱い(迷宮の読み込み・自動生成・エディタで使う)。</summary>
public static class Elevators
{
    /// <summary>標準のイベント「エレベーターのスイッチ」(elevator_switch)が立てるフラグ。</summary>
    public const string DefaultPower = "elevator_power";

    /// <summary>そのマス(止まる階のエレベーター)の縦穴が、スイッチ待ちで動かないか。動かないなら、要るフラグを返す。</summary>
    public static string? Unpowered(IEnumerable<ElevatorShaft> shafts, MazeCell cell, ICollection<string> flags) =>
        shafts.FirstOrDefault(s => s.Mark == cell.Mark)?.Power is { } power && !flags.Contains(power) ? power : null;

    /// <summary>
    /// 縦穴がその階のそのマスに止まれるか: 階の中で、岩盤でなく、ほかの仕掛けも印も無く(この縦穴のものは別)、どこかの辺が開いている。
    /// </summary>
    public static bool CanStop(MazeFloor floor, ElevatorShaft shaft)
    {
        if (!floor.InBounds(shaft.X, shaft.Y)) return false;
        var c = floor[shaft.Cell];
        bool mine = c.Feature == CellFeature.Elevator && c.Mark == shaft.Mark;
        if (!mine && (c.Feature != CellFeature.None || c.Mark != null)) return false;
        return DirectionExtensions.All.Any(d => c[d] != EdgeType.Wall);
    }

    /// <summary>縦穴が止まる階(0 が B1)。</summary>
    public static List<int> Stops(IReadOnlyList<MazeFloor> floors, ElevatorShaft shaft) =>
        Enumerable.Range(0, floors.Count).Where(f => shaft.Covers(f) && CanStop(floors[f], shaft)).ToList();

    /// <summary>縦穴を階に入れる(止まる階のマスをエレベーターにする)。止まる階を返す。</summary>
    public static List<int> Apply(IReadOnlyList<MazeFloor> floors, ElevatorShaft shaft)
    {
        var stops = Stops(floors, shaft);
        foreach (int f in stops)
        {
            var c = floors[f][shaft.Cell];
            c.Feature = CellFeature.Elevator;
            c.Mark = shaft.Mark;
        }
        return stops;
    }

    public static void ApplyAll(IReadOnlyList<MazeFloor> floors, IEnumerable<ElevatorShaft> shafts)
    {
        foreach (var s in shafts) Apply(floors, s);
    }

    /// <summary>縦穴を階から外す(止まっていたマスを、ふつうのマスに戻す)。</summary>
    public static void Clear(IReadOnlyList<MazeFloor> floors, ElevatorShaft shaft)
    {
        foreach (var f in floors)
        {
            if (!f.InBounds(shaft.X, shaft.Y)) continue;
            var c = f[shaft.Cell];
            if (c.Feature == CellFeature.Elevator && c.Mark == shaft.Mark)
            {
                c.Feature = CellFeature.None;
                c.Mark = null;
            }
        }
    }

    /// <summary>
    /// from〜to 階(1 が B1)に縦穴を通す座標を選ぶ。止まる階ができるだけ多く(2 階以上)、同じなら上と下の階に止まるもの、
    /// それでも同じなら乱数で。2 階以上に止まれる座標が無ければ null。
    /// </summary>
    public static ElevatorShaft? Best(IReadOnlyList<MazeFloor> floors, int from, int to, IRandom rng)
    {
        int lo = Math.Max(1, Math.Min(from, to)), hi = Math.Min(floors.Count, Math.Max(from, to));
        if (hi - lo < 1) return null;
        int width = floors.Skip(lo - 1).Take(hi - lo + 1).Max(f => f.Width), height = floors.Skip(lo - 1).Take(hi - lo + 1).Max(f => f.Height);
        var best = new List<ElevatorShaft>();
        int bestScore = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var s = new ElevatorShaft(x, y, lo, hi);
                var stops = Stops(floors, s);
                if (stops.Count < 2) continue;
                int score = stops.Count * 4 + (stops.Contains(lo - 1) ? 2 : 0) + (stops.Contains(hi - 1) ? 2 : 0);
                if (score > bestScore) { bestScore = score; best.Clear(); }
                if (score == bestScore) best.Add(s);
            }
        return best.Count == 0 ? null : best[rng.Next(0, best.Count)];
    }

    /// <summary>
    /// 前の形のエレベーター(階ごとに置いた elevator:組 の印)を縦穴に直す。組ごとに、置いてあった一番上の階から一番下の階までの範囲で
    /// 座標を選び直し(<see cref="Best"/>)、前のマスは消す。縦穴にできなかった組はそのまま残す。作った縦穴を返す。
    /// </summary>
    public static List<ElevatorShaft> FromLegacy(IReadOnlyList<MazeFloor> floors, IRandom rng, IEnumerable<ElevatorShaft>? existing = null)
    {
        var known = (existing ?? []).Select(s => s.Mark).ToHashSet(StringComparer.Ordinal);
        var groups = floors.SelectMany((f, i) => f.AllCells().Where(p => f[p].Feature == CellFeature.Elevator && f[p].Mark is { } m && !known.Contains(m))
                .Select(p => (Floor: i, Cell: p, Mark: f[p].Mark!)))
            .GroupBy(x => x.Mark).ToList();
        var made = new List<ElevatorShaft>();
        foreach (var g in groups)
        {
            var cells = g.ToList();
            foreach (var (f, p, _) in cells)
            {
                floors[f][p].Feature = CellFeature.None;
                floors[f][p].Mark = null;
            }
            int lo = cells.Min(x => x.Floor) + 1, hi = cells.Max(x => x.Floor) + 1;
            // 前の場所が全部同じ座標なら、そのまま縦穴にする
            var same = cells.Select(x => x.Cell).Distinct().ToList();
            var shaft = same.Count == 1 ? new ElevatorShaft(same[0].X, same[0].Y, lo, hi) : Best(floors, lo, hi, rng);
            if (shaft != null && Stops(floors, shaft).Count >= 2)
            {
                Apply(floors, shaft);
                made.Add(shaft);
            }
            else
            {
                // 縦穴にできなければ、前のまま戻す
                foreach (var (f, p, m) in cells)
                {
                    floors[f][p].Feature = CellFeature.Elevator;
                    floors[f][p].Mark = m;
                }
            }
        }
        return made;
    }
}
