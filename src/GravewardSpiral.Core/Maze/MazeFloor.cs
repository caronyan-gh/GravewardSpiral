namespace GravewardSpiral.Core.Maze;

/// <summary>
/// 辺(マスの北東南西の境目)の種類。辺は両側のマスが1つずつ持ち、両側の組み合わせが合っていなければならない。
/// 一方通行は「出るだけ」(OneWayOut。こちらからは通れる)と「入るだけ」(OneWayIn。こちらからは壁に見えて通れない)の組で表す。
/// </summary>
/// <summary>辺の種類。Locked は錠のある扉(その階の鍵、盗賊道具、ノックの呪文、蹴り開けるで開く。開けたら扉と同じ)。</summary>
public enum EdgeType { Open, Wall, Door, SecretDoor, OneWayOut, OneWayIn, Locked }

[Flags]
public enum Zone
{
    None = 0,
    /// <summary>暗闇。前が見えない。オートマップは、手で触れた周りの壁だけ記録する。</summary>
    Dark = 1,
    /// <summary>霧。前は見えるが、オートマップに記録されず、地図に自分の位置も出ない。</summary>
    Fog = 2,
    /// <summary>魔法禁止区域。呪文が唱えられない。</summary>
    AntiMagic = 4,
}

public enum CellFeature
{
    None,
    StairsUp,
    StairsDown,
    /// <summary>回転床。乗ると向きがでたらめに変わる(気づかせない)。</summary>
    Spinner,
    /// <summary>テレポーター。乗ると Target へ飛ぶ。</summary>
    Teleporter,
    /// <summary>落とし穴。ダメージを受けるが、同じ階に残る。</summary>
    Pit,
    /// <summary>シュート。下の階の Target へ落ちる。</summary>
    Chute,
    /// <summary>岩盤。入れないマス(四方が壁)。</summary>
    Solid,
    /// <summary>エレベーター。同じ組(印の elevator:組)のエレベーターがある階へ移れる。</summary>
    Elevator,
}

/// <summary>テレポーターとシュートの行き先。Floor が null なら同じ階(シュートは常に1つ下の階)。</summary>
public readonly record struct WarpTarget(int? Floor, GridPos Cell);

public sealed class MazeCell
{
    private readonly EdgeType[] _edges = [EdgeType.Wall, EdgeType.Wall, EdgeType.Wall, EdgeType.Wall];

    public EdgeType this[Direction d]
    {
        get => _edges[(int)d];
        set => _edges[(int)d] = value;
    }

    public Zone Zone { get; set; }
    public CellFeature Feature { get; set; }

    /// <summary>テレポーターとシュートの行き先。</summary>
    public WarpTarget? Target { get; set; }

    /// <summary>
    /// マスの印。key(その階の鍵が落ちている)/ boss:id(ボスがいる)/ elevator:組 / message:キー(文言のキー)など。
    /// 踏んだときに迷宮の画面が処理する。一度で済むもの(鍵、ボス)は ExplorationState が「済んだ」を覚える。
    /// </summary>
    public string? Mark { get; set; }

    public bool IsSolid => Feature == CellFeature.Solid;
}

/// <summary>迷宮の1階ぶん。形は変わらない(隠し扉を見つけたかなどは ExplorationState が持つ)。</summary>
public sealed class MazeFloor
{
    private readonly MazeCell[,] _cells;

    public MazeFloor(string id, int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "階の大きさは 1x1 以上です");
        Id = id;
        Width = width;
        Height = height;
        _cells = new MazeCell[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            _cells[x, y] = new MazeCell();
    }

