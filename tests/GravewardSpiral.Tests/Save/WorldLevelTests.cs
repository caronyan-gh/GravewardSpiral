using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

public class WorldLevelTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void WeakOrNoClanKeepsTheScenarioBand()
    {
        Assert.Equal((1.0, 5.0), WorldLevel.Band(1, 5, null));
        Assert.Equal((1.0, 5.0), WorldLevel.Band(1, 5, 2));
    }

    [Fact]
    public void StrongClanShiftsTheWholeBand()
    {
        // L8 のクランは、1階が L7 から(幅はそのまま)
        Assert.Equal((7.0, 11.0), WorldLevel.Band(1, 5, 8));
        Assert.Equal((7.0, 11.0), WorldLevel.Band(1, 5, 8.6));
    }

    [Fact]
    public void ClanPowerCountsLevelAndGear()
    {
        var a = CharacterFactory.QuickCreate(Data, "A", "human", "fighter", 6);
        var b = CharacterFactory.QuickCreate(Data, "B", "human", "fighter", 6);
        Assert.Equal(6, WorldLevel.ClanPower(Data, [a, b]), 3);
        a.Inventory[EquipSlot.MainHand]!.Bonus = 4;
        Assert.Equal(7, WorldLevel.ClanPower(Data, [a, b]), 3);   // 強化値 +4 と +0 の平均の半分
    }

    [Fact]
    public void WorldSettingsUseTheSessionBand()
    {
        var gen = MazeGenSettings.Load(Content);
        var request = GenerationRequest.Default(gen, 3);
        var session = GameSession.New(0, request, new MazeGenerator(gen).Generate(request).Dungeon);
        var enc = EncounterSettings.Load(Content);
        Assert.Equal(enc.TargetLevel(1), session.WorldSettings(enc).TargetLevel(1));
        (session.FirstLevel, session.LastLevel) = (9, 13);
        Assert.Equal(9, session.WorldSettings(enc).TargetLevel(0));
        Assert.Equal(13, session.WorldSettings(enc).TargetLevel(1));
        Assert.Equal(enc.FirstLevel, EncounterSettings.Load(Content).FirstLevel);   // 元の設定は変えない
    }

    [Fact]
    public void ScatterHonorsEventLevels()
    {
        var gen = MazeGenSettings.Load(Content);
        var request = GenerationRequest.Default(gen, 5);
        var data = GameData.Load(Content);
        foreach (var e in data.Events.Values.Where(e => e.Scatter != null)) e.Scatter!.MinLevel = 50;   // どれも高い世界だけ
        var low = new MazeGenerator(gen).Generate(request).Dungeon;
        Assert.Equal(0, EventPlacer.Scatter(data, low, gen.EventsPerFloor, 5, _ => 3));
        var high = new MazeGenerator(gen).Generate(request).Dungeon;
        Assert.True(EventPlacer.Scatter(data, high, gen.EventsPerFloor, 5, _ => 60) > 0);
    }

    [Fact]
    public void ClanCarriesTheClearCount()
    {
        var gen = MazeGenSettings.Load(Content);
        var request = GenerationRequest.Default(gen, 3);
        var session = GameSession.New(0, request, new MazeGenerator(gen).Generate(request).Dungeon);
        session.Clears = 2;
        var clan = ClanFile.From(session, "X", null);
        Assert.Equal(2, clan.Clears);
        var next = GameSession.New(1, request, new MazeGenerator(gen).Generate(request).Dungeon);
        clan.ApplyTo(next);
        Assert.Equal(2, next.Clears);
    }
}
