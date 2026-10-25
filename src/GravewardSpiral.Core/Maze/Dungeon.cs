namespace GravewardSpiral.Core.Maze;

/// <summary>迷宮全体(上から順の階)。B1 が Floors[0]。</summary>
public sealed class Dungeon
{
    public Dungeon(string id, IReadOnlyList<MazeFloor> floors)
    {
        if (floors.Count == 0) throw new ArgumentException("階が1つもありません", nameof(floors));
        Id = id;
        Floors = floors;
    }

    public string Id { get; }
    public IReadOnlyList<MazeFloor> Floors { get; }

    public List<string> Validate()
    {
        var problems = new List<string>();
        for (int i = 0; i < Floors.Count; i++)
        {
            var f = Floors[i];
            problems.AddRange(f.Validate());
            if (i > 0 && f.FindFeature(CellFeature.StairsUp) == null)
                problems.Add($"{f.Id} (B{i + 1}) に上り階段がありません");
            if (i < Floors.Count - 1 && f.FindFeature(CellFeature.StairsDown) == null)
                problems.Add($"{f.Id} (B{i + 1}) に下り階段がありません");
            foreach (var p in f.AllCells())
            {
                var c = f[p];
                if (c.Target is not { } t) continue;
                int target = c.Feature == CellFeature.Chute ? i + 1 : t.Floor ?? i;
                if (target < 0 || target >= Floors.Count)
                {
                    problems.Add($"{f.Id} ({p.X},{p.Y}) の行き先の階 B{target + 1} がありません");
                    continue;
                }
                var tf = Floors[target];
                if (!tf.InBounds(t.Cell.X, t.Cell.Y) || tf[t.Cell].IsSolid)
                    problems.Add($"{f.Id} ({p.X},{p.Y}) の行き先 B{target + 1} ({t.Cell.X},{t.Cell.Y}) に入れません");
            }
        }
        return problems;
    }
}

/// <summary>パーティのいる場所と向き。Floor は 0 が B1。</summary>
public readonly record struct PartyPosition(int Floor, GridPos Cell, Direction Facing);

/// <summary>
/// 迷宮の中の、遊んで変わる状態(いる場所、見つけた隠し扉、オートマップ、歩いた数)。セーブに入れる。
/// </summary>
public sealed class ExplorationState
{
    private readonly Dictionary<int, HashSet<EdgeKey>> _discovered = [];

    public ExplorationState(Dungeon dungeon, PartyPosition position)
    {
        Dungeon = dungeon;
        Position = position;
    }

    public Dungeon Dungeon { get; }
    public PartyPosition Position { get; set; }
    public AutoMap AutoMap { get; } = new();

    /// <summary>歩いた数(迷宮の中の時間の元)。回転は数えない。</summary>
    public long Steps { get; set; }

    /// <summary>前の戦闘から歩いた数(続けて遭遇しないための間)。</summary>
    public long StepsSinceCombat { get; set; }

    /// <summary>明かりの残り(分 = 歩数)。あるあいだは、暗闇でも見える(ライトなどの呪文)。</summary>
    public int LightMinutes { get; set; }

    /// <summary>落下を防ぐ残り(分)。あるあいだは、落とし穴でダメージを受けない(フェザー・フォール)。</summary>
    public int FeatherMinutes { get; set; }

    /// <summary>そのマスが、今のパーティにとって暗闇か(明かりがあれば暗くない)。</summary>
    public bool IsDark(int floor, GridPos cell) => LightMinutes <= 0 && Dungeon.Floors[floor][cell].Zone.HasFlag(Zone.Dark);

    public bool InDark => IsDark(Position.Floor, Position.Cell);

    /// <summary>1分(1歩)たった。明かりなどの残りを減らす。</summary>
    public void Tick()
    {
        if (LightMinutes > 0) LightMinutes--;
        if (FeatherMinutes > 0) FeatherMinutes--;
    }

    public MazeFloor Floor => Dungeon.Floors[Position.Floor];

    /// <summary>
    /// 敵のいる部屋(Floor.Rooms の番号)。階に入るたびに部屋ごとに抽選し直す(RoomsFloor がその階)。
    /// 倒すと空になる。逃げたら、いたまま。
    /// </summary>
    public HashSet<int> OccupiedRooms { get; } = [];
    public int RoomsFloor { get; set; } = -1;

    /// <summary>今の階の部屋に、敵がいるかを抽選する(今立っている部屋には置かない)。</summary>
    public void RollRooms(Rng.IRandom rng, double chance)
    {
        OccupiedRooms.Clear();
        RoomsFloor = Position.Floor;
        var f = Floor;
        for (int i = 0; i < f.Rooms.Count; i++)
            if (!f.Rooms[i].Contains(Position.Cell) && rng.NextDouble() < chance) OccupiedRooms.Add(i);
    }

