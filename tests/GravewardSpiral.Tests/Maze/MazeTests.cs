using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Tests.Maze;

public class MazeTests
{
    // 3x2: (0,0)-(1,0) は通路、(1,0)-(2,0) は扉、(0,0)-(0,1) は隠し扉、(1,1)-(2,1) は東へだけ、(2,0)-(2,1) は通路
    private const string Small = """
        floor small
        size 3 2
        wrap none
        start 0 0 E
        walls
        +-+-+-+
        |u  Dd|
        +S+-+ +
        |   > |
        +-+-+-+
        zones
        ..k
        .f.
        """;

    private static ExplorationState State(string text, out MazeNavigator nav)
    {
        var floor = MazeTextFormat.Parse(text.Replace("\n        ", "\n").TrimStart());
        var dungeon = new Dungeon("t", [floor]);
        var state = new ExplorationState(dungeon, new PartyPosition(0, floor.Start, floor.StartFacing));
        nav = new MazeNavigator(state);
        return state;
    }

    private static MazeFloor Parse(string text) => MazeTextFormat.Parse(text.Replace("\n        ", "\n").TrimStart());

    [Fact]
    public void StrafingMovesSidewaysWithoutTurning()
    {
        var state = State(Small, out var nav);
        state.Position = new PartyPosition(0, new GridPos(0, 1), Direction.North);
        Assert.Equal(MoveResult.Moved, nav.StrafeRight());
        Assert.Equal((new GridPos(1, 1), Direction.North), (state.Position.Cell, state.Position.Facing));
        Assert.Equal(MoveResult.Moved, nav.StrafeLeft());
        Assert.Equal(new GridPos(0, 1), state.Position.Cell);
        Assert.Equal(MoveResult.Bumped, nav.StrafeLeft());   // 西は迷宮の外
    }

    [Fact]
    public void RoomsAreReadWrittenAndRolledPerFloorVisit()
    {
        var f = Parse(Small + "\nroom 1 0 2 2\n");
        var room = Assert.Single(f.Rooms);
        Assert.Equal(new MazeRoom(1, 0, 2, 2), room);
        Assert.Equal(f.Rooms, Parse(MazeTextFormat.Write(f)).Rooms);   // 書き出して読み直しても同じ
        Assert.ThrowsAny<FormatException>(() => Parse(Small + "\nroom 2 0 2 2\n"));   // はみ出し

        var state = new ExplorationState(new Dungeon("t", [f]), new PartyPosition(0, new GridPos(0, 1), Direction.East));
        state.RollRooms(new Core.Rng.Pcg32(1, 1), chance: 1);
        Assert.Contains(0, state.OccupiedRooms);
        state.Position = new PartyPosition(0, new GridPos(1, 1), Direction.East);
        Assert.Equal(0, state.EnteredOccupiedRoom(new GridPos(0, 1)));   // 外から入ると戦う
        Assert.Null(state.EnteredOccupiedRoom(new GridPos(1, 0)));        // 部屋の中を歩いても、もう一度は起きない
        state.RollRooms(new Core.Rng.Pcg32(1, 1), chance: 1);
        Assert.Empty(state.OccupiedRooms);   // 今立っている部屋には置かない
        state.RollRooms(new Core.Rng.Pcg32(1, 1), chance: 0);
        state.Position = new PartyPosition(0, new GridPos(0, 1), Direction.East);
        Assert.Null(state.EnteredOccupiedRoom(new GridPos(1, 1)));
    }

