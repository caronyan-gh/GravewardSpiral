using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Magic;

/// <summary>5版から削られていた呪文の効果を足したもの。</summary>
public class SpellFixTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static IEnumerable<EffectPart> Parts(string id) => Data.Spells[id].Effects.SelectMany(e => e.Flatten());

    [Fact]
    public void MissingPartsAreBack()
    {
        Assert.Contains(Parts("ice_storm"), p => p.DamageType == "cold");
        Assert.Contains(Parts("flame_strike"), p => p.DamageType == "radiant");
        Assert.Contains(Parts("call_lightning"), p => p.Type == EffectPart.Save && p.Half);
        Assert.Contains(Parts("black_tentacles"), p => p.Condition == Condition.Restrained);
        Assert.Contains(Parts("sunburst"), p => p.Condition == Condition.Blinded);
        Assert.Contains(Parts("sunbeam"), p => p.Condition == Condition.Blinded);
        Assert.Contains(Parts("phantasmal_killer"), p => p.Condition == Condition.Frightened);
        Assert.Contains(Parts("heal"), p => p.Type == EffectPart.RemoveCondition && p.Condition == Condition.Blinded);
        Assert.Contains(Parts("heroism"), p => p.Type == EffectPart.ImmuneCondition && p.Condition == Condition.Frightened);
        Assert.True(Data.Spells["stinking_cloud"].Zone);
        Assert.Equal(SpellStatus.Roleplay, Data.Spells["dimension_door"].Status);
    }

    [Fact]
    public void SpiritualWeaponKeepsAttackingWithoutConcentration()
    {
        // 集中の要らない続く呪文: 唱えたあとも番ごとに攻撃し、ほかの集中の呪文を唱えても消えない。倒れたら次の敵へ
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "cleric", 5);
        c.Book("cleric").Prepared.Add("spiritual_weapon");
        c.Book("cleric").Prepared.Add("bless");
        var engine = new CombatEngine(Data, new Pcg32(2, 2));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 3)]);
        s.Surprised = null;
        foreach (var u in s.Groups[0].Units) u.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Cast, 0, -1, "spiritual_weapon", 2) });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Cast, -1, 0, "bless", 1) });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = PlayerCommand.Dodge });
        Assert.Equal(2, s.Log.Count(l => l.Key == "combat.zone"));
        Assert.Single(s.Lingerings);
    }

    [Fact]
    public void GuardianOfFaithBurnsThreeTimes()
    {
        // 唱えたときと、そのあと2回(20 ずつ、合わせて 60 まで)
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "cleric", 7);
        c.Book("cleric").Prepared.Add("guardian_of_faith");
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 4)]);
        s.Surprised = null;
        foreach (var u in s.Groups[0].Units) u.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Cast, 0, -1, "guardian_of_faith", 4) });
        for (int i = 0; i < 4; i++) engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = PlayerCommand.Dodge });
        Assert.Equal(3, s.Log.Count(l => l.Key is "combat.save_ok" or "combat.save_fail"));
    }

    [Fact]
    public void ABonusActionSpellIsFollowedByAnAction()
    {
        // 後列のクレリックがヒーリング・ワードを唱えると、同じ番に攻撃の初級呪文も唱える
        var front = Enumerable.Range(0, 3).Select(i => CharacterFactory.QuickCreate(Data, "F" + i, "human", "fighter", 3)).ToList();
        var cleric = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 3);
        cleric.Book("cleric").Prepared.Add("healing_word");
        cleric.Book("cleric").Cantrips.Add("sacred_flame");
        front[0].Hp = 5;
        var engine = new CombatEngine(Data, new Pcg32(4, 4));
        var s = engine.Start([.. front, cleric], [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [cleric.Id] = new(CommandKind.Cast, -1, 0, "healing_word", 1) });
        var casts = s.Log.Where(l => l.Key == "combat.cast" && l.Args[0] is LocalizedText t && t.Get("en") == "C").ToList();
        Assert.Equal(2, casts.Count);
        Assert.Equal("Sacred Flame", ((LocalizedText)casts[1].Args[1]).Get("en"));
        Assert.False(CombatEngine.IsBonusAction(Data.Spells["flame_blade"]));
    }

    [Fact]
    public void SecondWindIsFollowedByAnAttack()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "fighter", 3);
        c.Hp = 5;
        var engine = new CombatEngine(Data, new Pcg32(4, 4));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Feature, 0, FeatureId: "second_wind") });
        Assert.Contains(s.Log, l => l.Key is "combat.hit" or "combat.crit" or "combat.miss");
    }

    [Fact]
    public void TheFollowUpAttackUsesNoSecondBonusAction()
    {
        // セカンド・ウィンド(ボーナスアクション)のあとの攻撃では、武術の素手の一撃(もう1つのボーナスアクション)をしない
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "monk", 4);
        c.Classes.Add(new ClassLevel { ClassId = "fighter", Level = 1 });
        c.Level = 5;
        c.Hp = 5;
        var engine = new CombatEngine(Data, new Pcg32(4, 4));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Feature, 0, FeatureId: "second_wind") });
        Assert.Equal(1, s.Log.Count(l => l.Key is "combat.hit" or "combat.crit" or "combat.miss"));
    }

    [Theory]
    [InlineData("rage", "barbarian")]
    [InlineData("patient_defense", null)]
    public void FeaturesThatUseTheBonusActionStrikeOnce(string feature, string? second)
    {
        // 激怒に入る番と忍耐の守りの番は、ボーナスアクションを使うので、武術の素手の一撃をしない
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "monk", 4);
        if (second != null)
        {
            c.Classes.Add(new ClassLevel { ClassId = second, Level = 1 });
            c.Level = 5;
        }
        var engine = new CombatEngine(Data, new Pcg32(4, 4));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        s.Groups[0].Units[0].Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Feature, 0, FeatureId: feature) });
        Assert.Equal(1, s.Log.Count(l => l.Key is "combat.hit" or "combat.crit" or "combat.miss"));
    }

    [Fact]
    public void QuickCreateFillsThePreparedSpellsAfterTheAbilityRaise()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "wizard", 8);
        var cls = Data.Classes["wizard"];
        Assert.Equal(Spellcasting.PreparedCount(cls, c), c.Book("wizard").Prepared.Count);
    }

    [Theory]
    [InlineData(4, null, 1)]
    [InlineData(5, null, 2)]
    [InlineData(11, "spread", 3)]
    [InlineData(17, "spread", 4)]
    public void EldritchBlastFiresMoreBeamsWithCharacterLevel(int level, string? option, int beams)
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "warlock", level);
        if (!c.Book("warlock").Cantrips.Contains("eldritch_blast")) c.Book("warlock").Cantrips.Add("eldritch_blast");
        var engine = new CombatEngine(Data, new Pcg32(9, 9));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 6)]);   // 1本では倒れない相手
        s.Surprised = null;
        foreach (var u in s.Groups[0].Units) u.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Cast, 0, -1, "eldritch_blast", 0, Option: option) });
        Assert.Equal(beams, s.Log.Count(l => l.Key is "combat.hit" or "combat.crit" or "combat.miss"));
    }

    [Fact]
    public void ScorchingRayFiresOneMoreRayPerSlotLevel()
    {
        Assert.Equal(3, Data.Spells["scorching_ray"].BeamsAt(2));
        Assert.Equal(5, Data.Spells["scorching_ray"].BeamsAt(4));
        Assert.Equal("ranged", Data.Spells["scorching_ray"].Attack);
    }

    [Fact]
    public void ChainLightningHitsAtMostFour()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "wizard", 11);
        c.Book("wizard").Prepared.Add("chain_lightning");
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        var s = engine.Start([c], [(Data.Monsters["goblin"], 6)]);
        s.Surprised = null;
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Cast, 0, -1, "chain_lightning", 6) });
        Assert.Equal(4, s.Log.Count(l => l.Key is "combat.save_ok" or "combat.save_fail"));
    }
}
