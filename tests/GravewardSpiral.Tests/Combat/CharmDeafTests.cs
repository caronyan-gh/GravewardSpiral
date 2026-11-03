using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>魅了(魅了した相手には手を出せない)と聴覚喪失(鼓舞が届かない)。</summary>
public class CharmDeafTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static (CombatEngine Engine, CombatState State) Fight(params Character[] party)
    {
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start(party, [(Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        return (engine, s);
    }

    [Fact]
    public void ACharmedMemberWillNotAttackTheCharmer()
    {
        var (engine, s) = Fight(CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5));
        var p = s.Party[0];
        var ogre = s.Groups[0].Units[0];
        // 呪文でない行動(吸血鬼の魅了など)でも、かけた敵を覚える
        engine.ApplyTo(s, ogre, p, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Charmed }]);
        Assert.True(p.CharmedBy(ogre));
        int hp = ogre.Hp;
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Attack, TargetGroup: 0) });
        Assert.Equal(hp, ogre.Hp);
        Assert.Contains(s.Log, e => e.Key == "combat.charmed_refuse");
    }

    [Fact]
    public void TheAutomaticFollowUpSparesTheCharmer()
    {
        // セカンド・ウィンドのあとの自動の攻撃でも、魅了した敵には手を出さない(ほかに相手がいなければ身構える)
        var (engine, s) = Fight(CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5));
        var p = s.Party[0];
        var ogre = s.Groups[0].Units[0];
        p.Hp = p.MaxHp - 5;   // 先にオーガに殴られても倒れない
        engine.ApplyTo(s, ogre, p, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Charmed }]);
        int hp = ogre.Hp;
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Feature, TargetGroup: 0, FeatureId: "second_wind") });
        Assert.Equal(hp, ogre.Hp);
        Assert.DoesNotContain(s.Log, e => e.Key is "combat.hit" or "combat.miss" or "combat.crit" && e.Args.Length > 0 && Equals(e.Args[0], p.Name));
        Assert.Empty(s.AttackableGroups(p));   // 攻撃先の一覧にも出さない
    }

    [Fact]
    public void AreaSpellsLeaveOutTheCharmersGroup()
    {
        // 2グループに当たる呪文(ライトニング・ボルト)でも、魅了した敵のいるグループは巻き込まない
        var wizard = CharacterFactory.QuickCreate(Data, "W", "human", "wizard", 5);
        wizard.Book("wizard").Known.Add("lightning_bolt");
        wizard.Book("wizard").Prepared.Add("lightning_bolt");
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([wizard], [(Data.Monsters["goblin"], 2), (Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        var p = s.Party[0];
        var ogre = s.Groups[1].Units[0];
        engine.ApplyTo(s, ogre, p, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Charmed }]);
        int hp = ogre.Hp;
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Cast, 0, -1, "lightning_bolt", 3) });
        Assert.Equal(hp, ogre.Hp);
        Assert.Contains(s.Log, e => e.Key == "combat.cast" || e.Key.StartsWith("combat.spell"));
        Assert.True(s.Groups[0].Units.Any(u => u.Hp < u.MaxHp));
    }

    [Fact]
    public void ABeastsNextBiteDoesNotSwitchToTheCharmer()
    {
        // 獣の姿の2回の攻撃: 1回目で狙ったグループが倒れても、2回目に魅了した敵のグループへ乗り換えない(乱数を変えて何度も)
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var druid = CharacterFactory.QuickCreate(Data, "D", "human", "druid", 5);
            var engine = new CombatEngine(Data, new Pcg32(seed, seed));
            var s = engine.Start([druid], [(Data.Monsters["goblin"], 1), (Data.Monsters["ogre"], 1)]);
            s.Surprised = null;
            var p = s.Party[0];
            p.Shape = Data.Monsters["brown_bear"];
            var ogre = s.Groups[1].Units[0];
            s.Groups[0].Units[0].Hp = 1;
            engine.ApplyTo(s, ogre, p, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Charmed }]);
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Attack, TargetGroup: 0) });
            Assert.Equal(ogre.MaxHp, ogre.Hp);
        }
    }

    [Fact]
    public void ALingeringSpellDoesNotMoveOnToTheCharmer()
    {
        // スピリチュアル・ウェポン: 狙った敵が倒れても、魅了した敵へは移らない
        var cleric = CharacterFactory.QuickCreate(Data, "C", "human", "cleric", 5);
        cleric.Book("cleric").Prepared.Add("spiritual_weapon");
        var engine = new CombatEngine(Data, new Pcg32(1, 1));
        var s = engine.Start([cleric], [(Data.Monsters["goblin"], 1), (Data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        var p = s.Party[0];
        var goblin = s.Groups[0].Units[0];
        var ogre = s.Groups[1].Units[0];
        engine.ApplyTo(s, ogre, p, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Charmed }]);
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new(CommandKind.Cast, 0, -1, "spiritual_weapon", 2) });
        goblin.Hp = 0;   // 狙った敵が倒れた
        for (int i = 0; i < 3 && s.Outcome == CombatOutcome.Ongoing; i++)
        {
            p.Hp = p.MaxHp;
            int before = s.Log.Count;
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = PlayerCommand.Dodge });
            Assert.DoesNotContain(s.Log.Skip(before), e => e.Key == "combat.zone");
        }
        Assert.Equal(ogre.MaxHp, ogre.Hp);
    }

    [Fact]
    public void ACharmedFoeDoesNotTargetTheCharmer()
    {
        var (engine, s) = Fight(CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5));
        var p = s.Party[0];
        var ogre = s.Groups[0].Units[0];
        engine.ApplyTo(s, p, ogre, [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Charmed }]);
        int hp = p.Hp;
        for (int i = 0; i < 3; i++) engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = PlayerCommand.Dodge });
        Assert.Equal(hp, p.Hp);   // ほかに狙える仲間がいないので、オーガは手を出さない
    }

    [Fact]
    public void ADeafAllyCannotBeInspired()
    {
        var bard = CharacterFactory.QuickCreate(Data, "B", "human", "bard", 5);
        var (engine, s) = Fight(CharacterFactory.QuickCreate(Data, "F", "human", "fighter", 5), bard);
        var ally = s.Party[0];
        ally.Conditions.Add(new ActiveCondition { Condition = Condition.Deafened });
        int left = ClassFeatures.UsesLeft(Data, bard, "bardic_inspiration");
        engine.RunRound(s, new Dictionary<string, PlayerCommand>
        {
            [s.Party[1].Id] = new(CommandKind.Feature, TargetGroup: 0, TargetMember: 0, FeatureId: "bardic_inspiration"),
            [ally.Id] = PlayerCommand.Dodge,
        });
        Assert.DoesNotContain(ally.Modifiers, m => m.Stat == "inspire");
        Assert.Contains(s.Log, e => e.Key == "combat.deaf_no_inspire");
        Assert.Equal(left, ClassFeatures.UsesLeft(Data, bard, "bardic_inspiration"));   // 回数は使わない
    }
}
