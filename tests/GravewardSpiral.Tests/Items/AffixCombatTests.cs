using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Items;

/// <summary>戦闘で効く追加効果(属性のダメージ軽減・棘・追い打ち)。</summary>
public class AffixCombatTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static ItemInstance Rare(string id, string affix, int tier)
    {
        var it = ItemInstance.Of(id);
        it.Rare = it.Attuned = true;
        it.Affixes.Add(new AffixRoll { Id = affix, Tier = tier });
        return it;
    }

    [Fact]
    public void ReductionCutsOnlyThatDamageType()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        c.Inventory.Equipped[EquipSlot.Cloak] = Rare("plain_cloak", "reduce_fire", 4);   // T5: −4
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        var p = s.Party[0];
        int before = p.Hp;
        engine.ApplyToParty(s, s.Groups[0].Units[0], [
            new EffectPart { Type = EffectPart.Damage, Dice = "10", DamageType = "fire" },
            new EffectPart { Type = EffectPart.Damage, Dice = "3", DamageType = "cold" },
        ]);
        Assert.Equal(before - 6 - 3, p.Hp);
        Assert.Contains(s.Log, e => e.Key == "combat.reduced");
    }

    [Fact]
    public void NewAffixStatsAreValidAndSitOnTheirSlots()
    {
        Assert.DoesNotContain(Content.Problems, p => p.Contains("affixes"));
        Assert.Equal(5, Data.Affixes["flurry"].Kinds.Count);
        Assert.Equal(5, Data.Affixes["flurry"].Tiers[4].Passive[0].Value);   // T5 で 5%
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        c.Inventory.Equipped[EquipSlot.Armor] = Rare("chain_mail", "thorns", 4);
        Assert.Equal(6, c.PassiveBonus(Data, "thorns"));
        c.Inventory.Equipped[EquipSlot.Hands] = Rare("leather_gloves", "flurry", 4);
        Assert.Equal(5, c.PassiveBonus(Data, "extra_attack"));
    }

    [Fact]
    public void SkillAffixesHelpEventChecksAndChestInspection()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        int before = Chest.InspectBonus(Data, c);
        c.Inventory.Equipped[EquipSlot.Head] = Rare("plain_circlet", "skill_investigation", 4);   // T5: +4
        Assert.Equal(before + 4, Chest.InspectBonus(Data, c));
    }

    [Fact]
    public void UsesAffixAddsToThatClassPoolOnly()
    {
        // 憤怒の: 激怒の回数 +段/5 切り上げ(T6 で +2)。激怒を持たないクラスには効かない
        var barb = CharacterFactory.QuickCreate(Data, "B", "human", "barbarian", 3);
        int before = ClassFeatures.MaxUses(Data, barb, "rage");
        barb.Inventory.Equipped[EquipSlot.Waist] = Rare("leather_belt", "uses_rage", 5);
        Assert.Equal(before + 2, ClassFeatures.MaxUses(Data, barb, "rage"));
        var fighter = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 3);
        fighter.Inventory.Equipped[EquipSlot.Waist] = Rare("leather_belt", "uses_rage", 5);
        Assert.Equal(0, ClassFeatures.MaxUses(Data, fighter, "rage"));
    }

    [Fact]
    public void SweepAndFlurryHappenInRealFights()
    {
        // 確率を 100% にした写しのデータで、薙ぎ(もう1体にも)と追い打ち(もう1回)がログに出る
        var data = GameData.Load(Content);
        data.Affixes["cleave"].Tiers[0].Passive[0].Value = 100;
        data.Affixes["flurry"].Tiers[0].Passive[0].Value = 100;
        var c = CharacterFactory.QuickCreate(data, "T", "human", "fighter", 5);
        var main = c.Inventory[EquipSlot.MainHand]!;   // 薙ぎは、その武器で殴ったときだけ
        main.Rare = main.Attuned = true;
        main.Affixes.Add(new AffixRoll { Id = "cleave", Tier = 0 });
        c.Inventory.Equipped[EquipSlot.Ring2] = Rare("plain_ring", "flurry", 0);
        bool swept = false, again = false;
        for (ulong seed = 1; seed <= 20 && !(swept && again); seed++)
        {
            var engine = new CombatEngine(data, new Pcg32(seed, 3));
            var s = engine.Start([c], [(data.Monsters["goblin"], 4)]);
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Attack, TargetGroup: 0) });
            swept |= s.Log.Any(e => e.Key == "combat.cleave");
            again |= s.Log.Any(e => e.Key == "combat.extra_attack");
        }
        Assert.True(swept);
        Assert.True(again);
    }

    [Fact]
    public void SweepIsWeaponOnlyAndRollsItsNumbersPerItem()
    {
        var cleave = Data.Affixes["cleave"];
        Assert.Equal(["weapon"], cleave.Kinds);
        // L29(T9)の剣に付けると、確率は (T〜T+4) × 1.5 %(T5〜T9 の段)、数は 1〜T/4 体
        var rng = new Pcg32(4, 4);
        var chances = new HashSet<int>();
        var counts = new HashSet<int>();
        for (int i = 0; i < 600; i++)
        {
            var sword = ItemInstance.Of("longsword");
            sword.Rare = sword.Attuned = true;
            Loot.AddAffixes(Data, sword, Data.Items["longsword"], 6, 29, rng);
            if (sword.Affixes.FirstOrDefault(a => a.Id == "cleave") is not { } roll) continue;
            int t = roll.Tier + 1;
            var parts = sword.AffixParts(Data, roll, x => x.Passive).ToList();
            int chance = parts.Single(p => p.Stat == "cleave").Value!.Value, most = parts.Single(p => p.Stat == "cleave_targets").Value!.Value;
            Assert.InRange(chance, (int)Math.Floor(t * 1.5), (int)Math.Floor((t + 4) * 1.5));
            Assert.InRange(most, 1, Math.Max(1, t / 4));
            chances.Add(chance);
            counts.Add(most);
        }
        Assert.True(chances.Count >= 5 && counts.Count >= 2, $"{chances.Count} {counts.Count}");
    }

    [Fact]
    public void SturdyRaisesMaxHpWithoutSavingIt()
    {
        // 頑丈な: 最大 HP +4T−3〜4T。セーブには元の最大 HP だけが残る
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        int baseHp = c.MaxHp;
        var armor = c.Inventory[EquipSlot.Armor]!;
        armor.Rare = armor.Attuned = true;
        armor.Affixes.Add(new AffixRoll { Id = "sturdy", Tier = 2, Values = new() { ["max_hp"] = 11 } });   // T3: 9〜12 から 11
        c.ArmorClass(Data);
        Assert.Equal(baseHp + 11, c.MaxHp);
        c.MaxHp += 10;   // レベルが上がった分は元の分に入る
        Assert.Equal(baseHp + 10, c.BaseMaxHp);
        var json = System.Text.Json.JsonSerializer.Serialize(c, ContentLibrary.JsonOptions);
        Assert.Contains($"\"max_hp\":{baseHp + 10}", json.Replace(" ", ""));
        // 外すと最大 HP が戻り、今の HP も最大までになる
        c.Hp = c.MaxHp;
        armor.Affixes.Clear();
        c.ArmorClass(Data);
        Assert.Equal(baseHp + 10, c.MaxHp);
        Assert.Equal(c.MaxHp, c.Hp);
        Assert.Equal(["armor", "shield", "neck", "waist", "head"], Data.Affixes["sturdy"].Kinds);
    }

    [Fact]
    public void ImportedAffixesWorkInFights()
    {
        // 盾受けの(100% にした写し)で敵の近接は全部止まり、再生の で番の終わりに HP が戻る
        var data = GameData.Load(Content);
        data.Affixes["blocking"].Tiers[0].Passive[0].Value = 100;
        data.Affixes["blocking"].Tiers[0].Passive[0].ValueMax = 100;
        var c = CharacterFactory.QuickCreate(data, "T", "human", "fighter", 5);
        c.Inventory.Equipped[EquipSlot.OffHand] = Rare("shield", "blocking", 0);
        c.Inventory.Equipped[EquipSlot.Ring1] = Rare("plain_ring", "regenerating", 4);   // T5: +2
        var engine = new CombatEngine(data, new Pcg32(7, 7));
        var s = engine.Start([c], [(data.Monsters["wolf"], 3)]);   // 噛みつき(近接)だけ
        var p = s.Party[0];
        p.Hp = p.MaxHp - 10;
        for (int r = 0; r < 4; r++)
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Dodge) });
        Assert.Contains(s.Log, e => e.Key == "combat.block");
        Assert.DoesNotContain(s.Log, e => e.Key is "combat.hit" or "combat.crit" && e.Args.Length > 2 && Equals(e.Args[2], p.Name));
        Assert.True(p.Hp > p.MaxHp - 10);
    }

    [Fact]
    public void ImportedAffixStatsAddUp()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "wizard", 5);
        c.Inventory.Equipped[EquipSlot.Feet] = Rare("leather_boots", "swift", 4);
        c.Inventory.Equipped[EquipSlot.Neck] = Rare("plain_amulet", "potent", 4);
        c.Inventory.Equipped[EquipSlot.Waist] = Rare("leather_belt", "golden", 4);
        Assert.Equal(4, c.PassiveBonus(Data, "initiative"));
        Assert.Equal(4, c.PassiveBonus(Data, "spell_damage"));
        Assert.Equal(25, c.PassiveBonus(Data, "gold_find"));   // 振っていない品は幅の下(5T)
        Assert.Equal(["weapon"], Data.Affixes["bleeding"].Kinds);
        Assert.Equal(["shield"], Data.Affixes["blocking"].Kinds);
    }

    [Fact]
    public void SlotAffixesAddOnlyToSlotsTheCasterHas()
    {
        // 武器だけに付く第三環の: 3 レベルのスロット +1。4 レベルのスロットを持たない L5 のウィザードには、第四環の は効かない
        var wiz = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 5);
        int third = Core.Magic.Spellcasting.MaxSlots(Data, wiz, 3);
        var staff = Rare("quarterstaff", "slot_3", 0);
        staff.Affixes.Add(new AffixRoll { Id = "slot_4", Tier = 0 });
        wiz.Inventory.Equipped[EquipSlot.MainHand] = staff;
        Assert.Equal(third + 1, Core.Magic.Spellcasting.MaxSlots(Data, wiz, 3));
        Assert.Equal(0, Core.Magic.Spellcasting.MaxSlots(Data, wiz, 4));
        // 段でレベルを絞る: 5 レベルのスロットは L13(T5)から、9 レベルは L29(T9)から
        Assert.Equal(13, Data.Affixes["slot_5"].Tiers[0].MinLevel);
        Assert.Equal(29, Data.Affixes["slot_9"].Tiers[0].MinLevel);
        Assert.All(Enumerable.Range(1, 9), k => Assert.Equal(["weapon"], Data.Affixes[$"slot_{k}"].Kinds));
    }

    [Fact]
    public void SpellDcAndAttackFromGearStopAtThree()
    {
        // 威令の(呪文の難易度)は1つで段3つ +1。何か所に付けても装備の分は +3 まで
        var wiz = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 5);
        var cls = Data.Classes["wizard"];
        int dc = Core.Magic.Spellcasting.SaveDc(cls, wiz), hit = Core.Magic.Spellcasting.AttackBonus(cls, wiz);
        wiz.Inventory.Equipped[EquipSlot.Ring1] = Rare("plain_ring", "commanding", 6);   // T7: +3
        wiz.Inventory.Equipped[EquipSlot.Ring2] = Rare("plain_ring", "commanding", 6);
        wiz.Inventory.Equipped[EquipSlot.Neck] = Rare("plain_amulet", "unerring", 3);   // T4: +2
        Assert.Equal(dc + 3, Core.Magic.Spellcasting.SaveDc(Data, cls, wiz));
        Assert.Equal(hit + 2, Core.Magic.Spellcasting.AttackBonus(Data, cls, wiz));
        // 火術の: 火の呪文のダメージ +T+1
        wiz.Inventory.Equipped[EquipSlot.Head] = Rare("plain_circlet", "spell_fire", 4);
        Assert.Equal(6, wiz.PassiveBonus(Data, "spell_damage:fire"));
    }

    [Fact]
    public void SimpleMeleeWeaponsGetOnlySupportAffixes()
    {
        // ダガーやクォータースタッフには補助(技能・呪文など)だけ。ロングソードや弓には攻めの効果も付く
        var rng = new Pcg32(8, 8);
        foreach (var id in new[] { "dagger", "quarterstaff", "club" })
            for (int i = 0; i < 300; i++)
            {
                var w = ItemInstance.Of(id);
                w.Rare = true;
                Loot.AddAffixes(Data, w, Data.Items[id], 6, 13, rng);
                Assert.All(w.Affixes, a => Assert.True(Data.Affixes[a.Id].Support, $"{id} に {a.Id}"));
            }
        Assert.False(Data.Affixes["brutal"].Fits(Data.Items["dagger"]));
        Assert.True(Data.Affixes["brutal"].Fits(Data.Items["longsword"]));
        Assert.True(Data.Affixes["brutal"].Fits(Data.Items["shortbow"]));
        Assert.True(Data.Affixes["skill_stealth"].Fits(Data.Items["dagger"]));
    }

    [Fact]
    public void WeaponDamageAffixesDoNotStackAcrossHands()
    {
        // 残忍な(ダメージ +N)は、その武器で殴ったときだけ。逆手の武器の分は全部の攻撃には足さない
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        int before = c.DamageBonus(Data);
        c.Inventory.Equipped[EquipSlot.MainHand] = Rare("shortsword", "brutal", 4);
        c.Inventory.Equipped[EquipSlot.OffHand] = Rare("shortsword", "brutal", 4);
        Assert.Equal(before, c.DamageBonus(Data));
        // 指輪の 残忍な は全部の攻撃に効く
        c.Inventory.Equipped[EquipSlot.Ring1] = Rare("plain_ring", "brutal", 4);
        Assert.Equal(before + 3, c.DamageBonus(Data));
    }
}
