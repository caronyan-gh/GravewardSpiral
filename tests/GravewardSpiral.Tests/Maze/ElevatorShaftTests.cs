using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Maze;

public class ElevatorShaftTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);

    private static MazeFloor Open(int w, int h)
    {
        var f = new MazeFloor("f", w, h);
        foreach (var p in f.AllCells())
            foreach (var d in DirectionExtensions.All)
                if (f.InBounds(p.X + d.Dx(), p.Y + d.Dy())) MazeEdit.SetEdge(f, p, d, EdgeType.Open);
        return f;
    }

    [Fact]
    public void GeneratedElevatorsShareOneCellPerShaft()
    {
        var settings = MazeGenSettings.Load(Content);
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 8, FloorSizeMode.Fixed(16, 16))).Dungeon;
            var groups = dungeon.Floors.SelectMany((f, i) => f.AllCells().Where(p => f[p].Feature == CellFeature.Elevator).Select(p => (i, p, f[p].Mark)))
                .GroupBy(x => x.Mark).ToList();
            Assert.NotEmpty(groups);
            foreach (var g in groups)
            {
                Assert.Single(g.Select(x => x.p).Distinct());   // どの階でも同じ座標
                Assert.True(g.Count() >= 2);
            }
        }
    }

    [Fact]
    public void AShaftSkipsFloorsWhereTheCellIsNotOpen()
    {
        var floors = new List<MazeFloor> { Open(4, 4), Open(4, 4), Open(4, 4), Open(4, 4) };
        MazeEdit.SetFeature(floors[1], new GridPos(2, 2), CellFeature.Solid);   // B2 は岩: 素通り
        floors[2][new GridPos(2, 2)].Feature = CellFeature.StairsDown;          // B3 は階段: 止まらない
        var shaft = new ElevatorShaft(2, 2, 1, 4);
        Assert.Equal([0, 3], Elevators.Apply(floors, shaft));
        Assert.Equal(CellFeature.Elevator, floors[0][new GridPos(2, 2)].Feature);
        Assert.Equal(shaft.Mark, floors[3][new GridPos(2, 2)].Mark);
        Assert.True(floors[1][new GridPos(2, 2)].IsSolid);

        // 乗ると、座標はそのままで止まる階へ
        var state = new ExplorationState(new Dungeon("d", floors), new PartyPosition(0, new GridPos(2, 2), Direction.North));
        var nav = new MazeNavigator(state);
        Assert.Equal([0, 3], nav.ElevatorFloors());
        Assert.Equal(MoveResult.TookElevator, nav.TakeElevator(3));
        Assert.Equal(new GridPos(2, 2), state.Position.Cell);

        Elevators.Clear(floors, shaft);
        Assert.Equal(CellFeature.None, floors[3][new GridPos(2, 2)].Feature);
    }

    [Fact]
    public void AScenarioKeepsShaftsInDungeonsJsonAndTheGamePutsThemBack()
    {
        using var dir = new TestPaths.TempDir();
        var settings = MazeGenSettings.Load(Content);
        var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(4, 8, FloorSizeMode.Fixed(16, 16))).Dungeon;
        string folder = Path.Combine(dir.Path, "lift");
        var pack = Core.Save.ScenarioPack.New(folder, "lift", dungeon.Floors);
        Assert.NotEmpty(pack.Elevators);   // 自動生成のエレベーターは縦穴になる
        pack.Save(Content);
        // マップには書かず、dungeons.json にだけ書く
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(folder, "maps")), f => File.ReadAllText(f).Contains("elevator:"));
        Assert.Contains("\"elevators\"", File.ReadAllText(Path.Combine(folder, "dungeons.json")));

        var back = Core.Save.ScenarioPack.Load(folder, []);
        Assert.Equal(pack.Elevators, back.Elevators);
        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var loaded = DungeonLoader.ScenarioDungeon(content)!;
        foreach (var s in pack.Elevators)
            Assert.Equal(Elevators.Stops(pack.Floors, s), loaded.Floors.Select((f, i) => (f, i)).Where(x => x.f.InBounds(s.X, s.Y) && x.f[s.Cell].Mark == s.Mark).Select(x => x.i));
    }

    [Fact]
    public void AShaftWithAPowerFlagWaitsForTheSwitch()
    {
        var floors = new List<MazeFloor> { Open(3, 3), Open(3, 3) };
        var shaft = new ElevatorShaft(1, 1, 1, 2) { Power = Elevators.DefaultPower };
        Elevators.Apply(floors, shaft);
        var flags = new HashSet<string>();
        Assert.Equal(Elevators.DefaultPower, Elevators.Unpowered([shaft], floors[0][shaft.Cell], flags));
        flags.Add(Elevators.DefaultPower);
        Assert.Null(Elevators.Unpowered([shaft], floors[0][shaft.Cell], flags));
        Assert.Null(Elevators.Unpowered([shaft with { Power = null }], floors[0][shaft.Cell], []));

        // 標準のイベント「エレベーターのスイッチ」が、そのフラグを立てる
        var data = Core.Characters.GameData.Load(Content);
        var sw = data.Events["elevator_switch"];
        Assert.Contains(sw.Steps.SelectMany(s => s.Children().SelectMany(c => c)).Concat(sw.Steps), s => s.Type == "flag" && s.Id == Elevators.DefaultPower);

        // 書いて読み直しても、power は残る
        var json = System.Text.Json.JsonSerializer.Serialize(shaft, ContentLibrary.JsonOptions);
        Assert.Equal(shaft, System.Text.Json.JsonSerializer.Deserialize<ElevatorShaft>(json, ContentLibrary.JsonOptions));
    }

    [Fact]
    public void PartOneHasTheCoffinLift()
    {
        // 棺下ろしの籠: B1 の入口のそばから B5 まで、1本の縦穴
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(TestPaths.Content, "scenarios", ScenarioCatalog.Gravekeeper));
        var dungeon = DungeonLoader.ScenarioDungeon(content)!;
        var stops = dungeon.Floors.SelectMany((f, i) => f.AllCells().Where(p => f[p].Feature == CellFeature.Elevator).Select(p => (i, p))).ToList();
        Assert.Equal([new GridPos(12, 3)], stops.Select(x => x.p).Distinct());
        Assert.Equal([0, 1, 2, 3, 4], stops.Select(x => x.i).Order());
    }

    [Fact]
    public void OldElevatorGroupsBecomeShafts()
    {
        var floors = new List<MazeFloor> { Open(5, 5), Open(5, 5), Open(5, 5) };
        floors[0][new GridPos(1, 1)].Feature = CellFeature.Elevator;
        floors[0][new GridPos(1, 1)].Mark = "elevator:0";
        floors[2][new GridPos(3, 4)].Feature = CellFeature.Elevator;
        floors[2][new GridPos(3, 4)].Mark = "elevator:0";
        var made = Elevators.FromLegacy(floors, new Pcg32(1, 1));
        var shaft = Assert.Single(made);
        Assert.Equal((1, 3), (shaft.From, shaft.To));
        var cells = floors.SelectMany((f, i) => f.AllCells().Where(p => f[p].Feature == CellFeature.Elevator).Select(p => (i, p))).ToList();
        Assert.Equal(3, cells.Count);   // 全部の階が空いているので、B1〜B3 に止まる
        Assert.Single(cells.Select(x => x.p).Distinct());
        Assert.DoesNotContain(floors, f => f.AllCells().Any(p => f[p].Mark == "elevator:0"));
    }
}
