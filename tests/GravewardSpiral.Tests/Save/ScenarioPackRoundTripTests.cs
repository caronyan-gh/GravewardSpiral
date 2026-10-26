using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

/// <summary>エディタの保存で、読んだ中身を失わない・変えないこと(レビューの C01・C03〜C05)。</summary>
public class ScenarioPackRoundTripTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);

    private static string NewScenario(string root, string id, int floors = 3)
    {
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(5, floors, FloorSizeMode.Fixed(10, 10))).Dungeon;
        string folder = Path.Combine(root, id);
        ScenarioPack.New(folder, id, dungeon.Floors).Save(Content);
        return folder;
    }

    [Fact]
    public void AnUnreadableFileStopsTheSave()
    {
        using var dir = new TestPaths.TempDir();
        string folder = NewScenario(dir.Path, "broken");
        File.WriteAllText(Path.Combine(folder, "maps", "broken_b2.txt"), "これは迷宮ではない");
        File.WriteAllText(Path.Combine(folder, "items.json"), "[{ \"id\": ");
        var problems = new List<string>();
        var pack = ScenarioPack.Load(folder, problems);
        Assert.Contains("maps/broken_b2.txt", pack.LoadErrors);
        Assert.Contains("items.json", pack.LoadErrors);
        Assert.Throws<IOException>(() => pack.Save(Content));
        // 何も消えていない(B3 が詰められて B2 にならない、読めない items.json も残る)
        Assert.True(File.Exists(Path.Combine(folder, "maps", "broken_b3.txt")));
        Assert.Equal("[{ \"id\": ", File.ReadAllText(Path.Combine(folder, "items.json")));
        Assert.Throws<IOException>(() => pack.SaveAs(Path.Combine(dir.Path, "copy"), "copy", Content));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "copy")));
    }

    [Fact]
    public void NoNamedMonstersStaysNone()
    {
        using var dir = new TestPaths.TempDir();
        string folder = NewScenario(dir.Path, "quiet");
        var pack = ScenarioPack.Load(folder, []);
        pack.Named = [];   // いないことにする
        pack.Save(Content);
        var back = ScenarioPack.Load(folder, []);
        Assert.NotNull(back.Named);
        Assert.Empty(back.Named!);
    }

    [Fact]
    public void TheWorldOriginAndOwnEncounterSettingsSurviveASave()
    {
        using var dir = new TestPaths.TempDir();
        string folder = NewScenario(dir.Path, "kept");
        // 書き出した世界のように、由来とシードと固定のボスを持たせる。遭遇の設定も独自のものにする
        File.WriteAllText(Path.Combine(folder, "worlds.json"), """[{ "id": "kept", "seed": 42, "boss_picks": { "final": [{ "monster": "goblin", "count": 3 }] }, "text": { "prologue": { "en": "Once", "ja": "昔" } } }]""");
        var enc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "encounters.json")))!.AsArray();
        enc[0]!["cooldown"] = 123;
        File.WriteAllText(Path.Combine(folder, "encounters.json"), enc.ToJsonString());

        var pack = ScenarioPack.Load(folder, []);
        pack.FirstLevel = 3;
        pack.Save(Content);
        var worlds = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "worlds.json")))!.AsArray()[0]!;
        Assert.Equal(42, (int)worlds["seed"]!);
        Assert.Equal("昔", (string?)worlds["text"]!["prologue"]!["ja"]);
        // 固定のボスは、編集する主の組として bosses.json に移る(worlds.json に残すと編集の上に当たる。R04)
        Assert.Null(worlds["boss_picks"]);
        Assert.Equal("goblin", Assert.Single(ScenarioPack.Load(folder, []).BossGroups!).Monster);
        var encBack = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "encounters.json")))!.AsArray()[0]!;
        Assert.Equal(123, (int)encBack["cooldown"]!);
        Assert.Equal(3, (double)encBack["first_level"]!);
    }

    [Fact]
    public void AnEditedBossOfAnExportedWorldIsTheOneThatAppears()
    {
        // 書き出した世界の固定のボス(ゴブリン3体)を、エディタでオーガ1体に変えると、新しいゲームでもオーガ(再レビューの R04)
        using var dir = new TestPaths.TempDir();
        string folder = NewScenario(dir.Path, "fixed_boss");
        File.WriteAllText(Path.Combine(folder, "worlds.json"), """[{ "id": "fixed_boss", "seed": 42, "boss_picks": { "final": [{ "monster": "goblin", "count": 3 }] } }]""");
        var pack = ScenarioPack.Load(folder, []);
        pack.BossGroups = [new Core.Monsters.BossDef.BossGroup { Monster = "ogre", Count = 1 }];
        pack.Save(Content);

        var content = ContentLibrary.Load(TestPaths.Content, folder);
        var data = Core.Characters.GameData.Load(content);
        var session = GameSession.New(1, new GenerationRequest(1, 3, FloorSizeMode.Fixed(10, 10)), new Core.Maze.Dungeon("fixed_boss", pack.Floors));
        session.BossPicks[GameSession.FinalBossId] = [new Core.Monsters.BossDef.BossGroup { Monster = "wolf", Count = 2 }];   // 世界を作るときの抽選
        WorldPackDef.Of(content)?.ApplyBossPicks(session);
        Assert.Equal("ogre", session.Boss(data, GameSession.FinalBossId)!.Groups[0].Monster);
    }
}