    public string Id { get; }
    public string? NameJa { get; set; }
    public string? NameEn { get; set; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>東西の端がつながっている(東の端から出ると西の端に出る)。</summary>
    public bool WrapX { get; set; }

    /// <summary>南北の端がつながっている。</summary>
    public bool WrapY { get; set; }

    /// <summary>はめ込む部屋(プレハブ)の入口。生成するとき、この辺を外へ開ける。ふつうの階では使わない。</summary>
    public List<(GridPos Cell, Direction Dir)> Entrances { get; } = [];

    public GridPos Start { get; set; }
    public Direction StartFacing { get; set; } = Direction.North;

    /// <summary>部屋(生成で置いた四角)。入ると、そこにいる敵と戦うことがある(Wizardry の玄室)。</summary>
    public List<MazeRoom> Rooms { get; } = [];

    /// <summary>そのマスがどの部屋か(Rooms の番号)。部屋でなければ null。</summary>
    public int? RoomAt(GridPos p)
    {
        for (int i = 0; i < Rooms.Count; i++)
            if (Rooms[i].Contains(p)) return i;
        return null;
    }

    public MazeCell this[int x, int y] => _cells[x, y];
    public MazeCell this[GridPos p] => _cells[p.X, p.Y];

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>隣のマス。端のループを考える。迷宮の外なら null。</summary>
    public GridPos? Neighbor(GridPos p, Direction d)
    {
        int x = p.X + d.Dx(), y = p.Y + d.Dy();
        if (WrapX) x = (x + Width) % Width;
        if (WrapY) y = (y + Height) % Height;
        return InBounds(x, y) ? new GridPos(x, y) : null;
    }

    public IEnumerable<GridPos> AllCells()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            yield return new GridPos(x, y);
    }

    public GridPos? FindFeature(CellFeature feature) =>
        AllCells().Cast<GridPos?>().FirstOrDefault(p => this[p!.Value].Feature == feature);

    /// <summary>辺の辻褄を調べる。問題が無ければ空のリスト。</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var p in AllCells())
        {
            foreach (var d in DirectionExtensions.All)
            {
                var edge = this[p][d];
                var n = Neighbor(p, d);
                if (n == null)
                {
                    if (edge != EdgeType.Wall)
                        problems.Add($"{Id} ({p.X},{p.Y}) の{Name(d)}は迷宮の端なので壁にしてください");
                    continue;
                }
                var other = this[n.Value][d.Opposite()];
                if (!Matches(edge, other))
                    problems.Add($"{Id} ({p.X},{p.Y}) の{Name(d)} ({edge}) と ({n.Value.X},{n.Value.Y}) の{Name(d.Opposite())} ({other}) が合いません");
            }
        }
        foreach (var p in AllCells())
        {
            var c = this[p];
            if (c.IsSolid && DirectionExtensions.All.Any(d => c[d] != EdgeType.Wall))
                problems.Add($"{Id} ({p.X},{p.Y}) は岩盤なので、四方を壁にしてください");
            if (c.Feature is CellFeature.Teleporter or CellFeature.Chute && c.Target == null)
                problems.Add($"{Id} ({p.X},{p.Y}) の{(c.Feature == CellFeature.Chute ? "シュート" : "テレポーター")}に行き先がありません");
            if (c.Feature == CellFeature.Teleporter && c.Target is { Floor: null } t && (!InBounds(t.Cell.X, t.Cell.Y) || this[t.Cell].IsSolid))
                problems.Add($"{Id} ({p.X},{p.Y}) のテレポーターの行き先 ({t.Cell.X},{t.Cell.Y}) に入れません");
        }
        if (!InBounds(Start.X, Start.Y)) problems.Add($"{Id} の着く場所 ({Start.X},{Start.Y}) が階の外です");
        else if (this[Start].IsSolid) problems.Add($"{Id} の着く場所 ({Start.X},{Start.Y}) が岩盤です");
        return problems;
    }

    private static bool Matches(EdgeType a, EdgeType b) => (a, b) switch
    {
        (EdgeType.OneWayOut, EdgeType.OneWayIn) or (EdgeType.OneWayIn, EdgeType.OneWayOut) => true,
        (EdgeType.OneWayOut, _) or (EdgeType.OneWayIn, _) => false,
        _ => a == b,
    };

    private static string Name(Direction d) => d switch
    {
        Direction.North => "北",
        Direction.East => "東",
        Direction.South => "南",
        _ => "西",
    };
}

public readonly record struct GridPos(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>部屋の四角(左上の x, y と、幅と高さ)。</summary>
public sealed record MazeRoom(int X, int Y, int W, int H)
{
    public bool Contains(GridPos p) => p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H;
}
