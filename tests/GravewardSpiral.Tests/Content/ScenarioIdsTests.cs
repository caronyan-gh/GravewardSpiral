using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Content;

public class ScenarioIdsTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    [Fact]
    public void TheScenarioDiffIsLaidOverTheBase()
    {
        using var tmp = new TestPaths.TempDir();
        string sc = Path.Combine(tmp.Path, "sc");
        Directory.CreateDirectory(sc);
        File.WriteAllText(Path.Combine(sc, "classes.json"), """[ { "id": "samurai", "extends": "fighter" }, { "id": "bard", "disabled": true } ]""");
        var ids = ScenarioIds.Read(Path.Combine(TestPaths.Content, "srd"), sc);
        Assert.Contains("samurai", ids.Classes);       // 足した
        Assert.DoesNotContain("bard", ids.Classes);    // 外した
        Assert.Contains("fighter", ids.Classes);       // 書いていない土台はそのまま
        Assert.Equal(Data.Races.Keys.Order(), ids.Races.Order());
        Assert.Equal(Data.Spells.Keys.Order(), ids.Spells.Order());

        // そのクラスの人がいるクランは入れない
        var bard = CharacterFactory.QuickCreate(Data, "B", "human", "bard");
        var fighter = CharacterFactory.QuickCreate(Data, "F", "human", "fighter");
        var info = new ScenarioInfo(new PackManifest { Id = "sc", Kind = ScenarioCatalog.Kind, AllowImport = true }, sc, sc);
        Assert.True(ClanImport.CanEnter(info, ids, [], null, [fighter], _ => null));
        Assert.False(ClanImport.CanEnter(info, ids, [], null, [fighter, bard], _ => null));
        // 連れて入れないシナリオ(シリーズが違う)も入れない
        var closed = info with { Manifest = new PackManifest { Id = "sc", Kind = ScenarioCatalog.Kind, AllowImport = false, Series = "wiz" } };
        Assert.False(ClanImport.CanEnter(closed, ids, ["other"], null, [fighter], _ => null));
    }
}
