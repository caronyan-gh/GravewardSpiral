using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>ACGRW の祝福: 勝った戦闘の終わりに、倒した1体につき、生き残った人がそれぞれのレベルで HP を戻す。</summary>
public class AcgrwBlessingTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(16, 3)]
    [InlineData(17, 4)]
    [InlineData(22, 4)]
    [InlineData(23, 5)]
    public void ThePerFoeHealGrowsEverySixLevels(int level, int per) => Assert.Equal(per, CombatEngine.BlessingPerFoe(level));

    /// <summary>levels のファイターで、動けないゴブリン count 体を倒すまで戦う。HP は 1 から。</summary>
    private static (CombatState State, List<Character> Party) Win(int count, params int[] levels) => Win(Data, count, levels);

    private static (CombatState State, List<Character> Party) Win(GameData data, int count, params int[] levels)
    {
        var party = levels.Select((lv, i) => CharacterFactory.QuickCreate(data, $"F{i}", "human", "fighter", lv)).ToList();
        var engine = new CombatEngine(data, new Pcg32(3, 1));
        var s = engine.Start(party, [(Data.Monsters["goblin"], count)]);
        s.Surprised = null;
        foreach (var u in s.Groups[0].Units) u.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        foreach (var c in party) c.Hp = 1;
        while (s.Outcome == CombatOutcome.Ongoing)
            engine.RunRound(s, s.Party.ToDictionary(p => p.Id, _ => new PlayerCommand(CommandKind.Attack, 0)));
        return (s, party);
    }

    [Fact]
    public void EachSurvivorHealsByTheFoesDefeatedAtTheirOwnLevel()
    {
        var (s, party) = Win(4, 3, 5, 11);
        Assert.Equal(CombatOutcome.Victory, s.Outcome);
        Assert.Equal(1 + 4 * 1, party[0].Hp);
        Assert.Equal(1 + 4 * 2, party[1].Hp);
        Assert.Equal(1 + 4 * 3, party[2].Hp);
        Assert.Contains(s.Log, l => l.Key == "combat.acgrw_blessing");
    }

    [Fact]
    public void TheCurseOfLordBStopsTheBlessing()
    {
        var cursed = GameData.Load(Content);
        cursed.CurseOfLordB = true;
        var (s, party) = Win(cursed, 4, 5);
        Assert.Equal(CombatOutcome.Victory, s.Outcome);
        Assert.Equal(1, party[0].Hp);
        Assert.DoesNotContain(s.Log, l => l.Key == "combat.acgrw_blessing");
        Assert.False(Data.CurseOfLordB);   // 呪いを書いていない迷宮(第1部)では効く
    }

    [Fact]
    public void TheHealStopsAtMaxHp()
    {
        var (_, party) = Win(8, 1);
        Assert.Equal(Math.Min(party[0].MaxHp, 1 + 8), party[0].Hp);
    }

    [Fact]
    public void RunningAwayHealsNothing()
    {
        var c = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5);
        var engine = new CombatEngine(Data, new Pcg32(3, 1));
        var s = engine.Start([c], [(Data.Monsters["goblin"], 2)]);
        s.Surprised = null;
        foreach (var u in s.Groups[0].Units) u.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated });
        c.Hp = 1;
        while (s.Outcome == CombatOutcome.Ongoing) engine.RunRound(s, new Dictionary<string, PlayerCommand> { [s.Party[0].Id] = new(CommandKind.Flee) });
        Assert.Equal(CombatOutcome.Fled, s.Outcome);
        Assert.Equal(1, c.Hp);
    }
}
