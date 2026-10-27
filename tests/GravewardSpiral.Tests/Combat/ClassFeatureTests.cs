using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Combat;

/// <summary>クラスの能力(マルチクラスでは全部のクラスのもの。戦闘の技と、勝手に効く能力)。</summary>
public class ClassFeatureTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Character Make(string cls, int level) => CharacterFactory.QuickCreate(Data, "T", "human", cls, level);

    private static (CombatEngine Engine, CombatState State) Fight(IEnumerable<Character> party, string monster = "ogre", int count = 1, ulong seed = 1)
    {
        var engine = new CombatEngine(Data, new Pcg32(seed, seed));
        var s = engine.Start(party, [(Data.Monsters[monster], count)]);
        s.Surprised = null;   // 不意打ちで最初のラウンドが飛ばないように
        return (engine, s);
    }

    private static void Round(CombatEngine e, CombatState s, PartyMember p, PlayerCommand cmd) =>
        e.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = cmd });

    [Fact]
    public void EveryClassGivesItsFeaturesAtItsOwnLevel()
    {
        // マルチクラスでは、どちらから始めても、それぞれのクラスのレベルの技を持つ
        var fighterFirst = Make("fighter", 3);
        fighterFirst.Classes.Add(new ClassLevel { ClassId = "wizard", Level = 2 });
        fighterFirst.Level = 5;
        var wizardFirst = Make("wizard", 3);
        wizardFirst.Classes.Add(new ClassLevel { ClassId = "fighter", Level = 2 });
        wizardFirst.Level = 5;
        Assert.Contains(ClassFeatures.ActiveFor(fighterFirst), f => f.Id == "action_surge");
        Assert.Contains(ClassFeatures.ActiveFor(wizardFirst), f => f.Id == "action_surge");   // ファイター 2 で借りられる
        Assert.Equal(1, ClassFeatures.MaxUses(wizardFirst, "second_wind"));
        Assert.Equal(1, ClassFeatures.MaxUses(wizardFirst, "arcane_recovery"));
        Assert.Equal(1, ClassFeatures.MaxUses(fighterFirst, "arcane_recovery"));
        Assert.Equal(0, ClassFeatures.MaxUses(Make("fighter", 5), "arcane_recovery"));
        // 回数を数えるものは、どれもそのクラスを持っていなければ 0
        var paladin = Make("paladin", 1);
        Assert.Equal(["lay_on_hands"], ClassFeatures.Pools.Where(p => ClassFeatures.MaxUses(paladin, p) > 0));
    }

    [Fact]
    public void SecondWindHealsAndComesBackOnAShortRest()
    {
        var c = Make("fighter", 4);
        c.Hp = 5;
        var (e, s) = Fight([c]);
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        Round(e, s, s.Party[0], new PlayerCommand(CommandKind.Feature, FeatureId: "second_wind"));
        Assert.True(c.Hp >= 5 + 1 + 4);
        Assert.Equal(0, ClassFeatures.UsesLeft(c, "second_wind"));
        c.ShortRest(Data, new Pcg32(1, 1));
        Assert.Equal(1, ClassFeatures.UsesLeft(c, "second_wind"));
    }

    [Fact]
    public void RageHalvesPhysicalDamageAndStopsSpells()
    {
        var c = Make("barbarian", 3);
        var (e, s) = Fight([c]);
        var p = s.Party[0];
        Round(e, s, p, new PlayerCommand(CommandKind.Feature, 0, FeatureId: "rage"));
        Assert.True(p.Raging);
        Assert.Equal(2, ClassFeatures.UsesLeft(c, "rage"));   // L3 は 3 回
        p.Hp = p.MaxHp;
        e.Damage(s, p, 10, "slashing", false, false);
        Assert.Equal(p.MaxHp - 5, p.Hp);
        e.Damage(s, p, 10, "fire", false, false);
        Assert.Equal(p.MaxHp - 15, p.Hp);
        e.RunRound(s, new Dictionary<string, PlayerCommand>());   // 戦闘が続いても激怒は続く
        Assert.True(p.Raging || s.Outcome != CombatOutcome.Ongoing);
    }

    [Fact]
    public void LayOnHandsUsesThePool()
    {
        var paladin = Make("paladin", 2);
        var ally = Make("fighter", 2);
        ally.Hp = 3;
        var (e, s) = Fight([paladin, ally]);
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        int need = ally.MaxHp - 3;
        Round(e, s, s.Party[0], new PlayerCommand(CommandKind.Feature, -1, 1, FeatureId: "lay_on_hands"));
        Assert.Equal(Math.Min(ally.MaxHp, 3 + 10), ally.Hp);
        Assert.Equal(10 - Math.Min(10, need), ClassFeatures.UsesLeft(paladin, "lay_on_hands"));
    }

    [Fact]
    public void WildShapeTakesTheBeastsHpAndOverflowCarriesOver()
    {
        var c = Make("druid", 4);
        var (e, s) = Fight([c]);
        var p = s.Party[0];
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        int hp = c.Hp;
        Round(e, s, p, new PlayerCommand(CommandKind.Feature, FeatureId: "wild_shape"));
        Assert.Equal("black_bear", p.Shape?.Id);
        Assert.Equal(Data.Monsters["black_bear"].HpAverage, p.Hp);
        e.Damage(s, p, p.Hp + 3, "slashing", false, false);
        Assert.Null(p.Shape);
        Assert.Equal(hp - 3, c.Hp);
    }

    [Fact]
    public void WildShapeLetsTheDruidPickTheBeast()
    {
        Assert.Equal(["wolf", "black_bear"], ClassFeatures.WildShapeBeasts(Make("druid", 4)));
        Assert.Equal(["dire_wolf", "brown_bear"], ClassFeatures.WildShapeBeasts(Make("druid", 8)));
        var c = Make("druid", 4);
        var (e, s) = Fight([c]);
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        Round(e, s, s.Party[0], new PlayerCommand(CommandKind.Feature, FeatureId: "wild_shape", Option: "wolf"));
        Assert.Equal("wolf", s.Party[0].Shape?.Id);
        Assert.Equal("wolf", s.Snapshot()[0].Shape);
    }

    [Fact]
    public void ADruidOf18CastsInBeastForm()
    {
        Assert.False(ClassFeatures.CanCastIn(Make("druid", 17), raging: false, shaped: true));
        Assert.True(ClassFeatures.CanCastIn(Make("druid", 18), raging: false, shaped: true));
        Assert.Equal(99, ClassFeatures.MaxUses(Make("druid", 20), "wild_shape"));
    }

    [Fact]
    public void ABeastKeepsAttackingWhenItsGroupFalls()
    {
        // ヒグマの2回攻撃の1回目でグループが全滅しても、2回目で止まらない(次のグループへ)
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var c = Make("druid", 8);
            var engine = new CombatEngine(Data, new Pcg32(seed, seed));
            var s = engine.Start([c], [(Data.Monsters["goblin"], 1), (Data.Monsters["goblin"], 2)]);
            s.Surprised = null;
            s.Groups[0].Units[0].Hp = 1;
            var p = s.Party[0];
            p.Shape = Data.Monsters["brown_bear"];
            p.ShapeHp = p.Shape.HpAverage;
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Attack, 0) });
        }
    }

    [Fact]
    public void TurnUndeadDestroysWeakUndead()
    {
        // L5 のクレリックは、脅威度 1/2 以下のアンデッドを、セーヴに失敗したら滅ぼす(何度か振って、滅びるものが出る)
        int destroyed = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var c = Make("cleric", 5);
            var (e, s) = Fight([c], "zombie", 3, seed);
            Round(e, s, s.Party[0], new PlayerCommand(CommandKind.Feature, FeatureId: "turn_undead"));
            destroyed += s.Defeated.Count;
        }
        Assert.True(destroyed > 0);
    }

    [Fact]
    public void AuraOfProtectionAddsCharismaToAlliesSaves()
    {
        var paladin = Make("paladin", 6);
        var ally = Make("wizard", 6);
        var (_, s) = Fight([paladin, ally]);
        int cha = Math.Max(1, paladin.Scores.Mod(Ability.Cha));
        Assert.Equal(ally.SaveBonus(Data, Ability.Wis) + cha, s.Party[1].SaveBonus(Ability.Wis));
    }

    [Fact]
    public void EvasionTakesNothingOnASuccess()
    {
        var c = Make("rogue", 7);
        c.Scores[Ability.Dex] = 30;   // 必ず成功する
        var (e, s) = Fight([c]);
        var p = s.Party[0];
        int hp = p.Hp;
        e.ApplyToParty(s, s.Groups[0].Units[0], [new EffectPart { Type = EffectPart.Save, Ability = "dex", Dc = 5, Half = true,
            Fail = [new EffectPart { Type = EffectPart.Damage, Dice = "20", DamageType = "fire" }] }]);
        Assert.Equal(hp, p.Hp);
    }

    [Fact]
    public void ProtectionFromPoisonCuresAndProtects()
    {
        // 毒を解き、毒のダメージに抵抗し、毒にするセーヴに有利(この戦闘のあいだ)
        var c = Make("cleric", 3);
        var (e, s) = Fight([c]);
        var p = s.Party[0];
        p.Conditions.Add(new ActiveCondition { Condition = Condition.Poisoned });
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        c.Book("cleric").Prepared.Add("protection_from_poison");
        Round(e, s, p, new PlayerCommand(CommandKind.Cast, -1, 0, "protection_from_poison", 2));
        Assert.False(p.Has(Condition.Poisoned));
        Assert.Equal(0.5, p.DamageMultiplier("poison", false));
        Assert.Contains(p.Modifiers, m => m.Stat == "save_adv:poisoned");
    }

    [Fact]
    public void ArcaneRecoveryGivesSlotsBackOncePerLongRest()
    {
        var c = Make("wizard", 4);
        c.SlotsUsed[2] = 1;
        c.SlotsUsed[1] = 2;
        c.ShortRest(Data, new Pcg32(1, 1));
        Assert.Equal(2, c.LastSlotsRecovered);   // レベルの半分(2)まで: 2 レベルを1つ
        Assert.False(c.SlotsUsed.ContainsKey(2));
        c.ShortRest(Data, new Pcg32(1, 1));
        Assert.Equal(0, c.LastSlotsRecovered);
        c.LongRest();
        Assert.Equal(1, ClassFeatures.UsesLeft(c, "arcane_recovery"));
    }

    [Fact]
    public void EmpoweredSpellSpendsASorceryPoint()
    {
        var c = Make("sorcerer", 3);
        var spell = Data.Spells.Values.First(x => x.Level == 1 && x.Classes.Contains("sorcerer") && x.TargetsEnemies
            && x.Effects.Any(e => e.Type == EffectPart.Damage) && c.Books.Values.Any(b => b.Has(x.Id)) || x.Id == "magic_missile" && x.Classes.Contains("sorcerer"));
        c.Book("sorcerer").Known.Add(spell.Id);
        Assert.True(CombatEngine.CanEmpower(c, spell));
        var (e, s) = Fight([c]);
        Round(e, s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, -1, spell.Id, 1, Empower: true));
        Assert.Equal(2, ClassFeatures.UsesLeft(c, "sorcery_points"));
        Assert.Contains(s.Log, l => l.Key == "combat.empowered");
    }

    [Fact]
    public void AutoCommandsRageInTheFrontRow()
    {
        var c = Make("barbarian", 3);
        var (_, s) = Fight([c]);
        var cmd = CombatSimulator.AutoCommands(s, Data)[c.Id];
        Assert.Equal(CommandKind.Feature, cmd.Kind);
        Assert.Equal("rage", cmd.FeatureId);
        Assert.Equal(CommandKind.Attack, CombatSimulator.AutoCommands(s, Data, useSpells: false)[c.Id].Kind);
    }
}
