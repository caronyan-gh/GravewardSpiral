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
        var back = store.Load(2);
        Assert.Equal((7.0, 11.0, 6.0, 3), (back.FirstLevel!.Value, back.LastLevel!.Value, back.LevelShift, back.Clears));
        // 書き出さなくても、セーブのクランを新しいゲームへ連れていける
        var clans = ClanFile.FromSaves(store, n => $"枠 {n}");
        var (path, clan) = Assert.Single(clans);
        Assert.Equal("slot:2", path);
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

        var loaded = store.Load(1);
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
    public void SavingTwiceKeepsBackupAndSlotsAreListed()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        Assert.Equal(1, store.FirstEmptySlot());
        var s = NewSession(1);
        store.Save(s, Content);
        store.Save(s, Content);
        Assert.True(File.Exists(Path.Combine(dir.Path, "slot1", "save.bak.json")));
        Assert.Equal(2, store.FirstEmptySlot());
        store.Save(NewSession(2), Content);
        var list = store.List();
        Assert.Equal([1, 2], list.Select(l => l.Slot));
        Assert.Equal(["ルカ"], list[0].Party);
        store.Delete(1);
        Assert.Equal(1, store.FirstEmptySlot());
    }

    [Fact]
    public void NewerVersionIsRejected()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        store.Save(NewSession(1), Content);
        string path = Path.Combine(dir.Path, "slot1", "save.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\": 1", "\"version\": 99"));
        Assert.Throws<InvalidDataException>(() => store.Load(1));
    }
}
