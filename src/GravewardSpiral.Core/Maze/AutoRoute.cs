namespace GravewardSpiral.Core.Maze;

/// <summary>
/// 自動移動の道を選ぶ。オートマップで分かっていることだけを使い、同じ階の中で一番短い道を探す。
/// 通るのは通ったマスだけ。辺は、そのマスから見えた種類が通路・扉・一方通行の出口のときだけ通る
/// (錠の扉・見つけていない隠し扉・逆向きの一方通行・まだ見ていない辺は通らない)。
/// 地図に印のある仕掛け(テレポーター・落とし穴・シュート・回転床)は、行き先でなければ踏まない。
/// </summary>
public static class AutoRoute
{
    /// <summary>from から to への向きの並び。行けなければ null、同じマスなら空。</summary>
    public static List<Direction>? Find(MazeFloor floor, FloorMap map, GridPos from, GridPos to)
    {
        if (from == to) return [];
        if (!map.Visited.Contains(to)) return null;
        var came = new Dictionary<GridPos, (GridPos From, Direction Dir)>();
        var queue = new Queue<GridPos>([from]);
        var seen = new HashSet<GridPos> { from };
        while (queue.TryDequeue(out var p))
        {
            foreach (var d in DirectionExtensions.All)
            {
                if (floor.Neighbor(p, d) is not { } n || seen.Contains(n) || !map.Visited.Contains(n)) continue;
                if (map.Seen(p, d) is not (EdgeType.Open or EdgeType.Door or EdgeType.OneWayOut)) continue;
                if (n != to && map.Marks.ContainsKey(n)) continue;
                seen.Add(n);
                came[n] = (p, d);
                if (n == to) return Path(came, from, to);
                queue.Enqueue(n);
            }
        }
        return null;
    }

    private static List<Direction> Path(Dictionary<GridPos, (GridPos From, Direction Dir)> came, GridPos from, GridPos to)
    {
        var dirs = new List<Direction>();
        for (var p = to; p != from; p = came[p].From) dirs.Add(came[p].Dir);
        dirs.Reverse();
        return dirs;
    }
}
