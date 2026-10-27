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
