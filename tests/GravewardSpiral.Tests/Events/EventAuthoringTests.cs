using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Events;

public partial class EventTests
{
    private static EventSetDef TwinTemplate() => EventAuthoring.Templates(Content, Data.Events).Single(s => s.Id == "twin_switches");
    private static ScenarioPack AuthoringPack()
    {
        var (s, _) = Room("twin_room.txt", 0, 0);
        var f = s.Dungeon.Floors[0];
        foreach (var cell in f.AllCells()) f[cell].Mark = null;
        return new ScenarioPack { Floors = [f] };
    }

    [Fact]
    public void ImportSetIncludesInternalPartsAndSeparatesFlagsGatesAndCalls()
    {
        var p = AuthoringPack();
        var source = TwinTemplate();
        var east = EventAuthoring.ImportSet(p, source, Data.Events, LocalizedText.Of("East vault", "東の宝物庫"));
        var west = EventAuthoring.ImportSet(p, east, EventAuthoring.Events(p, Data.Events.Values).ToDictionary(e => e.Id), LocalizedText.Of("West vault", "西の宝物庫"));
        Assert.Equal(5, east.Parts.Count);
        Assert.Equal(4, east.Parts.Count(part => part.Place));
        Assert.Empty(east.OwnedFlags.Intersect(west.OwnedFlags));
        Assert.Empty(east.Parts.Select(part => part.Mark).Intersect(west.Parts.Select(part => part.Mark)));
        var a = p.Events.Single(e => e.Id == east.Parts[0].Target);
        var b = p.Events.Single(e => e.Id == east.Parts[1].Target);
        var check = p.Events.Single(e => e.Id == east.Parts.Single(part => !part.Place).Target);
        Assert.Contains(EventAuthoring.Steps(a), s => s.Type == "flag" && s.Id == a.OwnFlag);
        Assert.Contains(EventAuthoring.Steps(a), s => s.Type == "run" && s.Id == check.Id);
        Assert.Contains(EventAuthoring.Conditions(check), c => c.Type == "flag" && c.Id == b.OwnFlag);
        Assert.Contains(EventAuthoring.Steps(check), s => s.Type == "open" && s.Id == east.Parts.Single(part => part.Kind == "gate").Target);
        Assert.Null(Data.Events["twin_switch_a"].OwnFlag); // 標準のデータを変更しない
    }

    [Fact]
    public void TwoImportedTwinSetsDoNotOpenEachOthersGate()
    {
        var p = AuthoringPack();
        p.Floors.Add(MazeEdit.Copy(p.Floors[0], "other"));
        var east = EventAuthoring.ImportSet(p, TwinTemplate(), Data.Events, LocalizedText.Of("East", "東"));
        var west = EventAuthoring.ImportSet(p, TwinTemplate(), Data.Events, LocalizedText.Of("West", "西"));
        var data = GameData.Load(Content);
        foreach (var e in p.Events) data.Events[e.Id] = e;
        for (int i = 0; i < 2; i++)
        {
            var set = i == 0 ? east : west;
            EventAuthoring.Place(p, set.Parts[0].Mark, i, new(0, 0), false, []);
            EventAuthoring.Place(p, set.Parts[1].Mark, i, new(4, 0), false, []);
            EventAuthoring.Place(p, set.Parts[2].Mark, i, new(2, 0), false, []);
        }
        var dungeon = new Dungeon("t", p.Floors);
        var session = GameSession.New(0, new Core.MazeGen.GenerationRequest(1, 2, Core.MazeGen.FloorSizeMode.Fixed(5, 3)), dungeon);
        var state = new ExplorationState(dungeon, new PartyPosition(0, new(0, 0), Direction.North));
        void Press(EventSetDef set, int part, int floor, int x)
        {
            state.Position = new PartyPosition(floor, new(x, 0), Direction.North);
            Drive(new EventRunner(data.Events[set.Parts[part].Target], new EventContext
            {
                Data = data, Session = session, Maze = state, At = (floor, new(x, 0)), Rng = new Core.Rng.Pcg32(1, 3),
            }), [0]);
        }
        Press(east, 0, 0, 0);
        Press(west, 1, 1, 4);
        Assert.False(state.IsUnlocked(0, new(2, 0), Direction.South));
        Assert.False(state.IsUnlocked(1, new(2, 0), Direction.South));
        Press(east, 1, 0, 4);
        Assert.True(state.IsUnlocked(0, new(2, 0), Direction.South));
        Assert.False(state.IsUnlocked(1, new(2, 0), Direction.South));
    }

