namespace GravewardSpiral.Core.Maze;

/// <summary>
/// 迷宮を手で直す道具(シナリオエディタが使う)。辺は両側をそろえて変え、MazeFloor.Validate が通る形を保つ。
/// </summary>
public static class MazeEdit
{
    /// <summary>辺をクリックしたときに回す順(壁 → 通路 → 扉 → 隠し扉 → 錠のある扉 → この向きへだけ → 向こうからだけ → 壁)。</summary>
    public static readonly EdgeType[] EdgeCycle =
        [EdgeType.Wall, EdgeType.Open, EdgeType.Door, EdgeType.SecretDoor, EdgeType.Locked, EdgeType.OneWayOut, EdgeType.OneWayIn];

    /// <summary>
    /// p の d の辺を t にする(隣のマスの向かいの辺もそろえる。一方通行は OneWayOut と OneWayIn の組)。
    /// 迷宮の端(隣が無い)は壁にしかできない。どちらかが岩盤なら壁のまま。変えたら true。
    /// </summary>
    public static bool SetEdge(MazeFloor floor, GridPos p, Direction d, EdgeType t)
    {
        var n = floor.Neighbor(p, d);
        if (n == null || floor[p].IsSolid || floor[n.Value].IsSolid) t = EdgeType.Wall;
        if (floor[p][d] == t) return false;
        floor[p][d] = t;
        if (n != null)
            floor[n.Value][d.Opposite()] = t switch { EdgeType.OneWayOut => EdgeType.OneWayIn, EdgeType.OneWayIn => EdgeType.OneWayOut, _ => t };
        return true;
    }

    /// <summary>辺を EdgeCycle の次へ回す(back なら前へ)。</summary>
    public static EdgeType CycleEdge(MazeFloor floor, GridPos p, Direction d, bool back = false)
    {
        int i = Math.Max(0, Array.IndexOf(EdgeCycle, floor[p][d]));
        var next = EdgeCycle[(i + (back ? EdgeCycle.Length - 1 : 1)) % EdgeCycle.Length];
        SetEdge(floor, p, d, next);
        return floor[p][d];
    }

    /// <summary>
    /// 辺を筆の種類で塗る(パレット)。一方通行の筆は、一方通行でない辺を「p から出る向き」にし、
    /// もう一方通行の辺なら向きを逆にする(flip が false なら変えない。押したまま続けて塗るとき)。塗った後の種類を返す。
    /// </summary>
    public static EdgeType PaintEdge(MazeFloor floor, GridPos p, Direction d, EdgeType brush, bool flip = true)
    {
        var now = floor[p][d];
        var t = brush is EdgeType.OneWayOut or EdgeType.OneWayIn
            ? now switch
            {
                EdgeType.OneWayOut => flip ? EdgeType.OneWayIn : now,
                EdgeType.OneWayIn => flip ? EdgeType.OneWayOut : now,
                _ => EdgeType.OneWayOut,
            }
            : brush;
        SetEdge(floor, p, d, t);
        return floor[p][d];
    }

    /// <summary>印 mark のあるところ(階の番号とマス)。無ければ null。</summary>
    public static (int Floor, GridPos Cell)? FindMark(IReadOnlyList<MazeFloor> floors, string mark)
    {
        for (int i = 0; i < floors.Count; i++)
            foreach (var p in floors[i].AllCells())
                if (floors[i][p].Mark == mark) return (i, p);
        return null;
    }

    /// <summary>印 mark を、どの階からも消す。消した数を返す。</summary>
    public static int RemoveMark(IEnumerable<MazeFloor> floors, string mark)
    {
        int n = 0;
        foreach (var f in floors)
            foreach (var p in f.AllCells().Where(p => f[p].Mark == mark).ToList())
            {
                f[p].Mark = null;
                n++;
            }
        return n;
    }

