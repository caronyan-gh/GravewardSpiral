using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Items;

public class LootTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void AffixesAndDropsLoadWithoutProblems()
    {
        Assert.True(Content.IsValid, string.Join("\n", Content.Problems));
        Assert.NotEmpty(Data.Affixes);
        Assert.NotEmpty(Data.Drops);
        Assert.True(Data.Items["bloodthirst"].NoRandom);
    }

    [Fact]
    public void SomeItemsWaitForAHigherTreasureLevel()
    {
        var s = TreasureSettings.Load(Content);
        int Bags(double level) => Enumerable.Range(0, 3000).Count(i => Treasure.Roll(Data, s, "common", new Pcg32((ulong)i, 2), level).ItemId == "dimension_bag");
        Assert.Equal(0, Bags(3));   // 第1部なら B4 くらい(目安 L4)までは出ない
        Assert.True(Bags(4) > 0);
    }

    [Fact]
    public void ClanHouseMaterialsComeFromDeepChestsAndCertainMonsters()
    {
        var s = TreasureSettings.Load(Content);
        int Cornerstones(double depth) => Enumerable.Range(0, 400)
            .Count(i => Chest.Generate(Data, s, depth, 5, new Pcg32((ulong)i, 9)).Items.Any(it => it.ItemId == "obsidian_cornerstone"));
        Assert.Equal(0, Cornerstones(0.2));   // 浅い階では出ない
        Assert.InRange(Cornerstones(0.8), 20, 90);   // 深い階の宝箱からときどき
        int Drops(string monster) => Enumerable.Range(0, 200)
            .Sum(i => Loot.RollDrops(Data, s, [(Data.Monsters[monster], 1)], 15, new Pcg32((ulong)i, 4)).Count(it => it.ItemId == "ancient_dragon_scale"));
        Assert.InRange(Drops("adult_red_dragon"), 30, 90);   // 竜はよく落とす
        Assert.Equal(0, Drops("goblin"));
    }

    [Fact]
    public void AffixTierFollowsTheTreasureLevel()
    {
        // 段は L1・4・7・10・13 と3ずつ、そのあと4段ごとに間が1伸びる(L17・21・25・29、L34 …、L94 で T20)
        var flaming = Data.Affixes["elemental_fire"];
        Assert.Equal(0, flaming.TierFor(1));
        Assert.Equal(1, flaming.TierFor(4));
        Assert.Equal(4, flaming.TierFor(13));
        Assert.Equal(5, flaming.TierFor(17));
        Assert.Equal(19, flaming.TierFor(100));
        // 状態の追加効果は浅い階では出ない
        Assert.Equal(-1, Data.Affixes["onhit_stunning"].TierFor(3));
    }

    [Fact]
    public void AffixTiersAreChosenEvenlyFromTheTopFive()
    {
        // L13(T5)なら T1〜T5、L29(T9)なら T5〜T9 から均等に。L2 は T1 だけ
        foreach (var (level, low, high) in new[] { (13.0, 0, 4), (29.0, 4, 8), (2.0, 0, 0) })
        {
            var rng = new Pcg32(9, 9);
            var seen = new Dictionary<int, int>();
            for (int i = 0; i < 15000; i++)   // 候補が多いので、残忍な が出る回数を稼ぐ
            {
                var item = ItemInstance.Of("longsword");
                Loot.AddAffixes(Data, item, Data.Items["longsword"], 1, level, rng, 1);
                if (item.Affixes is [{ Id: "brutal" } roll]) seen[roll.Tier] = seen.GetValueOrDefault(roll.Tier) + 1;
            }
            Assert.Equal(low, seen.Keys.Min());
            Assert.Equal(high, seen.Keys.Max());
            Assert.Equal(high - low + 1, seen.Count);
            Assert.True(seen.Values.Max() < seen.Values.Min() * 2.5, string.Join(",", seen));   // おおよそ均等
        }
    }

    [Fact]
    public void MagicHasOnePrefixAndSuffixAndRareUpToThreeEach()
    {
        var s = TreasureSettings.Load(Content);
        var rng = new Pcg32(3, 7);
        int magic = 0, rare = 0, accessories = 0, six = 0;
        for (int i = 0; i < 3000; i++)
        {
            string rarity = i % 2 == 0 ? "uncommon" : "rare";
            var it = Treasure.Roll(Data, s, rarity, rng, 13, named: "common");
            var def = Data.Items[it.ItemId];
            if (def.Cursed || it.Affixes.Count == 0) continue;
            var affixes = it.Affixes.Select(a => Data.Affixes[a.Id]).ToList();
            int pre = affixes.Count(a => a.Position == "prefix"), suf = affixes.Count - pre;
            Assert.All(affixes, a => Assert.True(a.Fits(def), $"{a.Id} は {def.Id} に付かない"));
            if (def.Kind == ItemKind.Accessory) accessories++;
            if (it.Rare)
            {
                rare++;
                Assert.Equal("rare", rarity);
                Assert.InRange(pre, 0, 3);
                Assert.InRange(suf, 0, 3);
                if (it.Affixes.Count == 6) six++;
            }
            else
            {
                magic++;
                Assert.Equal("uncommon", rarity);
                Assert.DoesNotContain(affixes, a => a.RareOnly);   // 状態にならない、などはレアにだけ
                Assert.InRange(pre, 0, 1);
                Assert.InRange(suf, 0, 1);
            }
        }
        Assert.True(magic > 300 && rare > 300 && accessories > 100 && six > 0, $"{magic} {rare} {accessories} {six}");
    }

    [Fact]
    public void MagicWorksAtTwoThirdsAndUnattunedRareAtOneThird()
    {
        var c = CharacterFactory.QuickCreate(Data, "A", "human", "fighter");
        int before = c.ArmorClass(Data);
        var armor = c.Inventory[EquipSlot.Armor]!;
        armor.Affixes.Add(new AffixRoll { Id = "warding", Tier = 6 });   // T7: AC +3
        armor.Affixes.Add(new AffixRoll { Id = "resist_fire", Tier = 0 });
        bool Fire() => c.Passive(Data).Any(p => p.Type == EffectPart.Resist && p.DamageType == "fire");
        // マジック: 2/3(+3 → +2)。抵抗のような有る無しの効果は効く
        Assert.Equal(before + 2, c.ArmorClass(Data));
        Assert.True(Fire());
        // 同調していないレア: 1/3(+3 → +1)。有る無しの効果は効かない
        armor.Rare = true;
        Assert.Equal(before + 1, c.ArmorClass(Data));
        Assert.False(Fire());
        // 同調したレア: そのまま
        armor.Attuned = true;
        Assert.Equal(before + 3, c.ArmorClass(Data));
        Assert.True(Fire());
        // ダメージのダイスは倍率を持たせて振る。修正値が 0 になれば効かない
        var dmg = ItemInstance.Weaken(new EffectPart { Type = EffectPart.Damage, Dice = "2d6" }, 2.0 / 3)!;
        Assert.Equal(2.0 / 3, dmg.Scale!.Value, 6);
        Assert.Null(ItemInstance.Weaken(new EffectPart { Type = EffectPart.Modifier, Stat = "damage", Value = 1 }, 1.0 / 3));
    }

    [Fact]
    public void ConditionWardsRaiseTheSaveInsteadOfImmunity()
    {
        // 「自由の」は麻痺と拘束にならない、ではなく、そのセーヴに上乗せ(マジックなら 2/3)
        var c = CharacterFactory.QuickCreate(Data, "A", "human", "fighter");
        var boots = ItemInstance.Of("leather_boots");
        boots.Affixes.Add(new AffixRoll { Id = "immune_free_action", Tier = 0 });   // T3(L7 から): +3
        c.Inventory.Equipped[EquipSlot.Feet] = boots;
        Assert.Equal(2, c.PassiveBonus(Data, "save_vs:paralyzed"));
        Assert.Equal(2, c.PassiveBonus(Data, "save_vs:restrained"));
        Assert.DoesNotContain(c.Passive(Data), p => p.Type == EffectPart.ImmuneCondition);
        Assert.DoesNotContain(Data.Affixes.Values, a => a.Tiers.Any(t => t.Passive.Any(p => p.Type == EffectPart.ImmuneCondition)));
    }

    [Fact]
    public void RareItemsAttuneWhenEquippedEvenWithTheSameBase()
    {
        var c = CharacterFactory.QuickCreate(Data, "A", "human", "fighter");
        var inv = c.Inventory;
        var defs = Data.Items;
        for (int i = 0; i < 2; i++)
        {
            var ring = ItemInstance.Of("plain_ring");
            ring.Rare = true;
            ring.Affixes.Add(new AffixRoll { Id = "brutal", Tier = i });
            inv.Items.Add(ring);
            Assert.Equal(EquipResult.Ok, inv.Equip(inv.Items.IndexOf(ring), i == 0 ? EquipSlot.Ring1 : EquipSlot.Ring2, defs));
        }
        // 同じ元の品でも、レアの品は1つずつ別の品なので、2つとも同調する
        Assert.True(inv[EquipSlot.Ring1]!.Attuned);
        Assert.True(inv[EquipSlot.Ring2]!.Attuned);
        Assert.Equal(2, inv.AttunedCount);
        // 付け替えずに同調を選び直せる(解いて、また同調する)
        Assert.Equal(Inventory.AttuneResult.Ok, inv.Unattune(EquipSlot.Ring2, defs));
        Assert.Equal(1, inv.AttunedCount);
        Assert.Equal(Inventory.AttuneResult.Ok, inv.Attune(EquipSlot.Ring2, defs));
        Assert.Equal(Inventory.AttuneResult.NotNeeded, inv.Attune(EquipSlot.Ring2, defs));
        // 枠がいっぱいなら同調できない
        var third = ItemInstance.Of("plain_cloak");
        third.Rare = third.Attuned = true;
        inv.Equipped[EquipSlot.Cloak] = third;
        var fourth = ItemInstance.Of("leather_boots");
        fourth.Rare = true;
        inv.Equipped[EquipSlot.Feet] = fourth;
        Assert.Equal(Inventory.AttuneResult.Full, inv.Attune(EquipSlot.Feet, defs));
        inv.Equipped.Remove(EquipSlot.Cloak);
        inv.Equipped.Remove(EquipSlot.Feet);
        // マジックの品は同調しない(しなくても効く)
        var magic = ItemInstance.Of("plain_amulet");
        magic.Affixes.Add(new AffixRoll { Id = "resolute", Tier = 0 });
        inv.Items.Add(magic);
        inv.Equip(inv.Items.IndexOf(magic), EquipSlot.Neck, defs);
        Assert.False(inv[EquipSlot.Neck]!.Attuned);
        Assert.Equal(2, inv.AttunedCount);
    }

    [Fact]
    public void AffixesDoNotRepeatAGroup()
    {
        var rng = new Pcg32(5, 5);
        for (int i = 0; i < 200; i++)
        {
            var item = ItemInstance.Of("longsword");
            Loot.AddAffixes(Data, item, Data.Items["longsword"], 3, 12, rng);
            var groups = item.Affixes.Select(a => Data.Affixes[a.Id].Group).Where(g => g != null).ToList();
            Assert.Equal(groups.Count, groups.Distinct().Count());
            Assert.All(item.Affixes, a => Assert.Contains("weapon", Data.Affixes[a.Id].Kinds));
        }
    }

    [Fact]
    public void SameSeedGivesTheSameAffixes()
    {
        static string Roll()
        {
            var item = ItemInstance.Of("plate_armor");
            Loot.AddAffixes(Data, item, Data.Items["plate_armor"], 2, 8, new Pcg32(42, 1));
            return string.Join(",", item.Affixes.Select(a => $"{a.Id}#{a.Tier}"));
        }
        Assert.Equal(Roll(), Roll());
    }

    [Fact]
    public void ArmorAffixWorksWhileEquipped()
    {
        var c = CharacterFactory.QuickCreate(Data, "A", "human", "fighter");
        int before = c.ArmorClass(Data);
        var armor = c.Inventory[EquipSlot.Armor]!;
        armor.Rare = armor.Attuned = true;   // 同調したレアなら、そのまま効く
        armor.Affixes.Add(new AffixRoll { Id = "warding", Tier = 3 });   // T4: AC +2
        armor.Affixes.Add(new AffixRoll { Id = "resist_fire", Tier = 0 });
        Assert.Equal(before + 2, c.ArmorClass(Data));
        Assert.Contains(c.Passive(Data), p => p.Type == EffectPart.Resist && p.DamageType == "fire");
    }

    [Fact]
    public void AffixesRaiseThePriceAndStopStacking()
    {
        var plain = ItemInstance.Of("longsword");
        var fancy = ItemInstance.Of("longsword");
        Loot.AddAffixes(Data, fancy, Data.Items["longsword"], 1, 6, new Pcg32(1, 1));
        Assert.True(Treasure.Price(Data.Items["longsword"], fancy) > Treasure.Price(Data.Items["longsword"], plain));
        Assert.False(plain.SameKind(fancy));
    }

    [Fact]
    public void DropTableUsesTheMostSpecificMatch()
    {
        Assert.Equal("undead_0_1", Loot.TableFor(Data, Data.Monsters["ghoul"])!.Id);   // アンデッド CR 1
        Assert.StartsWith("cr_", Loot.TableFor(Data, Data.Monsters["wolf"])!.Id);       // 獣は CR の表
        Assert.StartsWith("dragon_", Loot.TableFor(Data, Data.Monsters["adult_red_dragon"])!.Id);
    }

    [Fact]
    public void BossAlwaysDropsFromItsTable()
    {
        var s = TreasureSettings.Load(Content);
        var groups = Data.Bosses["final"].Groups.Select(g => (Data.Monsters[g.Monster], g.Count)).ToList();
        var drops = Loot.RollDrops(Data, s, groups, 5, new Pcg32(7, 7), boss: "final");
        Assert.Contains(drops, d => Data.Items[d.ItemId].NoRandom);
    }

    [Fact]
    public void DropBoostChangesChanceAndWeights()
    {
        var s = TreasureSettings.Load(Content);
        var ghouls = new[] { (Data.Monsters["ghoul"], 200) };
        Assert.Empty(Loot.RollDrops(Data, s, ghouls, 3, new Pcg32(1, 1), boost: new DropBoost { Chance = 0 }));
        // 確率を上げ、墓守のつるはし以外の重みを 0 にすると、落とし物は全部それになる
        var boost = new DropBoost
        {
            Chance = 100,   // 表の 2% × 100 で必ず落とす
            Weights = new() { ["common"] = 0, ["uncommon"] = 0, ["coffin_lid"] = 0 },
        };
        var drops = Loot.RollDrops(Data, s, ghouls, 3, new Pcg32(1, 1), boost: boost);
        Assert.Equal(200, drops.Count);
        Assert.All(drops, d => Assert.Equal("gravekeepers_pick", d.ItemId));
        // テーマ ×2 と階 ×3 は掛け合わせて ×6
        var both = DropBoost.Combine(new DropBoost { Chance = 2, Weights = new() { ["rare"] = 2 } }, new DropBoost { Chance = 3, Weights = new() { ["rare"] = 3 } })!;
        Assert.Equal(6, both.Chance);
        Assert.Equal(6, both.Weights["rare"]);
    }

    [Fact]
    public void AboveTheNamedCapARarityBecomesABonusItem()
    {
        // うろつく敵の箱の L1 は名前つきの品がコモンまで: アンコモンが出たら +1 の武器にする
        var s = TreasureSettings.Load(Content);
        Assert.Equal("common", Treasure.Row(s, 1)!.Named);
        var rng = new Pcg32(5, 5);
        for (int i = 0; i < 200; i++)
        {
            var it = Treasure.Roll(Data, s, "uncommon", rng, 1, named: "common");
            var def = Data.Items[it.ItemId];
            Assert.True(it.Bonus == 1 || def.Cursed || def.Kind == ItemKind.Accessory && !def.IsMagic && it.Affixes.Count > 0, it.ItemId);
        }
        // 上限の内なら、名前つきの品も出る
        Assert.Contains(Enumerable.Range(0, 200).Select(_ => Treasure.Roll(Data, s, "uncommon", rng, 1, named: "uncommon")), it => Data.Items[it.ItemId].IsMagic);
    }

    [Fact]
    public void RoomsAndBossesGiveBetterTreasureThanWanderers()
    {
        var s = TreasureSettings.Load(Content);
        double Magic(TreasureSource src) => Treasure.Weights(s, 5, src) is var w ? (w.Sum(kv => kv.Key is "mundane" or "common" ? 0 : kv.Value) / w.Values.Sum()) : 0;
        Assert.True(Magic(TreasureSource.Room) > Magic(TreasureSource.Wander));
        Assert.True(Magic(TreasureSource.Boss) > Magic(TreasureSource.Room));
        // 部屋の箱は L8 から名前つきのレアが出る(第3部)。うろつく敵は L14 から
        Assert.Equal("uncommon", Treasure.Row(s, 8)!.Named);
        Assert.Equal("rare", Treasure.Row(s, 8, TreasureSource.Room)!.Named);
        Assert.Equal("rare", Treasure.Row(s, 14)!.Named);
    }

    [Fact]
    public void DropTableRaritiesAreClampedToTheFloor()
    {
        // 敵の落とし物の表の very_rare も、L5 のうろつく敵ならその表で出る一番上(rare)まで
        var s = TreasureSettings.Load(Content);
        Assert.Equal("rare", Treasure.Clamp(s, "very_rare", 5));
        Assert.Equal("uncommon", Treasure.Clamp(s, "very_rare", 1));
        Assert.Equal("common", Treasure.Clamp(s, "common", 1));
    }

    [Fact]
    public void NoRandomItemsNeverComeFromChestRolls()
    {
        var s = TreasureSettings.Load(Content);
        var rng = new Pcg32(9, 9);
        foreach (var rarity in new[] { "mundane", "common", "rare", "very_rare", "legendary" })
        {
            for (int i = 0; i < 300; i++) Assert.False(Data.Items[Treasure.Roll(Data, s, rarity, rng, 15).ItemId].NoRandom);
        }
    }

    [Fact]
    public void StandardItemsAndMonstersHaveNoUnknownFields()
    {
        // エディタの「JSON を確かめる」は知らない項目を問題にする。標準の品とモンスターを写したものが、そこで引っかからないこと
        var problems = new List<string>();
        foreach (var i in Content.All("items"))
        {
            try { System.Text.Json.JsonSerializer.Deserialize<ItemDef>(i.Data.GetRawText(), Core.Events.EventDraftCheck.Strict); }
            catch (System.Text.Json.JsonException ex) { problems.Add($"{i.Id}: {ex.Message}"); }
        }
        foreach (var m in Content.All("monsters"))
        {
            try { System.Text.Json.JsonSerializer.Deserialize<Core.Monsters.MonsterDef>(m.Data.GetRawText(), Core.Events.EventDraftCheck.Strict); }
            catch (System.Text.Json.JsonException ex) { problems.Add($"{m.Id}: {ex.Message}"); }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(10)));
    }

    [Fact]
    public void ANoRandomPlainWeaponIsNotRolledNorSold()
    {
        // イベントの品にした、魔法でない剣: ふつうの品にも、+N の元にも、商店にも出ない
        var data = GameData.Load(Content);
        var sword = System.Text.Json.JsonSerializer.Deserialize<ItemDef>("""{ "id": "event_sword", "kind": "weapon", "rarity": "mundane", "drop_only": true, "price": 15 }""", ContentLibrary.JsonOptions)!;
        Assert.True(sword.NoRandom);   // 古い名前でも読める
        data.Items[sword.Id] = sword;
        var s = TreasureSettings.Load(Content);
        var rng = new Pcg32(3, 3);
        foreach (var rarity in new[] { "mundane", "uncommon", "rare" })
            for (int i = 0; i < 500; i++) Assert.NotEqual("event_sword", Treasure.Roll(data, s, rarity, rng, 5).ItemId);
        Assert.DoesNotContain(Shop.Standard(data), i => i.Id == "event_sword");
        Assert.DoesNotContain("drop_only", System.Text.Json.JsonSerializer.Serialize(sword, ContentLibrary.JsonOptions));
    }

    [Fact]
    public void UniquesGetRandomAffixes()
    {
        var s = TreasureSettings.Load(Content);
        var item = Loot.Make(Data, s, Data.Items["bloodthirst"], 15, new Pcg32(3, 3));
        Assert.Equal(2, item.Affixes.Count);
        Assert.False(item.Identified);
    }

    [Fact]
    public void ClanHouseMaterialsAreNotSoldOrRandomLoot()
    {
        // 素材は深い宝箱・アンデッド・竜からだけ(決めたこと 82)。店にも、おまかせの宝にも出ない
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        foreach (var id in new[] { "obsidian_cornerstone", "consecrated_bell", "ancient_dragon_scale" })
        {
            Assert.True(data.Items[id].NoRandom, id);
            Assert.DoesNotContain(Shop.Standard(data), i => i.Id == id);
        }
    }
}
