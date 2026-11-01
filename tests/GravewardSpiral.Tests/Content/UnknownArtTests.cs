using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Content;

/// <summary>正体が分かる前の絵の種類。人型のうち亜人(SRD の分類)は demi_humanoid、人は種類の絵(humanoid)のまま。</summary>
public class UnknownArtTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    [Theory]
    [InlineData("goblin", "demi_humanoid")]
    [InlineData("orc", "demi_humanoid")]
    [InlineData("gnoll", "demi_humanoid")]
    [InlineData("kobold", "demi_humanoid")]
    [InlineData("bandit", null)]
    [InlineData("guard", null)]
    [InlineData("wolf", null)]
    public void DemiHumanoidsHaveTheirOwnShadow(string id, string? art)
    {
        Assert.Equal(art, Data.Monsters[id].UnknownArt);
        Assert.Equal(id == "wolf" ? "beast" : "humanoid", Data.Monsters[id].CreatureType);
    }

    [Fact]
    public void AScenarioMonsterKeepsItsShadowThroughTheEditor()
    {
        // エディタが読んで書き直しても unknown_art は残り、ゲームのデータでもその絵の種類になる
        using var dir = new TestPaths.TempDir();
        var content = ContentLibrary.Load(TestPaths.Content);
        var floors = new MazeGenerator(MazeGenSettings.Load(content)).Generate(new GenerationRequest(3, 2, FloorSizeMode.Fixed(10, 10))).Dungeon.Floors;
        string folder = Path.Combine(dir.Path, "demi");
        ScenarioPack.New(folder, "demi", floors).Save(content);
        File.WriteAllText(Path.Combine(folder, "monsters.json"),
            """[{"id": "bog_thing", "extends": "bandit", "name": {"en": "Bog Thing", "ja": "沼の何か"}, "unknown_art": "demi_humanoid"}]""");
        ScenarioPack.Load(folder, []).Save(content);
        Assert.Contains("demi_humanoid", File.ReadAllText(Path.Combine(folder, "monsters.json")));
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, folder));
        Assert.Equal("demi_humanoid", data.Monsters["bog_thing"].UnknownArt);
        Assert.Null(data.Monsters["bandit"].UnknownArt);
    }
}
