using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

public class OriginalItemsTests
{
    /// <summary>オリジナルの品を1つ持つシナリオ(名前だけ変えたロングソード)。</summary>
    private static string Scenario(TestPaths.TempDir dir, string id, string guid, string itemName, string extra = "")
    {
        dir.Write($"{id}/manifest.json", $$"""
            {"id": "{{id}}", "kind": "scenario", "name": {"en": "{{id}}"}, "version": "1.0", "format": 1, "guid": "{{guid}}", "depends": ["srd"]}
            """);
        dir.Write($"{id}/items.json", $$"""
            [{"id": "brand", "extends": "longsword", "name": {"en": "{{itemName}}", "ja": "{{itemName}}"}{{extra}}}]
            """);
        return Path.Combine(dir.Path, id);
    }

    private static GameSession Session(GameData data, string item)
    {
        var request = new GenerationRequest(5, 1, FloorSizeMode.Fixed(8, 8));
        var s = GameSession.New(1, request, new MazeGenerator(MazeGenSettings.Load(ContentLibrary.Load(TestPaths.Content))).Generate(request).Dungeon);
        s.ClanId = "clanA";
        var c = CharacterFactory.QuickCreate(data, "A", "human", "fighter");
        c.Inventory.Items.Add(ItemInstance.Of(item));
        s.Roster.Characters.Add(c);
        s.Roster.AddToParty(c);
        return s;
    }

    [Fact]
    public void ExtendsCopiesTheBaseAndChangesOnlyWhatIsWritten()
    {
        using var dir = new TestPaths.TempDir();
        var content = ContentLibrary.Load(TestPaths.Content, Scenario(dir, "tale_a", "aaaa", "Brand of A"));
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        var brand = data.Items["brand"];
        var sword = data.Items["longsword"];
        Assert.Equal("Brand of A", brand.Name.Get("en"));
        Assert.Equal(sword.Kind, brand.Kind);
        Assert.Equal(sword.Price, brand.Price);
        Assert.Equal(("tale_a", "aaaa"), (brand.OriginScenario, brand.OriginGuid));   // シナリオで定義した品は、出どころを覚える
        Assert.Null(sword.OriginGuid);   // 標準のパックの品はオリジナルではない
    }

    [Fact]
    public void AnOriginalItemSurvivesLeavingOrDeletingItsScenario()
    {
        using var dir = new TestPaths.TempDir();
        string a = Scenario(dir, "tale_a", "aaaa", "Brand of A");
        string b = Scenario(dir, "tale_b", "bbbb", "Brand of B", ", \"bonus\": 3");
        var dataA = GameData.Load(ContentLibrary.Load(TestPaths.Content, a));
        var s = Session(dataA, "brand");
        var it = s.Roster.Characters[0].Inventory.Items[0];
        OriginalItems.Snapshot(dataA, OriginalItems.All(s));
        Assert.Equal("aaaa", it.Origin!.Guid);
        var store = new SaveStore(Path.Combine(dir.Path, "saves"));
        store.Save(s, ContentLibrary.Load(TestPaths.Content, a));

        // シナリオの無い世界(や、消したあと): 写しを「GUID:id」で使う
        var plain = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        var loaded = store.Load("clanA", s.WorldKey);
        var item = loaded.Roster.Characters[0].Inventory.Items[0];
        OriginalItems.Attach(plain, OriginalItems.All(loaded));
        Assert.Equal("aaaa:brand", item.ItemId);
        Assert.Equal("Brand of A", plain.Items[item.ItemId].Name.Get("en"));
        Assert.Equal(plain.Items["longsword"].Kind, plain.Items[item.ItemId].Kind);

        // 同じ id の品を持つ別のシナリオでも、混ざらない
        var dataB = GameData.Load(ContentLibrary.Load(TestPaths.Content, b));
        var inB = store.Load("clanA", s.WorldKey);
        var itemB = inB.Roster.Characters[0].Inventory.Items[0];
        OriginalItems.Attach(dataB, OriginalItems.All(inB));
        Assert.Equal("aaaa:brand", itemB.ItemId);
        Assert.Equal("Brand of A", dataB.Items[itemB.ItemId].Name.Get("en"));
        Assert.Equal(3, dataB.Items["brand"].Bonus);

        // 出どころのシナリオに戻れば、パックの品に戻る(書き直して、もう一度読んでも同じ)
        OriginalItems.Snapshot(dataB, OriginalItems.All(inB));
        Assert.Equal("brand", itemB.Origin!.Id);
        var dataA2 = GameData.Load(ContentLibrary.Load(TestPaths.Content, a));
        OriginalItems.Attach(dataA2, [itemB]);
        Assert.Equal("brand", itemB.ItemId);
    }

    [Fact]
    public void AnItemWithNoDataAnywhereBecomesALostItem()
    {
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        var s = Session(data, "longsword");
        var ghost = ItemInstance.Of("no_such_item");
        s.Roster.Characters[0].Inventory.Items.Add(ghost);
        OriginalItems.Attach(data, OriginalItems.All(s));
        Assert.Contains("失われた品", data.Items["no_such_item"].Name.Get("ja"));
    }

    [Fact]
    public void ScenarioSpellsCanOnlyBeRenamed()
    {
        using var dir = new TestPaths.TempDir();
        string pack = Scenario(dir, "tale_s", "ssss", "Brand");
        dir.Write("tale_s/spells.json", """[{"id": "fire_bolt", "name": {"en": "Ember Dart", "ja": "熾火の矢"}}]""");
        var content = ContentLibrary.Load(TestPaths.Content, pack);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        var plain = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        Assert.Equal("熾火の矢", data.Spells["fire_bolt"].Name.Get("ja"));
        Assert.Equal(plain.Spells["fire_bolt"].Level, data.Spells["fire_bolt"].Level);   // 効果は標準のまま

        // 新しい呪文と、名前のほかの項目は作れない
        dir.Write("tale_s/spells.json", """[{"id": "my_new_spell", "name": {"en": "New"}}, {"id": "fire_bolt", "name": {"en": "X"}, "level": 9}]""");
        var bad = ContentLibrary.Load(TestPaths.Content, pack);
        Assert.Contains(bad.Problems, p => p.Contains("my_new_spell"));
        Assert.Contains(bad.Problems, p => p.Contains("fire_bolt") && p.Contains("名前のほか"));
    }

    [Fact]
    public void APackFromANewerGameIsNotRead()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("future/manifest.json", $$"""
            {"id": "future", "kind": "scenario", "version": "1.0", "format": {{ContentFormat.Current + 1}}, "depends": ["srd"]}
            """);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "future"));
        Assert.Contains(content.Problems, p => p.Contains("新しい"));
    }
}