    /// <summary>
    /// 印 mark を、階の空いたマス(仕掛けも印も無く、着く場所でない)のうち、着く場所から歩いて一番遠いところに置く
    /// (主の部屋を奥に置くため。歩いて行けるマスが無ければ、空いたマスのどれか)。置いたマスを返す。空きが無ければ null。
    /// </summary>
    public static GridPos? PlaceMarkFar(MazeFloor floor, string mark)
    {
        bool Free(GridPos p) => floor[p] is { IsSolid: false, Feature: CellFeature.None, Mark: null } && p != floor.Start;
        var dist = new Dictionary<GridPos, int> { [floor.Start] = 0 };
        var queue = new Queue<GridPos>([floor.Start]);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in DirectionExtensions.All)
                if (floor[p][d] != EdgeType.Wall && floor.Neighbor(p, d) is { } n && !floor[n].IsSolid && dist.TryAdd(n, dist[p] + 1))
                    queue.Enqueue(n);
        }
        GridPos? at = dist.Where(x => Free(x.Key)).OrderByDescending(x => x.Value).ThenBy(x => x.Key.Y).ThenBy(x => x.Key.X).Select(x => (GridPos?)x.Key).FirstOrDefault()
            ?? floor.AllCells().Where(Free).Select(p => (GridPos?)p).FirstOrDefault();
        if (at is { } cell) floor[cell].Mark = mark;
        return at;
    }

    /// <summary>
    /// マスの仕掛けを変える。岩盤にすると四方を壁にし、印・行き先も消す。テレポーターとシュートは行き先(target)を入れる。
    /// </summary>
    public static void SetFeature(MazeFloor floor, GridPos p, CellFeature feature, WarpTarget? target = null)
    {
        var c = floor[p];
        if (feature == CellFeature.Solid)
        {
            foreach (var d in DirectionExtensions.All) SetEdge(floor, p, d, EdgeType.Wall);
            c.Mark = null;
            c.Zone = Zone.None;
        }
        c.Feature = feature;
        c.Target = feature is CellFeature.Teleporter or CellFeature.Chute ? target : null;
    }

    /// <summary>四方を通れるようにする(迷宮の端は壁のまま。隣が岩盤なら壁のまま)。</summary>
    public static void OpenAll(MazeFloor floor, GridPos p)
    {
        foreach (var d in DirectionExtensions.All) SetEdge(floor, p, d, EdgeType.Open);
    }

    /// <summary>
    /// 何も無い階を作る(open なら外周だけ壁の広間、そうでなければ全部壁)。着く場所は真ん中、北向き。
    /// </summary>
    public static MazeFloor Blank(string id, int width, int height, bool open = true)
    {
        var f = new MazeFloor(id, width, height);
        if (open)
        {
            foreach (var p in f.AllCells())
            {
                if (p.X + 1 < width) SetEdge(f, p, Direction.East, EdgeType.Open);
                if (p.Y + 1 < height) SetEdge(f, p, Direction.South, EdgeType.Open);
            }
        }
        f.Start = new GridPos(width / 2, height / 2);
        return f;
    }

    /// <summary>
    /// 階の大きさを変えた写し(左上をそろえる)。広げたところは壁に囲まれたマス、はみ出したマスは消える(そこの仕掛け・印も)。
    /// 新しい外周は壁にする(端がつながる向きは、そのまま)。着く場所は中に寄せ、部屋ははみ出したぶんを切る。
    /// ほかの階からのテレポーターやシュートの行き先が外れたら、検査で見つかる。
    /// </summary>
    public static MazeFloor Resize(MazeFloor floor, int width, int height)
    {
        var f = new MazeFloor(floor.Id, width, height)
        {
            NameJa = floor.NameJa, NameEn = floor.NameEn, WrapX = floor.WrapX, WrapY = floor.WrapY,
            Start = new GridPos(Math.Clamp(floor.Start.X, 0, width - 1), Math.Clamp(floor.Start.Y, 0, height - 1)), StartFacing = floor.StartFacing,
        };
        foreach (var p in f.AllCells())
        {
            if (!floor.InBounds(p.X, p.Y)) continue;
            var from = floor[p];
            var to = f[p];
            foreach (var d in DirectionExtensions.All) to[d] = from[d];
            (to.Feature, to.Zone, to.Mark, to.Target) = (from.Feature, from.Zone, from.Mark, from.Target);
        }
        // 外周と、残ったマスと新しいマスのあいだは、両側をそろえて直す(新しいマスは壁で囲まれている)
        foreach (var p in f.AllCells())
            foreach (var d in DirectionExtensions.All)
            {
                var n = f.Neighbor(p, d);
                bool fresh = !floor.InBounds(p.X, p.Y) || n is { } q && !floor.InBounds(q.X, q.Y);
                // 端がつながる向きで大きさが変わったら、端をまたぐ辺は元の組ではないので壁にする
                bool wrapped = n is { } w && (Math.Abs(w.X - p.X) > 1 && width != floor.Width || Math.Abs(w.Y - p.Y) > 1 && height != floor.Height);
                if (n == null || fresh || wrapped || f[p].IsSolid || f[n.Value].IsSolid) f[p][d] = EdgeType.Wall;
            }
        foreach (var r in floor.Rooms)
        {
            int w = Math.Min(r.W, width - r.X), h = Math.Min(r.H, height - r.Y);
            if (r.X < width && r.Y < height && w > 0 && h > 0) f.Rooms.Add(r with { W = w, H = h });
        }
        foreach (var e in floor.Entrances.Where(e => f.InBounds(e.Cell.X, e.Cell.Y))) f.Entrances.Add(e);
        return f;
    }

    /// <summary>階をテキストに書いて読み直した写し(id を変えるときや、作り直す前に取っておくとき)。</summary>
    public static MazeFloor Copy(MazeFloor floor, string? id = null)
    {
        string text = MazeTextFormat.Write(floor);
        if (id != null) text = "floor " + id + text[text.IndexOf('\n')..];
        return MazeTextFormat.Parse(text, id ?? floor.Id);
    }
}
