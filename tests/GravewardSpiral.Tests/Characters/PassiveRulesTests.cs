using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>勝手に効く能力の when と effects(docs/class-data-design.md の Step 4c)。</summary>
public class PassiveRulesTests
{
    private static Character Multi(params (string Id, int Level)[] levels)
    {
        var c = new Character
        {
            RaceId = "human",
            ClassId = levels[0].Id,
            Level = levels.Sum(x => x.Level),
            Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(),
            MaxHp = 10,
            Hp = 10,
        };
        foreach (var a in Abilities.All) c.Scores[a] = 14;
        return c;
    }

    private static (GameData Data, ContentLibrary Content) WithPack(string file, string json)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write($"p/{file}", json);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    [Fact]
    public void ACopiedPaladinsAurasWorkFromTheData()
    {
        var (data, content) = WithPack("classes.json", """[{"id": "test_paladin", "extends": "paladin"}]""");
        Assert.Empty(content.Problems);
        var p = new PartyMember(Multi(("test_paladin", 10)), data);
        var friend = new PartyMember(Multi(("fighter", 10)), data);
        p.Allies = friend.Allies = [p, friend];
        Assert.Equal(2, friend.SaveBonus(Ability.Wis) - friend.Character.SaveBonus(data, Ability.Wis));   // 魅力 14 で +2
        Assert.True(friend.ImmuneTo(Condition.Frightened));
        p.Character.Hp = 0;   // 倒れたらオーラは消える
        Assert.Equal(0, friend.SaveBonus(Ability.Wis) - friend.Character.SaveBonus(data, Ability.Wis));
        Assert.False(friend.ImmuneTo(Condition.Frightened));
    }

    [Fact]
    public void UnarmoredDefenseUsesTheFirstClassOnly()
    {
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        var monkFirst = Multi(("monk", 1), ("barbarian", 1));
        monkFirst.Scores[Ability.Wis] = 18;   // +4
        monkFirst.Scores[Ability.Con] = 10;   // 0
        Assert.Equal(10 + 2 + 4, monkFirst.ArmorClass(data));
        Assert.Equal(["unarmored_defense_wis"], ClassFeatures.Passives(data, monkFirst).Where(x => x.StartsWith("unarmored")));
    }

