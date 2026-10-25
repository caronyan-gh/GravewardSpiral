using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Core.MazeGen;

/// <summary>
/// 迷宮全体を1つの道の図にして、行けるか・戻れるかを調べる。
/// 図の点は「立っていられるマス」(テレポーターとシュートのマスは踏むとすぐ飛ぶので、通り道としてだけ数える)と町。
/// 道は、通れる辺(扉・隠し扉・出るだけの一方通行を含む。東西南北の端のループも)、テレポーター、シュート、階段。
/// 調べること:
/// - 町から入って、すべてのマス(岩盤を除く)を踏めるか。
/// - 行けたマスのどこからでも、町へ戻れるか(出られなくならないか)。
/// </summary>
public static class ReachabilityValidator
{
    private readonly record struct Node(int Floor, GridPos Cell);

    private static readonly Node Town = new(-1, default);

    public sealed record Result(List<string> Problems, int StandingCells, int ReachedCells)
    {
        public bool Ok => Problems.Count == 0;
    }

    public static Result Validate(Dungeon dungeon) => Validate(dungeon, includeChutes: true);

    /// <param name="includeChutes">false なら、シュートを使わない(シュートを置く前の、階ごとの検査に使う)。</param>
    public static Result Validate(Dungeon dungeon, bool includeChutes)
    {
        var forward = new Dictionary<Node, List<Node>>();
        void Add(Node from, Node to)
        {
            if (!forward.TryGetValue(from, out var list)) forward[from] = list = [];
            list.Add(to);
        }

        var floors = dungeon.Floors;
        for (int f = 0; f < floors.Count; f++)
        {
            var floor = floors[f];
            foreach (var p in floor.AllCells())
            {
                var cell = floor[p];
                if (!Standing(cell)) continue;
                var here = new Node(f, p);
                foreach (var d in DirectionExtensions.All)
                {
                    if (!Passable(cell[d])) continue;
                    var n = floor.Neighbor(p, d);
                    if (n == null || floor[n.Value].IsSolid) continue;
                    var to = Resolve(dungeon, f, n.Value, includeChutes);
                    if (to != null) Add(here, to.Value);
                }
                // エレベーター: 同じ組のエレベーターの上へ
                if (cell.Feature == CellFeature.Elevator && cell.Mark != null)
                {
                    for (int g = 0; g < floors.Count; g++)
                    {
                        if (g == f) continue;
                        foreach (var q in floors[g].AllCells().Where(q => floors[g][q].Feature == CellFeature.Elevator && floors[g][q].Mark == cell.Mark))
                            Add(here, new Node(g, q));
                    }
                }
                // 階段: 着くのは反対の階段の上(MazeNavigator.UseStairs と同じ)
                if (cell.Feature == CellFeature.StairsUp)
                {
                    if (f == 0) Add(here, Town);
                    else if (floors[f - 1].FindFeature(CellFeature.StairsDown) is { } down) Add(here, new Node(f - 1, down));
                }
                else if (cell.Feature == CellFeature.StairsDown && f + 1 < floors.Count && floors[f + 1].FindFeature(CellFeature.StairsUp) is { } up)
                {
                    Add(here, new Node(f + 1, up));
                }
            }
        }
        var b1 = floors[0];
        var entry = new Node(0, b1.FindFeature(CellFeature.StairsUp) ?? b1.Start);
        Add(Town, entry);

        // 町から行けるところ
        var reached = Bfs(Town, forward);
        // 町へ戻れるところ(道を逆にたどる)
        var backward = new Dictionary<Node, List<Node>>();
        foreach (var (from, list) in forward)
        foreach (var to in list)
        {
            if (!backward.TryGetValue(to, out var back)) backward[to] = back = [];
            back.Add(from);
        }
        var canReturn = Bfs(Town, backward);

        var problems = new List<string>();
        int standing = 0, reachedCount = 0;
        for (int f = 0; f < floors.Count; f++)
        {
            var floor = floors[f];
            var unreachable = new List<GridPos>();
            var trapped = new List<GridPos>();
            foreach (var p in floor.AllCells())
            {
                var cell = floor[p];
                if (cell.IsSolid) continue;
                var node = new Node(f, p);
                if (Standing(cell))
                {
                    standing++;
                    if (reached.Contains(node))
                    {
                        reachedCount++;
                        if (!canReturn.Contains(node)) trapped.Add(p);
                    }
                    else
                    {
                        unreachable.Add(p);
                    }
                }
                else if (!TouchedFromReached(dungeon, f, p, reached))
                {
                    // 踏まれるだけのマス(テレポーター・シュート)は、どこかの行けるマスから踏めれば良い
                    unreachable.Add(p);
                }
            }
            if (unreachable.Count > 0)
                problems.Add($"B{f + 1}: 町から行けないマスが {unreachable.Count} 個あります(例: {string.Join(" ", unreachable.Take(5))})");
            if (trapped.Count > 0)
                problems.Add($"B{f + 1}: 入ると町へ戻れなくなるマスが {trapped.Count} 個あります(例: {string.Join(" ", trapped.Take(5))})");
        }
        return new Result(problems, standing, reachedCount);
    }

    private static bool TouchedFromReached(Dungeon dungeon, int f, GridPos p, HashSet<Node> reached)
    {
        var floor = dungeon.Floors[f];
        foreach (var d in DirectionExtensions.All)
        {
            var n = floor.Neighbor(p, d);
            if (n == null) continue;
            // n から p へ入れるか(n の側の辺で見る)
            if (reached.Contains(new Node(f, n.Value)) && Passable(floor[n.Value][d.Opposite()])) return true;
        }
        return false;
    }

    /// <summary>立っていられるマスか(岩盤・テレポーター・シュートでない)。</summary>
    private static bool Standing(MazeCell cell) =>
        !cell.IsSolid && cell.Feature is not (CellFeature.Teleporter or CellFeature.Chute);

    private static bool Passable(EdgeType e) => e is EdgeType.Open or EdgeType.Door or EdgeType.SecretDoor or EdgeType.Locked or EdgeType.OneWayOut;

    /// <summary>マスに入ったあと、実際に立つ場所。シュートを使わないときは、シュートのマスで止まる(その先は無い)。</summary>
    private static Node? Resolve(Dungeon dungeon, int f, GridPos p, bool includeChutes)
    {
        var cell = dungeon.Floors[f][p];
        return cell.Feature switch
        {
            CellFeature.Teleporter when cell.Target is { } t => new Node(t.Floor ?? f, t.Cell),
            CellFeature.Chute when cell.Target is { } t => includeChutes && f + 1 < dungeon.Floors.Count ? new Node(f + 1, t.Cell) : null,
            _ => new Node(f, p),
        };
    }

    private static HashSet<Node> Bfs(Node start, Dictionary<Node, List<Node>> graph)
    {
        var seen = new HashSet<Node> { start };
        var queue = new Queue<Node>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (!graph.TryGetValue(n, out var next)) continue;
            foreach (var m in next)
            {
                if (seen.Add(m)) queue.Enqueue(m);
            }
        }
        return seen;
    }
}
