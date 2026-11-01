using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>戦闘のログの行に、した側(行動した者の陣営)を持たせる。画面はその名前を味方は青、敵は赤で出す。</summary>
public class LogActorTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    [Fact]
    public void TheActorsSideIsOnItsOwnLinesOnly()
    {
        var c = CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5);
        c.MaxHp = c.Hp = 9999;
        var engine = new CombatEngine(Data, new Pcg32(3, 3));
        var s = engine.Start([c], [(Data.Monsters["ogre"], 1)], new MonsterScale(200, 1, 0, 0));
        for (int i = 0; i < 6 && s.Outcome == CombatOutcome.Ongoing; i++)
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Attack, 0) });

        static string? First(CombatLogEntry l) => l.Args.Length > 0 && l.Args[0] is LocalizedText t ? t.Get("en") : null;
        var attacks = s.Log.Where(l => l.Key is "combat.hit" or "combat.crit" or "combat.miss").ToList();
        // ファイターの攻撃は味方、オーガの攻撃は敵
        Assert.Contains(attacks, l => First(l) == "F" && l.ActorSide == Side.Party);
        Assert.Contains(attacks, l => First(l) != "F" && l.ActorSide == Side.Enemy);
        // 受けた側が {0} の行(ダメージ)には付けない
        Assert.All(s.Log.Where(l => l.Key == "combat.damage"), l => Assert.Null(l.ActorSide));
        Assert.Null(s.Acting);   // 手番の外では空
    }
}
