using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

public class DifficultyTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static GameSession Session(string preset)
    {
        var settings = MazeGenSettings.Load(Content);
        var request = new GenerationRequest(77, 3, FloorSizeMode.Fixed(12, 12));
        var s = GameSession.New(1, request, new MazeGenerator(settings).Generate(request).Dungeon);
        s.Difficulty = Difficulty.Of(preset);
        foreach (var cls in new[] { "fighter", "cleric", "wizard" })
        {
            var c = CharacterFactory.QuickCreate(Data, cls, "human", cls);
            c.Gold = 100;
            s.Roster.Characters.Add(c);
            s.Roster.AddToParty(c);
        }
        s.EnterMaze();
        return s;
    }

    [Fact]
    public void PresetsMatchTheDesign()
    {
        var w = Difficulty.Of(Difficulty.Wizardry);
        Assert.True(w.BodiesRemain && w.RiskyRevive && w.StrictSave);
        var f = Difficulty.Of(Difficulty.Fifth);
        Assert.False(f.BodiesRemain || f.RiskyRevive || f.StrictSave);
    }

    [Fact]
    public void WipeLeavesBodiesThatAnotherPartyCanCarryHome()
    {
        var s = Session(Difficulty.Wizardry);
        var at = new PartyPosition(1, new GridPos(3, 4), Direction.North);
        var fallen = s.Roster.PartyMembers.ToList();
        s.Wipe(at);
        Assert.Empty(s.Roster.Party);
        Assert.All(fallen, c => Assert.Equal(LifeState.Dead, c.State));
        Assert.Equal(3, s.BodiesAt(at).Count);
        Assert.Empty(s.BodiesAt(at with { Cell = new GridPos(0, 0) }));
        Assert.Equal(Location.Town, s.Location);

        // 新しい仲間で迎えに行く
        var rescuer = CharacterFactory.QuickCreate(Data, "rescue", "human", "fighter");
        s.Roster.Characters.Add(rescuer);
        Assert.True(s.Roster.AddToParty(rescuer));
        Assert.False(s.Roster.AddToParty(fallen[0]));   // 迷宮に残っている人は、酒場で入れられない
        var taken = s.RecoverBodies(at);
        Assert.Equal(3, taken.Count);
        Assert.Equal(4, s.Roster.Party.Count);
        Assert.Empty(s.BodiesAt(at));
    }

    [Fact]
    public void EasyWipeCarriesThePartyHomeForHalfTheGold()
    {
        var s = Session(Difficulty.Fifth);
        var party = s.Roster.PartyMembers.ToList();
        party[0].State = LifeState.Unconscious;
        party[0].Hp = 0;
        party[1].State = LifeState.Dead;
        s.Wipe(s.Exploration!.Position);
        Assert.Equal(3, s.Roster.Party.Count);
        Assert.Equal(LifeState.Alive, party[0].State);
        Assert.Equal(1, party[0].Hp);
        Assert.Equal(LifeState.Dead, party[1].State);   // 死んだ人は寺院で
        Assert.All(party, c => Assert.Equal(50, c.Gold));
        Assert.Empty(s.Roster.LeftInMaze);
    }

    [Fact]
    public void EasyReviveAlwaysWorks()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var c = CharacterFactory.QuickCreate(Data, "d", "human", "fighter");
            c.State = LifeState.Dead;
            Assert.Equal(100, Temple.ReviveChance(c, sure: true));
            Assert.Equal(Temple.ReviveResult.Revived, Temple.Revive(c, new Pcg32(seed, 1), sure: true));
        }
    }

    [Fact]
    public void DifficultyAndPendingCombatAreSaved()
    {
        using var dir = new TestPaths.TempDir();
        var s = Session(Difficulty.Custom);
        s.Difficulty.RiskyRevive = false;
        s.PendingCombat = new PendingCombat { Groups = [new() { Monster = "goblin", Count = 3 }], HpMult = 1.5 };
        s.ClanName = "灰の鐘団";
        var store = new SaveStore(dir.Path);
        store.Save(s, Content);
        var back = store.Load(s.Slot);
        Assert.Equal(Difficulty.Custom, back.Difficulty.Preset);
        Assert.False(back.Difficulty.RiskyRevive);
        Assert.True(back.Difficulty.BodiesRemain);
        Assert.Equal("goblin", back.PendingCombat!.Groups[0].Monster);
        Assert.Equal(3, back.PendingCombat.Groups[0].Count);
        Assert.Equal(1.5, back.PendingCombat.HpMult);
        Assert.Equal("灰の鐘団", back.ClanName);
    }
}
