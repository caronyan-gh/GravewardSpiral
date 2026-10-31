using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>
/// 戦闘のログの出どころ: おまけのダメージ(急所攻撃など)とボーナスアクションの攻撃と継続ダメージの行には、4つめに出どころを添える。
/// ふつうの攻撃とダメージの行には添えない(1射が2射に見えないように。ユーザーの指摘)。
/// </summary>
public class LogLabelTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));
    private static readonly MonsterScale Tough = new(200, 1, 0, 0);

    private static CombatState Fight(params Character[] party)
    {
        foreach (var c in party) c.MaxHp = c.Hp = 9999;
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var s = engine.Start(party, [(Data.Monsters["ogre"], 1)], Tough);
        for (int i = 0; i < 8 && s.Outcome == CombatOutcome.Ongoing; i++)
            engine.RunRound(s, party.ToDictionary(c => c.Id, _ => new PlayerCommand(CommandKind.Attack, 0)));
        return s;
    }

    [Fact]
    public void SneakAttackDamageIsLabelledButTheWeaponsOwnDamageIsNot()
    {
        var s = Fight(CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5), CharacterFactory.QuickCreate(Data, "R", "human", "rogue", 5));
        Assert.Contains(s.Log, l => l.Key == "combat.damage" && l.Args.Length == 4 && Equals(l.Args[3], "passive.sneak_attack"));
        Assert.Contains(s.Log, l => l.Key == "combat.damage" && l.Args.Length == 3);
        Assert.DoesNotContain(s.Log, l => l.Key is "combat.hit" or "combat.crit" && l.Args[0] is LocalizedText t && t.Get("en") == "R" && l.Args.Length == 4);
    }

    [Fact]
    public void ABonusActionStrikeIsLabelledOnItsAttackLine()
    {
        var s = Fight(CharacterFactory.QuickCreate(Data, "M", "human", "monk", 5));
        var attacks = s.Log.Where(l => l.Key is "combat.hit" or "combat.crit" or "combat.miss").ToList();
        Assert.Contains(attacks, l => l.Args.Length == 4 && Equals(l.Args[3], "combat.src.bonus_action"));   // 武術の素手の打撃
        Assert.Contains(attacks, l => l.Args.Length == 3);   // ふつうの攻撃
    }

    [Fact]
    public void OngoingDamageIsLabelled()
    {
        var c = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5);
        c.MaxHp = c.Hp = 9999;
        var engine = new CombatEngine(Data, new Pcg32(5, 5));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)], Tough);
        s.Party[0].Dots.Add(new ActiveDot { Key = "test", Dice = "3", DamageType = "fire", RoundsLeft = 3 });
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Attack, 0) });
        Assert.Contains(s.Log, l => l.Key == "combat.damage" && l.Args.Length == 4 && Equals(l.Args[2], "fire") && Equals(l.Args[3], "combat.src.dot"));
    }
}
