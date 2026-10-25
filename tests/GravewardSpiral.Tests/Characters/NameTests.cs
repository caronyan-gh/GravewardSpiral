using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Characters;

public class NameTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void EveryRaceHasNamesInBothLanguagesThatFitTheNameLimit()
    {
        foreach (var race in Data.RaceOrder)
        {
            Assert.True(Data.NameLists.TryGetValue(race, out var list), $"names.json に {race} がありません");
            Assert.NotEmpty(list!.Names);
            foreach (var n in list.Names)
            {
                Assert.True(n.Has("en") && n.Has("ja"), $"{race}: {n} に英語か日本語がありません");
                Assert.InRange(n.Get("en").Length, 1, CharacterFactory.MaxNameLength);
                Assert.InRange(n.Get("ja").Length, 1, CharacterFactory.MaxNameLength);
            }
        }
    }

    [Fact]
    public void PickUsesTheRaceListAndAvoidsTakenNames()
    {
        var rng = new Pcg32(5, 1);
        var dwarves = Data.NameLists["dwarf"].Names.Select(n => n.Get("ja")).ToList();
        var taken = dwarves.Skip(1).ToList();
        for (int i = 0; i < 20; i++)
            Assert.Equal(dwarves[0], RandomNames.Pick(Data, "dwarf", "ja", rng, taken));
        // 全部使われていても、名前は出す
        Assert.Contains(RandomNames.Pick(Data, "dwarf", "ja", rng, dwarves), dwarves);
        // 種族が決まっていなければ、全部の一覧から
        Assert.NotNull(RandomNames.Pick(Data, null, "en", rng));
    }
}
