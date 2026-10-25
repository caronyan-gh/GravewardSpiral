using System.IO.Compression;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Content;

public class ScenarioTests
{
    private static readonly ContentLibrary Base = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Base);

    /// <summary>小さなシナリオのパックを作る(階のテーマと文言を1つずつ上書きする)。</summary>
    private static string MakeScenario(string dir, string id, string version = "1.0")
    {
        Directory.CreateDirectory(Path.Combine(dir, "text"));
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""
            { "id": "{{id}}", "kind": "scenario", "name": { "en": "Test", "ja": "試しのシナリオ" }, "version": "{{version}}",
              "author": "tester", "levels": [5, 8], "depends": ["srd"], "order": 200 }
            """);
        File.WriteAllText(Path.Combine(dir, "floor_themes.json"), """
            [ { "id": "b_sunken", "min_depth": 0, "max_depth": 1, "name": { "en": "Sunken Market", "ja": "沈んだ市場" }, "types": { "undead": 2 } } ]
            """);
        File.WriteAllText(Path.Combine(dir, "text", "ja.json"), """{ "town.title": "沈んだ王都" }""");
        return dir;
    }

    [Fact]
    public void ScenariosAreFoundAsFoldersAndZips()
    {
        using var tmp = new TestPaths.TempDir();
        string search = Path.Combine(tmp.Path, "Scenarios");
        MakeScenario(Path.Combine(search, "b_folder"), "b_folder");
        string src = MakeScenario(Path.Combine(tmp.Path, "src", "b_zip"), "b_zip", "2.0");
        ZipFile.CreateFromDirectory(src, Path.Combine(search, "b_zip.zip"), CompressionLevel.Fastest, includeBaseDirectory: true);
        Directory.CreateDirectory(Path.Combine(search, "not_a_pack"));

        var problems = new List<string>();
        var found = ScenarioCatalog.Find([search], Path.Combine(tmp.Path, "cache"), problems);
        Assert.Empty(problems);
        Assert.Equal(["b_folder", "b_zip"], found.Select(s => s.Id));
        var zip = found.Single(s => s.Id == "b_zip");
        Assert.Equal("2.0", zip.Manifest.Version);
        Assert.Equal([5, 8], zip.Manifest.Levels);
        Assert.True(File.Exists(Path.Combine(zip.Directory, "floor_themes.json")));

        // 2回目は展開し直さない
        var again = ScenarioCatalog.Find([search], Path.Combine(tmp.Path, "cache"));
        Assert.Equal(zip.Directory, again.Single(s => s.Id == "b_zip").Directory);
    }

    [Fact]
    public void ZipsCannotWriteOutsideTheCache()
    {
        using var tmp = new TestPaths.TempDir();
        string zip = Path.Combine(tmp.Path, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(archive.CreateEntry("../escaped.txt").Open());
            w.Write("x");
        }
        Assert.Throws<InvalidDataException>(() => ScenarioCatalog.Extract(zip, Path.Combine(tmp.Path, "cache")));
        Assert.False(File.Exists(Path.Combine(tmp.Path, "escaped.txt")));
    }

    [Fact]
    public void AScenarioLaysOverTheBasePacks()
    {
        using var tmp = new TestPaths.TempDir();
        string dir = MakeScenario(Path.Combine(tmp.Path, "b"), "b_test");
        var lib = ContentLibrary.Load(TestPaths.Content, dir);
        Assert.True(lib.IsValid, string.Join("\n", lib.Problems));
        Assert.Equal("b_test", lib.Scenario!.Id);
        Assert.Equal("沈んだ王都", lib.Texts("ja")["town.title"]);
        var data = GameData.Load(lib);
        Assert.Contains("b_sunken", data.FloorThemes.Keys);
        Assert.Contains("goblin", data.Monsters.Keys);   // 標準のパックのデータも使える
        Assert.Null(Base.Scenario);
    }

    [Fact]
    public void ClansCarryTheRosterIntoANewGame()
    {
        using var tmp = new TestPaths.TempDir();
        var settings = MazeGenSettings.Load(Base);
        var request = new GenerationRequest(3, 2, FloorSizeMode.Fixed(10, 10));
        var first = GameSession.New(1, request, new MazeGenerator(settings).Generate(request).Dungeon);
        foreach (var cls in new[] { "fighter", "cleric", "wizard", "rogue" })
        {
            var c = CharacterFactory.QuickCreate(Data, cls, "human", cls, 5);
            c.Gold = 777;
            first.Roster.Characters.Add(c);
            if (cls != "rogue") first.Roster.AddToParty(c);   // ローグは控え
        }
        first.Roster.PartyMembers.First().Inventory.TryAdd(ItemInstance.Of("mithral_arrows", 20), Data.Items);
        var lost = CharacterFactory.QuickCreate(Data, "lost", "human", "fighter");
        first.Roster.Characters.Add(lost);
        first.Roster.LeftInMaze[lost.Id] = first.EnterMaze().Position;   // 迷宮に残った人は持ち出さない
        first.Flags.Add("cleared");

        string path = ClanFile.From(first, "灰の鐘団", "srd").Save(tmp.Path);
        Assert.EndsWith(ClanFile.Extension, path);
        var listed = ClanFile.List(tmp.Path);
        Assert.Single(listed);
        Assert.True(listed[0].Clan.Cleared);

        var second = GameSession.New(2, request, new MazeGenerator(settings).Generate(request).Dungeon);
        listed[0].Clan.ApplyTo(second);
        Assert.Equal(4, second.Roster.Characters.Count);
        Assert.Equal(3, second.Roster.Party.Count);
        Assert.All(second.Roster.Characters, c => Assert.Equal(5, c.Level));
        Assert.All(second.Roster.Characters, c => Assert.Equal(777, c.Gold));
        Assert.Contains(second.Roster.PartyMembers.First().Inventory.Items, i => i.ItemId == "mithral_arrows" && i.Count == 20);
        Assert.DoesNotContain(second.Roster.Characters, c => c.Name == "lost");
        // 写しなので、新しいゲームで変えても元のクランは変わらない
        second.Roster.Characters[0].Gold = 0;
        Assert.Equal(777, ClanFile.Load(path).Characters[0].Gold);
    }

    [Fact]
    public void ClanHouseStagesAddFacilitiesThenOnlyTheStorehouseGrows()
    {
        var levels = ClanHouseLevel.Load(Base);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, levels.Select(l => l.Level));
        var house = new ClanHouse();
        var c = CharacterFactory.QuickCreate(Data, "A", "human", "fighter");
        c.Gold = 10_000_000;
        Assert.False(house.Has(levels, ClanHouseLevel.Bed));
        Assert.Null(house.ExpandCost(levels));   // 最後の段階までは広げられない
        Assert.Equal(20, house.RosterLimit(levels));
        Assert.True(house.Upgrade([c], levels));   // 小さな家(金だけ)
        Assert.False(house.Upgrade([c], levels));   // 石造りの館には礎石が要る
        c.Inventory.Items.Add(ItemInstance.Of("obsidian_cornerstone", 2));
        house.Storage.Add(ItemInstance.Of("obsidian_cornerstone", 1));   // 倉庫の分も数える
        Assert.Equal(3, house.Count([c], "obsidian_cornerstone"));
        Assert.True(house.Upgrade([c], levels));
        Assert.Equal(0, house.Count([c], "obsidian_cornerstone"));   // 使った
        house.Storage.Add(ItemInstance.Of("consecrated_bell"));
        house.Storage.Add(ItemInstance.Of("ancient_dragon_scale", 3));
        while (house.Next(levels) != null) Assert.True(house.Upgrade([c], levels));
        Assert.True(house.Has(levels, ClanHouseLevel.Hall) && house.Has(levels, ClanHouseLevel.Library) && house.Has(levels, ClanHouseLevel.BedFull));
        Assert.Equal(30, house.RosterLimit(levels));
        Assert.Equal(50, house.Price(levels, ClanHouseLevel.Chapel, 100));   // 礼拝堂で半額
        int cap = house.Capacity(levels);
        long first = house.ExpandCost(levels)!.Value;
        Assert.True(house.Expand([c], levels));
        Assert.Equal(cap + 20, house.Capacity(levels));
        Assert.True(house.ExpandCost(levels) > first);   // だんだん高くなる
    }

    [Fact]
    public void ClanHouseStoresItemsAndTravelsWithTheClan()
    {
        var levels = ClanHouseLevel.Load(Base).Take(1).ToList();   // 最初のぼろ小屋だけで確かめる
        Assert.Equal("shack", levels[0].Id);
        var house = new ClanHouse();
        int cap = house.Capacity(levels);
        Assert.True(cap > 0);
        Assert.True(house.Deposit(ItemInstance.Of("iron_arrows", 20), Data.Items, levels));
        Assert.True(house.Deposit(ItemInstance.Of("iron_arrows", 30), Data.Items, levels));
        Assert.Single(house.Storage);   // 重ねられる品は1枠
        for (int i = 1; i < cap; i++) Assert.True(house.Deposit(ItemInstance.Of("longsword"), Data.Items, levels));
        Assert.False(house.Deposit(ItemInstance.Of("longsword"), Data.Items, levels));   // いっぱい
        Assert.Null(house.Next(levels));

        // 次の段階があれば、金を払って建て直す
        List<ClanHouseLevel> more = [.. levels, new ClanHouseLevel { Id = "lodge", Level = 2, Cost = 500, StorageSlots = cap + 10, Facilities = ["inn"] }];
        var c = CharacterFactory.QuickCreate(Data, "A", "human", "fighter");
        c.Gold = 499;
        Assert.False(house.Upgrade([c], more));
        c.Gold = 600;
        Assert.True(house.Upgrade([c], more));
        Assert.Equal(100, c.Gold);
        Assert.True(house.Has(more, "inn"));
        Assert.True(house.Deposit(ItemInstance.Of("longsword"), Data.Items, more));

        // クランファイルと一緒に持ち歩く
        using var tmp = new TestPaths.TempDir();
        var settings = MazeGenSettings.Load(Base);
        var request = new GenerationRequest(4, 2, FloorSizeMode.Fixed(10, 10));
        var s1 = GameSession.New(1, request, new MazeGenerator(settings).Generate(request).Dungeon);
        s1.Roster.Characters.Add(c);
        s1.ClanHouse = house;
        s1.ClanName = "倉庫番";
        ClanFile.From(s1, "倉庫番", null).Save(tmp.Path);
        var s2 = GameSession.New(2, request, new MazeGenerator(settings).Generate(request).Dungeon);
        ClanFile.List(tmp.Path)[0].Clan.ApplyTo(s2);
        Assert.Equal(2, s2.ClanHouse.Level);
        Assert.Equal(house.Storage.Count, s2.ClanHouse.Storage.Count);
        Assert.Equal("倉庫番", s2.ClanName);
    }
}