    /// <summary>
    /// 組み替えたときの届き方と重なり方(astra のレビュー、DB のテストケース 342): 自分への save_bonus は本人に、仲間への immune_damage は仲間にも、
    /// 別のまとまりや own の save_bonus は足し、max のまとまりはパーティ全体で一番大きいもの1つ。
    /// </summary>
    [Fact]
    public void EffectsReachTheRightPeopleAndStackByGroup()
    {
        var (d2, c2) = Assorted();
        Assert.Empty(c2.Problems);
        PartyMember Member(string cls) => new(Multi((cls, 1)), d2);
        var warded = Member("t_self");
        var giver = Member("t_poison");
        var friend = Member("fighter");
        warded.Allies = giver.Allies = friend.Allies = [warded, giver, friend];
        int Extra(PartyMember m, Ability a) => m.SaveBonus(a) - m.Character.SaveBonus(d2, a);
        Assert.Equal(3, Extra(warded, Ability.Wis));   // 自分への +3(仲間には届かない)
        Assert.Equal(0, Extra(friend, Ability.Wis));
        var traits = typeof(Combatant).GetMethod("DamageTraits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        bool PoisonImmune(PartyMember m) => (((bool, bool, bool))traits.Invoke(m, ["poison", false])!).Item1;
        Assert.True(PoisonImmune(giver));
        Assert.True(PoisonImmune(friend));   // 仲間にも効く
        var two = Member("t_two");
        var three = Member("t_three");
        var big1 = Member("t_big");
        var big2 = Member("t_big_small");
        friend.Allies = [friend, two, three, big1, big2];
        Assert.Equal(2 + 3 + 4, Extra(friend, Ability.Con));   // 別の能力の +2 と +3 は足し、max のまとまりは 4 と 1 の大きいほう
    }

    /// <summary>試しの能力と、それを1つずつ L1 で持つ試しのクラスを足したデータ。</summary>
    private static (GameData Data, ContentLibrary Content) Assorted()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "self_ward", "kind": "passive", "effects": [{"type": "save_bonus", "value": 3}]},
             {"id": "poison_aura", "kind": "passive", "when": "standing", "effects": [{"type": "immune_damage", "damage_type": "poison", "to": "allies"}]},
             {"id": "aura_two", "kind": "passive", "when": "standing", "effects": [{"type": "save_bonus", "to": "allies", "value": 2}]},
             {"id": "aura_three", "kind": "passive", "when": "standing", "effects": [{"type": "save_bonus", "to": "allies", "value": 3}]},
             {"id": "big_aura", "kind": "passive", "when": "standing", "stacks": "max", "group": "big", "effects": [{"type": "save_bonus", "to": "allies", "value": 4}]},
             {"id": "big_aura_small", "kind": "passive", "when": "standing", "stacks": "max", "group": "big", "effects": [{"type": "save_bonus", "to": "allies", "value": 1}]}]
            """);
        dir.Write("p/classes.json", """
            [{"id": "t_self", "extends": "fighter", "features": [{"id": "self_ward", "level": 1}]},
             {"id": "t_poison", "extends": "fighter", "features": [{"id": "poison_aura", "level": 1}]},
             {"id": "t_two", "extends": "fighter", "features": [{"id": "aura_two", "level": 1}]},
             {"id": "t_three", "extends": "fighter", "features": [{"id": "aura_three", "level": 1}]},
             {"id": "t_big", "extends": "fighter", "features": [{"id": "big_aura", "level": 1}]},
             {"id": "t_big_small", "extends": "fighter", "features": [{"id": "big_aura_small", "level": 1}]}]
            """);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    [Fact]
    public void ACopiedBarbarianAndRogueGetAdvantageAndEvasionFromTheData()
    {
        var (data, content) = WithPack("classes.json", """[{"id": "test_barbarian", "extends": "barbarian"}, {"id": "test_rogue", "extends": "rogue"}]""");
        Assert.Empty(content.Problems);
        var barb = new PartyMember(Multi(("test_barbarian", 7)), data);
        var rogue = new PartyMember(Multi(("test_rogue", 18)), data);
        Assert.True(FeatureChecks.InitiativeAdvantage(data, barb));
        Assert.Equal(0, FeatureChecks.Attack(data, barb, rogue, melee: true).Advantage);   // 激怒していなければ有利にならない
        barb.Raging = true;
        Assert.Equal(1, FeatureChecks.Attack(data, barb, rogue, melee: true).Advantage);   // 激怒の近接
        Assert.Equal(0, FeatureChecks.Attack(data, barb, rogue, melee: false).Advantage);
        Assert.True(FeatureChecks.Attack(data, barb, rogue, melee: true).CancelAdvantage);   // ローグ 18 への攻撃は有利にならない
        Assert.True(FeatureChecks.SaveAdvantage(data, barb, Ability.Dex));
        Assert.True(FeatureChecks.Evades(data, rogue));
        rogue.Conditions.Add(new Core.Combat.ActiveCondition { Condition = Condition.Stunned });
        Assert.False(FeatureChecks.Evades(data, rogue));   // 行動不能なら回避しない
    }

    /// <summary>
    /// 組み替えたときの有利とスタイル(astra のレビュー、DB のテストケース 345): 決めた能力値のセーヴの有利、max のまとまりの AC、
    /// 激怒中だけのダメージ、振り直しの値、持っているスタイルだけの表示。
    /// </summary>
    [Fact]
    public void ReassembledAdvantageAndStylesFollowTheData()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "wis_ward", "kind": "passive", "effects": [{"type": "advantage", "roll": "save", "ability": "wis"}]},
             {"id": "all_ward", "kind": "passive", "effects": [{"type": "advantage", "roll": "save"}]},
             {"id": "ac_two", "kind": "passive", "group": "g", "stacks": "max", "effects": [{"type": "ac_bonus", "value": 2}]},
             {"id": "ac_three", "kind": "passive", "group": "g", "stacks": "max", "effects": [{"type": "ac_bonus", "value": 3}]},
             {"id": "rage_hit", "kind": "passive", "when": "raging", "effects": [{"type": "damage_bonus", "on": "main_melee", "value": 3}]},
             {"id": "big_reroll", "kind": "passive", "effects": [{"type": "reroll_damage", "on": "main_melee", "value": 3}]}]
            """);
        dir.Write("p/classes.json", """
            [{"id": "t_wis", "extends": "fighter", "features": [{"id": "wis_ward", "level": 1}]},
             {"id": "t_all", "extends": "fighter", "features": [{"id": "all_ward", "level": 1}]},
             {"id": "t_ac", "extends": "fighter", "features": [{"id": "ac_two", "level": 1}, {"id": "ac_three", "level": 1}]},
             {"id": "t_rage", "extends": "fighter", "features": [{"id": "rage_hit", "level": 1}, {"id": "big_reroll", "level": 1}]},
             {"id": "t_archer", "extends": "fighter", "features": [{"id": "style_archery", "level": 1}]}]
            """);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        PartyMember Member(string cls) => new(Multi((cls, 1)), data);

        var wis = Member("t_wis");
        Assert.True(FeatureChecks.SaveAdvantage(data, wis, Ability.Wis));
        Assert.False(FeatureChecks.SaveAdvantage(data, wis, Ability.Dex));
        Assert.True(FeatureChecks.SaveAdvantage(data, Member("t_all"), Ability.Cha));   // 能力値を書かなければ全部

        var ac = Member("t_ac");
        var plain = Member("fighter");
        Assert.Equal(3, ac.Character.ArmorClass(data) - plain.Character.ArmorClass(data));   // max のまとまりは +3 だけ

        var rager = Member("t_rage");
        Assert.Equal(0, FeatureChecks.MainMeleeDamageBonus(data, rager));
        rager.Raging = true;
        Assert.Equal(3, FeatureChecks.MainMeleeDamageBonus(data, rager));   // 戦闘で激怒していれば
        Assert.Equal(3, FeatureChecks.MainMeleeRerollLow(data, rager));     // 振り直しは value の目まで

        var archer = Member("t_archer");
        archer.Character.Inventory.Equipped[EquipSlot.MainHand] = new Core.Items.ItemInstance { ItemId = "longsword", Identified = true };
        Assert.Empty(ClassFeatures.ActiveStyles(data, archer.Character));   // 弓術しか持たないので、剣では何も出さない
        archer.Character.Inventory.Equipped[EquipSlot.Ranged] = new Core.Items.ItemInstance { ItemId = "longbow", Identified = true };
        Assert.Equal(["style_archery"], ClassFeatures.ActiveStyles(data, archer.Character));
    }

    [Fact]
    public void OnHitEffectsFollowTheData()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "flame_edge", "kind": "passive", "effects": [{"type": "extra_damage", "on": "melee_weapon", "dice": "2d6", "damage_type": "fire"}]},
             {"id": "bolt_boost", "kind": "passive", "effects": [{"type": "spell_damage_bonus", "spell": "fire_bolt", "value": 2}]},
             {"id": "rage_drain", "kind": "passive", "when": "raging", "effects": [{"type": "drain", "on": "melee_weapon", "value": 50}]}]
            """);
        dir.Write("p/classes.json", """
            [{"id": "t_flame", "extends": "fighter", "features": [{"id": "flame_edge", "level": 1}, {"id": "bolt_boost", "level": 1}, {"id": "rage_drain", "level": 1}]},
             {"id": "t_paladin", "extends": "paladin"}]
            """);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var flame = new PartyMember(Multi(("t_flame", 1)), data);
        Assert.Equal([("2d6", "fire")], FeatureChecks.MeleeWeaponExtraDamage(data, flame));
        Assert.Equal(2, FeatureChecks.SpellDamageBonus(data, flame, "fire_bolt"));
        Assert.Equal(0, FeatureChecks.SpellDamageBonus(data, flame, "eldritch_blast"));
        Assert.Equal(0, FeatureChecks.MeleeWeaponDrain(data, flame));
        flame.Raging = true;
        Assert.Equal(50, FeatureChecks.MeleeWeaponDrain(data, flame));   // 激怒のあいだだけ
        Assert.Equal([("1d8", "radiant")], FeatureChecks.MeleeWeaponExtraDamage(data, new PartyMember(Multi(("t_paladin", 11)), data)));
    }

    [Fact]
    public void ReactionsAndStayingUpFollowTheData()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "shield_wall", "kind": "passive", "effects": [{"type": "reaction_reduce", "on": "attacked", "value": 4}]},
             {"id": "arrow_catch", "kind": "passive", "effects": [{"type": "reaction_halve", "on": "ranged_attacked", "message": "combat.uncanny_dodge"}]},
             {"id": "grit", "kind": "passive", "effects": [{"type": "stay_up", "ability": "wis", "value": 12, "step": 3}]}]
            """);
        dir.Write("p/classes.json", """
            [{"id": "t_guard", "extends": "fighter", "features": [{"id": "shield_wall", "level": 1}, {"id": "arrow_catch", "level": 1}, {"id": "grit", "level": 1}]}]
            """);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var guard = new PartyMember(Multi(("t_guard", 1)), data);
        Assert.Equal(new FeatureChecks.Reaction(false, null, null, 4, null), FeatureChecks.ReactionFor(data, guard, ranged: false));
        Assert.True(FeatureChecks.ReactionFor(data, guard, ranged: true)!.Halve);   // 飛び道具には、飛び道具に限る反応を先に
        Assert.Equal([new FeatureChecks.StayUpWay(Ability.Wis, 12, 3, null, null)], FeatureChecks.StayUp(data, guard));   // 激怒の条件が無ければいつも
    }

    [Fact]
    public void BadPassivesAreReported()
    {
        var (_, content) = WithPack("features.json", """
            [{"id": "bad_when", "kind": "passive", "when": "on_tuesday"},
             {"id": "bad_part", "kind": "passive", "effects": [{"type": "fly"}]},
             {"id": "bad_ac", "kind": "passive", "effects": [{"type": "ac_ability"}]},
             {"id": "bad_stacks", "kind": "passive", "stacks": "most"},
             {"id": "bad_roll", "kind": "passive", "effects": [{"type": "advantage", "roll": "dance"}]},
             {"id": "bad_extra", "kind": "passive", "effects": [{"type": "extra_damage", "on": "melee_weapon", "damage_type": "fire"}]},
             {"id": "bad_on", "kind": "passive", "effects": [{"type": "drain", "on": "spell", "value": 10}]},
             {"id": "bad_stay", "kind": "passive", "effects": [{"type": "stay_up", "value": 10}]}]
            """);
        string all = string.Join("\n", content.Problems);
        Assert.Contains("on_tuesday", all);
        Assert.Contains("'fly'", all);
        Assert.Contains("ac_ability には ability", all);
        Assert.Contains("stacks 'most'", all);
        Assert.Contains("advantage の roll", all);
        Assert.Contains("extra_damage には dice", all);
        Assert.Contains("drain の on", all);
        Assert.Contains("stay_up には ability", all);
    }
}
