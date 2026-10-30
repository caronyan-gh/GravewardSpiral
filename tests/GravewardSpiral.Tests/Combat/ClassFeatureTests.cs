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

    /// <summary>
    /// 組み替えた技と効果(astra のレビュー、DB のテストケース 348): 技はデータの pool と cost で回数を減らし、
    /// max のまとまりの反応と追加ダメージは一番大きいもの1つ、大小を比べられない max のまとまりは検査で拾う。
    /// </summary>
    [Fact]
    public void ReassembledActionsAndMaxGroupsFollowTheData()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "flurry_of_blows", "kind": "action", "target": "enemy_group", "pool": "ki", "cost": 2, "beast_form": false},
             {"id": "patient_defense", "kind": "action", "target": "enemy_group", "pool": "calm"},
             {"id": "guard_small", "kind": "passive", "group": "guard", "stacks": "max", "effects": [{"type": "reaction_reduce", "on": "attacked", "value": 4}]},
             {"id": "guard_big", "kind": "passive", "group": "guard", "stacks": "max", "effects": [{"type": "reaction_reduce", "on": "attacked", "value": 9}]},
             {"id": "fire_small", "kind": "passive", "group": "fire", "stacks": "max", "effects": [{"type": "extra_damage", "on": "melee_weapon", "dice": "1d6", "damage_type": "fire"}]},
             {"id": "fire_big", "kind": "passive", "group": "fire", "stacks": "max", "effects": [{"type": "extra_damage", "on": "melee_weapon", "dice": "2d6", "damage_type": "fire"}]}]
            """);
        dir.Write("p/classes.json", """
            [{"id": "test_monk", "extends": "monk", "pools": [{"id": "ki", "max": {"class_level": 1}, "recharge": "short", "from_level": 2}, {"id": "calm", "max": 2, "recharge": "short"}],
              "features": [{"id": "flurry_of_blows", "level": 2}, {"id": "patient_defense", "level": 2}, {"id": "guard_small", "level": 1}, {"id": "guard_big", "level": 1},
                           {"id": "fire_small", "level": 1}, {"id": "fire_big", "level": 1}, {"id": "martial_arts", "level": 1}, {"id": "unarmored_defense_wis", "level": 1}]}]
            """);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var c = CharacterFactory.QuickCreate(data, "T", "human", "test_monk", 5);
        var engine = new CombatEngine(data, new Pcg32(1, 1));
        var s = engine.Start([c], [(data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        var p = s.Party[0];
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Feature, FeatureId: "flurry_of_blows") });
        Assert.Equal(5 - 2, ClassFeatures.UsesLeft(data, c, "ki"));   // cost 2
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Feature, FeatureId: "patient_defense") });
        Assert.Equal(3, ClassFeatures.UsesLeft(data, c, "ki"));        // 気は減らず
        Assert.Equal(1, ClassFeatures.UsesLeft(data, c, "calm"));      // データの pool から減る

        Assert.Equal(9, FeatureChecks.ReactionFor(data, p, ranged: false)!.Bonus);   // max は大きいほう
        Assert.Equal([("2d6", "fire")], FeatureChecks.MeleeWeaponExtraDamage(data, p));

        using var dir2 = new TestPaths.TempDir();
        dir2.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir2.Write("p/features.json", """
            [{"id": "mix_a", "kind": "passive", "group": "mix", "stacks": "max", "effects": [{"type": "reaction_reduce", "on": "attacked", "value": 4}]},
             {"id": "mix_b", "kind": "passive", "group": "mix", "stacks": "max", "effects": [{"type": "reaction_halve", "on": "attacked"}]}]
            """);
        var bad = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir2.Path, "p"));
        GameData.Load(bad);
        Assert.Contains(bad.Problems, x => x.Contains("'mix'"));
    }

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
        Assert.Contains(ClassFeatures.ActiveFor(Data, fighterFirst), f => f.Id == "action_surge");
        Assert.Contains(ClassFeatures.ActiveFor(Data, wizardFirst), f => f.Id == "action_surge");   // ファイター 2 で借りられる
        Assert.Equal(1, ClassFeatures.MaxUses(Data, wizardFirst, "second_wind"));
        Assert.Equal(1, ClassFeatures.MaxUses(Data, wizardFirst, "arcane_recovery"));
        Assert.Equal(1, ClassFeatures.MaxUses(Data, fighterFirst, "arcane_recovery"));
        Assert.Equal(0, ClassFeatures.MaxUses(Data, Make("fighter", 5), "arcane_recovery"));
        // 回数を数えるものは、どれもそのクラスを持っていなければ 0
        var paladin = Make("paladin", 1);
        Assert.Equal(["lay_on_hands"], ClassFeatures.PoolsOf(Data, paladin));
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
        Assert.Equal(0, ClassFeatures.UsesLeft(Data, c, "second_wind"));
        c.ShortRest(Data, new Pcg32(1, 1));
        Assert.Equal(1, ClassFeatures.UsesLeft(Data, c, "second_wind"));
    }

    [Fact]
    public void RageHalvesPhysicalDamageAndStopsSpells()
    {
        var c = Make("barbarian", 3);
        var (e, s) = Fight([c]);
        var p = s.Party[0];
        Round(e, s, p, new PlayerCommand(CommandKind.Feature, 0, FeatureId: "rage"));
        Assert.True(p.Raging);
        Assert.Equal(2, ClassFeatures.UsesLeft(Data, c, "rage"));   // L3 は 3 回
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
        Assert.Equal(10 - Math.Min(10, need), ClassFeatures.UsesLeft(Data, paladin, "lay_on_hands"));
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
        Assert.Equal(["wolf", "black_bear"], ClassFeatures.WildShapeBeasts(Data, Make("druid", 4)));
        Assert.Equal(["dire_wolf", "brown_bear"], ClassFeatures.WildShapeBeasts(Data, Make("druid", 8)));
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
        Assert.False(ClassFeatures.CanCastIn(Data, Make("druid", 17), raging: false, shaped: true));
        Assert.True(ClassFeatures.CanCastIn(Data, Make("druid", 18), raging: false, shaped: true));
        Assert.Equal(99, ClassFeatures.MaxUses(Data, Make("druid", 20), "wild_shape"));
    }

    private static Character Multi(params (string Class, int Level)[] classes)
    {
        var c = Make(classes[0].Class, classes[0].Level);
        foreach (var (cls, lv) in classes.Skip(1)) c.Classes.Add(new ClassLevel { ClassId = cls, Level = lv });
        c.Level = classes.Sum(x => x.Level);
        return c;
    }

    [Fact]
    public void StrikeTechniquesAreNotUsableInBeastForm()
    {
        // ドルイド 4 / モンク 2 の狼: 連打は選べず、使おうとしても気は減らない
        var c = Multi(("druid", 4), ("monk", 2));
        var (e, s) = Fight([c]);
        var p = s.Party[0];
        p.Shape = Data.Monsters["wolf"];
        p.ShapeHp = p.Shape.HpAverage;
        Assert.False(ClassFeatures.UsableInBeastForm(ClassFeatures.Action(Data, "flurry_of_blows")!));
        Round(e, s, p, new PlayerCommand(CommandKind.Feature, 0, FeatureId: "flurry_of_blows"));
        Assert.Equal(2, ClassFeatures.UsesLeft(Data, c, "ki"));
        Assert.DoesNotContain(s.Log, l => l.Key == "combat.flurry");
    }

    [Fact]
    public void ActionSurgeDoesNotRepeatTheBonusAction()
    {
        // ファイター 2 / モンク 2 の素手: ふつうは攻撃1回 + 武術1回、サージでは 攻撃2回 + 武術1回 = 3回
        int Attacks(PlayerCommand cmd)
        {
            var c = Multi(("fighter", 2), ("monk", 2));
            c.Inventory.Equipped.Clear();
            var (e, s) = Fight([c], "ogre", 1, 4);
            s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
            s.Groups[0].Units[0].Hp = 999;
            Round(e, s, s.Party[0], cmd);
            return s.Log.Count(l => l.Key is "combat.hit" or "combat.crit" or "combat.miss" && l.Args[0] is LocalizedText t && t.Get("en") == c.Name);
        }
        Assert.Equal(2, Attacks(new PlayerCommand(CommandKind.Attack, 0)));
        Assert.Equal(3, Attacks(new PlayerCommand(CommandKind.Feature, 0, FeatureId: "action_surge")));
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
        Assert.Equal(1, ClassFeatures.UsesLeft(Data, c, "arcane_recovery"));
    }

    [Fact]
    public void EmpoweredSpellSpendsASorceryPoint()
    {
        var c = Make("sorcerer", 3);
        var spell = Data.Spells.Values.First(x => x.Level == 1 && Data.ClassesWithSpell(x.Id).Any(k => k.Id == "sorcerer") && x.TargetsEnemies
            && x.Effects.Any(e => e.Type == EffectPart.Damage) && c.Books.Values.Any(b => b.Has(x.Id)) || x.Id == "magic_missile" && Data.ClassesWithSpell(x.Id).Any(k => k.Id == "sorcerer"));
        c.Book("sorcerer").Known.Add(spell.Id);
        Assert.True(CombatEngine.CanEmpower(Data, c, spell));
        var (e, s) = Fight([c]);
        Round(e, s, s.Party[0], new PlayerCommand(CommandKind.Cast, 0, -1, spell.Id, 1, Empower: true));
        Assert.Equal(2, ClassFeatures.UsesLeft(Data, c, "sorcery_points"));
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

    [Fact]
    public void FlurryIsNotOfferedInArmor()
    {
        var c = Make("monk", 3);
        var flurry = ClassFeatures.Action(Data, "flurry_of_blows")!;
        Assert.True(ClassFeatures.CanUse(Data, c, flurry));
        c.Inventory.Equipped[GravewardSpiral.Core.Items.EquipSlot.Armor] = new GravewardSpiral.Core.Items.ItemInstance { ItemId = "leather_armor", Identified = true };
        Assert.False(ClassFeatures.CanUse(Data, c, flurry));   // 鎧では素手の連打が出ないので、選べない(気だけ減らない)
    }
}
