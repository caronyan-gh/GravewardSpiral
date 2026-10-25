namespace GravewardSpiral.Core.Maze;

/// <summary>
/// オートマップの記録。通ったマスと、見えた(または暗闇で触れた)辺を、階ごとに持つ。
/// 辺は「パーティから見えた種類」で記録する(見つけていない隠し扉は壁のまま)。
/// </summary>
public sealed class AutoMap
{
    private readonly Dictionary<int, FloorMap> _floors = [];

    public FloorMap For(int floor)
    {
        if (!_floors.TryGetValue(floor, out var map)) _floors[floor] = map = new FloorMap();
        return map;
    }

    public IReadOnlyDictionary<int, FloorMap> Floors => _floors;

    /// <summary>
    /// 今の場所から見えるものを記録する。霧の中では何も記録しない。
    /// 暗闇の中では、今のマスの周りの辺(手で触れたもの)だけを記録する。
    /// </summary>
    public void Record(ExplorationState state)
    {
        var pos = state.Position;
        var floor = state.Floor;
        var here = floor[pos.Cell];
        if (here.Zone.HasFlag(Zone.Fog)) return;

        var map = For(pos.Floor);
        map.Visited.Add(pos.Cell);
        foreach (var d in DirectionExtensions.All) map.See(pos.Cell, d, state.Apparent(pos.Floor, pos.Cell, d));
        if (state.InDark) return;

        foreach (var cell in MazeView.Compute(state))
        {
            if (floor[cell.Cell].Zone.HasFlag(Zone.Fog) || state.IsDark(pos.Floor, cell.Cell)) continue;   // 暗闇や霧のマスは、見えても記録しない
            foreach (var (dir, edge) in cell.VisibleEdges)
                map.See(cell.Cell, dir, edge);
        }
    }
}

public sealed class FloorMap
{
    public HashSet<GridPos> Visited { get; } = [];

    /// <summary>見えた辺(マスと向き → 見えた種類)。</summary>
    public Dictionary<EdgeKey, EdgeType> Edges { get; } = [];

    /// <summary>踏んで気づいた仕掛けの印(テレポーター・落とし穴・シュート)。回転床には付けない。</summary>
    public Dictionary<GridPos, CellFeature> Marks { get; } = [];

    public void See(GridPos cell, Direction d, EdgeType apparent) => Edges[new EdgeKey(cell, d)] = apparent;

    public EdgeType? Seen(GridPos cell, Direction d) =>
        Edges.TryGetValue(new EdgeKey(cell, d), out var e) ? e : null;
}
