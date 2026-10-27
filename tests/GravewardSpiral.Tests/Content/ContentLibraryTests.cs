using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Tests.Content;

public class ContentLibraryTests
{
    private static void Manifest(TestPaths.TempDir dir, string id, string depends = "", int order = 0) =>
        dir.Write($"{id}/manifest.json", $$"""{"id": "{{id}}", "depends": [{{depends}}], "order": {{order}}}""");

    [Fact]
    public void ALanguageFileAddsALanguage()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "base");
        Manifest(dir, "fr_pack", "\"base\"");
        dir.Write("base/text/ja.json", """{"language.name": "日本語", "town.title": "町"}""");
        dir.Write("base/text/en.json", """{"language.name": "English", "town.title": "Town"}""");
        // 別のパックに言語のファイルを置くだけで、言語が増える(足りないキーは英語で出す)
        dir.Write("fr_pack/text/fr.json", """{"language.name": "Français"}""");
        var lib = ContentLibrary.Load(dir.Path);
        Assert.Equal(["ja", "en", "fr"], lib.Languages);
        Assert.Equal("Français", lib.Texts("fr")["language.name"]);
        Assert.False(lib.Texts("fr").ContainsKey("town.title"));
    }

    [Fact]
    public void LaterPackOverridesAndDisables()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "base");
        Manifest(dir, "mod", "\"base\"");
        dir.Write("base/monsters.json", """[{"id": "goblin", "hp": 7}, {"id": "orc", "hp": 15}, {"id": "rat", "hp": 1}]""");
        dir.Write("mod/monsters.json", """[{"id": "goblin", "hp": 9}, {"id": "rat", "disabled": true}, {"id": "imp", "hp": 10}]""");

        var lib = ContentLibrary.Load(dir.Path);

        Assert.True(lib.IsValid, string.Join("\n", lib.Problems));
        Assert.Equal(["base", "mod"], lib.Packs.Select(p => p.Id));
        Assert.Equal(9, lib.Find("monsters", "goblin")!.Data.GetProperty("hp").GetInt32());
        Assert.Equal("mod", lib.Find("monsters", "goblin")!.Pack.Id);
        Assert.Null(lib.Find("monsters", "rat"));
        Assert.Equal(["goblin", "imp", "orc"], lib.All("monsters").Select(i => i.Id).Order());
    }

    [Fact]
    public void DependencyDecidesOrderEvenWhenNamesSortOtherwise()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "aaa_mod", "\"zzz_base\"");
        Manifest(dir, "zzz_base");
        dir.Write("zzz_base/items.json", """[{"id": "sword", "price": 10}]""");
        dir.Write("aaa_mod/items.json", """[{"id": "sword", "price": 99}]""");

        var lib = ContentLibrary.Load(dir.Path);

        Assert.Equal(["zzz_base", "aaa_mod"], lib.Packs.Select(p => p.Id));
        Assert.Equal(99, lib.Find("items", "sword")!.Data.GetProperty("price").GetInt32());
    }

    [Fact]
    public void ReportsProblems()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "base");
        Manifest(dir, "broken", "\"missing\"");
        dir.Write("base/monsters.json", """[{"id": "goblin"}, {"id": "goblin"}, {"name": "no id"}]""");
        dir.Write("base/items.json", """{"id": "not an array"}""");
        dir.Write("base/spells.json", """[{"id": "fire" """);

        var lib = ContentLibrary.Load(dir.Path);

        Assert.False(lib.IsValid);
        Assert.Contains(lib.Problems, p => p.Contains("'goblin' が2回"));
        Assert.Contains(lib.Problems, p => p.Contains("2 番目") || p.Contains("3 番目"));
        Assert.Contains(lib.Problems, p => p.Contains("items.json") && p.Contains("配列"));
        Assert.Contains(lib.Problems, p => p.Contains("spells.json") && p.Contains("読めません"));
        Assert.Contains(lib.Problems, p => p.Contains("'missing' がありません"));
    }

    [Fact]
    public void DependencyCycleIsReported()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "a", "\"b\"");
        Manifest(dir, "b", "\"a\"");
        var lib = ContentLibrary.Load(dir.Path);
        Assert.Contains(lib.Problems, p => p.Contains("輪"));
    }

    [Fact]
    public void TextsMergeWithLaterPackWinning()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "base");
        Manifest(dir, "mod", "\"base\"");
        dir.Write("base/text/ja.json", """{"a": "あ", "b": "い"}""");
        dir.Write("mod/text/ja.json", """{"b": "う"}""");
        var texts = ContentLibrary.Load(dir.Path).Texts("ja");
        Assert.Equal("あ", texts["a"]);
        Assert.Equal("う", texts["b"]);
    }

    [Fact]
    public void ReferenceCheckFindsMissingTargets()
    {
        using var dir = new TestPaths.TempDir();
        Manifest(dir, "base");
        dir.Write("base/monsters.json", """[{"id": "goblin", "drop": "gem"}]""");
        dir.Write("base/items.json", """[{"id": "sword"}]""");
        var lib = ContentLibrary.Load(dir.Path);
        var goblin = lib.Find("monsters", "goblin")!;
        lib.RequireReference(goblin, "drop", "items", goblin.Data.GetProperty("drop").GetString());
        Assert.Contains(lib.Problems, p => p.Contains("'gem' がありません"));
    }

    [Fact]
    public void LocalizedTextFallsBackToEnglish()
    {
        var both = System.Text.Json.JsonSerializer.Deserialize<LocalizedText>("""{"en": "Goblin", "ja": "ゴブリン"}""", ContentLibrary.JsonOptions)!;
        var enOnly = System.Text.Json.JsonSerializer.Deserialize<LocalizedText>("\"Owlbear\"", ContentLibrary.JsonOptions)!;
        Assert.Equal("ゴブリン", both.Get("ja"));
        Assert.Equal("Owlbear", enOnly.Get("ja"));
    }

    [Fact]
    public void RepositoryContentLoadsCleanly()
    {
        var lib = ContentLibrary.Load(TestPaths.Content);
        DungeonLoader.ValidateAll(lib);
        Assert.True(lib.IsValid, string.Join("\n", lib.Problems));
        Assert.Contains(lib.Packs, p => p.Id == "srd");
        Assert.NotNull(lib.Find(DungeonLoader.Collection, "devtest"));
    }

    [Fact]
    public void ACopyCanTurnOffWhatTheOriginalHas()
    {
        // 写したモンスターで "spellcasting": null と書けば、元が呪文を唱えても、写したものは唱えない(エディタの「呪文を唱える」を外したとき)
        using var dir = new TestPaths.TempDir();
        var baseMage = Data.Monsters.Values.First(m => m.Spellcasting != null && m.Usable);
        dir.Write("copy/manifest.json", """{"id": "copy", "depends": ["srd"]}""");
        dir.Write("copy/monsters.json", $$"""[{"id": "quiet_mage", "extends": "{{baseMage.Id}}", "spellcasting": null}]""");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "copy"));
        var data = GameData.Load(content);
        Assert.Null(data.Monsters["quiet_mage"].Spellcasting);
        Assert.NotNull(data.Monsters[baseMage.Id].Spellcasting);
    }

    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));
}
