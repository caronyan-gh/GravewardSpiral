using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.MazeGen;

/// <summary>新規ゲームで迷宮を作るときの指定(新規ゲームの画面で入力する)。</summary>
public sealed record GenerationRequest(ulong Seed, int Floors, FloorSizeMode SizeMode)
{
    public static GenerationRequest Default(MazeGenSettings s, ulong seed) =>
        new(seed, s.DefaultFloors, FloorSizeMode.Fixed(s.DefaultWidth, s.DefaultHeight));
}

/// <summary>階の大きさ。全階同じか、階ごとに範囲の中から選ぶ。</summary>
public sealed record FloorSizeMode(int MinWidth, int MaxWidth, int MinHeight, int MaxHeight)
{
    public static FloorSizeMode Fixed(int w, int h) => new(w, w, h, h);
    public bool IsFixed => MinWidth == MaxWidth && MinHeight == MaxHeight;
}

/// <summary>
/// 迷宮の自動生成。同じ指定(シード・階層・大きさ)と同じ設定なら、いつも同じ迷宮になる。
/// 1階の作り方:
/// 1. はめ込む部屋(プレハブ)の場所を先に取る。
/// 2. 部屋を置き、全マスを全域木でつなぐ(どのマスにも行ける)。部屋の出入り口は扉にする。
/// 3. 壁を少し開けてループを作り、行き止まりを岩盤で埋めて迷路のこまかさを減らす。
/// 4. 上り階段と、そこから遠い所に下り階段を置く。
/// 5. 隠し扉(遠回りの道がある扉だけ)、一方通行、暗闇・霧・魔法禁止の区域、回転床・落とし穴・テレポーターを置く。
/// 6. 階の中で行けるか・戻れるかを調べ、通らなければシードを変えて作り直す。
/// 全階を作ったあとにシュート(下の階への落とし穴)を足し、迷宮全体をもう一度調べる。
/// </summary>
public sealed class MazeGenerator
{
    private readonly MazeGenSettings _s;

    public MazeGenerator(MazeGenSettings settings) => _s = settings;

    public sealed record Output(Dungeon Dungeon, List<string> Log);

    public Output Generate(GenerationRequest req)
    {
        int floors = Math.Clamp(req.Floors, 1, _s.MaxFloors);
        var log = new List<string>();
        var sizeRng = RngStreams.Create(req.Seed, "mazegen/sizes");
        var list = new List<MazeFloor>();
        // 迷宮で1つだけの部屋は、深さの範囲の階から1つを先に選ぶ
        var onceRng = RngStreams.Create(req.Seed, "mazegen/once");
        var onceFloor = new Dictionary<PrefabRule, int>();
        // 入れるかは、組ごとに1回だけ抽選する(組の無い部屋は1つずつ)
        var groupIn = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var rule in _s.Prefabs.Where(r => r.Once))
        {
            string key = rule.Group ?? rule.File;
            if (!groupIn.TryGetValue(key, out bool included)) groupIn[key] = included = rule.Chance >= 1 || ((Rng.IRandom)onceRng).NextDouble() < rule.Chance;
            if (!included) continue;
            var eligible = Enumerable.Range(0, floors).Where(i =>
            {
                double d = floors == 1 ? 1.0 : i / (double)(floors - 1);
                return d >= rule.MinDepth && d <= rule.MaxDepth;
            }).ToList();
            if (eligible.Count > 0) onceFloor[rule] = eligible[onceRng.Next(0, eligible.Count)];
        }
        for (int i = 0; i < floors; i++)
        {
            int w = Math.Clamp(Pick(sizeRng, req.SizeMode.MinWidth, req.SizeMode.MaxWidth), _s.MinSize, _s.MaxSize);
            int h = Math.Clamp(Pick(sizeRng, req.SizeMode.MinHeight, req.SizeMode.MaxHeight), _s.MinSize, _s.MaxSize);
            double depth = floors == 1 ? 1.0 : i / (double)(floors - 1);
            MazeFloor? floor = null;
            for (int attempt = 0; attempt < _s.MaxAttempts && floor == null; attempt++)
            {
                var rng = RngStreams.Create(req.Seed, $"mazegen/floor{i}/try{attempt}");
                var candidate = new FloorBuilder(_s, rng, i, w, h, depth, isLast: i == floors - 1,
                    onceHere: onceFloor.Where(kv => kv.Value == i).Select(kv => kv.Key).ToHashSet()).Build();
                var check = ReachabilityValidator.Validate(new Dungeon("check", [candidate]), includeChutes: false);
                // 1階だけで調べるときは、下り階段の先が無いので、町へ戻れるかだけを見る
                if (check.Ok) floor = candidate;
                else log.Add($"B{i + 1} 作り直し({attempt + 1}回目): {string.Join(" / ", check.Problems)}");
            }
            if (floor == null)
                throw new InvalidOperationException($"B{i + 1} を {_s.MaxAttempts} 回作っても検査を通りませんでした(最後: {log.LastOrDefault()})");
            list.Add(floor);
        }

