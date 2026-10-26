using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Events;

public class LegendRumorTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static GameSession World(ulong seed)
    {
        var request = new GenerationRequest(seed, 2, FloorSizeMode.Fixed(10, 10));
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(request).Dungeon;
        return GameSession.New(0, request, dungeon);
    }

    [Fact]
    public void LegendaryEventsHaveRumorsAndSomeWorldsTellThem()
    {
        Assert.All(Data.Events.Values.Where(e => e.Scatter?.Rarity == "legendary"), e => Assert.NotNull(e.Rumor));
        var told = Enumerable.Range(1, 40).Select(i => World((ulong)i).LegendRumor(Data)).ToList();
        Assert.Contains(told, r => r == null);   // 噂の無い世界もある
        Assert.Contains(told, r => r != null);
        Assert.Equal(told[3]?.Id, World(4).LegendRumor(Data)?.Id);   // 同じ世界なら同じ噂
    }

    [Fact]
    public void EachWorldThatTellsTheRumorDoublesTheChanceUpToSixteen()
    {
        var s = World(1);
        Assert.Equal(1, EventPlacer.HeardMultiplier(s.LegendsHeard, "ev6_fallen_star"));
        Assert.True(s.HearLegend("ev6_fallen_star"));
        Assert.False(s.HearLegend("ev6_fallen_star"));   // 同じ世界で何度聞いても1回
        Assert.Equal(2, EventPlacer.HeardMultiplier(s.LegendsHeard, "ev6_fallen_star"));

        // ほかの世界で聞くたびに倍(クランの回数を持ち越した次の世界)
        var next = World(2);
        next.LegendsHeard = new(s.LegendsHeard);
        Assert.True(next.HearLegend("ev6_fallen_star"));
        Assert.Equal(4, EventPlacer.HeardMultiplier(next.LegendsHeard, "ev6_fallen_star"));
        next.LegendsHeard["ev6_fallen_star"] = 9;
        Assert.Equal(16, EventPlacer.HeardMultiplier(next.LegendsHeard, "ev6_fallen_star"));
    }

    [Fact]
    public void HeardLegendsAreKeptByTheClan()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var s = World(3);
        s.ClanId = "clanA";
        s.HearLegend("ev6_dead_court");
        store.Save(s, Content);
        Assert.Equal(1, store.LoadClan("clanA")!.LegendsHeard["ev6_dead_court"]);
        Assert.Equal(1, store.Load("clanA", s.WorldKey).LegendsHeard["ev6_dead_court"]);
        // 書き出したクランのファイルにも入る
        var file = ClanFile.From(s, "A", null);
        Assert.Equal(1, file.LegendsHeard["ev6_dead_court"]);
    }
}
