using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Rng;

public class RngTests
{
    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new Pcg32(42, 54);
        var b = new Pcg32(42, 54);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void KnownFirstValuesStayFixed()
    {
        // PCG32 の参照実装 (seed=42, stream=54) の最初の値。版が変わっても並びが変わらないことを固定する
        var rng = new Pcg32(42, 54);
        Assert.Equal(0xa15c02b7u, rng.NextUInt());
        Assert.Equal(0x7b47f409u, rng.NextUInt());
        Assert.Equal(0xba1d3330u, rng.NextUInt());
    }

    [Fact]
    public void NextStaysInRangeAndCoversIt()
    {
        var rng = new Pcg32(1, 1);
        var seen = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.Next(-3, 4);
            Assert.InRange(v, -3, 3);
            seen.Add(v);
        }
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void SaveAndRestoreContinuesSequence()
    {
        var rng = new Pcg32(7, 3);
        for (int i = 0; i < 10; i++) rng.NextUInt();
        var saved = rng.Save();
        var expected = Enumerable.Range(0, 20).Select(_ => rng.NextUInt()).ToArray();
        var restored = Pcg32.FromState(saved);
        Assert.Equal(expected, Enumerable.Range(0, 20).Select(_ => restored.NextUInt()).ToArray());
    }

    [Fact]
    public void StreamsAreIndependentOfEachOther()
    {
        var x = new RngStreams(123);
        var y = new RngStreams(123);
        // x だけ戦闘で多く振っても、宝の並びは同じ
        for (int i = 0; i < 500; i++) x[RngStreams.Names.Combat].NextUInt();
        for (int i = 0; i < 50; i++)
            Assert.Equal(y[RngStreams.Names.Loot].NextUInt(), x[RngStreams.Names.Loot].NextUInt());
    }

    [Fact]
    public void StreamsSaveAndRestore()
    {
        var s = new RngStreams(99);
        s[RngStreams.Names.Combat].NextUInt();
        s[RngStreams.Names.Loot].NextUInt();
        var saved = s.Save();
        var next = s[RngStreams.Names.Loot].NextUInt();
        var restored = RngStreams.Restore(99, saved);
        Assert.Equal(next, restored[RngStreams.Names.Loot].NextUInt());
    }

    [Theory]
    [InlineData("12345", 12345UL)]
    [InlineData(" 7 ", 7UL)]
    public void ParseSeedReadsNumbers(string text, ulong expected) =>
        Assert.Equal(expected, RngStreams.ParseSeed(text));

    [Fact]
    public void ParseSeedHashesText()
    {
        Assert.Equal(RngStreams.ParseSeed("墓守の螺旋"), RngStreams.ParseSeed("墓守の螺旋"));
        Assert.NotEqual(RngStreams.ParseSeed("abc"), RngStreams.ParseSeed("abd"));
        Assert.Null(RngStreams.ParseSeed("  "));
    }
}