        // シュート: 下の階ができてから、行き先を決める
        for (int i = 0; i + 1 < floors; i++)
        {
            double depth = floors == 1 ? 1.0 : i / (double)(floors - 1);
            if (depth < _s.Chutes.MinDepth) continue;
            var rng = RngStreams.Create(req.Seed, $"mazegen/chutes{i}");
            int count = _s.Chutes.Count.Pick(rng);
            for (int k = 0; k < count; k++)
            {
                var from = FloorBuilder.PickPlainCell(list[i], rng, corridorOnly: true);
                var to = FloorBuilder.PickPlainCell(list[i + 1], rng, corridorOnly: false);
                if (from == null || to == null) continue;
                list[i][from.Value].Feature = CellFeature.Chute;
                list[i][from.Value].Target = new WarpTarget(null, to.Value);
                var check = ReachabilityValidator.Validate(new Dungeon("check", list), includeChutes: true);
                // シュートのマスで道が切れたら、置くのをやめる。到達の検査は錠を通れるものとして見るので、
                // 鍵への道がシュートでふさがれていないかは、ボットで歩いて確かめる
                if (!check.Ok || !MazeBot.Explore(new Dungeon("check", list)).Ok)
                {
                    list[i][from.Value].Feature = CellFeature.None;
                    list[i][from.Value].Target = null;
                }
            }
        }

        // エレベーター: 何階かずつの組に縦穴を1本ずつ通す(全部の階ができてから)。同じ座標で、空いている階に止まる
        if (floors >= _s.ElevatorMinFloors && _s.ElevatorEvery >= 2)
        {
            var rng = RngStreams.Create(req.Seed, "mazegen/elevators");
            for (int g = 0; g * _s.ElevatorEvery < floors; g++)
            {
                int from = g * _s.ElevatorEvery + 1, to = Math.Min(floors, (g + 1) * _s.ElevatorEvery);
                if (to - from < 1) continue;
                if (Elevators.Best(list, from, to, rng) is { } shaft) Elevators.Apply(list, shaft);
            }
        }

        var dungeon = new Dungeon($"gen-{req.Seed}", list);
        var final = ReachabilityValidator.Validate(dungeon);
        var structural = dungeon.Validate();
        if (!final.Ok || structural.Count > 0)
            throw new InvalidOperationException("生成した迷宮が検査を通りません: " + string.Join(" / ", final.Problems.Concat(structural)));
        return new Output(dungeon, log);
    }

    private static int Pick(IRandom rng, int min, int max) => max <= min ? min : rng.Next(min, max + 1);
}

/// <summary>1階を作る。</summary>
internal sealed class FloorBuilder
{
    private readonly MazeGenSettings _s;
    private readonly IRandom _rng;
    private readonly int _index;
    private readonly double _depth;
    private readonly bool _isLast;
    private readonly MazeFloor _f;
    private readonly int[,] _room;          // 部屋の番号(0 は通路、-1 はプレハブ)
    private readonly bool[,] _locked;       // プレハブの中(作りを変えない)
    private readonly List<(GridPos Cell, Direction Dir)> _prefabEntrances = [];
    private readonly List<RoomRect> _rooms = [];

    private readonly record struct RoomRect(int X, int Y, int W, int H)
    {
        public bool Contains(GridPos p) => p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H;
    }

    private readonly HashSet<PrefabRule> _onceHere;

