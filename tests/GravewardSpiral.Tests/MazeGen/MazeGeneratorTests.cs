using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;

namespace GravewardSpiral.Tests.MazeGen;

public class MazeGeneratorTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly MazeGenSettings Settings = MazeGenSettings.Load(Content);

    private static Dungeon Generate(ulong seed, int floors = 10, FloorSizeMode? size = null, MazeGenSettings? settings = null) =>
        new MazeGenerator(settings ?? Settings).Generate(new GenerationRequest(seed, floors, size ?? FloorSizeMode.Fixed(20, 20))).Dungeon;

    private static string Text(Dungeon d) => string.Join("\n----\n", d.Floors.Select(MazeTextFormat.Write));

    [Fact]
    public void GeneratedFloorsSurviveTheTextFormat()
    {
        // シナリオの迷宮は、生成したものを手作りのマップの形式で保存して使う。書いて読み直しても同じになること
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var dungeon = Generate(seed, 6);
            var back = new Dungeon(dungeon.Id, dungeon.Floors.Select(f => MazeTextFormat.Parse(MazeTextFormat.Write(f), f.Id)).ToList());
            Assert.Equal(Text(dungeon), Text(back));
            Assert.Empty(back.Validate());
            Assert.True(MazeBot.Explore(back).Ok);
            for (int i = 0; i < dungeon.Floors.Count; i++)
            {
                Assert.Equal(dungeon.Floors[i].Rooms.Count, back.Floors[i].Rooms.Count);
                Assert.Equal(dungeon.Floors[i].Start, back.Floors[i].Start);
            }
        }
    }

    [Fact]
    public void SettingsLoadFromPack()
    {
        Assert.Equal(6, Settings.DefaultFloors);   // 第1部 A は L1〜5 の 6 階
        Assert.Equal(new IntRange(2, 5), Settings.RoomSize);
        Assert.Equal(0.6, Settings.DarkFogOverlapMinDepth);
    }

    [Fact]
    public void GeneratedFloorsKeepTheirRooms()
    {
        var d = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(77, 3, FloorSizeMode.Fixed(20, 20))).Dungeon;
        foreach (var f in d.Floors)
        {
            Assert.NotEmpty(f.Rooms);
            foreach (var r in f.Rooms) Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= f.Width && r.Y + r.H <= f.Height);
        }
    }

    [Fact]
    public void SameSeedGivesSameDungeon()
    {
        Assert.Equal(Text(Generate(12345)), Text(Generate(12345)));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentDungeons()
    {
        Assert.NotEqual(Text(Generate(1)), Text(Generate(2)));
    }

    [Fact]
    public void ManySeedsPassAllChecks()
    {
        var sizes = new[] { FloorSizeMode.Fixed(20, 20), FloorSizeMode.Fixed(8, 8), new FloorSizeMode(8, 40, 8, 30) };
        for (ulong seed = 0; seed < 150; seed++)
        {
            var size = sizes[seed % 3];
            var dungeon = Generate(seed, floors: 6, size: size);
            Assert.Empty(dungeon.Validate());
            var reach = ReachabilityValidator.Validate(dungeon);
            Assert.True(reach.Ok, $"seed {seed}: {string.Join(" / ", reach.Problems)}");
        }
    }

    [Fact]
    public void GeneratedFloorsRoundTripThroughText()
    {
        foreach (var floor in Generate(777).Floors)
        {
            var text = MazeTextFormat.Write(floor);
            Assert.Equal(text, MazeTextFormat.Write(MazeTextFormat.Parse(text)));
        }
    }

    [Fact]
    public void RandomSizesStayInRange()
    {
        var d = Generate(99, floors: 12, size: new FloorSizeMode(10, 30, 12, 24));
        Assert.All(d.Floors, f =>
        {
            Assert.InRange(f.Width, 10, 30);
            Assert.InRange(f.Height, 12, 24);
        });
        Assert.True(d.Floors.Select(f => (f.Width, f.Height)).Distinct().Count() > 1);
    }

    [Fact]
    public void NoTinyZonesAndNoOverlapOnShallowFloors()
    {
        for (ulong seed = 0; seed < 40; seed++)
        {
            var d = Generate(seed);
            for (int i = 0; i < d.Floors.Count; i++)
            {
                var f = d.Floors[i];
                double depth = i / (double)(d.Floors.Count - 1);
                foreach (var (zone, rule) in new[] { (Zone.Dark, Settings.Dark), (Zone.Fog, Settings.Fog), (Zone.AntiMagic, Settings.AntiMagic) })
                {
                    foreach (var group in Groups(f, p => f[p].Zone.HasFlag(zone)))
                        Assert.True(group >= rule.Size.Min, $"seed {seed} B{i + 1}: {zone} の区域が {group} マスしかない");
                }
                if (depth < Settings.DarkFogOverlapMinDepth)
                    Assert.DoesNotContain(f.AllCells(), p => f[p].Zone.HasFlag(Zone.Dark) && f[p].Zone.HasFlag(Zone.Fog));
                if (depth < Settings.SpinnerInDarkMinDepth)
                    Assert.DoesNotContain(f.AllCells(), p => f[p].Feature == CellFeature.Spinner && f[p].Zone.HasFlag(Zone.Dark));
            }
        }
    }

    [Fact]
    public void ShallowFloorsHaveNoDeepGimmicks()
    {
        var d = Generate(5);
        var b1 = d.Floors[0];
        Assert.DoesNotContain(b1.AllCells(), p => b1[p].Feature is CellFeature.Teleporter or CellFeature.Chute);
        Assert.False(b1.WrapX || b1.WrapY);
        Assert.Null(d.Floors[^1].FindFeature(CellFeature.StairsDown));
    }

    [Fact]
    public void DownStairsAreFarFromUpStairs()
    {
        var d = Generate(2024);
        foreach (var f in d.Floors.Take(d.Floors.Count - 1))
        {
            var up = f.FindFeature(CellFeature.StairsUp)!.Value;
            var down = f.FindFeature(CellFeature.StairsDown)!.Value;
            int manhattan = Math.Abs(up.X - down.X) + Math.Abs(up.Y - down.Y);
            Assert.True(manhattan >= 6, $"{f.Id}: 階段が近すぎる ({up} と {down})");
        }
    }

    [Fact]
    public void PrefabIsPlacedWhenConfigured()
    {
        var settings = MazeGenSettings.Load(Content, "devtest");
        Assert.Single(settings.Prefabs);
        var d = Generate(3, floors: 3, settings: settings);
        foreach (var f in d.Floors)
        {
            // 祠の柱: 四方が入れるマスに囲まれた岩盤
            Assert.Contains(f.AllCells(), p => f[p].IsSolid && DirectionExtensions.All.All(dir => f.Neighbor(p, dir) is { } n && !f[n].IsSolid));
        }
    }

    [Fact]
    public void ValidatorFindsUnreachableAndTrappedCells()
    {
        const string text = """
            floor bad
            size 3 1
            wrap none
            start 0 0 E
            walls
            +-+-+-+
            |u> | |
            +-+-+-+
            """;
        var floor = MazeTextFormat.Parse(text.Replace("\n            ", "\n").TrimStart());
        var result = ReachabilityValidator.Validate(new Dungeon("bad", [floor]));
        Assert.Contains(result.Problems, p => p.Contains("戻れなく"));
        Assert.Contains(result.Problems, p => p.Contains("行けない"));
    }

    /// <summary>条件に合うマスの、上下左右につながったまとまりの大きさ。</summary>
    private static List<int> Groups(MazeFloor f, Func<GridPos, bool> match)
    {
        var seen = new HashSet<GridPos>();
        var sizes = new List<int>();
        foreach (var start in f.AllCells())
        {
            if (!match(start) || !seen.Add(start)) continue;
            int n = 0;
            var q = new Queue<GridPos>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                n++;
                foreach (var d in DirectionExtensions.All)
                {
                    if (f.Neighbor(p, d) is { } m && match(m) && seen.Add(m)) q.Enqueue(m);
                }
            }
            sizes.Add(n);
        }
        return sizes;
    }
}
