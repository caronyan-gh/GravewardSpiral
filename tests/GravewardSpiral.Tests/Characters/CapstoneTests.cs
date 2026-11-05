using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>クラスの 20 の能力(サブクラスなし)と、鎧の枠のローブ。</summary>
public class CapstoneTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Dictionary<string, PlayerCommand> All(CombatState s, PlayerCommand cmd) => s.Party.ToDictionary(p => p.Id, _ => cmd);

    [Theory]
    [InlineData("barbarian", "primal_champion")]
    [InlineData("paladin", "holy_avatar")]
    [InlineData("ranger", "foe_slayer")]
    [InlineData("rogue", "stroke_of_luck")]
    [InlineData("monk", "martial_mastery")]
    [InlineData("cleric", "supreme_healing")]
    [InlineData("druid", "lord_of_the_wild")]
    [InlineData("sorcerer", "empowered_apex")]
    [InlineData("warlock", "eldritch_master")]
    [InlineData("bard", "endless_inspiration")]
    public void CapstoneComesAtLevel20(string cls, string feature)
    {
        Assert.False(ClassFeatures.HasPassive(Data, CharacterFactory.QuickCreate(Data, "A", "human", cls, 19), feature));
        Assert.True(ClassFeatures.HasPassive(Data, CharacterFactory.QuickCreate(Data, "A", "human", cls, 20), feature));
    }

    [Fact]
    public void RobeCountsAsNoArmorButAddsItsBonus()
    {
        var monk = CharacterFactory.QuickCreate(Data, "M", "human", "monk", 5);
        monk.Inventory.Equipped.Remove(EquipSlot.Armor);
        int bare = monk.ArmorClass(Data);
        monk.Inventory.Equipped[EquipSlot.Armor] = new ItemInstance { ItemId = "robe", Bonus = 3, Identified = true };
        Assert.False(monk.WearingArmor(Data));
        Assert.True(ClassFeatures.MartialArts(Data, monk));
        Assert.Equal(bare + ItemInstance.HitAcBonus(3), monk.ArmorClass(Data));
        var wizard = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 1);
        Assert.True(wizard.CanWear(Data, Data.Items["robe"]));   // 鎧の習熟は要らない
    }

    [Fact]
    public void LifedrinkerAddsNecroticCharisma()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "warlock", 12);
        var p = new CombatEngine(Data, new Pcg32(1, 1)).Start([c], [(Data.Monsters["goblin"], 1)]).Party[0];
        int cha = c.Scores.Mod(Ability.Cha);
        Assert.Contains(FeatureChecks.MeleeWeaponExtraDamage(Data, p), x => x.Type == "necrotic" && x.Dice == cha.ToString());
        Assert.Equal(0, FeatureChecks.MeleeWeaponDrain(Data, p));   // もう吸わない
    }

    [Fact]
    public void RangerFoeSlayerDoublesWisdomOnShots()
    {
        var c = CharacterFactory.QuickCreate(Data, "R", "human", "ranger", 20);
        var p = new CombatEngine(Data, new Pcg32(1, 1)).Start([c], [(Data.Monsters["goblin"], 1)]).Party[0];
        int wis = Math.Max(0, c.Scores.Mod(Ability.Wis));
        Assert.Equal(wis, FeatureChecks.WeaponDamageBonus(Data, p));   // 武器の攻撃に1回
        Assert.Equal(wis, FeatureChecks.RangedShotDamageBonus(Data, p));   // 飛び道具はもう1回(合わせて 2 倍)
        Assert.True(FeatureChecks.SteadyShot(Data, p));
    }

    [Fact]
    public void WarlockGetsAFifthBeam()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "warlock", 20);
        var p = new CombatEngine(Data, new Pcg32(1, 1)).Start([c], [(Data.Monsters["goblin"], 1)]).Party[0];
        Assert.Equal(1, FeatureChecks.ExtraBeams(Data, p, "eldritch_blast"));
        Assert.Equal(0, FeatureChecks.ExtraBeams(Data, p, "fire_bolt"));
    }

    [Fact]
    public void StrokeOfLuckTurnsAtMostOneMissPerRound()
    {
        var c = CharacterFactory.QuickCreate(Data, "R", "human", "rogue", 20);
        bool used = false;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var engine = new CombatEngine(Data, new Pcg32(seed, 7));
            var s = engine.Start([c], [(Data.Monsters["adult_red_dragon"], 1)]);
            s.Surprised = null;   // 不意打ちで番が飛ばないように
            engine.RunRound(s, All(s, new PlayerCommand(CommandKind.Attack, TargetGroup: 0)));
            int n = s.Log.Count(e => e.Key == "combat.stroke_of_luck");
            Assert.True(n <= 1);
            used |= n == 1;
        }
        Assert.True(used);
    }

    [Fact]
    public void SupremeHealingAlwaysHealsTheMaximum()
    {
        var healed = new HashSet<int>();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var cleric = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 20);
            var ally = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 20);
            ally.Hp = 1;
            if (!cleric.Books["cleric"].Prepared.Contains("cure_wounds")) cleric.Books["cleric"].Prepared.Add("cure_wounds");
            var engine = new CombatEngine(Data, new Pcg32(seed, 3));
            var s = engine.Start([cleric, ally], [(Data.Monsters["goblin"], 1)]);
            engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, TargetMember: 1, SpellId: "cure_wounds", SlotLevel: 1));
            healed.Add(s.Party[1].Character.Hp - 1);
        }
        Assert.Single(healed);
        var cl = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 20);
        Assert.Equal(8 + cl.Scores.Mod(Ability.Wis), healed.Single());
    }

    [Fact]
    public void ArcaneApexSetsTheLowestDiceToMax()
    {
        // 8d6 の 5 個(魅力 20 なら)を最大にすると、セーヴに成功して半分でも 16 を下回らない
        int lowest = int.MaxValue;
        string log = "";
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var c = CharacterFactory.QuickCreate(Data, "S", "human", "sorcerer", 20);
            c.Scores[Ability.Cha] = 20;
            if (!c.Books["sorcerer"].Known.Contains("fireball")) c.Books["sorcerer"].Known.Add("fireball");
            var engine = new CombatEngine(Data, new Pcg32(seed, 5));
            var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
            var ogre = s.Groups[0].Units[0];
            int before = ogre.Hp;
            engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, TargetGroup: 0, SpellId: "fireball", SlotLevel: 3, Empower: true));
            int dealt = before - Math.Max(0, ogre.Hp);
            if (dealt < lowest) { lowest = dealt; log = string.Join(" | ", s.Log.Select(e => e.Key + ":" + string.Join(",", e.Args))); }
        }
        Assert.True(lowest >= 16, $"lowest {lowest} " + log);
    }

    [Fact]
    public void EndlessInspirationKeepsEveryoneInspired()
    {
        var bard = CharacterFactory.QuickCreate(Data, "B", "human", "bard", 20);
        var fighter = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5);
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([bard, fighter], [(Data.Monsters["goblin"], 1)]);
        engine.RunRound(s, All(s, PlayerCommand.Dodge));
        Assert.All(s.Party, p => Assert.Contains(p.Modifiers, m => m.Stat == "inspire"));
    }

    [Fact]
    public void SpellMasteryCastsWithoutASlot()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 18);
        if (!c.Books["wizard"].Known.Contains("magic_missile")) c.Books["wizard"].Known.Add("magic_missile");
        c.Books["wizard"].Prepared.Remove("magic_missile");
        Assert.Equal(2, Spellcasting.MasteryMaxLevel(Data, c));
        c.MasterySpell = "magic_missile";
        Assert.True(Spellcasting.IsMastery(Data, c, Data.Spells["magic_missile"]));
        Assert.True(Spellcasting.ListedAt(Data, c, Data.Spells["magic_missile"], 0));
        int slots = Spellcasting.SlotsLeft(Data, c, 1);
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, TargetGroup: 0, SpellId: "magic_missile", SlotLevel: 0));
        Assert.Contains(s.Log, e => e.Key == "combat.cast");
        Assert.Equal(slots, Spellcasting.SlotsLeft(Data, c, 1));
        // 3 レベルの呪文は L20 から
        Assert.DoesNotContain(Spellcasting.MasteryChoices(Data, c), sp => sp.Level > 2);
    }

    [Fact]
    public void LordOfTheWildBringsTheSummonToEveryBattle()
    {
        var c = CharacterFactory.QuickCreate(Data, "D", "human", "druid", 20);
        Assert.Contains(Spellcasting.CompanionChoices(Data, c), sp => sp.Id == "conjure_animals");
        c.CompanionSpell = "conjure_animals";
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([c], [(Data.Monsters["goblin"], 1)]);
        Assert.Contains(s.Log, e => e.Key == "combat.companion");
        Assert.Contains(s.Lingerings, l => l.Unit is { Count: 2 });
        Assert.Null(s.Party[0].Concentration);
    }

    [Fact]
    public void LordOfTheWildSurvivesSwitchingConcentration()
    {
        var c = CharacterFactory.QuickCreate(Data, "D", "human", "druid", 20);
        c.CompanionSpell = "conjure_animals";
        c.Book("druid").Prepared.AddRange(["entangle", "faerie_fire"]);
        var engine = new CombatEngine(Data, new Pcg32(1, 3));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, TargetGroup: 0, SpellId: "entangle", SlotLevel: 1));
        engine.CastSpell(s, s.Party[0], new PlayerCommand(CommandKind.Cast, TargetGroup: 0, SpellId: "faerie_fire", SlotLevel: 1));
        Assert.Equal("faerie_fire", s.Party[0].Concentration);
        Assert.Single(s.Lingerings, l => l.Unit != null);   // 集中を替えても、森の主の召喚は残る
    }

    [Fact]
    public void EpicSpellBoonDoesNotApplyToScrolls()
    {
        int Heal(bool boon)
        {
            var c = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 20);
            c.Hp = 1;
            c.Inventory.Items.Clear();
            if (boon) c.EpicBoons.Add("spell");
            c.Inventory.TryAdd(ItemInstance.Of("scroll_cure_wounds"), Data.Items);
            var engine = new CombatEngine(Data, new Pcg32(1, 3));
            var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
            engine.UseItem(s, s.Party[0], new PlayerCommand(CommandKind.Item, TargetMember: 0, ItemIndex: 0));
            return c.Hp - 1;
        }
        Assert.Equal(Heal(false), Heal(true));
    }

    [Fact]
    public void FoeSlayerCountsEveryWeaponAttack()
    {
        var c = CharacterFactory.QuickCreate(Data, "R", "human", "ranger", 20);
        c.Scores[Ability.Wis] = 18;
        // どの手の武器でも、投げても +判断力。飛び道具はさらにもう1回
        Assert.Equal(4, PassiveRules.Sum(Data, c, "damage_bonus", "weapon"));
        Assert.Equal(4, PassiveRules.Sum(Data, c, "damage_bonus", "ranged_shot"));
    }

    [Fact]
    public void AutoCastsTheMasterySpellWhenSlotsAreGone()
    {
        var c = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 18);
        if (!c.Book("wizard").Known.Contains("magic_missile")) c.Book("wizard").Known.Add("magic_missile");
        c.MasterySpell = "magic_missile";
        c.Book("wizard").Cantrips.Clear();
        for (int lv = 1; lv <= 9; lv++) c.SlotsUsed[lv] = 100;
        var engine = new CombatEngine(Data, new Pcg32(1, 3));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        var cmd = CombatSimulator.AutoCommands(s, Data)[c.Id];
        Assert.Equal(CommandKind.Cast, cmd.Kind);
        Assert.Equal("magic_missile", cmd.SpellId);
        Assert.Equal(0, cmd.SlotLevel);
    }

    [Fact]
    public void PrimalChampionHalvesFireWhileRaging()
    {
        var c = CharacterFactory.QuickCreate(Data, "B", "human", "barbarian", 20);
        var p = new CombatEngine(Data, new Pcg32(1, 1)).Start([c], [(Data.Monsters["goblin"], 1)]).Party[0];
        p.Raging = true;
        Assert.Contains(GravewardSpiral.Core.Characters.PassiveRules.Effects(Data, c, "resist_damage", p.Now), e => e.Effect.DamageType == "fire");
        Assert.Contains(GravewardSpiral.Core.Characters.PassiveRules.Effects(Data, c, "no_advantage_against", p.Now), _ => true);
    }
}