    [Fact]
    public void PlacementRejectsDuplicateOrOccupiedCellAndMovesExplicitly()
    {
        var p = AuthoringPack();
        var e = new EventDef { Id = "switch", OwnFlag = EventAuthoring.NewId(p, "flag") };
        p.Events.Add(e);
        EventAuthoring.Place(p, "event:switch", 0, new(0, 0), false, []);
        Assert.Throws<InvalidOperationException>(() => EventAuthoring.Place(p, "event:switch", 0, new(4, 0), false, []));
        Assert.Equal("event:switch", p.Floors[0][new GridPos(0, 0)].Mark);
        EventAuthoring.Place(p, "event:switch", 0, new(4, 0), true, []);
        Assert.Null(p.Floors[0][new GridPos(0, 0)].Mark);
        p.Floors[0][new GridPos(0, 0)].Mark = "key";
        Assert.Throws<InvalidOperationException>(() => EventAuthoring.Place(p, "event:switch", 0, new(0, 0), true, []));
        Assert.Equal("event:switch", p.Floors[0][new GridPos(4, 0)].Mark);
    }

    [Fact]
    public void PlaceRemainingIsAtomicAndIncludesGateButNotInternalEvent()
    {
        var p = AuthoringPack();
        var set = EventAuthoring.ImportSet(p, TwinTemplate(), Data.Events, LocalizedText.Of("Vault", "宝物庫"));
        MazeEdit.SetEdge(p.Floors[0], new(2, 0), Direction.South, EdgeType.Wall);
        Assert.Throws<InvalidOperationException>(() => EventAuthoring.PlaceRemaining(p, set, 0));
        Assert.All(p.Floors[0].AllCells(), c => Assert.Null(p.Floors[0][c].Mark));
        MazeEdit.SetEdge(p.Floors[0], new(2, 0), Direction.South, EdgeType.Locked);
        EventAuthoring.PlaceRemaining(p, set, 0);
        Assert.Empty(EventAuthoring.Check(p, Data.Events.Values));
        Assert.Empty(EventAuthoring.Locations(p, set.Parts.Last().Mark));
        EventAuthoring.PlaceRemaining(p, set, 0); // 既に置いた部品を増やさない
        Assert.Equal(4, p.Floors[0].AllCells().Count(c => p.Floors[0][c].Mark != null));
    }

