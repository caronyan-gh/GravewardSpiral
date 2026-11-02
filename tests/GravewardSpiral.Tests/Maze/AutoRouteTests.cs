using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Tests.Maze;

/// <summary>自動移動の道: 通ったマスと、見えた通路・扉だけを通る。</summary>
public class AutoRouteTests
{
    private static readonly MazeFloor Floor = new("t", 5, 5);

    /// <summary>cells を通ったことにし、並んだマスのあいだの辺を edge で見たことにする(両側から)。</summary>
    private static FloorMap Corridor(EdgeType edge, params (int X, int Y)[] cells)
    {
        var map = new FloorMap();
        foreach (var (x, y) in cells) map.Visited.Add(new GridPos(x, y));
        for (int i = 0; i + 1 < cells.Length; i++)
        {
            var a = new GridPos(cells[i].X, cells[i].Y);
            var b = new GridPos(cells[i + 1].X, cells[i + 1].Y);
            var d = DirectionExtensions.All.First(dd => Floor.Neighbor(a, dd) == b);
            map.See(a, d, edge);
            map.See(b, d.Opposite(), edge == EdgeType.OneWayOut ? EdgeType.Wall : edge);
        }
        return map;
    }

    [Fact]
    public void FollowsTheVisitedCorridor()
    {
        var map = Corridor(EdgeType.Open, (0, 0), (1, 0), (1, 1), (2, 1));
        Assert.Equal([Direction.East, Direction.South, Direction.East], AutoRoute.Find(Floor, map, new(0, 0), new(2, 1)));
        Assert.Empty(AutoRoute.Find(Floor, map, new(1, 1), new(1, 1))!);
    }

    [Fact]
    public void NeverGoesThroughSquaresNotYetVisited()
    {
        var map = Corridor(EdgeType.Open, (0, 0), (1, 0), (2, 0));
        map.Visited.Remove(new GridPos(1, 0));
        Assert.Null(AutoRoute.Find(Floor, map, new(0, 0), new(2, 0)));
        Assert.Null(AutoRoute.Find(Floor, map, new(0, 0), new(1, 0)));   // 行き先も、通ったマスだけ
    }

    [Theory]
    [InlineData(EdgeType.Door, true)]
    [InlineData(EdgeType.Locked, false)]
    [InlineData(EdgeType.Wall, false)]
    public void PassesOnlyOpenDoors(EdgeType edge, bool passes)
    {
        var map = Corridor(edge, (0, 0), (1, 0));
        Assert.Equal(passes, AutoRoute.Find(Floor, map, new(0, 0), new(1, 0)) != null);
    }

    [Fact]
    public void OneWayDoorsOnlyOneWay()
    {
        var map = Corridor(EdgeType.OneWayOut, (0, 0), (1, 0));
        Assert.NotNull(AutoRoute.Find(Floor, map, new(0, 0), new(1, 0)));
        Assert.Null(AutoRoute.Find(Floor, map, new(1, 0), new(0, 0)));
    }

    [Fact]
    public void StepsAroundKnownTrapsUnlessTheyAreTheGoal()
    {
        // 上の道 (0,0)→(1,0)→(2,0) と、下の回り道 (0,0)→(0,1)→(1,1)→(2,1)→(2,0)
        var map = Corridor(EdgeType.Open, (0, 0), (1, 0), (2, 0));
        var around = Corridor(EdgeType.Open, (0, 0), (0, 1), (1, 1), (2, 1), (2, 0));
        foreach (var c in around.Visited) map.Visited.Add(c);
        foreach (var (k, v) in around.Edges) map.Edges[k] = v;
        Assert.Equal(2, AutoRoute.Find(Floor, map, new(0, 0), new(2, 0))!.Count);
        map.Marks[new GridPos(1, 0)] = CellFeature.Pit;
        Assert.Equal(4, AutoRoute.Find(Floor, map, new(0, 0), new(2, 0))!.Count);
        Assert.Single(AutoRoute.Find(Floor, map, new(0, 0), new(1, 0))!);
    }
}
