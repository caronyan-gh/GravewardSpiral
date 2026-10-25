using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

public class SaveTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static GameSession NewSession(int slot)
    {
        var settings = MazeGenSettings.Load(Content);
        var request = new GenerationRequest(4242, 4, FloorSizeMode.Fixed(12, 12));
        var dungeon = new MazeGenerator(settings).Generate(request).Dungeon;
        var s = GameSession.New(slot, request, dungeon);
        var choices = new CharacterChoices("ルカ", "human", "fighter", "soldier", Alignment.Good,
            new AbilityScores { Str = 15, Dex = 14, Con = 13, Int = 12, Wis = 10, Cha = 8 }, [], ["acrobatics", "history"], []);
        var c = CharacterFactory.Create(Data, choices);
        c.Inventory.TryAdd(ItemInstance.Of("potion_of_healing", 2), Data.Items);
        c.Lasting.Add(new Core.Combat.LastingCondition { Condition = Core.Effects.Condition.Poisoned, Minutes = 30 });
        c.SlotsUsed[1] = 1;
        s.Roster.Characters.Add(c);
        s.Roster.AddToParty(c);
        return s;
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>前の形(saves/slot&lt;N&gt;/save.json。名簿もクランのことも全部入り)のセーブを書く。</summary>
    private static string WriteOldSlot(string root, int slot, GameSession s, int version = 2, bool withClanId = true)
    {
        var data = SaveData.From(s, Content);
        data.Version = version;
        if (!withClanId) data.ClanId = null;
        string dir = Path.Combine(root, $"slot{slot}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "save.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data, Json));
        return path;
    }

    [Fact]
    public void SaveKeepsTheWorldLevelAndClearsAndOffersItsClan()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var s = NewSession(2);
        s.SetWorldLevel(Core.Combat.EncounterSettings.Load(Content), 8);
        s.Clears = 3;
        s.ClanName = "灰の手";
        store.Save(s, Content);
        var back = store.Load(s.ClanId, s.WorldKey);
        Assert.Equal((7.0, 11.0, 6.0, 3), (back.FirstLevel!.Value, back.LastLevel!.Value, back.LevelShift, back.Clears));
        // セーブのクランを、新しい世界で使える(名簿は写さない)
        var clans = ClanFile.FromSaves(store, id => id);
        var (path, clan) = Assert.Single(clans);
        Assert.Equal("clan:" + s.ClanId, path);
        Assert.Equal("灰の手", clan.Name);
        Assert.Equal(3, clan.Clears);
        Assert.Single(clan.Characters);
    }

    [Fact]
    public void SaveAndLoadKeepsEverything()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var s = NewSession(1);
        var state = s.EnterMaze();
        var nav = new MazeNavigator(state);
        nav.TurnRight();
        nav.Forward();
        nav.Forward();
        state.Discover(0, new GridPos(1, 1), Direction.East);
        state.Notes.Add(new MazeNote { Floor = 2, Cell = new GridPos(3, 4), EventId = "wall_writing", Lines = [new LocalizedText(new Dictionary<string, string> { ["ja"] = "壁に文字", ["en"] = "Words" })] });
        s.Rng[RngStreams.Names.Combat].NextUInt();
        uint nextCombat = RngStreams.Restore(s.Rng.MasterSeed, s.Rng.Save())[RngStreams.Names.Combat].NextUInt();
        store.Save(s, Content);
        // 名簿はクランのファイル、世界のことは世界のファイル
        Assert.True(File.Exists(Path.Combine(dir.Path, s.ClanId, "clan.json")));
        Assert.DoesNotContain("\"inventory\"", File.ReadAllText(store.WorldFile(s.ClanId, s.WorldKey)));

        var loaded = store.Load(s.ClanId, s.WorldKey);
        Assert.Equal(Location.Maze, loaded.Location);
        Assert.Equal(s.Request, loaded.Request with { SizeMode = s.Request.SizeMode });
        Assert.Equal(s.Dungeon.Floors.Select(MazeTextFormat.Write), loaded.Dungeon.Floors.Select(MazeTextFormat.Write));
        var c = loaded.Roster.PartyMembers.Single();
        Assert.Equal("ルカ", c.Name);
        Assert.Equal(s.Roster.Characters[0].MaxHp, c.MaxHp);
        Assert.Equal("longsword", c.Inventory[EquipSlot.MainHand]!.ItemId);
        Assert.Equal(2, c.Inventory.Items.Single(i => i.ItemId == "potion_of_healing").Count);
        Assert.Equal(s.Roster.Characters[0].Lasting.Count, c.Lasting.Count);
        Assert.Equal(state.Position, loaded.Exploration!.Position);
        Assert.Equal(state.Steps, loaded.Exploration.Steps);
        Assert.Equal(state.StepsSinceCombat, loaded.Exploration.StepsSinceCombat);
        Assert.True(loaded.Exploration.IsDiscovered(0, new GridPos(1, 1), Direction.East));
        Assert.True(loaded.Exploration.IsNoted(2, new GridPos(3, 4), "wall_writing"));
        Assert.Equal("Words", loaded.Exploration.Notes.Single().Lines.Single().Get("en"));
        Assert.Equal(state.AutoMap.For(0).Visited.Count, loaded.Exploration.AutoMap.For(0).Visited.Count);
        Assert.Equal(state.AutoMap.For(0).Edges.Count, loaded.Exploration.AutoMap.For(0).Edges.Count);
        Assert.Equal(nextCombat, loaded.Rng[RngStreams.Names.Combat].NextUInt());   // 乱数の続きも同じ
    }

    [Fact]
    public void SavingTwiceKeepsBackupAndWorldsAreListedPerClan()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        Assert.False(store.HasAny());
        var s = NewSession(1);
        s.ClanId = "clan_a";
        store.Save(s, Content);
        store.Save(s, Content);
        Assert.True(File.Exists(Path.ChangeExtension(store.WorldFile("clan_a", s.WorldKey), ".bak.json")));
        // 同じクランが別の世界へ(セーブの数に上限は無い)
        for (int i = 2; i <= 5; i++)
        {
            var other = NewSession(i);
            other.ClanId = i == 5 ? "clan_b" : "clan_a";
            other.Scenario = $"world{i}";
            store.Save(other, Content);
        }
        var clans = store.ListClans();
        Assert.Equal(2, clans.Count);
        var a = clans.Single(c => c.Id == "clan_a");
        Assert.Equal(4, a.Worlds.Count);
        Assert.Equal(["ルカ"], a.Worlds[0].Party);
        Assert.True(store.WorldExists("clan_a", "world3"));
        Assert.False(store.WorldExists("clan_b", "world3"));
        store.DeleteWorld("clan_a", "world3");
        Assert.False(store.WorldExists("clan_a", "world3"));
        Assert.NotNull(store.LoadClan("clan_a"));   // 世界を消しても、クランは残る
        store.DeleteClan("clan_b");
        Assert.Single(store.ListClans());
    }

    [Fact]
    public void TheRosterIsSharedAcrossWorldsAndPeopleInAnotherMazeAreAway()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var a = NewSession(1);
        a.ClanId = "clan_a";
        a.Scenario = "world_a";
        var luka = a.Roster.Characters[0];
        var extra = CharacterFactory.QuickCreate(Data, "Rowan", "human", "cleric", 1);
        a.Roster.Characters.Add(extra);
        a.Roster.AddToParty(extra);
        a.EnterMaze();   // 世界Aの迷宮で中断する
        store.Save(a, Content);

        // 世界B: 同じクランの名簿(写さない)。Aの迷宮にいる2人は使えない
        var b = store.Load("clan_a", "world_a");
        b.Scenario = "world_b";
        b.Location = Location.Town;
        b.Exploration = null;
        b.Roster.Party.Clear();
        store.MarkAway(b);
        Assert.Equal("world_a", b.Roster.Away[luka.Id]);
        Assert.False(b.Roster.AddToParty(b.Roster.Find(luka.Id)!));
        var newcomer = CharacterFactory.QuickCreate(Data, "Hilda", "dwarf", "fighter", 1);
        b.Roster.Characters.Add(newcomer);
        Assert.True(b.Roster.AddToParty(newcomer));
        newcomer.Xp = 999;
        store.Save(b, Content);

        // Aに戻ると、Bで作った人も同じ名簿にいて、Bで変わったことも出る
        var back = store.Load("clan_a", "world_a");
        Assert.Equal(999, back.Roster.Find(newcomer.Id)!.Xp);
        Assert.Equal(2, back.Roster.PartyMembers.Count());   // Aのパーティはそのまま
        Assert.Empty(back.Roster.Away);
        // Aで町に戻れば、Bでも使える
        back.Location = Location.Town;
        back.Exploration = null;
        store.Save(back, Content);
        var b2 = store.Load("clan_a", "world_b");
        Assert.Empty(b2.Roster.Away);
        Assert.True(b2.Roster.AddToParty(b2.Roster.Find(luka.Id)!));
    }

    [Fact]
    public void OldSlotSavesMoveToClansAndWorlds()
    {
        using var dir = new TestPaths.TempDir();
        // 版 1(シナリオもクランの id も無い)と、版 2 の同じクランの2つの世界
        var legacy = NewSession(1);
        string legacyPath = WriteOldSlot(dir.Path, 1, legacy, version: 1, withClanId: false);
        File.WriteAllText(legacyPath, File.ReadAllText(legacyPath).Replace("\"scenario\": null,", ""));
        var older = NewSession(2);
        older.ClanId = "clan_x";
        older.Scenario = "world_old";
        WriteOldSlot(dir.Path, 2, older);
        System.Threading.Thread.Sleep(20);
        var newer = NewSession(3);
        newer.ClanId = "clan_x";
        newer.Scenario = "world_new";
        newer.Roster.Characters[0].Xp = 777;
        WriteOldSlot(dir.Path, 3, newer);

        var store = new SaveStore(dir.Path);
        Assert.Equal(3, store.MigrateOldSlots(Content));
        Assert.False(File.Exists(Path.Combine(dir.Path, "slot1", "save.json")));   // slot1 は、いまはクラン slot1 のフォルダ
        Assert.True(File.Exists(Path.Combine(dir.Path, "old", "slot1", "save.json")));   // 元は残す
        var clans = store.ListClans();
        Assert.Equal(["slot1", "clan_x"], clans.Select(c => c.Id).Order().Reverse());
        var first = store.Load("slot1", ScenarioCatalog.Gravekeeper);   // 版 1 は第1部
        Assert.Equal("ルカ", first.Roster.PartyMembers.Single().Name);
        Assert.Equal(2, clans.Single(c => c.Id == "clan_x").Worlds.Count);
        Assert.Equal(777, store.Load("clan_x", "world_old").Roster.Characters[0].Xp);   // 新しいセーブの名簿がクランの名簿
        Assert.Equal(0, store.MigrateOldSlots(Content));   // 2回目は何もしない
    }

    [Fact]
    public void ClanFilesStillCarryTheClanId()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var s = NewSession(1);
        s.ClanId = "clan_a";
        store.Save(s, Content);
        var clan = ClanFile.From(store.Load("clan_a", s.WorldKey), "灰の手", null);
        Assert.Equal("clan_a", clan.Id);
        var next = NewSession(9);
        clan.ApplyTo(next);
        Assert.Equal("clan_a", next.ClanId);
    }

    [Fact]
    public void ClanNamesAreKatakanaAndFixedPerClan()
    {
        var parts = ClanNameParts.Load(Content);
        string ja = parts.Pick("clan_a", "ja")!;
        Assert.Equal(ja, parts.Pick("clan_a", "ja"));
        Assert.All(ja, ch => Assert.InRange(ch, '゠', 'ヿ'));   // 画面のキーボードで打てるカタカナだけ
        Assert.InRange(ja.Length, 2, 16);
        Assert.Contains(" ", parts.Pick("clan_a", "en"));
        Assert.Contains(Enumerable.Range(0, 30), i => parts.Pick($"c{i}", "ja") != ja);
    }

    [Fact]
    public void AnOldWorldWithoutAScenarioIsReadAsPartOne()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var s = NewSession(1);
        store.Save(s, Content);   // 自由な迷宮(シナリオ無し)
        Assert.Null(store.Load(s.ClanId, s.WorldKey).Scenario);
        // 第1部が標準のパックにあったころ(版 1)の世界のファイル
        string path = store.WorldFile(s.ClanId, s.WorldKey);
        File.WriteAllText(path, File.ReadAllText(path).Replace($"\"version\": {SaveStore.CurrentVersion}", "\"version\": 1").Replace("\"scenario\": null,", ""));
        Assert.Equal(ScenarioCatalog.Gravekeeper, store.Load(s.ClanId, s.WorldKey).Scenario);
    }

    [Fact]
    public void NewerVersionIsRejected()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var s = NewSession(1);
        store.Save(s, Content);
        string path = store.WorldFile(s.ClanId, s.WorldKey);
        File.WriteAllText(path, File.ReadAllText(path).Replace($"\"version\": {SaveStore.CurrentVersion}", "\"version\": 99"));
        Assert.Throws<InvalidDataException>(() => store.Load(s.ClanId, s.WorldKey));
    }
}
