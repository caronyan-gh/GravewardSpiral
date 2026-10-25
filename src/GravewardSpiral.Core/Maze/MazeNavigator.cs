using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Maze;

public enum MoveResult
{
    Moved,
    Turned,
    /// <summary>壁(見つけていない隠し扉、入るだけの一方通行を含む)にぶつかった。</summary>
    Bumped,
    FoundSecretDoor,
    NothingFound,
    /// <summary>階段で階を移った。</summary>
    ChangedFloor,
    /// <summary>B1 の上り階段から町へ出る。</summary>
    ExitToTown,
    NoStairs,
    /// <summary>錠のある扉にぶつかった(開ける方法は迷宮の画面が聞く)。</summary>
    Locked,
    /// <summary>エレベーターで階を移った。</summary>
    TookElevator,
    /// <summary>テレポーターで飛ばされた。</summary>
    Teleported,
    /// <summary>落とし穴に落ちた(同じ階に残る)。</summary>
    FellIntoPit,
    /// <summary>シュートで下の階へ落ちた。</summary>
    FellDownChute,
}

/// <summary>迷宮の中の移動。結果はオートマップにも記録する。</summary>
public sealed class MazeNavigator
{
    private readonly IRandom _random;

    /// <param name="random">回転床で向きを決める乱数。</param>
    public MazeNavigator(ExplorationState state, IRandom? random = null)
    {
        State = state;
        _random = random ?? new Pcg32(0, 0);
        State.AutoMap.Record(State);
    }

    public ExplorationState State { get; }

    public MoveResult TurnLeft() => Turn(State.Position.Facing.Left());
    public MoveResult TurnRight() => Turn(State.Position.Facing.Right());
    public MoveResult TurnAround() => Turn(State.Position.Facing.Opposite());

    private MoveResult Turn(Direction facing)
    {
        State.Position = State.Position with { Facing = facing };
        State.AutoMap.Record(State);
        return MoveResult.Turned;
    }

    /// <summary>前へ進む。扉はそのまま通れる(Wizardry と同じ)。</summary>
    public MoveResult Forward() => Step(State.Position.Facing);

    /// <summary>向きを変えずに後ろへ下がる。</summary>
    public MoveResult Backward() => Step(State.Position.Facing.Opposite());

    /// <summary>向きを変えずに左へ1歩(横歩き)。</summary>
    public MoveResult StrafeLeft() => Step(State.Position.Facing.Left());

    /// <summary>向きを変えずに右へ1歩(横歩き)。</summary>
    public MoveResult StrafeRight() => Step(State.Position.Facing.Right());

    private MoveResult Step(Direction d)
    {
        var pos = State.Position;
        var edge = State.Apparent(pos.Floor, pos.Cell, d);
        var next = State.Floor.Neighbor(pos.Cell, d);
        if (edge == EdgeType.Locked && next != null)
        {
            State.AutoMap.For(pos.Floor).See(pos.Cell, d, edge);
            return MoveResult.Locked;
        }
        if (edge == EdgeType.Wall || next == null)
        {
            // ぶつかった壁は、暗闇でも霧でも分かる
            if (!State.Cell.Zone.HasFlag(Zone.Fog)) State.AutoMap.For(pos.Floor).See(pos.Cell, d, edge);
            return MoveResult.Bumped;
        }
        State.Position = pos with { Cell = next.Value };
        State.Steps++;
        State.StepsSinceCombat++;
        State.Tick();
        State.AutoMap.Record(State);
        return Arrive();
    }

