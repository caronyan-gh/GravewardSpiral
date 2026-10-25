using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

public class FloorThemeTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void EveryDepthHasATheme()
    {
        foreach (var depth in new[] { 0, 0.2, 0.4, 0.6, 0.8, 1.0 })
            Assert.NotNull(FloorAdvisor.ThemeFor(Data, depth));
        Assert.Equal("a_first_grave", FloorAdvisor.ThemeFor(Data, 1.0)!.Id);
    }

    [Fact]
    public void ThemesLeanTheRosterTowardTheirTypes()
    {
        var s = EncounterSettings.Load(Content);
        int undead = 0, total = 0;
        for (ulong seed = 0; seed < 20; seed++)
        {
            var roster = Encounters.Roster(Data, s, 0.6, 6, new Pcg32(seed, 5));
            undead += roster.Count(id => Data.Monsters[id].CreatureType == "undead");
            total += roster.Count;
        }
        // 回廊の階(アンデッドの重み 6)では、顔ぶれの多くがアンデッドになる
        Assert.True(undead * 3 > total, $"undead {undead} / {total}");
    }

    [Fact]
    public void AdvisorSuggestsMonstersAndItemsForTheFloor()
    {
        var s = EncounterSettings.Load(Content);
        var theme = FloorAdvisor.ThemeFor(Data, 0.8);
        var monsters = FloorAdvisor.Monsters(Data, s, 0.8, 6, theme);
        Assert.NotEmpty(monsters);
        Assert.Equal("undead", monsters[0].Monster.CreatureType);
        var rarities = FloorAdvisor.ItemRarities(TreasureSettings.Load(Content), 0.8);
        Assert.Equal(1.0, rarities.Values.Sum(), 6);
        Assert.NotEmpty(FloorAdvisor.Items(Data, TreasureSettings.Load(Content), 0.8));
    }
}
