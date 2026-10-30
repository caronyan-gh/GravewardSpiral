using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Tests.Combat;

/// <summary>
/// 一撃で倒す(instant_kill。Wizardry の #1〜3 の忍者の首はね: レベル × 2%、上限 50%、敵は 0〜34 の乱数が 10 + レベルを超えたときだけ倒れる)。
/// 1回の攻撃の行動で1度だけ、ダメージを与えたときに振る。no_boss ならボスと名のある敵には効かない。
/// </summary>
public class InstantKillTests
{
    private static (GameData Data, ContentLibrary Content) WithFeature(string effect)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", $$"""[{"id": "t_kill", "kind": "passive", "effects": [{{effect}}]}]""");
        dir.Write("p/classes.json", """[{"id": "t_ninja", "extends": "fighter", "features": [{"id": "t_kill", "level": 1}]}]""");
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    /// <summary>素手の忍者 1 人と、HP を大きくしたオーガ 1 体。攻撃を rounds 回して、一撃で倒した文が出たか。</summary>
    private static bool Killed(GameData data, MonsterScale scale, bool armed = false, int rounds = 15, string monster = "ogre")
    {
        var c = CharacterFactory.QuickCreate(data, "N", "human", "t_ninja", 20);
        if (!armed) c.Inventory.Equipped.Remove(EquipSlot.MainHand);
        c.MaxHp = c.Hp = 9999;
        var engine = new CombatEngine(data, new Pcg32(7, 7));
        var s = engine.Start([c], [(data.Monsters[monster], 1)], scale);
        for (int i = 0; i < rounds && s.Outcome == CombatOutcome.Ongoing; i++)
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Attack, 0) });
        return s.Log.Any(l => l.Key == "combat.instant_kill");
    }

    private static readonly MonsterScale Tough = new(200, 1, 0, 0);

    [Fact]
    public void TheWizardryFormulaIsLevelTimesTwoUpToFifty()
    {
        var (data, content) = WithFeature("""{"type": "instant_kill", "on": "unarmed", "value": {"class_level": 2, "max": 50}, "resist_base": 10, "resist_die": 35}""");
        Assert.Empty(content.Problems);
        var c = CharacterFactory.QuickCreate(data, "N", "human", "t_ninja", 20);
        var p = new PartyMember(c, data);
        Assert.Equal(40, FeatureChecks.InstantKill(data, p, unarmed: true, melee: true)!.Value.Chance);
        Assert.Null(FeatureChecks.InstantKill(data, p, unarmed: false, melee: true));   // 素手だけ
    }

    [Fact]
    public void ACertainKillDropsTheTargetWhateverItsHp()
    {
        var (data, _) = WithFeature("""{"type": "instant_kill", "on": "unarmed", "value": 100}""");
        Assert.True(Killed(data, Tough));
        Assert.False(Killed(data, Tough, armed: true));   // on: unarmed なので、武器では出ない
    }

    /// <summary>武器の攻撃のあとの素手の攻撃(モンクの武術)でも振る。当てはまらない攻撃は、1度だけの機会を使わない。</summary>
    [Fact]
    public void AnUnarmedFollowUpStillGetsTheRoll()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """[{"id": "t_kill", "kind": "passive", "effects": [{"type": "instant_kill", "on": "unarmed", "value": 100}]}]""");
        dir.Write("p/classes.json", """[{"id": "t_ninja", "extends": "monk", "features": [{"id": "martial_arts", "level": 1}, {"id": "t_kill", "level": 1}], "pools": []}]""");
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        Assert.True(Killed(data, Tough, armed: true));   // 短剣で殴ったあとの武術の素手で倒す
    }

    [Fact]
    public void NoBossKeepsBossesAndNamedFoesSafe()
    {
        var (data, _) = WithFeature("""{"type": "instant_kill", "on": "melee", "value": 100, "no_boss": true}""");
        Assert.True(Killed(data, Tough));
        Assert.False(Killed(data, Tough with { Boss = true }));
        var (open, _) = WithFeature("""{"type": "instant_kill", "on": "melee", "value": 100}""");
        Assert.True(Killed(open, Tough with { Boss = true }));   // 書かなければボスにも効く(原作どおり)
    }

    [Fact]
    public void HighLevelFoesResistTheWizardryWay()
    {
        // 0〜34 の乱数が 10 + CR を超えたときだけ。CR 24 以上には効かない
        var (data, _) = WithFeature("""{"type": "instant_kill", "on": "melee", "value": 100, "resist_base": 10, "resist_die": 35}""");
        var big = data.Monsters.Values.First(m => m.Cr >= 24);
        Assert.False(Killed(data, Tough, monster: big.Id, rounds: 30));
    }

    [Fact]
    public void BadInstantKillsAreReported()
    {
        var (_, badOn) = WithFeature("""{"type": "instant_kill", "on": "magic", "value": 10}""");
        Assert.Contains(badOn.Problems, p => p.Contains("instant_kill の on は"));
        var (_, noValue) = WithFeature("""{"type": "instant_kill", "on": "melee"}""");
        Assert.Contains(noValue.Problems, p => p.Contains("instant_kill には value"));
        var (_, other) = WithFeature("""{"type": "attack_bonus", "on": "main_melee", "value": 1, "no_boss": true}""");
        Assert.Contains(other.Problems, p => p.Contains("instant_kill だけに書けます"));
    }

    /// <summary>敵の攻撃の kill の chance: HP に関わらず倒す(パーティの人は死ぬ)。resist_instant_kill の能力ごとに振って、どれかに当たればこらえる。</summary>
    [Fact]
    public void FoesCanDecapitateAndTheResistsRollSeparately()
    {
        var (data, content) = WithFeature("""{"type": "resist_instant_kill", "value": 100}""");
        Assert.Empty(content.Problems);
        GravewardSpiral.Core.Effects.EffectPart Chop() => new() { Type = GravewardSpiral.Core.Effects.EffectPart.Kill, Chance = 100, Message = "combat.decapitated" };

        var fighter = CharacterFactory.QuickCreate(data, "F", "human", "fighter", 10);
        var engine = new CombatEngine(data, new Pcg32(3, 3));
        var s = engine.Start([fighter], [(data.Monsters["ogre"], 1)]);
        engine.ApplyToParty(s, s.Groups[0].Units[0], [Chop()]);
        Assert.Equal(LifeState.Dead, fighter.State);
        Assert.Contains(s.Log, l => l.Key == "combat.decapitated");

        var ninja = CharacterFactory.QuickCreate(data, "N", "human", "t_ninja", 10);   // こらえる 100%
        s = engine.Start([ninja], [(data.Monsters["ogre"], 1)]);
        engine.ApplyToParty(s, s.Groups[0].Units[0], [Chop()]);
        Assert.Equal(LifeState.Alive, ninja.State);
        Assert.Contains(s.Log, l => l.Key == "combat.instant_kill_resisted");
    }

    [Fact]
    public void ChanceIsOnlyForKill()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/monsters.json", """
            [{"id": "t_bunny", "extends": "giant_rat", "actions": [{"id": "bite", "name": {"en": "Bite", "ja": "噛みつき"}, "to_hit": 5,
              "effects": [{"type": "kill", "chance": 150}, {"type": "damage", "dice": "1d4", "damage_type": "piercing", "chance": 5}]}]}]
            """);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        GameData.Load(content);
        Assert.Equal(2, content.Problems.Count(p => p.Contains("chance は kill だけに書けて、1〜100")));
    }

    /// <summary>同じ攻撃のダメージで気絶した人にも、首はねを振る(HP の多い人だけが死ぬ、にならない)。</summary>
    [Fact]
    public void TheBlowThatKnocksYouOutCanStillTakeYourHead()
    {
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content));
        var c = CharacterFactory.QuickCreate(data, "F", "human", "fighter", 1);
        c.MaxHp = 100;   // 大きすぎるダメージの即死にならないように(あふれる分が最大 HP に届かない)
        c.Hp = 5;
        var engine = new CombatEngine(data, new Pcg32(5, 5));
        var s = engine.Start([c], [(data.Monsters["ogre"], 1)]);
        engine.ApplyToParty(s, s.Groups[0].Units[0], [
            new GravewardSpiral.Core.Effects.EffectPart { Type = GravewardSpiral.Core.Effects.EffectPart.Damage, Dice = "10", DamageType = "slashing" },
            new GravewardSpiral.Core.Effects.EffectPart { Type = GravewardSpiral.Core.Effects.EffectPart.Kill, Chance = 100 }]);
        Assert.Equal(LifeState.Dead, c.State);
        Assert.Contains(s.Log, l => l.Key == "combat.instant_killed");   // 首はねで死んだ
    }

    /// <summary>一時的な HP だけが減った攻撃も、ダメージを与えたものとして首はねを振る。</summary>
    [Fact]
    public void DamageToTemporaryHpCountsAsAHit()
    {
        var (data, _) = WithFeature("""{"type": "instant_kill", "on": "melee", "value": 100}""");
        var c = CharacterFactory.QuickCreate(data, "N", "human", "t_ninja", 20);
        c.MaxHp = c.Hp = 9999;
        var engine = new CombatEngine(data, new Pcg32(7, 7));
        var s = engine.Start([c], [(data.Monsters["ogre"], 1)], Tough);
        s.Groups[0].Units[0].TempHp = 100000;
        for (int i = 0; i < 15 && s.Outcome == CombatOutcome.Ongoing; i++)
            engine.RunRound(s, new Dictionary<string, PlayerCommand> { [c.Id] = new(CommandKind.Attack, 0) });
        Assert.Contains(s.Log, l => l.Key == "combat.instant_kill");
    }

    /// <summary>こらえる効果も stacks: max なら、同じ組は一番大きいものだけを振る(別の組は別々に)。</summary>
    [Fact]
    public void ResistsInTheSameMaxGroupDoNotAddUp()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """
            [{"id": "t_r15", "kind": "passive", "group": "t_rk", "stacks": "max", "effects": [{"type": "resist_instant_kill", "value": 15}]},
             {"id": "t_r40", "kind": "passive", "group": "t_rk", "stacks": "max", "effects": [{"type": "resist_instant_kill", "value": 40}]},
             {"id": "t_r5", "kind": "passive", "effects": [{"type": "resist_instant_kill", "value": 5}]}]
            """);
        dir.Write("p/classes.json", """[{"id": "t_guard", "extends": "fighter", "features": [{"id": "t_r15", "level": 1}, {"id": "t_r40", "level": 1}, {"id": "t_r5", "level": 1}]}]""");
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Empty(content.Problems);
        var p = new PartyMember(CharacterFactory.QuickCreate(data, "G", "human", "t_guard", 5), data);
        Assert.Equal([5, 40], FeatureChecks.InstantKillResists(data, p).Order());
    }
}
