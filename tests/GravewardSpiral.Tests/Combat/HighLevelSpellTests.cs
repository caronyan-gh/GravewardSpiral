using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

public class HighLevelSpellTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void MeteorSwarmHitsEveryGroupWithFireAndBludgeoning()
    {
        var m = Data.Spells["meteor_swarm"];
        Assert.Equal(SpellTarget.AllEnemies, m.Target);
        var fail = m.Effects.Single().Fail;
        Assert.Equal(["fire", "bludgeoning"], fail.Select(f => f.DamageType!).ToArray());
    }

    [Fact]
    public void HasteGivesAnExtraAttackAndSpellsCanGrantImmunity()
    {
        Assert.True(Content.IsValid, string.Join("\n", Content.Problems));
        Assert.Contains(Data.Spells["haste"].Effects, e => e.Type == EffectPart.Modifier && e.Stat == "attacks" && e.Value == 1);
        Assert.Contains(Data.Spells["freedom_of_movement"].Effects, e => e.Type == EffectPart.ImmuneCondition && e.Condition == Condition.Paralyzed);
        Assert.True(Data.Spells.Values.Count(s => s.Usable && s.Level >= 7) >= 25);
    }

    [Fact]
    public void TimedImmunityStopsTheCondition()
    {
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([CharacterFactory.QuickCreate(Data, "A", "human", "fighter", 5)], [(Data.Monsters["goblin"], 1)]);
        var p = s.Party[0];
        Assert.False(p.Immune(Condition.Paralyzed));
        p.Modifiers.Add(new ActiveModifier { Stat = "immune:paralyzed", RoundsLeft = 10 });
        Assert.True(p.Immune(Condition.Paralyzed));
    }

    [Fact]
    public void HighLevelWizardUsesALeveledSpellOnASoloBoss()
    {
        var engine = new CombatEngine(Data, new Pcg32(2, 2));
        var wizard = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 20);
        var s = engine.Start([wizard], [(Data.Monsters["ancient_red_dragon"], 1)]);
        var cmd = CombatSimulator.AutoCommands(s, Data)[s.Party[0].Id];
        Assert.Equal(CommandKind.Cast, cmd.Kind);
        Assert.True(cmd.SlotLevel >= 5, $"{cmd.SpellId} を {cmd.SlotLevel} レベルで唱えた");   // 初級呪文ではなく、高いスロット
    }
}
