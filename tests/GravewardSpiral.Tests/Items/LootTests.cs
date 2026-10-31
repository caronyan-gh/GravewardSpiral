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
        var flaming = Data.Affixes["elemental_fire"];
        Assert.Equal(0, flaming.TierFor(1));
        Assert.Equal(1, flaming.TierFor(6));
        Assert.Equal(3, flaming.TierFor(20));
        // 状態の追加効果は浅い階では出ない
        Assert.Equal(-1, Data.Affixes["onhit_stunning"].TierFor(3));
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
        armor.Affixes.Add(new AffixRoll { Id = "warding", Tier = 2 });
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
