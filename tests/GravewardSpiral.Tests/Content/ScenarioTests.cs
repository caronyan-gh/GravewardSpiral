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

    /// <summary>小さなシナリオのパックを作る(階のテーマと文言を1つずつ上書きする)。dungeon が false なら、迷宮の無いシナリオ(一覧に出ない)。</summary>
    private static string MakeScenario(string dir, string id, string version = "1.0", bool dungeon = true)
    {
        Directory.CreateDirectory(Path.Combine(dir, "text"));
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""
            { "id": "{{id}}", "kind": "scenario", "name": { "en": "Test", "ja": "試しのシナリオ" }, "version": "{{version}}",
              "author": "tester", "levels": [5, 8], "depends": ["srd"], "order": 200{{(dungeon ? $", \"dungeon\": \"{id}\"" : "")}} }
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
    public void AScenarioWithoutAMazeIsNotListed()
    {
        // シナリオは迷宮まで決まった世界(決めたこと #91・#104)。毎回迷宮を作るのは、シナリオなしの自由な迷宮だけ
        using var tmp = new TestPaths.TempDir();
        string search = Path.Combine(tmp.Path, "Scenarios");
        MakeScenario(Path.Combine(search, "with_maze"), "with_maze");
        MakeScenario(Path.Combine(search, "no_maze"), "no_maze", dungeon: false);
        var problems = new List<string>();
        var found = ScenarioCatalog.Find([search], Path.Combine(tmp.Path, "cache"), problems);
        Assert.Equal(["with_maze"], found.Select(s => s.Id));
        Assert.Contains(problems, p => p.StartsWith("no_maze") && p.Contains("迷宮がありません"));
    }

    [Fact]
    public void RequiredClearsSurviveTheEditorSave()
    {
        using var tmp = new TestPaths.TempDir();
        string dir = Path.Combine(tmp.Path, "b_req");
        var pack = ScenarioPack.New(dir, "b_req", new MazeGenerator(MazeGenSettings.Load(Base)).Generate(new GenerationRequest(7, 1, FloorSizeMode.Fixed(10, 10))).Dungeon.Floors);
        pack.Manifest.RequiresClear.Add(new RequiredClear { Guid = "g-1", Id = "gravekeeper", Name = LocalizedText.Of("Part 1", "第1部") });
        pack.Save(Base);
        var m = ContentLibrary.ReadManifest(dir, out var error);
        Assert.Null(error);
        var r = Assert.Single(m!.RequiresClear);
        Assert.Equal(("g-1", "gravekeeper", "第1部"), (r.Guid, r.Id, r.Name!.Get("ja")));
    }

    [Fact]
    public void AScenarioPlaysTracksFromTheStandardShelf()
    {
        // 第2部は自分の曲のファイルを持たず、標準の棚の曲を割り当てる(B4 は B3 と同じ曲、イベントの子守歌も)
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(TestPaths.Content, "scenarios", "sunken"));
        Assert.True(content.IsValid, string.Join(" / ", content.Problems));
        Assert.EndsWith("liturgy_of_the_drowned.mp3", content.FindFile("audio/bgm/maze_b4.mp3"));
        Assert.EndsWith("sleep_in_the_tide.mp3", content.FindFile("audio/bgm/lullaby.mp3"));
        Assert.EndsWith("where_the_water_meets_the_reed.mp3", content.FindFile("audio/bgm/town.mp3"));
        Assert.Null(content.FindFile("audio/bgm/maze_b2.mp3"));   // 割り当てていない階は、元の曲(maze)のまま
        // 標準のパックだけなら、棚に置いただけの曲は流れない
        Assert.EndsWith("srd/audio/bgm/town.mp3", Base.FindFile("audio/bgm/town.mp3")!.Replace(Path.DirectorySeparatorChar, '/'));
        Assert.Null(Base.FindFile("audio/bgm/maze_b3.mp3"));
        Assert.Equal(26, Base.All(MusicTrack.Collection).Count);
        // 第3部も同じ。見送りの儀式の曲(イベントの rite)と底の階の曲
        var part3 = ContentLibrary.Load(TestPaths.Content, Path.Combine(TestPaths.Content, "scenarios", "devourer"));
        Assert.True(part3.IsValid, string.Join(" / ", part3.Problems));
        Assert.EndsWith("bow_to_the_waves.mp3", part3.FindFile("audio/bgm/rite.mp3"));
        Assert.EndsWith("beneath_the_stone_weight.mp3", part3.FindFile("audio/bgm/maze_b10.mp3"));
        Assert.EndsWith("crown_upon_the_floor.mp3", part3.FindFile("audio/bgm/ending.mp3"));
    }

    [Fact]
    public void AMissingShelfTrackIsAProblem()
    {
        using var tmp = new TestPaths.TempDir();
        string dir = MakeScenario(Path.Combine(tmp.Path, "b_music"), "b_music");
        var m = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!.AsObject();
        m["music"] = new System.Text.Json.Nodes.JsonObject { ["town"] = "no_such_track" };
        File.WriteAllText(Path.Combine(dir, "manifest.json"), m.ToJsonString());
        var content = ContentLibrary.Load(TestPaths.Content, dir);
        Assert.Contains(content.Problems, p => p.Contains("no_such_track"));
    }

    [Fact]
    public void ADroppedZipIsCopiedOrMovedIntoTheScenarios()
    {
        using var tmp = new TestPaths.TempDir();
        string user = Path.Combine(tmp.Path, "Scenarios"), bundled = Path.Combine(tmp.Path, "bundled"), cache = Path.Combine(tmp.Path, "cache");
        string src = MakeScenario(Path.Combine(tmp.Path, "src", "b_drop"), "b_drop");
        string zip = Path.Combine(tmp.Path, "downloads", "some name.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        ZipFile.CreateFromDirectory(src, zip, CompressionLevel.Fastest, includeBaseDirectory: true);

        var check = ScenarioInstall.Check(zip, user, [bundled], cache);
        Assert.Equal(DropProblem.None, check.Problem);
        Assert.Empty(check.Existing);
        string dest = ScenarioInstall.Install(check, user, move: false);
        Assert.Equal(Path.Combine(user, "b_drop.zip"), dest);
        Assert.True(File.Exists(zip));
        Assert.Equal(["b_drop"], ScenarioCatalog.Find([user], cache).Select(s => s.Id));

        // 置いてあるものを落としても、置き直さない
        Assert.Equal(DropProblem.AlreadyPlaced, ScenarioInstall.Check(dest, user, [bundled], cache).Problem);

        // 同じ id を移動で置くと、古い方と入れ替わり、元は消える
        var again = ScenarioInstall.Check(src, user, [bundled], cache);
        Assert.Equal([dest], again.Existing);
        string folder = ScenarioInstall.Install(again, user, move: true);
        Assert.Equal(Path.Combine(user, "b_drop"), folder);
        Assert.False(Directory.Exists(src));
        Assert.False(File.Exists(dest));
        Assert.Equal([folder], ScenarioCatalog.Find([user], cache).Select(s => s.Source));
    }

    [Fact]
    public void AZipReplacesAPlacedFolderAndALockedOneIsLeftAsItWas()
    {
        using var tmp = new TestPaths.TempDir();
        string user = Path.Combine(tmp.Path, "Scenarios"), bundled = Path.Combine(tmp.Path, "bundled"), cache = Path.Combine(tmp.Path, "cache");
        string placed = MakeScenario(Path.Combine(user, "b_drop"), "b_drop");
        string zip = Path.Combine(tmp.Path, "downloads", "b_drop.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        ZipFile.CreateFromDirectory(MakeScenario(Path.Combine(tmp.Path, "src", "b_drop"), "b_drop", "2.0"), zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        string[] Entries() => Directory.EnumerateFileSystemEntries(user).Select(Path.GetFileName).Order().ToArray()!;

        // 置いてあるフォルダの中のファイルを使っていると、入れ替えずに投げ、フォルダも中身もそのまま(仮のものは残さない)
        var check = ScenarioInstall.Check(zip, user, [bundled], cache);
        Assert.Equal([placed], check.Existing);
        using (File.Open(Path.Combine(placed, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => ScenarioInstall.Install(check, user, move: true));
        Assert.Equal(["b_drop"], Entries());
        Assert.True(File.Exists(Path.Combine(placed, "manifest.json")));
        Assert.True(File.Exists(zip));

        // 使い終われば入れ替わる
        Assert.Equal(Path.Combine(user, "b_drop.zip"), ScenarioInstall.Install(ScenarioInstall.Check(zip, user, [bundled], cache), user, move: false));
        Assert.Equal(["b_drop.zip"], Entries());

        // 前に置きかけて残ったもの(仮の名前のもの、ファイルの無い id のフォルダ)は、次に置くときに片付ける
        Directory.CreateDirectory(Path.Combine(user, "b_drop", "maps"));
        File.WriteAllText(Path.Combine(user, ".b_drop.zip.0123.new"), "x");
        Assert.Equal(Path.Combine(user, "b_drop.zip"), ScenarioInstall.Install(ScenarioInstall.Check(zip, user, [bundled], cache), user, move: false));
        Assert.Equal(["b_drop.zip"], Entries());
    }

    [Fact]
    public void DroppedThingsThatAreNotPlayableScenariosAreRefused()
    {
        using var tmp = new TestPaths.TempDir();
        string user = Path.Combine(tmp.Path, "Scenarios"), bundled = Path.Combine(tmp.Path, "bundled"), cache = Path.Combine(tmp.Path, "cache");
        MakeScenario(Path.Combine(bundled, "gravekeeper"), "gravekeeper");
        DropProblem Check(string path) => ScenarioInstall.Check(path, user, [bundled], cache).Problem;

        Assert.Equal(DropProblem.NoDungeon, Check(MakeScenario(Path.Combine(tmp.Path, "no_maze"), "no_maze", dungeon: false)));
        Assert.Equal(DropProblem.Bundled, Check(MakeScenario(Path.Combine(tmp.Path, "copy"), "gravekeeper")));
        string plain = Path.Combine(tmp.Path, "plain");
        Directory.CreateDirectory(plain);
        Assert.Equal(DropProblem.NotScenario, Check(plain));
        string text = Path.Combine(tmp.Path, "note.txt");
        File.WriteAllText(text, "hi");
        Assert.Equal(DropProblem.NotScenario, Check(text));
        string broken = Path.Combine(tmp.Path, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "manifest.json"), "{ not json");
        Assert.Equal(DropProblem.Unreadable, Check(broken));
        Assert.False(Directory.Exists(user));
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
