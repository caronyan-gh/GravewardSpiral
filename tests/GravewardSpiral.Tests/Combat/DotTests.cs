using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>続くダメージ(番の初めにダメージ。同じものは重ならない)。</summary>
public class DotTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static (CombatEngine Engine, CombatState State, PartyMember P, MonsterUnit M) Fight()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 10);
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        var m = s.Groups[0].Units[0];
        m.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });   // 敵は動かない
        return (engine, s, s.Party[0], m);
    }

    private static EffectPart Dot(string dice, string type, int rounds) => new() { Type = EffectPart.Dot, Dice = dice, DamageType = type, Rounds = rounds };

    [Fact]
    public void ADotHurtsAtTheStartOfEachTurnUntilItRunsOut()
    {
        var (e, s, p, m) = Fight();
        e.ApplyToParty(s, m, [Dot("3", "fire", 2)]);
        int hp = p.Hp;
        var dodge = new Dictionary<string, PlayerCommand> { [p.Id] = PlayerCommand.Dodge };
        e.RunRound(s, dodge);
        Assert.Equal(hp - 3, p.Hp);
        e.RunRound(s, dodge);
        Assert.Equal(hp - 6, p.Hp);
        Assert.Empty(p.Dots);
        e.RunRound(s, dodge);
        Assert.Equal(hp - 6, p.Hp);
    }

    [Fact]
    public void TheSameDotDoesNotStackButDifferentOnesDo()
    {
        var (e, s, p, m) = Fight();
        e.ApplyToParty(s, m, [Dot("2", "fire", 1)]);
        e.ApplyToParty(s, m, [Dot("5", "fire", 3)]);   // 同じ敵の同じ種類: ダイスの大きいほう、長いほう
        Assert.Single(p.Dots);
        Assert.Equal("5", p.Dots[0].Dice);
        Assert.Equal(3, p.Dots[0].RoundsLeft);
        e.ApplyToParty(s, m, [Dot("4", "poison", 2)]);   // 違うものは重なる
        Assert.Equal(2, p.Dots.Count);
    }

    [Fact]
    public void BundledDataHasDots()
    {
        Assert.Contains(Data.Monsters["pit_fiend"].Actions.SelectMany(a => a.Effects).SelectMany(x => x.Flatten()), x => x.Type == EffectPart.Dot && x.DamageType == "poison");
        Assert.Contains(Data.Monsters["fire_elemental"].Actions.SelectMany(a => a.Effects), x => x.Type == EffectPart.Dot);
        Assert.Contains(Data.Spells["acid_arrow"].Effects, x => x.Type == EffectPart.Dot);
        Assert.Contains(Data.Spells["heat_metal"].Effects, x => x.Type == EffectPart.Dot);
    }
}
