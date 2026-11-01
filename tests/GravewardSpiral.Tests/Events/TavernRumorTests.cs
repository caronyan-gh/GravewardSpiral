using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Events;

public class TavernRumorTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    /// <summary>NextDouble が決まった値を順に返す(格のずれを振る1回目と、重みで選ぶ2回目)。</summary>
    private sealed class Doubles(params double[] values) : IRandom
    {
        private int _i;
        public uint NextUInt() => (uint)(values[Math.Min(_i++, values.Length - 1)] * 4294967296.0);
        public int Next(int min, int maxExclusive) => min;
    }

    private static readonly List<TavernRumors.Candidate> All =
    [
        new(0, "gossip", 1), new(1, "tip", 1), new(2, "near", 1), new(3, "exact", 1),
    ];

    [Fact]
    public void ADrinkTellsItsOwnTierMostOfTheTime()
    {
        Assert.Equal("near", TavernRumors.Pick(All, 2, null, new Doubles(0.5, 0.5))!.Text);
        Assert.Equal("exact", TavernRumors.Pick(All, 2, null, new Doubles(0.01, 0.5))!.Text);   // 1つ上
        Assert.Equal("tip", TavernRumors.Pick(All, 2, null, new Doubles(0.99, 0.5))!.Text);    // 1つ下
        Assert.Equal("gossip", TavernRumors.Pick(All, 0, null, new Doubles(0.99, 0.5))!.Text);  // ただより下は無い
    }

    [Fact]
    public void NothingAtTheDrinkMeansNoStoryAndAnEmptyTierAboveFallsBack()
    {
        Assert.Null(TavernRumors.Pick(All, 4, null, new Doubles(0.5, 0.5)));   // 火酒の話が無ければ、酒を出さない
        Assert.Equal("exact", TavernRumors.Pick(All, 3, null, new Doubles(0.01, 0.5))!.Text);   // 上の格が空なら、下へ戻る
    }

    [Fact]
    public void TheLastRumorIsNotRepeatedWhenThereIsAnother()
    {
        List<TavernRumors.Candidate> two = [new(0, "a", 1), new(0, "b", 1)];
        for (double x = 0.05; x < 1; x += 0.1)
            Assert.Equal("b", TavernRumors.Pick(two, 0, "a", new Doubles(0.5, x))!.Text);
    }

    [Fact]
    public void BaseRumorsCoverSmallTalkAndTipsAndPassTheCheck()
    {
        Assert.Contains(Data.Rumors.Values, r => r.Tier == 0);
        Assert.Contains(Data.Rumors.Values, r => r.Tier == 1);
        Assert.All(Data.Rumors.Values, r => Assert.Empty(RumorDef.Check(r)));
        Assert.Contains(RumorDef.Check(new RumorDef { Id = "x", Text = LocalizedText.Of("a", "あ"), Tier = 5 }), p => p.Contains("tier"));
    }

    [Fact]
    public void TheInformantAndMerchantComeAndGoBetweenTrips()
    {
        Assert.All(Enumerable.Range(0, 50), i => Assert.True(TavernRumors.Present(2, 7, i)));   // 冒険者はいつもいる
        var merchant = Enumerable.Range(0, 200).Select(i => TavernRumors.Present(4, 7, i * 37)).ToList();
        Assert.Contains(true, merchant);
        Assert.Contains(false, merchant);
        Assert.Equal(TavernRumors.Present(4, 7, 123), TavernRumors.Present(4, 7, 123));   // 町にいるあいだは変わらない
    }
}
