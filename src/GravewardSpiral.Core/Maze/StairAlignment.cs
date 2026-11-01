namespace GravewardSpiral.Core.Maze;

/// <summary>
/// 階段とシュートの上下の座標がそろっているか(迷宮の設定 align_stairs。Wizardry のように、下りた真下に出る)。
/// そろえる迷宮では、上の階の下り階段と下の階の上り階段が同じ座標で、シュートは真下の同じ座標へ落ちる。
/// エレベーターは縦穴なので、この設定と関係なくいつも同じ座標。
/// </summary>
public static class StairAlignment
{
    /// <summary>ずれている所。Floor は上の階(0 が B1)、Up は上の階の下り階段かシュートの座標、Down は下の階の上り階段か落ちる先。</summary>
    public sealed record Gap(int Floor, GridPos Up, GridPos Down, bool Chute);

    /// <summary>ずれている所(無ければ空)。</summary>
    public static IEnumerable<Gap> Gaps(IReadOnlyList<MazeFloor> floors)
    {
        for (int i = 0; i + 1 < floors.Count; i++)
        {
            var down = floors[i].FindFeature(CellFeature.StairsDown);
            var up = floors[i + 1].FindFeature(CellFeature.StairsUp);
            if (down is { } d && up is { } u && d != u) yield return new Gap(i, d, u, false);
            foreach (var p in floors[i].AllCells().Where(p => floors[i][p].Feature == CellFeature.Chute))
                if (floors[i][p].Target is { } t && t.Cell != p) yield return new Gap(i, p, t.Cell, true);
        }
    }

    /// <summary>
    /// 上の階の階段を動かしたとき、相手の階の階段も同じ座標へ動かす(エディタ)。floor の cell に feature(上り・下り階段)を置いたあとで呼ぶ。
    /// 相手の階の前の階段は消し、動いた先のマスは岩盤なら空ける。相手の階が無い(B1 の上り・最下層の下り)か、座標が相手の階の外なら何もしない。
    /// </summary>
    /// <returns>動かした相手の階(0 から)。動かさなければ null。</returns>
    public static int? MovePartner(IReadOnlyList<MazeFloor> floors, int floor, GridPos cell, CellFeature feature)
    {
        int other = feature switch
        {
            CellFeature.StairsDown => floor + 1,
            CellFeature.StairsUp => floor - 1,
            _ => -1,
        };
        if (other < 0 || other >= floors.Count) return null;
        var f = floors[other];
        if (!f.InBounds(cell.X, cell.Y)) return null;
        var partner = feature == CellFeature.StairsDown ? CellFeature.StairsUp : CellFeature.StairsDown;
        foreach (var p in f.AllCells().Where(p => f[p].Feature == partner && p != cell).ToList())
            MazeEdit.SetFeature(f, p, CellFeature.None);
        if (f[cell].Feature != partner) MazeEdit.SetFeature(f, cell, partner);
        return other;
    }
}
