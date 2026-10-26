using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

/// <summary>id を変える・階を消す・動かすで、参照が一緒に直ること(レビューの C06〜C08、C10)。</summary>
public class ScenarioEditTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);

    private static ScenarioPack Pack(string dir, int floors = 3)
    {
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(7, floors, FloorSizeMode.Fixed(10, 10))).Dungeon;
        return ScenarioPack.New(Path.Combine(dir, "edit"), "edit", dungeon.Floors);
    }

    private static EventDef Ev(string json) => System.Text.Json.JsonSerializer.Deserialize<EventDef>(json, ContentLibrary.JsonOptions)!;

    [Fact]
    public void RenamingAnEventFixesDoneConditionsEverywhere()
    {
        using var dir = new TestPaths.TempDir();
        var pack = Pack(dir.Path);
        pack.Events.Add(Ev("""{ "id": "a", "steps": [] }"""));
        pack.Events.Add(Ev("""{ "id": "b", "when": [{ "type": "done", "id": "a" }], "steps": [{ "type": "choice", "options": [{ "when": [{ "type": "done", "id": "a" }], "steps": [{ "type": "run", "id": "a" }] }] }] }"""));
        pack.Rumors.Add(new RumorDef { Id = "r", When = [new EventCondition { Type = "done", Id = "a" }] });
        ScenarioEdit.RenameEvent(pack, "a", "a2");
        var b = pack.Events.Single(e => e.Id == "b");
        Assert.Equal("a2", b.When[0].Id);
        Assert.Equal("a2", b.Steps[0].Options[0].When[0].Id);
        Assert.Equal("a2", b.Steps[0].Options[0].Steps[0].Id);
        Assert.Equal("a2", pack.Rumors[0].When[0].Id);
        Assert.Contains(pack.Events, e => e.Id == "a2");
    }

    [Fact]
    public void RenamingAMonsterFixesBossesAndEventCombats()
    {
        using var dir = new TestPaths.TempDir();
        var pack = Pack(dir.Path);
        pack.Monsters.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = "edit_ogre", ["extends"] = "ogre" });
        pack.RosterOf(0).Add("edit_ogre");
        pack.BossGroups = [new BossDef.BossGroup { Monster = "edit_ogre", Count = 1 }];
        pack.Bosses.Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "id": "warden", "groups": [{ "monster": "edit_ogre", "count": 2 }] }""")!.AsObject());
        pack.Events.Add(Ev("""{ "id": "fight", "steps": [{ "type": "combat", "groups": [{ "monster": "edit_ogre", "count": 1 }] }] }"""));
        ScenarioEdit.RenameMonster(pack, "edit_ogre", "edit_brute");
        Assert.Equal("edit_brute", (string?)pack.Monsters[0]["id"]);
        Assert.Equal("edit_brute", pack.Rosters[0][0]);
        Assert.Equal("edit_brute", pack.BossGroups![0].Monster);
        Assert.Equal("edit_brute", (string?)pack.Bosses[0]["groups"]![0]!["monster"]);
        Assert.Equal("edit_brute", pack.Events[0].Steps[0].Groups[0].Monster);
    }

    [Fact]
    public void RemovingAFloorMovesWhatPointsBelowIt()
    {
        using var dir = new TestPaths.TempDir();
        var pack = Pack(dir.Path, 4);
        pack.Named = [new NamedSpawn { Id = "a", Floor = 0 }, new NamedSpawn { Id = "b", Floor = 1 }, new NamedSpawn { Id = "c", Floor = 3 }];
        var cell = pack.Floors[0].AllCells().First(p => pack.Floors[0][p].Feature == CellFeature.None && pack.Floors[0][p].Mark == null);
        pack.Floors[0][cell].Feature = CellFeature.Teleporter;
        pack.Floors[0][cell].Target = new WarpTarget(3, cell);
        ScenarioEdit.RemoveFloor(pack, 1);   // B2 を消す
        Assert.Equal(3, pack.Floors.Count);
        Assert.Equal(["a", "c"], pack.Named!.Select(n => n.Id));
        Assert.Equal(2, pack.Named.Single(n => n.Id == "c").Floor);   // B4 → B3
        Assert.Equal(2, pack.Floors[0][cell].Target!.Value.Floor);
    }

    [Fact]
    public void MovingAFloorCarriesItsNamedMonstersAndTargets()
    {
        using var dir = new TestPaths.TempDir();
        var pack = Pack(dir.Path, 3);
        pack.Named = [new NamedSpawn { Id = "a", Floor = 0 }, new NamedSpawn { Id = "c", Floor = 2 }];
        var top = pack.Floors[0];
        ScenarioEdit.MoveFloor(pack, 0, 1);   // B1 を B2 へ
        Assert.Same(top, pack.Floors[1]);
        Assert.Equal(1, pack.Named!.Single(n => n.Id == "a").Floor);
        Assert.Equal(2, pack.Named.Single(n => n.Id == "c").Floor);
    }

    [Fact]
    public void AScenarioSpellWithoutANameIsAProblemNotACrash()
    {
        using var dir = new TestPaths.TempDir();
        var pack = Pack(dir.Path);
        pack.Spells.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = "fireball" });
        pack.Save(Content);
        var content = ContentLibrary.Load(TestPaths.Content, pack.Directory);
        Assert.Contains(content.Problems, p => p.Contains("fireball") && p.Contains("name"));
    }
}
