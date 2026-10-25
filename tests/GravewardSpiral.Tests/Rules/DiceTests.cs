using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Rules;

public class DiceTests
{
    [Theory]
    [InlineData("d20", 1, 20, 10.5)]
    [InlineData("2d6+3", 5, 15, 10)]
    [InlineData("1d8 + 1d6 - 1", 1, 13, 7)]
    [InlineData("3", 3, 3, 3)]
    [InlineData("-2+1d4", -1, 2, 0.5)]
    [InlineData("2D6", 2, 12, 7)]
    public void ParsesRangeAndAverage(string text, int min, int max, double average)
    {
        var expr = DiceExpression.Parse(text);
        Assert.Equal(min, expr.Min);
        Assert.Equal(max, expr.Max);
        Assert.Equal(average, expr.Average, 6);
    }

    [Fact]
    public void KeepHighestAverageMatchesKnownValue()
    {
        // 4d6 で高い3つを残す(能力値の作り方)の期待値は 15869/1296 ≒ 12.2446
        var expr = DiceExpression.Parse("4d6kh3");
        Assert.Equal(3, expr.Min);
        Assert.Equal(18, expr.Max);
        Assert.Equal(15869.0 / 1296.0, expr.Average, 6);
    }

    [Fact]
    public void AdvantageAndDisadvantageAverages()
    {
        Assert.Equal(13.825, DiceExpression.Parse("2d20kh1").Average, 6);
        Assert.Equal(7.175, DiceExpression.Parse("2d20kl1").Average, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("2d0")]
    [InlineData("0d6")]
    [InlineData("2d6 3")]
    [InlineData("4d6kh5")]
    [InlineData("1d6x")]
    [InlineData("1001d6")]
    public void RejectsBadExpressions(string text)
    {
        Assert.False(DiceExpression.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void RollsStayInRange()
    {
        var rng = new Pcg32(5, 5);
        var expr = DiceExpression.Parse("3d6+2");
        for (int i = 0; i < 2000; i++)
            Assert.InRange(expr.Roll(rng).Total, expr.Min, expr.Max);
    }

    [Fact]
    public void RollsAreReproducibleWithSameSeed()
    {
        var expr = DiceExpression.Parse("8d6");
        var a = new Pcg32(11, 2);
        var b = new Pcg32(11, 2);
        for (int i = 0; i < 100; i++) Assert.Equal(expr.Roll(a).Total, expr.Roll(b).Total);
    }

    [Fact]
    public void KeepHighestKeepsTheHighest()
    {
        var rng = new Pcg32(3, 9);
        var expr = DiceExpression.Parse("4d6kh3");
        for (int i = 0; i < 500; i++)
        {
            var roll = expr.Roll(rng);
            var dice = roll.Parts[0].Rolls;
            Assert.Equal(dice.Sum() - dice.Min(), roll.Total);
        }
    }

    [Fact]
    public void CriticalDoublesDiceButNotModifier()
    {
        var crit = DiceExpression.Parse("1d8+3").WithDoubledDice();
        Assert.Equal("2d8+3", crit.ToString());
        Assert.Equal(5, crit.Min);
        Assert.Equal(19, crit.Max);
    }

    [Fact]
    public void PlusCombinesExpressions()
    {
        var upcast = DiceExpression.Parse("8d6").Plus(DiceExpression.Parse("1d6"));
        Assert.Equal("8d6+1d6", upcast.ToString());
        Assert.Equal(31.5, upcast.Average, 6);
    }

    [Fact]
    public void DescribeShowsEachDie()
    {
        var roll = DiceExpression.Parse("2d6+3").Roll(new Pcg32(1, 1));
        Assert.Matches(@"^2d6\+3 = \[\d, \d\] \+ 3 = \d+$", roll.Describe());
    }
}
