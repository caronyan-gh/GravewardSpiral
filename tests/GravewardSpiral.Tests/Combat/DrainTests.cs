using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>吸う(与えたダメージの一部を、使った本人の HP に戻す)。</summary>
public class DrainTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static (CombatEngine Engine, CombatState State, PartyMember P, MonsterUnit M) Fight()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        return (engine, s, s.Party[0], s.Groups[0].Units[0]);
    }

    [Fact]
    public void DrainHealsTheSourceByAShareOfTheDamage()
    {
        var (engine, s, p, m) = Fight();
        m.Hp = 10;
        int before = p.Hp;
        engine.ApplyToParty(s, m, [
            new EffectPart { Type = EffectPart.Damage, Dice = "6", DamageType = "piercing" },
            new EffectPart { Type = EffectPart.Damage, Dice = "8", DamageType = "necrotic" },
            new EffectPart { Type = EffectPart.Drain, Value = 50 },
        ]);
        Assert.Equal(before - 14, p.Hp);
        Assert.Equal(17, m.Hp);   // 14 の半分
    }

    [Fact]
    public void DrainCanCountOnlyOneDamageType()
    {
        // ヴァンパイアの噛みつき: 死霊の分だけ吸う
        var (engine, s, p, m) = Fight();
        m.Hp = 10;
        engine.ApplyToParty(s, m, [
            new EffectPart { Type = EffectPart.Damage, Dice = "6", DamageType = "piercing" },
            new EffectPart { Type = EffectPart.Damage, Dice = "8", DamageType = "necrotic" },
            new EffectPart { Type = EffectPart.Drain, Value = 100, DamageType = "necrotic" },
        ]);
        Assert.Equal(18, m.Hp);
    }

    [Fact]
    public void DrainOnlyCountsHpTheTargetActuallyHad()
    {
        var (engine, s, p, m) = Fight();
        m.Hp = 10;
        p.Hp = 3;
        engine.ApplyToParty(s, m, [
            new EffectPart { Type = EffectPart.Damage, Dice = "8", DamageType = "necrotic" },
            new EffectPart { Type = EffectPart.Drain, Value = 100 },
        ]);
        Assert.Equal(13, m.Hp);
    }

    [Fact]
    public void BundledDataDrains()
    {
        Assert.Contains(Data.Spells["vampiric_touch"].Effects, e => e.Type == EffectPart.Drain && e.Value == 50);
        Assert.Contains(Data.Monsters["vampire_spawn"].Actions.SelectMany(a => a.Effects), e => e.Type == EffectPart.Drain && e.DamageType == "necrotic");
        Assert.Contains(Data.Affixes.Values, a => a.Tiers.Any(t => t.OnHit.Any(e => e.Type == EffectPart.Drain)));
    }
}
