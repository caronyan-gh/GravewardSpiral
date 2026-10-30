using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

/// <summary>クランを連れて入れるか(allow_import と series)と、連れていく人のクラス・種族・呪文が入る先にあるか。</summary>
public class ClanImportTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    private static readonly Dictionary<string, string?> SeriesOf = new() { ["wiz1"] = "wiz", ["wiz2"] = "wiz", ["other"] = null };

    private static bool Allows(PackManifest? to, params string?[] played) => ClanImport.Allows(to, played, id => SeriesOf.GetValueOrDefault(id));

    [Fact]
    public void SeriesLetsOnlyClansOfThatSeriesIn()
    {
        var wiz3 = new PackManifest { Id = "wiz3", AllowImport = false, Series = "wiz" };
        Assert.True(Allows(wiz3, "wiz1", "wiz2"));
        Assert.True(Allows(wiz3, "wiz3"));            // 自分の世界をまた始める
        Assert.False(Allows(wiz3, "wiz1", "other"));  // ほかのシナリオでも遊んだクラン
        Assert.False(Allows(wiz3, "wiz1", null));     // 自由な迷宮でも遊んだクラン
        Assert.False(Allows(wiz3));                   // どこでも遊んでいない
        Assert.True(ClanImport.Any(wiz3));

        var closed = new PackManifest { Id = "solo", AllowImport = false };
        Assert.False(Allows(closed, "wiz1"));
        Assert.False(ClanImport.Any(closed));
        Assert.True(Allows(new PackManifest { Id = "open" }, "other", null));   // allow_import ならどのクランでも
        Assert.True(Allows(null, "wiz1"));   // 自由な迷宮
    }

    [Fact]
    public void MissingClassesRacesAndSpellsAreListed()
    {
        var c = CharacterFactory.QuickCreate(Data, "Sam", "human", "fighter", 3);
        Assert.Empty(ClanImport.Missing(Data, [c]));
        c.Classes.Add(new ClassLevel { ClassId = "samurai", Level = 1 });
        c.Book("samurai").Known.Add("halito");
        var missing = ClanImport.Missing(Data, [c]);
        Assert.Contains("Sam: class 'samurai'", missing);
        Assert.Contains("Sam: spell 'halito'", missing);
    }

    [Fact]
    public void TheManifestReadsSeries()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("s/manifest.json", """{"id": "s", "kind": "scenario", "depends": ["srd"], "allow_import": false, "series": "wiz"}""");
        var m = ContentLibrary.ReadManifest(Path.Combine(dir.Path, "s"), out _);
        Assert.Equal("wiz", m!.Series);
        Assert.False(m.AllowImport);
    }
}