    [Fact]
    public void ExistingTwinSwitchIsCheckedAgainstStandardSetDefinition()
    {
        var p = AuthoringPack();
        p.Floors[0][new GridPos(0, 0)].Mark = "event:twin_switch_a";
        var errors = EventAuthoring.Check(p, Data.Events.Values, [TwinTemplate()]);
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("スイッチB"));
        Assert.Contains(errors, e => e.Contains("開く扉"));
    }

    [Fact]
    public void SetCopyRewritesNestedStateAndTimerButPreservesSharedReferences()
    {
        var root = new EventDef { Id = "root", Steps = [new() { Type = "cases", Cases = [new()
        {
            When = [new() { Type = "counter", Id = "score" }, new() { Type = "done", Id = "helper" }],
            Then = [new() { Type = "counter_set", Id = "score", Value = 2 }, new() { Type = "flag", Id = "shared" }, new() { Type = "timer", Id = "helper", Value = 3 }],
        }] }] };
        var helper = new EventDef { Id = "helper", Steps = [new() { Type = "unflag", Id = "private" }, new() { Type = "warp", Id = "landing" }] };
        var source = new EventSetDef
        {
            Id = "source", OwnedFlags = ["private"], OwnedCounters = ["score"],
            Parts = [new() { Target = "root" }, new() { Target = "helper", Place = false }, new() { Kind = "dest", Target = "landing" }],
        };
        var p = AuthoringPack();
        var set = EventAuthoring.ImportSet(p, source, new Dictionary<string, EventDef> { [root.Id] = root, [helper.Id] = helper }, LocalizedText.Of("Test", "テスト"));
        Assert.Contains(EventAuthoring.Conditions(p.Events[0]), c => c.Type == "done" && c.Id == p.Events[1].Id);
        Assert.Contains(EventAuthoring.Conditions(p.Events[0]), c => c.Type == "counter" && c.Id == set.OwnedCounters[0]);
        Assert.Contains(EventAuthoring.Steps(p.Events[0]), s => s.Type == "timer" && s.Id == p.Events[1].Id);
        Assert.Contains(EventAuthoring.Steps(p.Events[0]), s => s.Type == "flag" && s.Id == "shared");
        Assert.Equal(set.OwnedFlags[0], p.Events[1].Steps[0].Id);
        Assert.Equal(set.Parts[2].Target, p.Events[1].Steps[1].Id);
        Assert.Equal("private", helper.Steps[0].Id);
        var before = p.Events.Count;
        source.Parts.Add(new() { Target = "missing" });
        Assert.Throws<InvalidOperationException>(() => EventAuthoring.ImportSet(p, source, new Dictionary<string, EventDef> { [root.Id] = root, [helper.Id] = helper }, LocalizedText.Of("Bad", "不正")));
        Assert.Equal(before, p.Events.Count);
        Assert.Single(p.EventSets);
    }

    [Fact]
    public void SetCheckReportsAllMissingAndIgnoresInternalPartPlacement()
    {
        var p = AuthoringPack();
        var set = EventAuthoring.ImportSet(p, TwinTemplate(), Data.Events, LocalizedText.Of("Vault", "宝物庫"));
        Assert.Equal(4, EventAuthoring.Check(p, Data.Events.Values).Count);
        EventAuthoring.Place(p, set.Parts[0].Mark, 0, new(0, 0), false, []);
        EventAuthoring.Place(p, set.Parts[1].Mark, 0, new(4, 0), false, []);
        EventAuthoring.Place(p, set.Parts[2].Mark, 0, new(2, 0), false, []);
        EventAuthoring.Place(p, set.Parts[3].Mark, 0, new(2, 1), false, []);
        Assert.Empty(EventAuthoring.Check(p, Data.Events.Values));
        p.Floors[0][new GridPos(1, 0)].Mark = set.Parts[0].Mark;
        Assert.Contains(EventAuthoring.Check(p, Data.Events.Values), x => x.Contains("重複配置"));
    }

    [Fact]
    public void EventCopySeparatesItsOwnFlagButKeepsSharedFlag()
    {
        var p = new ScenarioPack();
        var e = new EventDef { Id = "a", OwnFlag = "private", Steps = [new() { Type = "flag", Id = "private" }, new() { Type = "flag", Id = "shared" }], When = [new() { Type = "flag", Id = "private" }] };
        p.Events.Add(e);
        var copy = EventAuthoring.DuplicateEvent(p, e);
        Assert.NotEqual(e.OwnFlag, copy.OwnFlag);
        Assert.Equal(copy.OwnFlag, copy.Steps[0].Id);
        Assert.Equal(copy.OwnFlag, copy.When[0].Id);
        Assert.Equal("shared", copy.Steps[1].Id);
        ScenarioEdit.RenameEvent(p, copy.Id, "renamed");
        Assert.Equal(copy.OwnFlag, copy.Steps[0].Id);
    }

    [Fact]
    public void OwnFlagsLimitScatterWithoutChangingOrdinaryEvents()
    {
        var e = new EventDef { Scatter = new() { Max = 8 } };
        Assert.Equal(8, EventPlacer.MaxPerWorld(e));
        e.OwnFlag = "flag_001";
        Assert.Equal(1, EventPlacer.MaxPerWorld(e));
    }

    [Fact]
    public void ScatterDoesNotDuplicatePartiallyPlacedOwnedChain()
    {
        var p = AuthoringPack();
        var data = GameData.Load(Content);
        data.Events.Clear();
        data.Events["a"] = new() { Id = "a", Place = "maze", OwnFlag = "owned", Scatter = new() { Chain = "pair", Step = 1 } };
        data.Events["b"] = new() { Id = "b", Place = "maze", Scatter = new() { Chain = "pair", Step = 2 } };
        var dungeon = new Dungeon("t", p.Floors);
        Assert.Single(EventPlacer.ScatterChains(data, dungeon, new(1, 1), 1));
        foreach (var cell in p.Floors[0].AllCells()) p.Floors[0][cell].Mark = null;
        p.Floors[0][new GridPos(0, 0)].Mark = "event:b";
        Assert.Empty(EventPlacer.ScatterChains(data, dungeon, new(1, 1), 1));
        Assert.Single(p.Floors[0].AllCells().Where(c => p.Floors[0][c].Mark != null));
    }

    [Fact]
    public void ShortIdsAvoidDefinitionsReferencesStandardContentAndPendingCopies()
    {
        var p = AuthoringPack();
        p.Flags.Add(new() { Id = "flag_001" });
        p.Events.Add(new() { Id = "event_001", Steps = [new() { Type = "flag", Id = "flag_002" }] });
        var standard = new EventDef { Id = "event_002", OwnFlag = "flag_003" };
        Assert.Equal("flag_004", EventAuthoring.NewId(p, "flag", [standard]));
        var copy = EventAuthoring.DuplicateEvent(p, standard, [standard]);
        Assert.Equal("event_003", copy.Id);
        Assert.Equal("flag_004", copy.OwnFlag);
        var set = EventAuthoring.ImportSet(p, TwinTemplate(), Data.Events, LocalizedText.Of("East", "東"));
        var another = EventAuthoring.ImportSet(p, set, EventAuthoring.Events(p, Data.Events.Values).ToDictionary(e => e.Id), LocalizedText.Of("West", "西"));
        Assert.All(set.OwnedFlags.Concat(another.OwnedFlags), id => Assert.Matches("^flag_[0-9]{3,}$", id));
        Assert.Empty(set.OwnedFlags.Intersect(another.OwnedFlags));
        Assert.Equal(p.Events.Count, p.Events.Select(e => e.Id).Distinct().Count());
        Assert.Equal("flag_003", standard.OwnFlag);
    }

    [Fact]
    public void AuthoringMetadataSurvivesSaveAndDoesNotChangeLegacyFlags()
    {
        using var dir = new TestPaths.TempDir();
        string path = Path.Combine(dir.Path, "flag_demo");
        var p = ScenarioPack.New(path, "flag_demo", AuthoringPack().Floors);
        p.Flags.Add(new() { Id = "legacy_global", Name = LocalizedText.Of("The sluice is open", "水門を開いた"), Note = "既存の名前を保持" });
        p.Events.Add(new() { Id = "legacy", Steps = [new() { Type = "flag", Id = "legacy_global" }] });
        var set = EventAuthoring.ImportSet(p, TwinTemplate(), Data.Events, LocalizedText.Of("East vault", "東の宝物庫"));
        EventAuthoring.Place(p, set.Parts[0].Mark, 0, new(0, 0), false, []);
        p.Save(Content);
        var errors = new List<string>();
        var back = ScenarioPack.Load(path, errors);
        Assert.Empty(errors);
        Assert.Equal(ContentFormat.Current, back.Manifest.Format);
        Assert.Equal("legacy_global", back.Events.Single(e => e.Id == "legacy").Steps[0].Id);
        Assert.Equal("水門を開いた", back.Flags[0].Name!.Get("ja"));
        Assert.False(back.EventSets[0].Parts.Last().Place);
        Assert.Equal(p.Events[1].OwnFlag, back.Events[1].OwnFlag);
        Assert.Equal(3, EventAuthoring.Check(back, Data.Events.Values).Count);
        var loaded = GameData.Load(ContentLibrary.Load(TestPaths.Content, path));
        foreach (var e in back.Events) Assert.Empty(EventCheck.Check(loaded, e));
        // 任意の画面確認用。指定した出力先へ、検証した同じデータをコピーする。
        if (Environment.GetEnvironmentVariable("GW_AUTHORING_EVIDENCE") is { Length: > 0 } output)
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(output, Path.GetRelativePath(path, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }
    }
}