    /// <summary>
    /// 着いたマスの仕掛けを動かす。テレポーターとシュートの行き先では、もう一度は動かさない(行き先は仕掛けの無いマスにする)。
    /// </summary>
    private MoveResult Arrive()
    {
        var pos = State.Position;
        var cell = State.Cell;
        var map = State.AutoMap.For(pos.Floor);
        bool fog = cell.Zone.HasFlag(Zone.Fog);
        switch (cell.Feature)
        {
            case CellFeature.Spinner:
                // 回転床: 向きがでたらめに変わる。気づかせないので、地図にも印を付けない
                State.Position = pos with { Facing = (Direction)_random.Next(0, 4) };
                State.AutoMap.Record(State);
                return MoveResult.Moved;
            case CellFeature.Teleporter when cell.Target is { } t:
                if (!fog) map.Marks[pos.Cell] = CellFeature.Teleporter;
                State.Position = new PartyPosition(t.Floor ?? pos.Floor, t.Cell, pos.Facing);
                State.AutoMap.Record(State);
                return MoveResult.Teleported;
            case CellFeature.Pit:
                if (!fog) map.Marks[pos.Cell] = CellFeature.Pit;
                return MoveResult.FellIntoPit;
            case CellFeature.Chute when cell.Target is { } t:
                if (!fog) map.Marks[pos.Cell] = CellFeature.Chute;
                State.Position = new PartyPosition(pos.Floor + 1, t.Cell, pos.Facing);
                State.AutoMap.Record(State);
                return MoveResult.FellDownChute;
            default:
                return MoveResult.Moved;
        }
    }

    /// <summary>正面の壁を調べる。隠し扉があれば見つける。</summary>
    public MoveResult Search()
    {
        var pos = State.Position;
        if (State.Floor[pos.Cell][pos.Facing] == EdgeType.SecretDoor && !State.IsDiscovered(pos.Floor, pos.Cell, pos.Facing))
        {
            State.Discover(pos.Floor, pos.Cell, pos.Facing);
            State.AutoMap.Record(State);
            return MoveResult.FoundSecretDoor;
        }
        return MoveResult.NothingFound;
    }

    /// <summary>このエレベーターで行ける階(同じ組の印のエレベーターがある階。今の階を含む)。</summary>
    public List<int> ElevatorFloors()
    {
        if (State.Cell.Feature != CellFeature.Elevator || State.Cell.Mark is not { } group) return [];
        return Enumerable.Range(0, State.Dungeon.Floors.Count)
            .Where(f => State.Dungeon.Floors[f].AllCells().Any(p => State.Dungeon.Floors[f][p].Feature == CellFeature.Elevator
                && State.Dungeon.Floors[f][p].Mark == group))
            .ToList();
    }

    /// <summary>エレベーターで floor の階の、同じ組のエレベーターの上へ移る。</summary>
    public MoveResult TakeElevator(int floor)
    {
        if (!ElevatorFloors().Contains(floor) || floor == State.Position.Floor) return MoveResult.NoStairs;
        var group = State.Cell.Mark;
        var f = State.Dungeon.Floors[floor];
        var cell = f.AllCells().First(p => f[p].Feature == CellFeature.Elevator && f[p].Mark == group);
        State.Position = new PartyPosition(floor, cell, State.Position.Facing);
        State.AutoMap.Record(State);
        return MoveResult.TookElevator;
    }

    /// <summary>今のマスの階段を使う。降りた先(上った先)では、反対の階段の上に着く。</summary>
    public MoveResult UseStairs()
    {
        var pos = State.Position;
        var feature = State.Cell.Feature;
        int target;
        CellFeature arrive;
        switch (feature)
        {
            case CellFeature.StairsDown when pos.Floor + 1 < State.Dungeon.Floors.Count:
                target = pos.Floor + 1;
                arrive = CellFeature.StairsUp;
                break;
            case CellFeature.StairsUp when pos.Floor == 0:
                return MoveResult.ExitToTown;
            case CellFeature.StairsUp:
                target = pos.Floor - 1;
                arrive = CellFeature.StairsDown;
                break;
            default:
                return MoveResult.NoStairs;
        }
        var floor = State.Dungeon.Floors[target];
        var cell = floor.FindFeature(arrive) ?? floor.Start;
        State.Position = new PartyPosition(target, cell, pos.Facing);
        State.AutoMap.Record(State);
        return MoveResult.ChangedFloor;
    }
}
