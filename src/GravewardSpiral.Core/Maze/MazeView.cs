namespace GravewardSpiral.Core.Maze;

/// <summary>
/// 3D 表示の視界。今のマスから前へ奥行き4マス(今のマスを含む)、左右1列ずつを見る。
/// 線画の描画とオートマップの記録が、同じ視界を使う。
/// </summary>
public static class MazeView
{
    public const int Depth = 4;

    /// <summary>見えるマス。Depth は 0 が今のマス、Lateral は -1 が左・0 が正面・1 が右。</summary>
    public sealed record ViewCell(int Depth, int Lateral, GridPos Cell, IReadOnlyList<(Direction Dir, EdgeType Edge)> VisibleEdges);

    /// <summary>向こうが見える辺か。扉は見えない。一方通行は、通れる側からは扉に、反対側からは壁に見えるので、どちらも見えない。</summary>
    public static bool SeeThrough(EdgeType apparent) => apparent is EdgeType.Open;

    /// <summary>扉に見える辺か(ふつうの扉と、一方通行の通れる側)。</summary>
    public static bool LooksLikeDoor(EdgeType apparent) => apparent is EdgeType.Door or EdgeType.OneWayOut or EdgeType.Locked;

    public static List<ViewCell> Compute(ExplorationState state) =>
        Compute(state, state.Position);

    public static List<ViewCell> Compute(ExplorationState state, PartyPosition pos)
    {
        var floor = state.Dungeon.Floors[pos.Floor];
        var facing = pos.Facing;
        var result = new List<ViewCell>();

        EdgeType Edge(GridPos c, Direction d) => state.Apparent(pos.Floor, c, d);

        GridPos? center = pos.Cell;
        GridPos? left = null, right = null;
        bool leftVisible = false, rightVisible = false;

        for (int depth = 0; depth < Depth && center != null; depth++)
        {
            var c = center.Value;
            result.Add(new ViewCell(depth, 0, c,
            [
                (facing, Edge(c, facing)),
                (facing.Left(), Edge(c, facing.Left())),
                (facing.Right(), Edge(c, facing.Right())),
            ]));

            // 横のマス: 正面のマスから横へ見えるか、一つ手前の横のマスから前へ見えるか
            var newLeft = floor.Neighbor(c, facing.Left());
            leftVisible = newLeft != null && (SeeThrough(Edge(c, facing.Left()))
                                              || (leftVisible && left != null && SeeThrough(Edge(left.Value, facing))));
            var newRight = floor.Neighbor(c, facing.Right());
            rightVisible = newRight != null && (SeeThrough(Edge(c, facing.Right()))
                                                || (rightVisible && right != null && SeeThrough(Edge(right.Value, facing))));
            left = newLeft;
            right = newRight;
            if (leftVisible)
                result.Add(new ViewCell(depth, -1, left!.Value, [(facing, Edge(left.Value, facing)), (facing.Left(), Edge(left.Value, facing.Left()))]));
            if (rightVisible)
                result.Add(new ViewCell(depth, 1, right!.Value, [(facing, Edge(right.Value, facing)), (facing.Right(), Edge(right.Value, facing.Right()))]));

            center = SeeThrough(Edge(c, facing)) ? floor.Neighbor(c, facing) : null;
        }
        return result;
    }
}
