using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Items;

public class ItemTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);

    /// <summary>テストごとに読み直す(試しのアイテムを足すため)。</summary>
    private static GameData Fresh() => GameData.Load(Content);

    private static Character Fighter(GameData data) => CharacterFactory.QuickCreate(data, "A", "human", "fighter");

    [Fact]
    public void BiggerBagsCostMore()
    {
        var data = Fresh();
        var bags = Shop.Standard(data).Where(d => d.Kind == ItemKind.Backpack).ToDictionary(d => d.Id);
        Assert.Equal((8, 150.0), (bags["shoulder_bag"].Slots, bags["shoulder_bag"].Price));   // 店で売る
        Assert.Equal((10, 500.0), (bags["backpack"].Slots, bags["backpack"].Price));
        Assert.Equal((15, 1000.0), (data.Items["dimension_bag"].Slots, data.Items["dimension_bag"].Price));   // 迷宮で見つける
        Assert.True(data.Items["dimension_bag"].IsMagic);
        // 枠が多いほど高い(魔法の袋は店に並ばない)
        var all = data.Items.Values.Where(d => d.Kind == ItemKind.Backpack && d.Id != "sack").OrderBy(d => d.Price).ToList();
        Assert.DoesNotContain(all.Zip(all.Skip(1)), p => p.First.Slots > p.Second.Slots);
        Assert.DoesNotContain(bags.Values, d => d.IsMagic);
    }

    [Fact]
    public void HandsTakeOneThrownWeaponButTheRangedSlotTakesTheStack()
    {
        var data = Fresh();
        var a = Fighter(data);
        a.Inventory.Items.Clear();
        a.Inventory.Equipped.Remove(EquipSlot.OffHand);
        Assert.True(a.Inventory.TryAdd(ItemInstance.Of("dagger", 3), data.Items));
        Assert.Equal(EquipResult.Ok, a.Inventory.Equip(0, EquipSlot.OffHand, data.Items));
        Assert.Equal(1, a.Inventory[EquipSlot.OffHand]!.Count);   // 手には1本
        Assert.Equal(2, a.Inventory.Items.Single(i => i.ItemId == "dagger").Count);
        Assert.Equal(EquipResult.Ok, a.Inventory.Equip(a.Inventory.Items.FindIndex(i => i.ItemId == "dagger"), EquipSlot.Ranged, data.Items));
        Assert.Equal(2, a.Inventory[EquipSlot.Ranged]!.Count);   // 遠隔の枠には束で
        Assert.Equal(EquipResult.Ok, a.Inventory.Unequip(EquipSlot.Ranged, data.Items));
        Assert.Equal(EquipResult.Ok, a.Inventory.Unequip(EquipSlot.OffHand, data.Items));
        Assert.Equal(3, a.Inventory.Items.Single(i => i.ItemId == "dagger").Count);   // 外すと袋の束に戻る
        a.Inventory.Items.Add(ItemInstance.Of("dagger"));   // 重ねられるようになる前のセーブ
        a.Inventory.MergeStacks(data.Items);
        Assert.Equal(4, a.Inventory.Items.Single(i => i.ItemId == "dagger").Count);
    }

    [Fact]
    public void CursesStayHiddenUntilWorn()
    {
        var data = Fresh();
        foreach (var def in data.Items.Values.Where(d => d.Cursed))
        {
            var look = Assert.IsType<LooksLike>(def.LooksLike);   // 呪いの品には、みな見せかけがある
            var shown = data.Items[look.Id];
            Assert.False(shown.Cursed);
            Assert.Equal((def.Kind, def.Slot), (shown.Kind, shown.Slot));
        }
        var a = Fighter(data);
        var sword = new ItemInstance { ItemId = "cursed_longsword_of_misfortune", Identified = false };
        a.Inventory.Items.Add(sword);
        RestStudy.Run([a]);
        Assert.True(sword.Identified);
        var seen = sword.Apparent(data.Items);
        Assert.Equal(("longsword", 1), (seen.ItemId, seen.Bonus));   // 鑑定しても、+1 のロングソードに見える
        Assert.Equal(EquipResult.Ok, a.Inventory.Equip(a.Inventory.Items.IndexOf(sword), EquipSlot.MainHand, data.Items));
        Assert.True(sword.CurseKnown);   // 握って初めて分かる
        Assert.Same(sword, sword.Apparent(data.Items));
        Assert.Equal(EquipResult.Cursed, a.Inventory.Unequip(EquipSlot.MainHand, data.Items));
    }

    [Fact]
    public void ShortRestStudiesOneItemEachWithoutRolls()
    {
        var data = Fresh();
        var a = Fighter(data);
        var b = Fighter(data);
        var down = Fighter(data);
        down.Hp = 0;   // 倒れている人は調べない
        ItemInstance Unknown(string id) => new() { ItemId = id, Identified = false };
        var mine = Unknown("ring_of_protection");
        var extra = Unknown("cloak_of_protection");
        var third = Unknown("wand_of_magic_missiles");
        a.Inventory.Items.AddRange([mine, extra, third]);
        var done = RestStudy.Run([a, b, down]);
        Assert.Equal(2, done.Count);   // 1人1個(b は a の残りを調べる)
        Assert.Same(mine, done[0].Item);
        Assert.Same(extra, done[1].Item);
        Assert.True(mine.Identified && extra.Identified);
        Assert.False(third.Identified);   // 次の休みに
        Assert.Single(RestStudy.Run([a, b, down]));
        Assert.Empty(RestStudy.Run([a, b, down]));
    }

    [Fact]
    public void WeaponsAndArmorComeFromSrd()
    {
        var data = Fresh();
        Assert.True(Content.IsValid, string.Join("\n", Content.Problems));
        Assert.Equal("1d8", data.Items["longsword"].Damage);
        Assert.Equal("1d10", data.Items["longsword"].VersatileDamage);
        Assert.Equal("ハーフプレート", data.Items["half_plate_armor"].Name.Get("ja"));
        Assert.Equal(2, data.Items["half_plate_armor"].DexCap);
        Assert.Equal(0, data.Items["plate_armor"].DexCap);
        Assert.Contains("two_handed", data.Items["greatsword"].Properties);
    }

    [Fact]
    public void PlusBonusesFollowThe5eRarities()
    {
        Assert.Equal(1, Treasure.BonusFor("uncommon", ItemKind.Weapon));
        Assert.Equal(0, Treasure.BonusFor("uncommon", ItemKind.Armor));
        Assert.Equal(1, Treasure.BonusFor("rare", ItemKind.Armor));
        Assert.Equal(3, Treasure.BonusFor("legendary", ItemKind.Armor));
        Assert.Equal("very_rare", Treasure.RarityOf(3, ItemKind.Shield));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    [InlineData(20, 4)]
    [InlineData(-2, -2)]
    public void HighEnchantmentsStopRaisingHitAndAc(int bonus, int hitAc) => Assert.Equal(hitAc, ItemInstance.HitAcBonus(bonus));

    [Fact]
    public void ArmorAndShieldBonusesRaiseAc()
    {
        var data = Fresh();
        var c = Fighter(data);
        int ac = c.ArmorClass(data);
        c.Inventory.Equipped[EquipSlot.Armor].Bonus = 2;
        c.Inventory.Equipped[EquipSlot.OffHand].Bonus = 1;
        Assert.Equal(ac + 3, c.ArmorClass(data));
    }

    [Fact]
    public void PassiveEffectsNeedAttunementAndCursesStick()
    {
        var data = Fresh();
        data.Items["test_ring"] = new ItemDef
        {
            Id = "test_ring", Kind = ItemKind.Accessory, Slot = "ring", Rarity = "rare", Attunement = true,
            Passive = [new EffectPart { Type = EffectPart.Modifier, Stat = "ac", Value = 1 }, new EffectPart { Type = EffectPart.SetAbility, Ability = "str", Value = 19 }],
        };
        data.Items["test_curse"] = new ItemDef
        {
            Id = "test_curse", Kind = ItemKind.Accessory, Slot = "cloak", Rarity = "uncommon", Cursed = true,
            Passive = [new EffectPart { Type = EffectPart.Modifier, Stat = "ac", Value = -2 }],
        };
        var c = Fighter(data);
        int ac = c.ArmorClass(data);
        c.Inventory.Items.Add(ItemInstance.Of("test_ring"));
        Assert.Equal(EquipResult.Ok, c.Inventory.Equip(c.Inventory.Items.Count - 1, EquipSlot.Ring1, data.Items));
        Assert.True(c.Inventory[EquipSlot.Ring1]!.Attuned);
        Assert.Equal(ac + 1, c.ArmorClass(data));
        Assert.Equal(19, c.Stats(data).Str);
        c.Inventory[EquipSlot.Ring1]!.Attuned = false;   // 同調していなければ効かない
        Assert.Equal(ac, c.ArmorClass(data));

        c.Inventory.Items.Add(ItemInstance.Of("test_curse"));
        c.Inventory.Equip(c.Inventory.Items.Count - 1, EquipSlot.Cloak, data.Items);
        Assert.Equal(EquipResult.Cursed, c.Inventory.Unequip(EquipSlot.Cloak, data.Items));
        Assert.Equal(1, c.Inventory.BreakCurses(data.Items));
        Assert.Null(c.Inventory[EquipSlot.Cloak]);
    }

    [Fact]
    public void TreasureRollsByDepth()
    {
        var data = Fresh();
        var s = TreasureSettings.Load(Content);
        var rng = new Pcg32(3, 3);
        var shallow = Enumerable.Range(0, 300).Select(_ => Treasure.PickRarity(s, 0, rng)).ToList();
        var deep = Enumerable.Range(0, 300).Select(_ => Treasure.PickRarity(s, 1, rng)).ToList();
        Assert.DoesNotContain("legendary", shallow);
        Assert.DoesNotContain("rare", shallow);
        Assert.Contains("rare", deep);   // 第1部 A の最下層(L5 あたり)で、たまに rare
        var items = Treasure.RollMany(data, s, 0.5, 50, rng);
        Assert.All(items, i => Assert.True(data.Items.ContainsKey(i.ItemId)));
        Assert.Contains(items, i => i.Bonus > 0 && !i.Identified);
    }

    [Fact]
    public void ChestTrapsNeedTheRightName()
    {
        var data = Fresh();
        var party = new List<Character> { Fighter(data), CharacterFactory.QuickCreate(data, "R", "halfling", "rogue") };
        var rng = new Pcg32(4, 4);
        var chest = new Chest { Trap = ChestTrap.CrossbowBolt, Gold = 101, Tier = 0, Items = [ItemInstance.Of("potion_of_healing")] };
        Assert.Equal(Chest.DisarmResult.Triggered, chest.Disarm(data, party[1], ChestTrap.PoisonNeedle, rng));
        int hp = party[1].Hp;
        var r = chest.Trigger(data, party, party[1], rng);
        Assert.True(party[1].Hp < hp || party[1].State == LifeState.Dead);
        Assert.False(r.Teleport);
        Assert.Equal(ChestTrap.None, chest.Trap);
        long gold = party.Sum(c => c.Gold);
        var left = chest.Loot(data, party.Where(c => c.IsAlive).ToList());
        Assert.Empty(left);
        Assert.Equal(gold + 101, party.Sum(c => c.Gold));
    }

    [Fact]
    public void GasPoisonsAndTheTempleCures()
    {
        var data = Fresh();
        var party = new List<Character> { Fighter(data) };
        party[0].Scores.Con = 3;   // セーヴに落ちやすく
        var chest = new Chest { Trap = ChestTrap.GasBomb, Tier = 4 };
        for (int i = 0; i < 20 && party[0].Lasting.Count == 0; i++)
        {
            chest.Trap = ChestTrap.GasBomb;
            chest.Trigger(data, party, party[0], new Pcg32((ulong)i, 1));
        }
        Assert.Contains(party[0].Lasting, l => l.Condition == Condition.Poisoned);
        Temple.Cure(party[0]);
        Assert.Empty(party[0].Lasting);
    }

    [Fact]
    public void PartyGoldPaysInOrder()
    {
        var data = Fresh();
        var a = Fighter(data);
        var b = Fighter(data);
        a.Gold = 30;
        b.Gold = 100;
        Assert.False(PartyGold.Pay([a, b], 200));
        Assert.True(PartyGold.Pay([a, b], 50));
        Assert.Equal(0, a.Gold);
        Assert.Equal(80, b.Gold);
    }

    [Fact]
    public void ReviveCanTurnToAshesAndThenBeLost()
    {
        var data = Fresh();
        var c = Fighter(data);
        c.Scores.Con = 3;
        c.State = LifeState.Dead;
        var seen = new HashSet<LifeState>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            c.State = seed % 2 == 0 ? LifeState.Dead : LifeState.Ashes;
            Temple.Revive(c, new Pcg32(seed, 2));
            seen.Add(c.State);
        }
        Assert.Contains(LifeState.Alive, seen);
        Assert.Contains(LifeState.Ashes, seen);
        Assert.Contains(LifeState.Lost, seen);
    }

    [Fact]
    public void PotionsHealAndAreUsedUp()
    {
        var data = Fresh();
        var c = Fighter(data);
        c.Hp = 1;
        c.Inventory.Items.Add(ItemInstance.Of("potion_of_healing", 2));
        var s = new CombatState { Party = [new PartyMember(c, data)], Groups = [] };
        var engine = new CombatEngine(data, new Pcg32(5, 5));
        int index = c.Inventory.Items.FindIndex(i => i.ItemId == "potion_of_healing");
        engine.UseItem(s, s.Party[0], new PlayerCommand(CommandKind.Item, -1, 0, ItemIndex: index));
        Assert.True(c.Hp >= 5);
        Assert.Equal(1, c.Inventory.Items.Single(i => i.ItemId == "potion_of_healing").Count);
    }

    [Fact]
    public void MagicWeaponsHitHarder()
    {
        var data = Fresh();
        data.Items["test_flame"] = new ItemDef
        {
            Id = "test_flame", Kind = ItemKind.Weapon, Base = "longsword", Rarity = "rare", Damage = "1d8", DamageType = "slashing", Category = "martial",
            OnHit = [new EffectPart { Type = EffectPart.Damage, Dice = "10", DamageType = "fire" }],
        };
        var c = Fighter(data);
        c.Inventory.Equipped[EquipSlot.MainHand] = ItemInstance.Of("test_flame");
        var engine = new CombatEngine(data, new Pcg32(6, 6));
        var s = engine.Start([c], [(data.Monsters["ogre"], 1)]);
        for (int i = 0; i < 6 && s.Outcome == CombatOutcome.Ongoing; i++) engine.RunRound(s, CombatSimulator.AutoCommands(s, data));
        Assert.Contains(s.Log, e => e.Key == "combat.damage" && e.Args.Length > 2 && (string)e.Args[2] == "fire" && (int)e.Args[1] == 10);
    }

    [Fact]
    public void KeyItemsSellOnlyWithBuybackAndCostMoreToBuyBack()
    {
        var data = Fresh();
        var sigil = data.Items["sigil_star"];
        var item = ItemInstance.Of("sigil_star");
        Assert.True(Shop.CanSell(sigil));
        long sell = Shop.SellPrice(sigil, item);
        Assert.Equal(sell * (long)sigil.Buyback!.Value, Shop.BuyPrice(sigil, item));
        var plainKey = new ItemDef { Id = "k", Kind = ItemKind.Key, Price = 20 };
        Assert.False(Shop.CanSell(plainKey));
        Assert.True(Shop.CanSell(data.Items["longsword"]));
    }

    [Fact]
    public void AmmoStacksAndSellsInBundles()
    {
        var data = Fresh();
        var iron = data.Items["iron_arrows"];
        Assert.Contains(iron, Shop.Standard(data));
        var bundle = ItemInstance.Of("iron_arrows", iron.Bundle);
        Assert.Equal(20, bundle.Count);
        Assert.Equal(20 * Treasure.Price(iron, ItemInstance.Of("iron_arrows")), Shop.BuyPrice(iron, bundle));
        var c = Fighter(data);
        c.Inventory.Items.Clear();
        Assert.True(c.Inventory.TryAdd(ItemInstance.Of("iron_arrows", 20), data.Items));
        Assert.True(c.Inventory.TryAdd(ItemInstance.Of("iron_arrows", 500), data.Items));
        Assert.Single(c.Inventory.Items);   // 本数に上限はなく、1 枠に重なる
        Assert.Equal(520, c.Inventory.Items[0].Count);
    }
}
