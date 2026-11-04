using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
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
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
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
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
        c.Inventory.Equipped[EquipSlot.Armor] = Rare("chain_mail", "thorns", 4);
        Assert.Equal(6, c.PassiveBonus(Data, "thorns"));
        c.Inventory.Equipped[EquipSlot.Hands] = Rare("leather_gloves", "flurry", 4);
        Assert.Equal(5, c.PassiveBonus(Data, "extra_attack"));
    }

    [Fact]
    public void SkillAffixesHelpEventChecksAndChestInspection()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
        int before = Chest.InspectBonus(Data, c);
        c.Inventory.Equipped[EquipSlot.Head] = Rare("plain_circlet", "skill_investigation", 4);   // T5: +4
        Assert.Equal(before + 4, Chest.InspectBonus(Data, c));
    }

    [Fact]
    public void UsesAffixAddsToThatClassPoolOnly()
    {
        // 憤怒の: 激怒の回数 +段/5 切り上げ(T6 で +2。L17 から)。激怒を持たないクラスには効かない
        var barb = CharacterFactory.QuickCreate(Data, "B", "human", "barbarian", 17);
        int before = ClassFeatures.MaxUses(Data, barb, "rage");
        barb.Inventory.Equipped[EquipSlot.Waist] = Rare("leather_belt", "uses_rage", 5);
        Assert.Equal(before + 2, ClassFeatures.MaxUses(Data, barb, "rage"));
        var fighter = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 17);
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
        // 頑丈な: 最大 HP +4T−3〜4T。セーブには元の最大 HP だけが残る(T3 は L7 から全部効く)
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 7);
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
    public void ThornsKillStopsTheMultiattackAndCountsOnce()
    {
        // 噛みつきで棘に倒れたヒグマは、残りのかぎ爪を振らず、倒した数も1体だけ
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
            c.Inventory.Equipped[EquipSlot.Armor] = Rare("chain_mail", "thorns", 4);
            var engine = new CombatEngine(Data, new Pcg32(seed, 3));
            var s = engine.Start([c], [(Data.Monsters["brown_bear"], 1)]);
            s.Surprised = null;
            var bear = s.Groups[0].Units[0];
            bear.Hp = 1;
            s.Order.Clear();
            s.Order.Add(bear);
            s.Order.Add(s.Party[0]);
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Dodge) });
            Assert.True(s.Defeated.Count <= 1, $"seed {seed}: {s.Defeated.Count}");
            int slain = s.Log.FindIndex(e => e.Key == "combat.slain");
            if (slain >= 0) Assert.DoesNotContain(s.Log.Skip(slain), e => e.Key == "combat.hit" && e.Args.Length > 0 && e.Args[0] is not string);
        }
    }

    [Fact]
    public void DrainCountsAllDamageWhateverTheOrder()
    {
        // 吸う部品は、並びのほかのダメージを全部起こしてから数える(追加効果の並び順で吸う量が変わらない)
        int Healed(bool drainFirst)
        {
            var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
            var engine = new CombatEngine(Data, new Pcg32(1, 1));
            var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
            var ogre = s.Groups[0].Units[0];
            ogre.Hp = 1;
            var drain = new EffectPart { Type = EffectPart.Drain, Value = 50 };
            EffectPart Hit(string type) => new() { Type = EffectPart.Damage, Dice = "6", DamageType = type };
            engine.ApplyToParty(s, ogre, drainFirst ? [Hit("slashing"), drain, Hit("fire")] : [Hit("slashing"), Hit("fire"), drain]);
            return ogre.Hp - 1;
        }
        Assert.Equal(6, Healed(false));
        Assert.Equal(Healed(false), Healed(true));
    }

    [Fact]
    public void SlotAffixAddsToPactSlotsOfThatLevel()
    {
        // 第二環の(L4 から): L4 のウォーロックは協約のスロットが2レベル2つ → 3つ。ウィザードはふつうのスロットに足す
        var lock4 = CharacterFactory.QuickCreate(Data, "K", "human", "warlock", 4);
        var wiz4 = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 4);
        int pact = Core.Magic.Spellcasting.MaxSlots(Data, lock4, 2), regular = Core.Magic.Spellcasting.MaxSlots(Data, wiz4, 2);
        var staff = Rare("quarterstaff", "slot_2", 0);
        lock4.Inventory.Equipped[EquipSlot.MainHand] = staff;
        wiz4.Inventory.Equipped[EquipSlot.MainHand] = Rare("quarterstaff", "slot_2", 0);
        Assert.Equal(pact + 1, Core.Magic.Spellcasting.MaxSlots(Data, lock4, 2));
        Assert.Equal(pact + 1, Core.Magic.Spellcasting.PactSlots(Data, lock4));
        Assert.Equal(regular + 1, Core.Magic.Spellcasting.MaxSlots(Data, wiz4, 2));
        // 協約のスロットと違うレベルなら効かない
        staff.Affixes[0] = new AffixRoll { Id = "slot_1", Tier = 0 };
        Assert.Equal(0, Core.Magic.Spellcasting.MaxSlots(Data, lock4, 1));
    }

    [Fact]
    public void BleedingWeakensLikeDamage()
    {
        // 出血(続くダメージ)は、マジックなら 2/3・同調していないレアなら 1/3 の目で効く(消えない)
        var sword = Rare("longsword", "bleeding", 4);
        double? Scale(bool rare, bool attuned)
        {
            sword.Rare = rare;
            sword.Attuned = attuned;
            return sword.AffixOnHit(Data, 13).Single(p => p.Type == EffectPart.Dot).Scale;
        }
        Assert.Null(Scale(true, true));
        Assert.Equal(2.0 / 3, Scale(false, false)!.Value, 6);
        Assert.Equal(1.0 / 3, Scale(true, false)!.Value, 6);
    }

    [Fact]
    public void SituationalAffixesHelpOnlyTheirOwnUse()
    {
        foreach (var id in new[] { "remedies", "mending", "ambushing", "executing", "cornered", "mercy" }) Assert.True(Data.Affixes.ContainsKey(id), id);
        Assert.True(Data.Affixes["mercy"].Support);
        // 薬効の(T5 は 6〜12、振っていなければ 6): 自分が飲む水薬の回復が増える
        int Potion(bool ring)
        {
            var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
            if (ring) c.Inventory.Equipped[EquipSlot.Ring1] = Rare("plain_ring", "remedies", 4);
            c.Inventory.Items.Clear();
            c.Inventory.TryAdd(ItemInstance.Of("potion_of_healing"), Data.Items);
            var engine = new CombatEngine(Data, new Pcg32(3, 3));
            var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
            var p = s.Party[0];
            p.Hp = 1;
            engine.UseItem(s, p, new PlayerCommand(CommandKind.Item, -1, 0, ItemIndex: 0));
            return p.Hp;
        }
        Assert.Equal(Potion(false) + 6, Potion(true));
        // 手当ての(T5 は 3〜6、振っていなければ 3): 小休憩のヒットダイス1つごと
        int Rest(bool belt)
        {
            var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
            if (belt) c.Inventory.Equipped[EquipSlot.Waist] = Rare("leather_belt", "mending", 4);
            c.Hp = 1;
            c.HitDice = 1;
            c.ShortRest(Data, new Pcg32(5, 5));
            return c.Hp;
        }
        Assert.Equal(Rest(false) + 3, Rest(true));
        // 窮地の(T5 は +2): HP が 1/3 以下のあいだだけ AC が上がる
        var f = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
        f.Inventory.Equipped[EquipSlot.Cloak] = Rare("plain_cloak", "cornered", 4);
        var st = new CombatEngine(Data, new Pcg32(1, 1)).Start([f], [(Data.Monsters["ogre"], 1)]);
        var pm = st.Party[0];
        int full = pm.Ac;
        pm.Hp = pm.MaxHp / 3;
        Assert.Equal(full + 2, pm.Ac);
    }

    [Fact]
    public void HpConditionalDamageLooksAtEachCleaveTarget()
    {
        // 薙ぎで当てた2体目にも、その敵の HP で先制の(満タン)・とどめの(半分以下)を付け直す
        int[]? Sweep(ulong seed, string? affix)
        {
            var c = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 1);
            c.Inventory.Equipped.Clear();
            var sword = ItemInstance.Of("longsword");
            sword.Rare = sword.Attuned = true;
            sword.Affixes.Add(new AffixRoll { Id = "cleave", Tier = 0, Values = new() { ["cleave"] = 100, ["cleave_targets"] = 1 } });
            if (affix != null) sword.Affixes.Add(new AffixRoll { Id = affix, Tier = 0, Values = new() { [affix == "ambushing" ? "damage_vs_full" : "damage_vs_low"] = 5 } });
            c.Inventory.Equipped[EquipSlot.MainHand] = sword;
            var engine = new CombatEngine(Data, new Pcg32(seed, 3));
            var s = engine.Start([c], [(Data.Monsters["ogre"], 2)]);
            s.Surprised = null;
            var units = s.Groups[0].Units;
            units[1].Hp = units[1].MaxHp / 2;
            int[] before = units.Select(u => u.Hp).ToArray();
            s.Order.Clear();
            s.Order.Add(s.Party[0]);
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Attack, 0) });
            return units.Any(u => u.Hp <= 0) ? null : units.Select((u, i) => before[i] - u.Hp).ToArray();   // 倒れたら比べない
        }
        int checkedSeeds = 0;
        for (ulong seed = 1; seed <= 200 && checkedSeeds < 3; seed++)
        {
            var plain = Sweep(seed, null);
            if (plain == null || plain.Any(d => d <= 0)) continue;   // 両方に当たって、どちらも倒れていないときだけ比べる
            Assert.Equal([plain[0] + 5, plain[1]], Sweep(seed, "ambushing"));
            Assert.Equal([plain[0], plain[1] + 5], Sweep(seed, "executing"));
            checkedSeeds++;
        }
        Assert.True(checkedSeeds > 0);
    }

    [Fact]
    public void WeaponChoiceCountsHpConditionalDamage()
    {
        // 満タンの敵には、先制の(+7)が付いた剣の方が、隣で撃つ弓より見込みが高い
        var archer = CharacterFactory.QuickCreate(Data, "A", "human", "fighter", 13);
        archer.Inventory.Equipped.Clear();
        archer.Scores[Core.Rules.Ability.Str] = 10;
        archer.Scores[Core.Rules.Ability.Dex] = 20;
        var sword = Rare("longsword", "ambushing", 4);
        sword.Bonus = 1;
        sword.Affixes[0].Values = new() { ["damage_vs_full"] = 7 };
        var bow = ItemInstance.Of("longbow");
        bow.Bonus = 1;
        archer.Inventory.Equipped[EquipSlot.MainHand] = sword;
        archer.Inventory.Equipped[EquipSlot.Ranged] = bow;
        var engine = new CombatEngine(Data, new Pcg32(2, 3));
        var s = engine.Start([archer], [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        s.Order.Clear();
        s.Order.Add(s.Party[0]);
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Attack, 0) });
        var first = s.Log.First(e => e.Key is "combat.hit" or "combat.miss" or "combat.crit");
        Assert.Equal(Data.Items["longsword"].Name, first.Args[1]);
    }

    [Fact]
    public void WeaponEstimateUsesTheWearersTier()
    {
        // 炎の T5 は L13 なら平均が大きいが、L1 では届く段階の分(武器を選ぶ見積もりも同じ)
        var sword = Rare("longsword", "elemental_fire", 4);
        Assert.True(Loot.OnHitAverage(Data, sword, 1) < Loot.OnHitAverage(Data, sword, 13));
        Assert.Equal(Loot.OnHitAverage(Data, sword), Loot.OnHitAverage(Data, sword, 13));
    }

    [Fact]
    public void AffixesWorkOnlyUpToTheTierTheWearerReaches()
    {
        // T5(L13)の頑丈な(17〜20 から 19)を L5 の人が着ると、L5 で届く T2(5〜8)の同じ位置の 7。L13 で全部の 19
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 5);
        int baseHp = c.BaseMaxHp;
        var armor = c.Inventory[EquipSlot.Armor]!;
        armor.Rare = armor.Attuned = true;
        var roll = new AffixRoll { Id = "sturdy", Tier = 4, Values = new() { ["max_hp"] = 19 } };
        armor.Affixes.Add(roll);
        Assert.Equal(1, ItemInstance.UsableTier(Data, roll, 5));
        c.ArmorClass(Data);
        Assert.Equal(baseHp + 7, c.MaxHp);
        Assert.Equal(19, armor.AffixParts(Data, roll, t => t.Passive).Single().Value);   // 着る人を決めなければ付いた段階のまま
        c.Level = 13;
        c.ArmorClass(Data);
        Assert.Equal(baseHp + 19, c.MaxHp);
        // まだどの段階にも届かない追加効果は効かない(呪文スロットのように、出始めが高いもの)
        var late = Data.Affixes.Values.First(a => a.Tiers[0].MinLevel > 5 && a.Kinds.Contains("armor"));
        var lateRoll = new AffixRoll { Id = late.Id, Tier = 0 };
        Assert.Equal(-1, ItemInstance.UsableTier(Data, lateRoll, 5));
        Assert.Empty(armor.AffixParts(Data, lateRoll, t => t.Passive, 5));
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
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "wizard", 13);
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
        // 武器だけに付く第三環の: 3 レベルのスロット +1(L7 から)。4 レベルのスロットを持たない L10 のパラディンには、第四環の は効かない
        var wiz = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 7);
        int third = Core.Magic.Spellcasting.MaxSlots(Data, wiz, 3);
        wiz.Inventory.Equipped[EquipSlot.MainHand] = Rare("quarterstaff", "slot_3", 0);
        Assert.Equal(third + 1, Core.Magic.Spellcasting.MaxSlots(Data, wiz, 3));
        var pal = CharacterFactory.QuickCreate(Data, "P", "human", "paladin", 10);
        pal.Inventory.Equipped[EquipSlot.MainHand] = Rare("longsword", "slot_4", 0);
        Assert.Equal(0, Core.Magic.Spellcasting.MaxSlots(Data, pal, 4));
        // 段でレベルを絞る: 5 レベルのスロットは L13(T5)から、9 レベルは L29(T9)から
        Assert.Equal(13, Data.Affixes["slot_5"].Tiers[0].MinLevel);
        Assert.Equal(29, Data.Affixes["slot_9"].Tiers[0].MinLevel);
        Assert.All(Enumerable.Range(1, 9), k => Assert.Equal(["weapon"], Data.Affixes[$"slot_{k}"].Kinds));
    }

    [Fact]
    public void SpellDcAndAttackFromGearStopAtThree()
    {
        // 威令の(呪文の難易度)は1つで段3つ +1。何か所に付けても装備の分は +3 まで
        var wiz = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 13);
        var cls = Data.Classes["wizard"];
        int dc = Core.Magic.Spellcasting.SaveDc(cls, wiz), hit = Core.Magic.Spellcasting.AttackBonus(cls, wiz);
        wiz.Inventory.Equipped[EquipSlot.Ring1] = Rare("plain_ring", "commanding", 3);   // T4: +2
        wiz.Inventory.Equipped[EquipSlot.Ring2] = Rare("plain_ring", "commanding", 3);
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
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 13);
        int before = c.DamageBonus(Data);
        c.Inventory.Equipped[EquipSlot.MainHand] = Rare("shortsword", "brutal", 4);
        c.Inventory.Equipped[EquipSlot.OffHand] = Rare("shortsword", "brutal", 4);
        Assert.Equal(before, c.DamageBonus(Data));
        // 指輪の 残忍な は全部の攻撃に効く
        c.Inventory.Equipped[EquipSlot.Ring1] = Rare("plain_ring", "brutal", 4);
        Assert.Equal(before + 3, c.DamageBonus(Data));
    }

    [Fact]
    public void SlotAffixKeepsTheSlotsLeftWhenPutOnAndTakenOff()
    {
        // 装備で増えたスロットは、付けた時点では空(休むと満タン)。外すと残りを新しい最大までに切り詰める。付け外しで増えない
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "wizard", 5);
        c.Inventory.Equipped.Remove(EquipSlot.MainHand);
        int max = Spellcasting.MaxSlots(Data, c, 1);
        Assert.Equal(max, Spellcasting.SlotsLeft(Data, c, 1));
        c.Inventory.Equipped[EquipSlot.MainHand] = Rare("quarterstaff", "slot_1", 0);
        int bonus = Spellcasting.MaxSlots(Data, c, 1) - max;
        Assert.True(bonus > 0);
        Assert.Equal(max, Spellcasting.SlotsLeft(Data, c, 1));            // 付けた: 最大だけ増える
        c.LongRest(data: Data);
        Assert.Equal(max + bonus, Spellcasting.SlotsLeft(Data, c, 1));    // 休むと満タン
        Spellcasting.UseSlot(Data, c, 1);
        Spellcasting.UseSlot(Data, c, 1);
        int left = max + bonus - 2;
        Assert.Equal(left, Spellcasting.SlotsLeft(Data, c, 1));
        var staff = c.Inventory.Equipped[EquipSlot.MainHand];
        c.Inventory.Equipped.Remove(EquipSlot.MainHand);
        Assert.Equal(Math.Min(left, max), Spellcasting.SlotsLeft(Data, c, 1));   // 外した: 残りはそのまま(最大まで)
        c.Inventory.Equipped[EquipSlot.MainHand] = staff;
        Assert.Equal(Math.Min(left, max), Spellcasting.SlotsLeft(Data, c, 1));   // 付け直しても増えない
    }

    [Fact]
    public void OffHandWeaponNonAttackAffixesApply()
    {
        // 逆手の武器でも、命中・ダメージ以外の追加効果(技能など)は持っているだけで効く
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "rogue", 5);
        c.Inventory.Equipped[EquipSlot.MainHand] = ItemInstance.Of("dagger");
        c.Inventory.Equipped.Remove(EquipSlot.OffHand);
        int before = c.SkillBonus(Data, "stealth");
        c.Inventory.Equipped[EquipSlot.OffHand] = Rare("dagger", "skill_stealth", 4);
        Assert.True(c.SkillBonus(Data, "stealth") > before);
    }
}
