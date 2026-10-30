using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

public class CombatTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static List<Character> Party(int level = 1) =>
    [
        CharacterFactory.QuickCreate(Data, "A", "human", "fighter", level),
        CharacterFactory.QuickCreate(Data, "B", "dwarf", "fighter", level),
        CharacterFactory.QuickCreate(Data, "C", "human", "paladin", level),
        CharacterFactory.QuickCreate(Data, "D", "halfling", "rogue", level),
        CharacterFactory.QuickCreate(Data, "E", "elf", "ranger", level),
        CharacterFactory.QuickCreate(Data, "F", "human", "cleric", level),
    ];

    private static CombatState Manual(List<Character> party, MonsterDef def, int count) => new()
    {
        Party = party.Select(c => new PartyMember(c, Data)).ToList(),
        Groups = [new EnemyGroup(def, Enumerable.Range(0, count).Select(_ => new MonsterUnit(def, def.HpAverage)).ToList())],
    };

    [Fact]
    public void SrdMonstersAreConverted()
    {
        Assert.True(Data.Monsters.Count >= 330);
        Assert.True(Data.Monsters.Values.Count(m => m.Usable) >= 320);
        var goblin = Data.Monsters["goblin"];
        Assert.Equal("ゴブリン", goblin.Name.Get("ja"));
        Assert.Equal(15, goblin.Ac);
        Assert.Equal(50, goblin.Xp);
        Assert.Equal(4, goblin.Action("scimitar")!.ToHit);
        var dragon = Data.Monsters["young_red_dragon"];
        var breath = dragon.Action("fire_breath")!;
        Assert.Equal(5, breath.Recharge);
        Assert.Equal(ActionTarget.Row, breath.Target);
        Assert.Contains("fire", dragon.Immune);
        Assert.NotEmpty(dragon.Multiattack);
    }

    [Fact]
    public void ResistanceVulnerabilityAndImmunity()
    {
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var skel = Data.Monsters["skeleton"];   // 殴打に脆弱、毒に完全耐性
        var s = Manual(Party(), skel, 1);
        var u = s.Groups[0].Units[0];
        int hp = u.Hp;
        engine.Damage(s, u, 3, "bludgeoning", false, false);
        Assert.Equal(hp - 6, u.Hp);
        engine.Damage(s, u, 50, "poison", false, false);
        Assert.Equal(hp - 6, u.Hp);
        var ghost = Data.Monsters["ghost"];   // 魔法でない武器の物理に抵抗
        var s2 = Manual(Party(), ghost, 1);
        var g = s2.Groups[0].Units[0];
        hp = g.Hp;
        engine.Damage(s2, g, 9, "slashing", false, magical: false);
        Assert.Equal(hp - 4, g.Hp);
    }

    [Fact]
    public void FallingToZeroAndDeath()
    {
        var engine = new CombatEngine(Data, new Pcg32(2, 1));
        var party = Party();
        var s = Manual(party, Data.Monsters["goblin"], 1);
        var a = s.Party[0];
        engine.Damage(s, a, a.Hp + 1, "slashing", false, false);
        Assert.Equal(LifeState.Unconscious, a.Character.State);
        Assert.False(a.Standing);
        Assert.DoesNotContain(a, s.FrontRowMembers);          // 倒れた人は前列から外れ、後ろの人が前に出る
        Assert.Contains(s.Party[3], s.FrontRowMembers);
        engine.Damage(s, a, 1, "slashing", crit: true, false);  // 倒れているあいだのクリティカルは失敗 2 つ
        Assert.Equal(2, a.DeathFailures);
        engine.Damage(s, a, 1, "slashing", false, false);
        Assert.Equal(LifeState.Dead, a.Character.State);

        var b = s.Party[1];
        engine.Damage(s, b, b.Hp + b.MaxHp, "slashing", false, false);   // 残りが最大 HP 以上なら即死
        Assert.Equal(LifeState.Dead, b.Character.State);
    }

    [Fact]
    public void ParalysisMakesStrAndDexSavesFail()
    {
        var engine = new CombatEngine(Data, new Pcg32(3, 1));
        var s = Manual(Party(), Data.Monsters["goblin"], 1);
        var p = s.Party[0];
        p.Conditions.Add(new ActiveCondition { Condition = Condition.Paralyzed, RoundsLeft = 3 });
        for (int i = 0; i < 50; i++) Assert.False(engine.SavingThrow(p, Core.Rules.Ability.Dex, 2));
        Assert.False(p.CanAct);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void EncountersFitTheBudget(double depth)
    {
        var settings = EncounterSettings.Load(Content);
        for (ulong seed = 0; seed < 200; seed++)
        {
            var e = Encounters.Build(Data, settings, depth, 6, new Pcg32(seed, 9));
            Assert.InRange(e.Groups.Count, 1, 4);
            Assert.All(e.Groups, g => Assert.True(g.Def.Usable));
            Assert.All(e.Groups, g => Assert.True(g.Count >= 1));
            if (e.Groups.Sum(g => g.Count) > 1) Assert.True(e.AdjustedXp <= e.Budget, $"seed {seed}: {e.AdjustedXp} > {e.Budget}");
        }
    }

    [Fact]
    public void ThresholdsFollow5e()
    {
        Assert.Equal(25, Encounters.Threshold(1, EncounterDifficulty.Easy));
        Assert.Equal(12700, Encounters.Threshold(20, EncounterDifficulty.Deadly));
        Assert.Equal(12700 * 1.1, Encounters.Threshold(21, EncounterDifficulty.Deadly), 3);
        Assert.Equal(2, Encounters.CountMultiplier(3));
        Assert.Equal(EncounterDifficulty.Medium, EncounterSettings.Load(Content).Difficulty.Keys.Skip(1).First());
    }

    [Fact]
    public void ExtraAttackByClassAndLevel()
    {
        Assert.Equal(1, CombatEngine.AttacksPerAction(Data, CharacterFactory.QuickCreate(Data, "x", "human", "fighter", 4)));
        Assert.Equal(2, CombatEngine.AttacksPerAction(Data, CharacterFactory.QuickCreate(Data, "x", "human", "fighter", 5)));
        Assert.Equal(3, CombatEngine.AttacksPerAction(Data, CharacterFactory.QuickCreate(Data, "x", "human", "fighter", 11)));
        Assert.Equal(2, CombatEngine.AttacksPerAction(Data, CharacterFactory.QuickCreate(Data, "x", "human", "ranger", 5)));
        Assert.Equal(1, CombatEngine.AttacksPerAction(Data, CharacterFactory.QuickCreate(Data, "x", "human", "rogue", 9)));
    }

    [Fact]
    public void SimulatedFightsHaveSensibleOutcomes()
    {
        var goblins = CombatSimulator.Run(Data, 200, 7, _ => Party(), _ => [(Data.Monsters["goblin"], 3)]);
        Assert.True(goblins.WinRate > 0.95, $"ゴブリン3体に {goblins.WinRate:P0}");
        var dragon = CombatSimulator.Run(Data, 100, 7, _ => Party(), _ => [(Data.Monsters["young_red_dragon"], 1)]);
        Assert.True(dragon.WinRate < 0.1, $"若い赤竜に {dragon.WinRate:P0}");
    }

    [Fact]
    public void SameSeedGivesSameFight()
    {
        string Fight()
        {
            var engine = new CombatEngine(Data, new Pcg32(99, 3));
            var s = engine.Start(Party(), [(Data.Monsters["orc"], 2), (Data.Monsters["wolf"], 2)]);
            while (s.Outcome == CombatOutcome.Ongoing && s.Round < 50) engine.RunRound(s, CombatSimulator.AutoCommands(s, Data));
            return string.Join("\n", s.Log.Select(l => l.Key + ":" + string.Join(",", l.Args.Select(a => a.ToString()))));
        }
        Assert.Equal(Fight(), Fight());
    }

    [Fact]
    public void VictoryGivesXpAndWakesTheFallen()
    {
        var party = Party();
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        var s = engine.Start(party, [(Data.Monsters["goblin"], 2)]);
        while (s.Outcome == CombatOutcome.Ongoing && s.Round < 50) engine.RunRound(s, CombatSimulator.AutoCommands(s, Data));
        Assert.Equal(CombatOutcome.Victory, s.Outcome);
        Assert.NotNull(s.Reward);
        Assert.Equal(100 / s.Reward!.Survivors, s.Reward.XpEach);
        Assert.All(party.Where(c => c.State != LifeState.Dead), c => Assert.True(c.Hp >= 1 && c.State == LifeState.Alive));
        Assert.All(party.Where(c => c.State != LifeState.Dead), c => Assert.Equal(s.Reward.XpEach, c.Xp));
    }

    [Fact]
    public void MeleeReachIsLimitedToFrontRowAndNearGroups()
    {
        var s = Manual(Party(), Data.Monsters["goblin"], 1);
        s.Groups.Add(new EnemyGroup(Data.Monsters["kobold"], [new MonsterUnit(Data.Monsters["kobold"], 5)]));
        s.Groups.Add(new EnemyGroup(Data.Monsters["wolf"], [new MonsterUnit(Data.Monsters["wolf"], 11)]));
        Assert.Equal([0, 1], s.AttackableGroups(s.Party[0]));        // 前列のファイター: 近くの2グループ
        Assert.Equal([0, 1, 2], s.AttackableGroups(s.Party[4]));     // 後列のレンジャー(長弓): どこでも
        Assert.Empty(s.AttackableGroups(s.Party[5]));                // 後列のクレリック(メイス): 届かない
    }

    [Fact]
    public void FleeingCarriesTheFallenOutAtOneHp()
    {
        var party = Party(3);
        var engine = new CombatEngine(Data, new Pcg32(7, 7));
        var s = engine.Start(party, [(Data.Monsters["goblin"], 2)]);
        party[0].State = LifeState.Unconscious;
        party[0].Hp = 0;
        s.Party[0].Hp = 0;
        for (int i = 0; i < 30 && s.Outcome == CombatOutcome.Ongoing; i++)
            engine.RunRound(s, s.Party.Where(p => p.CanAct).ToDictionary(p => p.Id, _ => new PlayerCommand(CommandKind.Flee)));
        Assert.Equal(CombatOutcome.Fled, s.Outcome);
        Assert.Equal(LifeState.Alive, party[0].State);
        Assert.True(party[0].Hp >= 1);
    }

    [Fact]
    public void TwoLightWeaponsGiveAnOffHandAttack()
    {
        int Swings(bool dual)
        {
            var rogue = CharacterFactory.QuickCreate(Data, "R", "human", "rogue");
            rogue.Inventory.Equipped[Core.Items.EquipSlot.MainHand] = Core.Items.ItemInstance.Of("shortsword");
            if (dual) rogue.Inventory.Equipped[Core.Items.EquipSlot.OffHand] = Core.Items.ItemInstance.Of("dagger");
            else rogue.Inventory.Equipped.Remove(Core.Items.EquipSlot.OffHand);
            var engine = new CombatEngine(Data, new Pcg32(3, 3));
            var s = engine.Start([rogue], [(Data.Monsters["ogre"], 1)]);
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Attack, 0) });
            return s.Log.Count(e => e.Key is "combat.hit" or "combat.crit" or "combat.miss" && e.Args[0]?.ToString() == "R");
        }
        Assert.Equal(1, Swings(dual: false));
        Assert.Equal(2, Swings(dual: true));
    }

    [Fact]
    public void AttacksPickTheStrongerEquippedWeapon()
    {
        string WeaponUsed(string main, bool frontRow)
        {
            var ranger = CharacterFactory.QuickCreate(Data, "R", "human", "ranger");
            ranger.Inventory.Equipped[Core.Items.EquipSlot.MainHand] = Core.Items.ItemInstance.Of(main);
            ranger.Inventory.Equipped[Core.Items.EquipSlot.Ranged] = Core.Items.ItemInstance.Of("longbow");
            ranger.Inventory.Equipped.Remove(Core.Items.EquipSlot.OffHand);
            var party = frontRow ? new List<Character> { ranger } : [.. Party().Take(3), ranger];
            var engine = new CombatEngine(Data, new Pcg32(5, 5));
            var s = engine.Start(party, [(Data.Monsters["ogre"], 1)]);
            var me = s.Party.First(x => x.Character == ranger);
            var cmds = s.Party.ToDictionary(x => x.Id, x => x == me ? new PlayerCommand(CommandKind.Attack, 0) : PlayerCommand.Dodge);
            engine.RunRound(s, cmds);
            return s.Log.First(e => e.Key is "combat.hit" or "combat.crit" or "combat.miss" && e.Args[0]?.ToString() == "R").Args[1].ToString()!;
        }
        Assert.Equal("Longbow", WeaponUsed("club", frontRow: true));        // 棍棒(1d4)より、隣接で不利でも弓(1d8+敏捷)
        Assert.Equal("Greatsword", WeaponUsed("greatsword", frontRow: true));   // 両手剣(2d6)なら近接
        Assert.Equal("Longbow", WeaponUsed("greatsword", frontRow: false));     // 後列からは弓だけ
    }

    [Fact]
    public void BackpackWeaponsAreNotUsed()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "wizard");
        c.Inventory.Equipped.Remove(Core.Items.EquipSlot.Ranged);
        c.Inventory.Equipped.Remove(Core.Items.EquipSlot.MainHand);
        c.Inventory.Items.Add(Core.Items.ItemInstance.Of("light_crossbow"));
        Assert.False(new PartyMember(c, Data).HasReach);
    }

    [Fact]
    public void ThrownWeaponsRunOutAndAreLostOnlyWhenFleeing()
    {
        Character Thrower()
        {
            var c = CharacterFactory.QuickCreate(Data, "R", "human", "rogue");
            c.Inventory.Equipped.Remove(Core.Items.EquipSlot.MainHand);
            c.Inventory.Equipped[Core.Items.EquipSlot.Ranged] = Core.Items.ItemInstance.Of("dagger", 2);
            return c;
        }
        // 後列から2本投げると尽き、3ラウンド目は届かない
        var rogue = Thrower();
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        var s = engine.Start([.. Party().Take(3), rogue], [(Data.Monsters["ogre"], 1)]);
        var me = s.Party.First(x => x.Character == rogue);
        Dictionary<string, PlayerCommand> Cmd(PlayerCommand c) => s.Party.ToDictionary(x => x.Id, x => x == me ? c : PlayerCommand.Dodge);
        for (int r = 0; r < 2; r++) engine.RunRound(s, Cmd(new PlayerCommand(CommandKind.Attack, 0)));
        Assert.Equal(2, me.Thrown.Values.Sum());
        Assert.Contains(s.Log, e => e.Key == "combat.thrown_out");
        Assert.False(me.HasReach);
        // 逃げると、投げたぶんをなくす
        for (int r = 0; r < 30 && s.Outcome == CombatOutcome.Ongoing; r++)
            engine.RunRound(s, s.Party.ToDictionary(x => x.Id, _ => new PlayerCommand(CommandKind.Flee, 0)));
        Assert.Equal(CombatOutcome.Fled, s.Outcome);
        Assert.Null(rogue.Inventory[Core.Items.EquipSlot.Ranged]);
        Assert.Contains(s.Log, e => e.Key == "combat.thrown_lost");

        // 勝てば拾って戻る
        var winner = Thrower();
        var engine2 = new CombatEngine(Data, new Pcg32(6, 6));
        var s2 = engine2.Start([.. Party().Take(3), winner], [(Data.Monsters["rat"], 1)]);
        for (int r = 0; r < 30 && s2.Outcome == CombatOutcome.Ongoing; r++)
            engine2.RunRound(s2, s2.Party.ToDictionary(x => x.Id, _ => new PlayerCommand(CommandKind.Attack, 0)));
        Assert.Equal(CombatOutcome.Victory, s2.Outcome);
        var me2 = s2.Party.First(x => x.Character == winner);
        Assert.Empty(me2.Thrown);   // 片づけた
        int left = winner.Inventory[Core.Items.EquipSlot.Ranged]?.Count ?? 0;
        Assert.InRange(left, 2 - s2.Log.Where(e => e.Key == "combat.thrown_lost").Sum(e => (int)e.Args[2]), 2);
    }

    [Fact]
    public void MetallicDragonBreathsShareOneRecharge()
    {
        var silver = Data.Monsters["adult_silver_dragon"];
        Assert.Equal(2, silver.Actions.Count(a => a.RechargeGroup == "breath"));   // 冷気の息と麻痺の息
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var s = engine.Start(Party(), [(silver, 1)]);
        var dragon = s.Groups[0].Alive.First();
        bool sawBreath = false;
        for (int r = 0; r < 12 && s.Outcome == CombatOutcome.Ongoing; r++)
        {
            engine.RunRound(s, s.Party.ToDictionary(x => x.Id, _ => PlayerCommand.Dodge));
            if (dragon.Spent.Count == 0) continue;
            sawBreath = true;
            Assert.All(silver.Actions.Where(a => a.RechargeGroup == "breath"), a => Assert.Contains(a.Id, dragon.Spent));   // どちらかを使えば両方使えない
        }
        Assert.True(sawBreath);
    }

    [Fact]
    public void ReturningWeaponsCanBeThrownEveryRound()
    {
        var rogue = CharacterFactory.QuickCreate(Data, "R", "human", "rogue");
        rogue.Inventory.Equipped.Remove(Core.Items.EquipSlot.MainHand);
        rogue.Inventory.Equipped[Core.Items.EquipSlot.Ranged] = new Core.Items.ItemInstance { ItemId = "returning_dagger", Identified = true };
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        var s = engine.Start([.. Party().Take(3), rogue], [(Data.Monsters["ogre"], 1)]);
        var me = s.Party.First(x => x.Character == rogue);
        for (int r = 0; r < 4 && s.Outcome == CombatOutcome.Ongoing; r++)
            engine.RunRound(s, s.Party.ToDictionary(x => x.Id, x => x == me ? new PlayerCommand(CommandKind.Attack, 0) : PlayerCommand.Dodge));
        Assert.Empty(me.Thrown);   // 戻ってくるので減らない
        Assert.True(me.HasReach);
        Assert.True(Data.Items["dwarven_thrower"].Has("returning") && Data.Items["dwarven_thrower"].Has("thrown"));
    }

    [Fact]
    public void SpecialAmmoIsSpentAndCanBeSwappedInCombat()
    {
        var archer = CharacterFactory.QuickCreate(Data, "A", "elf", "ranger");
        archer.Inventory.Equipped[Core.Items.EquipSlot.Ranged] = Core.Items.ItemInstance.Of("longbow");
        archer.Inventory.Equipped[Core.Items.EquipSlot.Ammo] = Core.Items.ItemInstance.Of("arrows_plus1", 2);
        archer.Inventory.Equipped[Core.Items.EquipSlot.MainHand] = Core.Items.ItemInstance.Of("club");
        archer.Inventory.Items.Clear();
        archer.Inventory.Items.Add(Core.Items.ItemInstance.Of("iron_bolts", 20));    // 弓には合わない
        archer.Inventory.Items.Add(Core.Items.ItemInstance.Of("mithral_arrows", 20));
        List<Character> party = [.. Party().Take(3), archer];
        var engine = new CombatEngine(Data, new Pcg32(8, 8));
        var s = engine.Start(party, [(Data.Monsters["ogre"], 1)]);
        var me = s.Party.First(x => x.Character == archer);
        Dictionary<string, PlayerCommand> Cmd(PlayerCommand c) => s.Party.ToDictionary(x => x.Id, x => x == me ? c : PlayerCommand.Dodge);

        engine.RunRound(s, Cmd(new PlayerCommand(CommandKind.Attack, 0)));
        Assert.Equal(1, archer.Inventory[Core.Items.EquipSlot.Ammo]!.Count);
        engine.RunRound(s, Cmd(new PlayerCommand(CommandKind.Attack, 0)));
        Assert.Null(archer.Inventory[Core.Items.EquipSlot.Ammo]);   // 尽きたら外れる(次はふつうの矢)
        Assert.Contains(s.Log, e => e.Key == "combat.ammo_out");

        int mithral = archer.Inventory.Items.FindIndex(i => i.ItemId == "mithral_arrows");
        engine.RunRound(s, Cmd(new PlayerCommand(CommandKind.Item, -1, -1, ItemIndex: mithral)));
        Assert.Equal("mithral_arrows", archer.Inventory[Core.Items.EquipSlot.Ammo]!.ItemId);
        Assert.Contains(s.Log, e => e.Key == "combat.ammo_set");
        Assert.Equal("arrow", Data.Items["longbow"].UsesAmmo);
        Assert.Equal("bolt", Data.Items["light_crossbow"].UsesAmmo);
        Assert.Null(Data.Items["club"].UsesAmmo);
    }
}