    /// <summary>from から今のマスへ来て、敵のいる部屋に踏み込んだか(部屋の中を歩いても、もう一度は起きない)。</summary>
    public int? EnteredOccupiedRoom(GridPos from)
    {
        if (RoomsFloor != Position.Floor || Floor.RoomAt(Position.Cell) is not int room || !OccupiedRooms.Contains(room)) return null;
        return Floor.RoomAt(from) == room ? null : room;
    }
    public MazeCell Cell => Floor[Position.Cell];

    public bool IsDiscovered(int floor, GridPos cell, Direction d) =>
        _discovered.TryGetValue(floor, out var set) && set.Contains(new EdgeKey(cell, d));

    /// <summary>隠し扉を見つけた(両側から見えるようにする)。</summary>
    public void Discover(int floor, GridPos cell, Direction d)
    {
        if (!_discovered.TryGetValue(floor, out var set)) _discovered[floor] = set = [];
        set.Add(new EdgeKey(cell, d));
        var n = Dungeon.Floors[floor].Neighbor(cell, d);
        if (n != null) set.Add(new EdgeKey(n.Value, d.Opposite()));
    }

    private readonly Dictionary<int, HashSet<EdgeKey>> _unlocked = [];

    /// <summary>済んだマスの印(拾った鍵、倒したボス)。</summary>
    public HashSet<(int Floor, GridPos Cell)> UsedMarks { get; } = [];

    /// <summary>
    /// 書き留めたメモ(何度でも起こるイベントの文を、その階のメモに残したもの)。書き留めた場所のイベントは、もう起こらない。
    /// </summary>
    public List<MazeNote> Notes { get; } = [];

    /// <summary>この迷宮のこのマスで、このイベントを書き留めたか(ほかのマスや、ほかの迷宮の同じイベントには効かない)。</summary>
    public bool IsNoted(int floor, GridPos cell, string eventId) =>
        Notes.Any(n => n.Floor == floor && n.Cell == cell && n.EventId == eventId);

    /// <summary>拾った鍵(階の番号。その階の錠のある扉を全部開けられる)。</summary>
    public HashSet<int> Keys { get; } = [];

    public bool IsUnlocked(int floor, GridPos cell, Direction d) =>
        _unlocked.TryGetValue(floor, out var set) && set.Contains(new EdgeKey(cell, d));

    /// <summary>錠を開けた(両側から通れるようにする)。</summary>
    public void Unlock(int floor, GridPos cell, Direction d)
    {
        if (!_unlocked.TryGetValue(floor, out var set)) _unlocked[floor] = set = [];
        set.Add(new EdgeKey(cell, d));
        var n = Dungeon.Floors[floor].Neighbor(cell, d);
        if (n != null) set.Add(new EdgeKey(n.Value, d.Opposite()));
    }

    public IEnumerable<(int Floor, GridPos Cell, Direction Dir)> UnlockedEdges() =>
        _unlocked.SelectMany(kv => kv.Value.Select(e => (kv.Key, e.Cell, e.Dir)));

    /// <summary>パーティから見た辺の種類。見つけていない隠し扉は壁に、見つけた隠し扉は扉に見える。開けた錠は扉に見える。</summary>
    public EdgeType Apparent(int floor, GridPos cell, Direction d)
    {
        var edge = Dungeon.Floors[floor][cell][d];
        return edge switch
        {
            EdgeType.SecretDoor => IsDiscovered(floor, cell, d) ? EdgeType.Door : EdgeType.Wall,
            EdgeType.OneWayIn => EdgeType.Wall,
            EdgeType.Locked => IsUnlocked(floor, cell, d) ? EdgeType.Door : EdgeType.Locked,
            _ => edge,
        };
    }

    public IEnumerable<(int Floor, GridPos Cell, Direction Dir)> DiscoveredEdges() =>
        _discovered.SelectMany(kv => kv.Value.Select(e => (kv.Key, e.Cell, e.Dir)));
}

public readonly record struct EdgeKey(GridPos Cell, Direction Dir);

/// <summary>書き留めたメモ1つ(どの階のどのマスで、どのイベントの、どの文か。文は言語ごとに持つ)。</summary>
public sealed class MazeNote
{
    public int Floor { get; set; }
    public GridPos Cell { get; set; }
    public string EventId { get; set; } = "";
    public List<Content.LocalizedText> Lines { get; set; } = [];
}