    public FloorBuilder(MazeGenSettings s, IRandom rng, int index, int w, int h, double depth, bool isLast, HashSet<PrefabRule>? onceHere = null)
    {
        _onceHere = onceHere ?? [];
        _s = s;
        _rng = rng;
        _index = index;
        _depth = depth;
        _isLast = isLast;
        _f = new MazeFloor($"gen_b{index + 1}", w, h)
        {
            WrapX = depth >= s.WrapMinDepth && rng.NextDouble() < s.WrapChance,
        };
        _f.WrapY = depth >= s.WrapMinDepth && rng.NextDouble() < s.WrapChance / 2;
        _room = new int[w, h];
        _locked = new bool[w, h];
    }

    public MazeFloor Build()
    {
        PlacePrefabs();
        PlaceRooms();
        ConnectAll();
        AddLoops();
        FillDeadEnds();
        PlaceStairs();
        PlaceSecretDoors();
        PlaceOneWays();
        PlaceRegions(_s.Dark, Zone.Dark);
        PlaceRegions(_s.Fog, Zone.Fog);
        PlaceRegions(_s.AntiMagic, Zone.AntiMagic);
        PlaceSpinners();
        PlacePits();
        PlaceTeleporters();
        PlaceLocks();
        foreach (var r in _rooms) _f.Rooms.Add(new MazeRoom(r.X, r.Y, r.W, r.H));
        return _f;
    }

    // ---- 錠と鍵 -----------------------------------------------------------

    /// <summary>
    /// 錠のある扉を置く。扉のうち、閉じると上り階段の側から向こうへ行けなくなるもの(道を分けるもの)を選び、
    /// 鍵は上り階段の側(錠より手前)の、遠めのマスに置く。だから鍵を取る前に錠の向こうへ行く必要は無い。
    /// </summary>
    private void PlaceLocks()
    {
        if (_depth < _s.Locks.MinDepth) return;
        int count = _s.Locks.Count.Pick(_rng);
        var doors = EdgesOf(EdgeType.Door).Where(e => !_locked[e.Cell.X, e.Cell.Y]
            && Neighbor(e.Cell, e.Dir) is { } n && !_locked[n.X, n.Y]).OrderBy(_ => _rng.NextUInt()).ToList();
        int placed = 0;
        foreach (var (p, d) in doors)
        {
            if (placed >= count) break;
            var other = Neighbor(p, d)!.Value;
            var dist = DistancesWithout(_f.Start, (p, d));
            if (dist.ContainsKey(other) || !dist.ContainsKey(p)) continue;   // 道を分けない扉、手前に無い扉は使わない
            int far = dist.Values.Max();
            var keyCells = dist.Where(kv => kv.Value >= far / 2 && _f[kv.Key].Feature == CellFeature.None && _f[kv.Key].Mark == null
                && !_locked[kv.Key.X, kv.Key.Y]).Select(kv => kv.Key).ToList();
            if (keyCells.Count == 0) continue;
            SetEdge(p, d, EdgeType.Locked, EdgeType.Locked);
            _f[keyCells[_rng.Next(0, keyCells.Count)]].Mark = "key";
            placed++;
        }
    }

