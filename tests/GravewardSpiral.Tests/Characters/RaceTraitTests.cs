using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>種族の特性(抵抗、セーヴの有利、幸運、不屈の耐久、竜の息)。</summary>
public class RaceTraitTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static PartyMember Member(string race, string cls = "fighter", int level = 1) =>
        new(CharacterFactory.QuickCreate(Data, "T", race, cls, level), Data);

    [Fact]
    public void RacesResistTheirDamageTypes()
    {
        Assert.Equal(0.5, Member("dwarf").DamageMultiplier("poison", false));
        Assert.Equal(0.5, Member("tiefling").DamageMultiplier("fire", false));
        Assert.Equal(0.5, Member("dragonborn").DamageMultiplier("fire", false));
        Assert.Equal(1, Member("human").DamageMultiplier("fire", false));
    }

    [Fact]
    public void AHalfOrcDropsTo1HpOncePerLongRest()
    {
        var p = Member("half_orc");
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([p.Character], [(Data.Monsters["goblin"], 1)]);
        var me = s.Party[0];
        engine.Damage(s, me, me.Hp + 1, "slashing", false, false);   // 即死にはならない量
        Assert.Equal(1, me.Hp);
        Assert.True(me.Standing);
        engine.Damage(s, me, 5, "slashing", false, false);
        Assert.False(me.Standing);   // 2回目は踏みとどまらない
        me.Character.State = LifeState.Alive;   // 起きてから宿に泊まる
        me.Character.Hp = 1;
        me.Character.LongRest();
        Assert.Equal(1, ClassFeatures.UsesLeft(Data, me.Character, "relentless_endurance"));
    }

    [Fact]
    public void ALuckyHalflingRarelyFailsOnANatural1()
    {
        // DC 2 のセーヴは 1 を出したときだけ失敗する。幸運なら振り直すので、失敗は 1/400 ほどになる
        var engine = new CombatEngine(Data, new Pcg32(7, 7));
        var halfling = Member("halfling");
        var human = Member("human");
        int Fails(PartyMember m) => Enumerable.Range(0, 2000).Count(_ => !engine.SavingThrow(m, Ability.Str, 2 + m.SaveBonus(Ability.Str)));
        Assert.True(Fails(human) > 50);
        Assert.True(Fails(halfling) < 20);
    }

    [Fact]
    public void DragonbornBreathBurnsANearGroupAndComesBackOnAShortRest()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "dragonborn", "fighter", 3);
        Assert.Contains(ClassFeatures.ActiveFor(Data, c), f => f.Id == "breath_weapon");
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var s = engine.Start([c], [(Data.Monsters["goblin"], 3)]);
        s.Surprised = null;
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Feature, 0, FeatureId: "breath_weapon") });
        Assert.Contains(s.Log, l => l.Key == "combat.feature" && l.Args.Contains("feature.breath_weapon"));
        Assert.Equal(0, ClassFeatures.UsesLeft(Data, c, "breath_weapon"));
        c.ShortRest(Data, new Pcg32(1, 1));
        Assert.Equal(1, ClassFeatures.UsesLeft(Data, c, "breath_weapon"));
        Assert.DoesNotContain(ClassFeatures.ActiveFor(Data, CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 3)), f => f.Id == "breath_weapon");
    }

    [Fact]
    public void ChestTrapsUseRaceResistanceAndEndurance()
    {
        // 爆発の箱(火)は、ティーフリングには半分。同じ乱数なら人間より少ない
        int Hurt(string race)
        {
            var c = CharacterFactory.QuickCreate(Data, "T", race, "fighter", 5);
            c.MaxHp = c.Hp = 500;
            new GravewardSpiral.Core.Items.Chest { Trap = "exploding_box", Tier = 3 }.Trigger(Data, [c], c, new Pcg32(5, 5));
            return 500 - c.Hp;
        }
        Assert.True(Hurt("tiefling") < Hurt("human"));
        // ハーフオークは HP 1 で踏みとどまる
        var orc = CharacterFactory.QuickCreate(Data, "T", "half_orc", "fighter", 1);
        orc.MaxHp = 200;   // 即死にならないよう、最大 HP を大きく
        orc.Hp = 1;
        new GravewardSpiral.Core.Items.Chest { Trap = "crossbow_bolt", Tier = 3 }.Trigger(Data, [orc], orc, new Pcg32(5, 5));
        Assert.Equal(LifeState.Alive, orc.State);
        Assert.Equal(1, orc.Hp);
        // 即死になる大きなダメージ(残りを超えた分が最大 HP 以上)では踏みとどまらない
        var big = CharacterFactory.QuickCreate(Data, "T", "half_orc", "fighter", 1);
        big.MaxHp = big.Hp = 2;
        new GravewardSpiral.Core.Items.Chest { Trap = "crossbow_bolt", Tier = 3 }.Trigger(Data, [big], big, new Pcg32(5, 5));
        Assert.Equal(LifeState.Dead, big.State);
    }

    [Fact]
    public void ElvesAreNotPutToSleepByEyebite()
    {
        var elf = CharacterFactory.QuickCreate(Data, "T", "elf", "fighter", 5);
        var human = CharacterFactory.QuickCreate(Data, "U", "human", "fighter", 5);
        var engine = new CombatEngine(Data, new Pcg32(2, 2));
        var s = engine.Start([elf, human], [(Data.Monsters["goblin"], 1)]);
        var part = new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Unconscious, Rounds = 10 };
        // 呪文から来た眠り(アイバイトの失敗のとき)は、エルフには効かない
        var cast = typeof(CombatEngine).GetNestedType("CastContext", System.Reflection.BindingFlags.NonPublic)!;
        var ctx = Activator.CreateInstance(cast, s.Party[1], Data.Spells["eyebite"], 6, 3, 99);
        var add = typeof(CombatEngine).GetMethod("AddCondition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var m in s.Party) add.Invoke(engine, [s, m, part, null, ctx]);
        Assert.False(s.Party[0].Has(Condition.Unconscious));
        Assert.True(s.Party[1].Has(Condition.Unconscious));
    }
}