    [Fact]
    public void ParsesEdgesFeaturesAndZones()
    {
        var f = Parse(Small);
        Assert.Equal((3, 2), (f.Width, f.Height));
        Assert.Equal(new GridPos(0, 0), f.Start);
        Assert.Equal(Direction.East, f.StartFacing);
        Assert.Equal(EdgeType.Open, f[0, 0][Direction.East]);
        Assert.Equal(EdgeType.Door, f[1, 0][Direction.East]);
        Assert.Equal(EdgeType.Door, f[2, 0][Direction.West]);
        Assert.Equal(EdgeType.SecretDoor, f[0, 0][Direction.South]);
        Assert.Equal(EdgeType.OneWayOut, f[1, 1][Direction.East]);
        Assert.Equal(EdgeType.OneWayIn, f[2, 1][Direction.West]);
        Assert.Equal(EdgeType.Wall, f[0, 0][Direction.North]);
        Assert.Equal(CellFeature.StairsUp, f[0, 0].Feature);
        Assert.Equal(CellFeature.StairsDown, f[2, 0].Feature);
        Assert.Equal(Zone.Dark, f[2, 0].Zone);
        Assert.Equal(Zone.Fog, f[1, 1].Zone);
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void WriteThenParseRoundTrips()
    {
        var f = Parse(Small);
        var again = MazeTextFormat.Parse(MazeTextFormat.Write(f));
        foreach (var p in f.AllCells())
        {
            foreach (var d in DirectionExtensions.All) Assert.Equal(f[p][d], again[p][d]);
            Assert.Equal(f[p].Zone, again[p].Zone);
            Assert.Equal(f[p].Feature, again[p].Feature);
        }
    }

    [Fact]
    public void DevtestMapsRoundTripAndValidate()
    {
        var lib = ContentLibrary.Load(TestPaths.Content);
        var dungeon = DungeonLoader.Load(lib, "devtest");
        Assert.Empty(dungeon.Validate());
        foreach (var f in dungeon.Floors)
            Assert.Equal(MazeTextFormat.Write(f), MazeTextFormat.Write(MazeTextFormat.Parse(MazeTextFormat.Write(f))));
    }

    [Theory]
    [InlineData("+-+\n| |\n+ +", "端")]          // 南の端が開いている
    [InlineData("+-+\n|x|\n+-+", "文字")]
    public void RejectsBadMaps(string walls, string expected)
    {
        string text = $"floor bad\nsize 1 1\nwrap none\nstart 0 0 N\nwalls\n{walls}\n";
        var problems = new List<string>();
        try
        {
            problems.AddRange(MazeTextFormat.Parse(text).Validate());
        }
        catch (FormatException ex)
        {
            problems.Add(ex.Message);
        }
        Assert.Contains(problems, p => p.Contains(expected));
    }

    [Fact]
    public void MismatchedEdgesAreReported()
    {
        var f = Parse(Small);
        f[0, 0][Direction.East] = EdgeType.Wall;   // 片側だけ壁にする
        Assert.Contains(f.Validate(), p => p.Contains("合いません"));
    }

    [Fact]
    public void MovesThroughOpenAndDoorsAndBumpsWalls()
    {
        var s = State(Small, out var nav);
        Assert.Equal(MoveResult.Moved, nav.Forward());       // (0,0) -> (1,0)
        Assert.Equal(MoveResult.Moved, nav.Forward());       // 扉を通って (2,0)
        Assert.Equal(new GridPos(2, 0), s.Position.Cell);
        Assert.Equal(MoveResult.Bumped, nav.Forward());      // 東の端
        Assert.Equal(2, s.Steps);
        Assert.Equal(MoveResult.Moved, nav.Backward());      // 向きを変えずに戻る
        Assert.Equal(new GridPos(1, 0), s.Position.Cell);
        Assert.Equal(Direction.East, s.Position.Facing);
    }

    [Fact]
    public void OneWayPassesOnlyOneWay()
    {
        var s = State(Small, out var nav);
        s.Position = new PartyPosition(0, new GridPos(1, 1), Direction.East);
        Assert.Equal(MoveResult.Moved, nav.Forward());       // 東へは通れる
        nav.TurnAround();
        Assert.Equal(MoveResult.Bumped, nav.Forward());      // 西へは壁
        Assert.Equal(EdgeType.Wall, s.Apparent(0, new GridPos(2, 1), Direction.West));
    }

    [Fact]
    public void SecretDoorIsWallUntilFound()
    {
        var s = State(Small, out var nav);
        nav.TurnRight();                                      // 南を向く
        Assert.Equal(MoveResult.Bumped, nav.Forward());
        Assert.Equal(MoveResult.FoundSecretDoor, nav.Search());
        Assert.Equal(EdgeType.Door, s.Apparent(0, new GridPos(0, 1), Direction.North));   // 反対側からも見える
        Assert.Equal(MoveResult.Moved, nav.Forward());
        Assert.Equal(MoveResult.NothingFound, nav.Search());
    }

    [Fact]
    public void WrapConnectsEdges()
    {
        const string wrapped = """
            floor w
            size 3 1
            wrap x
            start 0 0 W
            walls
            +-+-+-+

            +-+-+-+
            """;
        var floor = MazeTextFormat.Parse(wrapped.Replace("\n            ", "\n").TrimStart());
        Assert.Empty(floor.Validate());
        var s = new ExplorationState(new Dungeon("w", [floor]), new PartyPosition(0, floor.Start, Direction.West));
        var nav = new MazeNavigator(s);
        Assert.Equal(MoveResult.Moved, nav.Forward());
        Assert.Equal(new GridPos(2, 0), s.Position.Cell);
    }

    [Fact]
    public void StairsMoveBetweenFloorsAndTopExitsToTown()
    {
        var dungeon = DungeonLoader.Load(ContentLibrary.Load(TestPaths.Content), "devtest");
        var b1 = dungeon.Floors[0];
        var s = new ExplorationState(dungeon, new PartyPosition(0, b1.FindFeature(CellFeature.StairsDown)!.Value, Direction.North));
        var nav = new MazeNavigator(s);
        Assert.Equal(MoveResult.ChangedFloor, nav.UseStairs());
        Assert.Equal(1, s.Position.Floor);
        Assert.Equal(CellFeature.StairsUp, s.Cell.Feature);
        Assert.Equal(MoveResult.ChangedFloor, nav.UseStairs());
        Assert.Equal(0, s.Position.Floor);
        Assert.Equal(CellFeature.StairsDown, s.Cell.Feature);
        s.Position = s.Position with { Cell = b1.FindFeature(CellFeature.StairsUp)!.Value };
        Assert.Equal(MoveResult.ExitToTown, nav.UseStairs());
        s.Position = s.Position with { Cell = b1.Start };
        Assert.Equal(MoveResult.NoStairs, nav.UseStairs());
    }

    [Fact]
    public void AutoMapRecordsVisitedAndSeenEdges()
    {
        var s = State(Small, out _);
        var map = s.AutoMap.For(0);
        Assert.Contains(new GridPos(0, 0), map.Visited);
        Assert.Equal(EdgeType.Wall, map.Seen(new GridPos(0, 0), Direction.South));   // 見つけていない隠し扉は壁
        Assert.Equal(EdgeType.Door, map.Seen(new GridPos(1, 0), Direction.East));     // 前に見えた扉
        Assert.DoesNotContain(new GridPos(1, 0), map.Visited);                       // 見えただけで通っていない
        Assert.Null(map.Seen(new GridPos(2, 0), Direction.East));                     // 暗闇のマスは見えても記録しない
    }

    [Fact]
    public void FogRecordsNothing()
    {
        var s = State(Small, out var nav);
        s.Position = new PartyPosition(0, new GridPos(0, 1), Direction.East);
        nav.Forward();                                        // 霧の (1,1) へ
        Assert.Equal(new GridPos(1, 1), s.Position.Cell);
        var map = s.AutoMap.For(0);
        Assert.DoesNotContain(new GridPos(1, 1), map.Visited);
        Assert.Null(map.Seen(new GridPos(1, 1), Direction.East));
    }

    [Fact]
    public void DarknessRecordsOnlyTouchedWalls()
    {
        var s = State(Small, out var nav);
        nav.Forward();
        nav.Forward();                                        // 暗闇の (2,0)
        var map = s.AutoMap.For(0);
        Assert.Contains(new GridPos(2, 0), map.Visited);
        Assert.Equal(EdgeType.Wall, map.Seen(new GridPos(2, 0), Direction.East));
        nav.TurnRight();                                       // 南を向いても、前のマスは見えない
        Assert.Null(map.Seen(new GridPos(2, 1), Direction.South));
    }

    [Fact]
    public void ViewStopsAtWallsAndSeesSideOpenings()
    {
        var s = State(Small, out _);
        var view = MazeView.Compute(s);
        // 東向き: (0,0) -> (1,0) は見え、扉の先の (2,0) は見えない
        Assert.Contains(view, v => v.Depth == 1 && v.Lateral == 0 && v.Cell == new GridPos(1, 0));
        Assert.DoesNotContain(view, v => v.Cell == new GridPos(2, 0));
        // (1,0) の右(南)は壁なので、(1,1) は見えない
        Assert.DoesNotContain(view, v => v.Cell == new GridPos(1, 1));
    }
}