    /// <summary>start からの歩数(錠と、指定の辺を通らない。一方通行は向きを守る。テレポーターは使わない)。</summary>
    private Dictionary<GridPos, int> DistancesWithout(GridPos start, (GridPos Cell, Direction Dir) blocked)
    {
        var dist = new Dictionary<GridPos, int> { [start] = 0 };
        var queue = new Queue<GridPos>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in DirectionExtensions.All)
            {
                var e = _f[p][d];
                if (!Passable(e) || e == EdgeType.Locked) continue;
                if ((p, d) == blocked) continue;
                if (Neighbor(p, d) is not { } n || (n, d.Opposite()) == blocked || _f[n].IsSolid || dist.ContainsKey(n)) continue;
                dist[n] = dist[p] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    // ---- 1. はめ込む部屋 -------------------------------------------------

    private void PlacePrefabs()
    {
        foreach (var rule in _s.Prefabs)
        {
            if (rule.Once ? !_onceHere.Contains(rule)
                : _depth < rule.MinDepth || _depth > rule.MaxDepth || _rng.NextDouble() >= rule.Chance) continue;
            var prefab = MazeTextFormat.Parse(File.ReadAllText(rule.ResolvedPath), rule.File);
            if (prefab.Width + 2 > _f.Width || prefab.Height + 2 > _f.Height) continue;
            for (int tries = 0; tries < 20; tries++)
            {
                int ox = _rng.Next(1, _f.Width - prefab.Width);
                int oy = _rng.Next(1, _f.Height - prefab.Height);
                bool free = true;
                for (int x = ox - 1; x <= ox + prefab.Width && free; x++)
                for (int y = oy - 1; y <= oy + prefab.Height && free; y++)
                    free = !_locked[x, y];
                if (!free) continue;
                foreach (var p in prefab.AllCells())
                {
                    var src = prefab[p];
                    var dst = _f[p.X + ox, p.Y + oy];
                    foreach (var d in DirectionExtensions.All) dst[d] = src[d];
                    dst.Zone = src.Zone;
                    dst.Mark = src.Mark;
                    dst.Feature = src.Feature is CellFeature.StairsUp or CellFeature.StairsDown ? CellFeature.None : src.Feature;
                    if (src.Target is { Floor: null } t) dst.Target = new WarpTarget(null, new GridPos(t.Cell.X + ox, t.Cell.Y + oy));
                    _locked[p.X + ox, p.Y + oy] = true;
                    _room[p.X + ox, p.Y + oy] = -1;
                }
                foreach (var (cell, dir) in prefab.Entrances) _prefabEntrances.Add((new GridPos(cell.X + ox, cell.Y + oy), dir));
                break;
            }
        }
    }

    // ---- 2. 部屋と全域木 ----------------------------------------------------

    private void PlaceRooms()
    {
        int area = _f.Width * _f.Height;
        int target = (int)(area * _s.RoomDensity);
        int used = 0;
        for (int tries = 0; tries < 200 && used < target; tries++)
        {
            int w = _s.RoomSize.Pick(_rng), h = _s.RoomSize.Pick(_rng);
            if (w + 2 > _f.Width || h + 2 > _f.Height) continue;
            int x = _rng.Next(1, _f.Width - w), y = _rng.Next(1, _f.Height - h);
            bool free = true;
            for (int i = x - 1; i <= x + w && free; i++)
            for (int j = y - 1; j <= y + h && free; j++)
                free = _room[i, j] == 0 && !_locked[i, j];
            if (!free) continue;
            int id = _rooms.Count + 1;
            _rooms.Add(new RoomRect(x, y, w, h));
            for (int i = x; i < x + w; i++)
            for (int j = y; j < y + h; j++)
            {
                _room[i, j] = id;
                if (i + 1 < x + w) Open(new GridPos(i, j), Direction.East, EdgeType.Open);
                if (j + 1 < y + h) Open(new GridPos(i, j), Direction.South, EdgeType.Open);
            }
            used += w * h;
        }
    }

    /// <summary>全マスをつなぐ(クラスカル法)。部屋とプレハブは、最初から1つにまとめておく。</summary>
    private void ConnectAll()
    {
        var dsu = new Dsu(_f.Width * _f.Height);
        foreach (var p in _f.AllCells())
        {
            foreach (var d in new[] { Direction.East, Direction.South })
            {
                var n = Neighbor(p, d);
                if (n == null) continue;
                bool sameRoom = _room[p.X, p.Y] != 0 && _room[p.X, p.Y] == _room[n.Value.X, n.Value.Y];
                bool insidePrefab = _locked[p.X, p.Y] && _locked[n.Value.X, n.Value.Y];
                if (sameRoom || (insidePrefab && Passable(_f[p][d]))) dsu.Union(Id(p), Id(n.Value));
            }
        }
        foreach (var r in _rooms)
        {
            for (int i = r.X; i < r.X + r.W; i++)
            for (int j = r.Y; j < r.Y + r.H; j++)
                dsu.Union(Id(new GridPos(r.X, r.Y)), Id(new GridPos(i, j)));
        }

        var edges = CandidateEdges();
        Shuffle(edges);
        foreach (var (p, d) in edges)
        {
            var n = Neighbor(p, d)!.Value;
            if (dsu.Find(Id(p)) == dsu.Find(Id(n))) continue;
            dsu.Union(Id(p), Id(n));
            Open(p, d, IsRoomBoundary(p, n) && _rng.NextDouble() < _s.DoorChance ? EdgeType.Door : EdgeType.Open);
        }
    }

    /// <summary>開けてよい辺(プレハブの中と、入口でないプレハブの周りは除く)。東と南の向きだけを数える。</summary>
    private List<(GridPos, Direction)> CandidateEdges()
    {
        var list = new List<(GridPos, Direction)>();
        var entrances = new HashSet<(GridPos, Direction)>();
        foreach (var (c, d) in _prefabEntrances)
        {
            entrances.Add((c, d));
            if (Neighbor(c, d) is { } n) entrances.Add((n, d.Opposite()));
        }
        foreach (var p in _f.AllCells())
        {
            foreach (var d in new[] { Direction.East, Direction.South })
            {
                var n = Neighbor(p, d);
                if (n == null || _f[p][d] != EdgeType.Wall) continue;
                bool touchesPrefab = _locked[p.X, p.Y] || _locked[n.Value.X, n.Value.Y];
                if (touchesPrefab && !entrances.Contains((p, d))) continue;
                list.Add((p, d));
            }
        }
        return list;
    }

    private void AddLoops()
    {
        foreach (var (p, d) in CandidateEdges())
        {
            if (_rng.NextDouble() >= _s.LoopChance) continue;
            var n = Neighbor(p, d)!.Value;
            Open(p, d, IsRoomBoundary(p, n) && _rng.NextDouble() < _s.DoorChance ? EdgeType.Door : EdgeType.Open);
        }
    }

    /// <summary>行き止まりの通路を岩盤で埋める(つながりは変わらない)。</summary>
    private void FillDeadEnds()
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            foreach (var p in _f.AllCells())
            {
                if (_room[p.X, p.Y] != 0 || _locked[p.X, p.Y] || _f[p].IsSolid) continue;
                var open = OpenDirs(p);
                if (open.Count != 1 || _rng.NextDouble() >= _s.DeadEndFill) continue;
                SetWall(p, open[0]);
                _f[p].Feature = CellFeature.Solid;
                changed = true;
            }
            if (!changed) break;
        }
    }

    // ---- 3. 階段 --------------------------------------------------------------

    private void PlaceStairs()
    {
        var up = PickPlainCell(_f, _rng, corridorOnly: false, exclude: Locked) ?? throw new InvalidOperationException("階段を置くマスがありません");
        _f[up].Feature = CellFeature.StairsUp;
        _f.Start = up;
        _f.StartFacing = OpenDirs(up).FirstOrDefault();
        if (_isLast) return;

        // 下り階段は、上り階段から遠いマス(遠い方から 1 割のうちのどれか)
        var dist = Distances(up);
        var far = dist.Where(kv => kv.Value > 0 && Plain(kv.Key) && !_locked[kv.Key.X, kv.Key.Y])
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList();
        if (far.Count == 0) throw new InvalidOperationException("下り階段を置くマスがありません");
        int top = Math.Max(1, far.Count / 10);
        _f[far[_rng.Next(0, top)].Key].Feature = CellFeature.StairsDown;
    }

    // ---- 4. 扉と一方通行 -----------------------------------------------------

    /// <summary>扉を隠し扉にする。遠回りの道がある扉だけ(隠し扉を見つけなくても進める)。</summary>
    private void PlaceSecretDoors()
    {
        foreach (var (p, d) in EdgesOf(EdgeType.Door))
        {
            if (_rng.NextDouble() >= _s.SecretDoorChance || _locked[p.X, p.Y]) continue;
            if (!StillConnectedWithout(p, d)) continue;
            SetEdge(p, d, EdgeType.SecretDoor, EdgeType.SecretDoor);
        }
    }

    /// <summary>一方通行。戻る道が別にあるときだけ(階の中で検査して、だめなら戻す)。</summary>
    private void PlaceOneWays()
    {
        if (_depth < _s.OneWay.MinDepth) return;
        int count = _s.OneWay.Count.Pick(_rng);
        var candidates = EdgesOf(EdgeType.Open).Where(e => !_locked[e.Cell.X, e.Cell.Y]).ToList();
        Shuffle(candidates);
        foreach (var (p, d) in candidates)
        {
            if (count == 0) break;
            var n = Neighbor(p, d)!.Value;
            if (!StillConnectedWithout(p, d)) continue;
            bool forward = _rng.NextDouble() < 0.5;
            SetEdge(p, d, forward ? EdgeType.OneWayOut : EdgeType.OneWayIn, forward ? EdgeType.OneWayIn : EdgeType.OneWayOut);
            if (ReachabilityValidator.Validate(new Dungeon("check", [_f]), includeChutes: false).Ok) count--;
            else SetEdge(p, d, EdgeType.Open, EdgeType.Open);
            _ = n;
        }
    }

    // ---- 5. 区域 ---------------------------------------------------------------

    /// <summary>暗闇・霧・魔法禁止の区域。決めた大きさ以上にまとまった区域だけを作る。</summary>
    private void PlaceRegions(RegionRule rule, Zone zone)
    {
        if (_depth < rule.MinDepth) return;
        int count = rule.Count.Pick(_rng);
        bool overlapOk = zone == Zone.AntiMagic || _depth >= _s.DarkFogOverlapMinDepth;
        for (int k = 0; k < count; k++)
        {
            for (int tries = 0; tries < 10; tries++)
            {
                var seed = PickPlainCell(_f, _rng, corridorOnly: false, exclude: p => _locked[p.X, p.Y] || !CanZone(p, zone, overlapOk));
                if (seed == null) return;
                int size = rule.Size.Pick(_rng);
                var region = GrowRegion(seed.Value, size, p => !_locked[p.X, p.Y] && CanZone(p, zone, overlapOk));
                if (region.Count < rule.Size.Min) continue;
                foreach (var p in region) _f[p].Zone |= zone;
                break;
            }
        }
    }

    private bool CanZone(GridPos p, Zone zone, bool overlapOk)
    {
        var c = _f[p];
        if (c.IsSolid || c.Zone.HasFlag(zone)) return false;
        if (zone == Zone.AntiMagic || overlapOk) return true;
        var other = zone == Zone.Dark ? Zone.Fog : Zone.Dark;
        return !c.Zone.HasFlag(other);
    }

    private List<GridPos> GrowRegion(GridPos start, int size, Func<GridPos, bool> ok)
    {
        var region = new List<GridPos> { start };
        var seen = new HashSet<GridPos> { start };
        var frontier = new List<GridPos> { start };
        while (region.Count < size && frontier.Count > 0)
        {
            int i = _rng.Next(0, frontier.Count);
            var p = frontier[i];
            var next = OpenDirs(p).Select(d => Neighbor(p, d)!.Value).Where(n => !seen.Contains(n) && ok(n)).ToList();
            if (next.Count == 0)
            {
                frontier.RemoveAt(i);
                continue;
            }
            var n = next[_rng.Next(0, next.Count)];
            seen.Add(n);
            region.Add(n);
            frontier.Add(n);
        }
        return region;
    }

    // ---- 6. 仕掛け ------------------------------------------------------------

    /// <summary>回転床: 曲がり角や十字路(向きが変わると迷う所)。浅い階では暗闇に置かない。</summary>
    private void PlaceSpinners()
    {
        if (_depth < _s.Spinners.MinDepth) return;
        int count = _s.Spinners.Count.Pick(_rng);
        bool darkOk = _depth >= _s.SpinnerInDarkMinDepth;
        var candidates = _f.AllCells().Where(p => Plain(p) && !_locked[p.X, p.Y] && (darkOk || !_f[p].Zone.HasFlag(Zone.Dark)) && IsJunctionOrCorner(p)).ToList();
        Shuffle(candidates);
        foreach (var p in candidates.Take(count)) _f[p].Feature = CellFeature.Spinner;
    }

    /// <summary>落とし穴: まっすぐな通路の途中(ダメージだけで、同じ階に残る)。</summary>
    private void PlacePits()
    {
        if (_depth < _s.Pits.MinDepth) return;
        int count = _s.Pits.Count.Pick(_rng);
        var candidates = _f.AllCells().Where(p => Plain(p) && !_locked[p.X, p.Y] && _room[p.X, p.Y] == 0 && IsStraight(p)).ToList();
        Shuffle(candidates);
        foreach (var p in candidates.Take(count)) _f[p].Feature = CellFeature.Pit;
    }

    /// <summary>
    /// テレポーター: 行き先と組で置く。決めた割合で、テレポーターでしか入れない閉じた部屋を作る(中に戻りのテレポーターも置く)。
    /// </summary>
    private void PlaceTeleporters()
    {
        if (_depth < _s.Teleporters.MinDepth) return;
        int count = _s.Teleporters.Count.Pick(_rng);
        if (count > 0 && _rng.NextDouble() < _s.TeleportRoomChance && MakeTeleportRoom()) count--;
        for (int k = 0; k < count; k++)
        {
            var from = PickPlainCell(_f, _rng, corridorOnly: true, exclude: Locked);
            if (from == null) return;
            var dist = Distances(from.Value);
            var far = dist.Where(kv => kv.Value > 3 && Plain(kv.Key) && kv.Key != from.Value).Select(kv => kv.Key).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
            if (far.Count == 0) continue;
            var to = far[_rng.Next(0, far.Count)];
            _f[from.Value].Feature = CellFeature.Teleporter;
            _f[from.Value].Target = new WarpTarget(null, to);
            if (!ReachabilityValidator.Validate(new Dungeon("check", [_f]), includeChutes: false).Ok)
            {
                _f[from.Value].Feature = CellFeature.None;
                _f[from.Value].Target = null;
            }
        }
    }

    private bool MakeTeleportRoom()
    {
        var rooms = _rooms.Where(r => r.W * r.H >= 4).ToList();
        Shuffle(rooms);
        foreach (var r in rooms)
        {
            var cells = Enumerable.Range(0, r.W * r.H).Select(i => new GridPos(r.X + i % r.W, r.Y + i / r.W)).ToList();
            if (cells.Any(p => _f[p].Feature != CellFeature.None || _f[p].Zone != Zone.None)) continue;
            // 部屋の周りの辺を全部壁にして閉じる
            var closed = new List<(GridPos, Direction, EdgeType)>();
            foreach (var p in cells)
            foreach (var d in DirectionExtensions.All)
            {
                var n = Neighbor(p, d);
                if (n == null || r.Contains(n.Value) || _f[p][d] == EdgeType.Wall) continue;
                closed.Add((p, d, _f[p][d]));
                SetWall(p, d);
            }
            var entry = PickPlainCell(_f, _rng, corridorOnly: true, exclude: p => r.Contains(p) || _locked[p.X, p.Y]);
            var exitTarget = PickPlainCell(_f, _rng, corridorOnly: false, exclude: p => r.Contains(p) || _locked[p.X, p.Y] || p == entry);
            if (entry == null || exitTarget == null)
            {
                Restore(closed);
                continue;
            }
            var landing = cells[0];
            var exit = cells[^1];
            _f[entry.Value].Feature = CellFeature.Teleporter;
            _f[entry.Value].Target = new WarpTarget(null, landing);
            _f[exit].Feature = CellFeature.Teleporter;
            _f[exit].Target = new WarpTarget(null, exitTarget.Value);
            if (ReachabilityValidator.Validate(new Dungeon("check", [_f]), includeChutes: false).Ok) return true;
            _f[entry.Value].Feature = CellFeature.None;
            _f[entry.Value].Target = null;
            _f[exit].Feature = CellFeature.None;
            _f[exit].Target = null;
            Restore(closed);
        }
        return false;
    }

    private void Restore(List<(GridPos Cell, Direction Dir, EdgeType Edge)> edges)
    {
        foreach (var (p, d, e) in edges)
        {
            var other = e switch { EdgeType.OneWayOut => EdgeType.OneWayIn, EdgeType.OneWayIn => EdgeType.OneWayOut, _ => e };
            SetEdge(p, d, e, other);
        }
    }

    // ---- 道具 -------------------------------------------------------------------

    private bool Locked(GridPos p) => _locked[p.X, p.Y];

    private bool Plain(GridPos p) => _f[p].Feature == CellFeature.None;

    /// <summary>仕掛けの無い、入れるマスを選ぶ。corridorOnly なら部屋の中を除く。</summary>
    internal static GridPos? PickPlainCell(MazeFloor f, IRandom rng, bool corridorOnly, Func<GridPos, bool>? exclude = null)
    {
        var list = f.AllCells().Where(p =>
        {
            var c = f[p];
            if (c.Feature != CellFeature.None || (exclude?.Invoke(p) ?? false)) return false;
            int open = DirectionExtensions.All.Count(d => c[d] != EdgeType.Wall);
            return open > 0 && (!corridorOnly || open <= 2);
        }).ToList();
        return list.Count == 0 ? null : list[rng.Next(0, list.Count)];
    }

    private GridPos? Neighbor(GridPos p, Direction d) => _f.Neighbor(p, d);

    private int Id(GridPos p) => p.Y * _f.Width + p.X;

    private bool IsRoomBoundary(GridPos a, GridPos b) => _room[a.X, a.Y] != _room[b.X, b.Y];

    private static bool Passable(EdgeType e) => e is EdgeType.Open or EdgeType.Door or EdgeType.SecretDoor or EdgeType.Locked or EdgeType.OneWayOut;

    private List<Direction> OpenDirs(GridPos p) =>
        DirectionExtensions.All.Where(d => _f[p][d] != EdgeType.Wall && Neighbor(p, d) != null).ToList();

    private bool IsJunctionOrCorner(GridPos p)
    {
        var open = OpenDirs(p);
        return open.Count >= 3 || (open.Count == 2 && open[0] != open[1].Opposite());
    }

    private bool IsStraight(GridPos p)
    {
        var open = OpenDirs(p);
        return open.Count == 2 && open[0] == open[1].Opposite();
    }

    private void Open(GridPos p, Direction d, EdgeType type) => SetEdge(p, d, type, type);

    private void SetWall(GridPos p, Direction d) => SetEdge(p, d, EdgeType.Wall, EdgeType.Wall);

    private void SetEdge(GridPos p, Direction d, EdgeType here, EdgeType there)
    {
        _f[p][d] = here;
        if (Neighbor(p, d) is { } n) _f[n][d.Opposite()] = there;
    }

    /// <summary>東と南の向きで数えた、その種類の辺。</summary>
    private List<(GridPos Cell, Direction Dir)> EdgesOf(EdgeType type)
    {
        var list = new List<(GridPos, Direction)>();
        foreach (var p in _f.AllCells())
        foreach (var d in new[] { Direction.East, Direction.South })
            if (_f[p][d] == type && Neighbor(p, d) != null) list.Add((p, d));
        return list;
    }

    /// <summary>その辺を壁にしても、辺の両側がまだつながっているか(遠回りの道があるか)。</summary>
    private bool StillConnectedWithout(GridPos p, Direction d)
    {
        var target = Neighbor(p, d)!.Value;
        var saveHere = _f[p][d];
        var saveThere = _f[target][d.Opposite()];
        SetWall(p, d);
        bool ok = Distances(p).ContainsKey(target);
        _f[p][d] = saveHere;
        _f[target][d.Opposite()] = saveThere;
        return ok;
    }

    /// <summary>通れる辺だけをたどった歩数(テレポーターは数えない)。</summary>
    private Dictionary<GridPos, int> Distances(GridPos start)
    {
        var dist = new Dictionary<GridPos, int> { [start] = 0 };
        var q = new Queue<GridPos>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in DirectionExtensions.All)
            {
                if (!Passable(_f[p][d])) continue;
                var n = Neighbor(p, d);
                if (n == null || _f[n.Value].IsSolid || dist.ContainsKey(n.Value)) continue;
                dist[n.Value] = dist[p] + 1;
                q.Enqueue(n.Value);
            }
        }
        return dist;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private sealed class Dsu
    {
        private readonly int[] _parent;

        public Dsu(int n)
        {
            _parent = new int[n];
            for (int i = 0; i < n; i++) _parent[i] = i;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }
            return x;
        }

        public void Union(int a, int b) => _parent[Find(a)] = Find(b);
    }
}
