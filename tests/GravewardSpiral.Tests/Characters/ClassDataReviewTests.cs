using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>クラスの分離の見直し(外部のレビュー、DB のテストケース T352・T348)で見つかった所の確かめ。</summary>
public class ClassDataReviewTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    private static (GameData Data, ContentLibrary Content) WithPack(params (string File, string Json)[] files)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        foreach (var (file, json) in files) dir.Write("p/" + file, json);
        dir.NameFeatures("p");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    private static Character Make(params (string Id, int Level)[] levels)
    {
        var c = new Character
        {
            Id = "t", Name = "T", RaceId = "human", ClassId = levels[0].Id, Level = levels.Sum(x => x.Level),
            Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(), MaxHp = 40, Hp = 40,
        };
        foreach (var a in Abilities.All) c.Scores[a] = 10;
        return c;
    }

    /// <summary>呪文を使い始めていない協約魔術のクラスの表は使わない(先に取った L3 からのクラスが、あとのウォーロックの表を奪わない)。</summary>
    [Fact]
    public void AnUnstartedPactClassDoesNotPickThePactTable()
    {
        var (data, content) = WithPack(
            ("spell_progressions.json", """[{"id": "big_pact", "pact": {"slots": 7, "level": 4}}]"""),
            ("classes.json", """
                [{"id": "t_late_pact", "extends": "warlock", "spellcasting": {"ability": "cha", "style": "pact", "progression": "big_pact", "from_level": 3,
                  "known": {"class_level": 1}, "spell_access": [{"lv": 3, "spelllv": 1, "set": "warlock_1"}]}}]
                """));
        Assert.Empty(content.Problems);
        var c = Make(("t_late_pact", 1), ("warlock", 3));
        Assert.Equal((2, 2), (Spellcasting.PactSlots(data, c), Spellcasting.PactSlotLevel(data, c)));   // ウォーロック 3 の表
        var started = Make(("t_late_pact", 3), ("warlock", 3));
        Assert.Equal((7, 4), (Spellcasting.PactSlots(data, started), Spellcasting.PactSlotLevel(data, started)));   // 使い始めれば、先に取ったクラスの表
    }

    /// <summary>レイ・オン・ハンズの回数の名前を変えても、使えるかはデータの pool で決まる。</summary>
    [Fact]
    public void LayOnHandsUsesItsDataPool()
    {
        var (data, content) = WithPack(
            ("features.json", """[{"id": "t_hands", "extends": "lay_on_hands", "pool": "healing_hands"}]"""),
            ("classes.json", """[{"id": "t_healer", "extends": "paladin", "features": [{"id": "t_hands", "level": 1}], "pools": [{"id": "healing_hands", "max": {"class_level": 5}, "recharge": "long"}]}]"""));
        Assert.Empty(content.Problems);
        var c = Make(("t_healer", 2));
        var f = ClassFeatures.Action(data, "t_hands")!;
        Assert.Equal("lay_on_hands", f.Part);
        Assert.Equal(10, ClassFeatures.UsesLeft(data, c, "healing_hands"));
        Assert.True(ClassFeatures.CanUse(data, c, f));
    }

    /// <summary>反応の強いほうを選ぶとき、能力値の修正も数える(4 + 魅力 +5 の 9 は、決まった 6 より強い)。</summary>
    [Fact]
    public void ReactionsCompareWithTheAbilityModifier()
    {
        var (data, content) = WithPack(
            ("features.json", """
                [{"id": "t_parry", "kind": "passive", "group": "t_cut", "stacks": "max", "effects": [{"type": "reaction_reduce", "on": "attacked", "value": 6}]},
                 {"id": "t_grace", "kind": "passive", "group": "t_cut", "stacks": "max", "effects": [{"type": "reaction_reduce", "on": "attacked", "value": 4, "ability": "cha"}]}]
                """),
            ("classes.json", """[{"id": "t_dancer", "extends": "fighter", "features": [{"id": "t_parry", "level": 1}, {"id": "t_grace", "level": 1}]}]"""));
        Assert.Empty(content.Problems);
        var c = Make(("t_dancer", 1));
        c.Scores[Ability.Cha] = 20;
        var r = FeatureChecks.ReactionFor(data, new PartyMember(c, data), ranged: false)!;
        Assert.Equal((Ability.Cha, 4), (r.Ability, r.Bonus));
    }

    /// <summary>技の id と動きの部品は別。セカンド・ウィンドを写して回復の違う派生の技を作り、もとの技と一緒に持てる。</summary>
    [Fact]
    public void DerivedActionsShareAPartButKeepTheirOwnNumbers()
    {
        var (data, content) = WithPack(
            ("features.json", """[{"id": "t_deep_breath", "extends": "second_wind", "dice": "2d8", "values": {"bonus": {"class_level": 2}}, "pool": "t_breath"}]"""),
            ("classes.json", """
                [{"id": "t_warden", "extends": "fighter", "pools": [{"id": "second_wind", "max": 1, "recharge": "short"}, {"id": "t_breath", "max": 2, "recharge": "long"}],
                  "features": [{"id": "second_wind", "level": 1}, {"id": "t_deep_breath", "level": 3}]}]
                """));
        Assert.Empty(content.Problems);
        var c = Make(("t_warden", 5));
        var actions = ClassFeatures.ActiveFor(data, c).ToList();
        Assert.Equal(["second_wind", "t_deep_breath"], actions.Select(f => f.Id));
        Assert.All(actions, f => Assert.Equal("second_wind", f.Part));
        Assert.Equal("2d8", actions[1].Dice);
        Assert.Equal(5, ClassFeatures.SecondWindBonus(data, c, "second_wind"));
        Assert.Equal(10, ClassFeatures.SecondWindBonus(data, c, "t_deep_breath"));
        Assert.Equal(5, ClassFeatures.SecondWindBonus(data, c));   // id を省けば、その部品の最初の技

        var (_, bad) = WithPack(("features.json", """[{"id": "t_shout", "kind": "action", "part": "no_such_part", "target": "none"}]"""));
        Assert.Contains(bad.Problems, p => p.Contains("features 't_shout': 技(action)の動きはコアの部品"));
    }

    /// <summary>唱えられない・集中できないはデータ(no_cast・no_concentration)。激怒は両方、獣の姿は no_cast だけで、獣の呪文で外れる。</summary>
    [Fact]
    public void CastingLimitsComeFromTheData()
    {
        Assert.False(ClassFeatures.CanCastIn(Data, Make(("barbarian", 1)), raging: true, shaped: false));
        Assert.False(ClassFeatures.CanCastIn(Data, Make(("druid", 2)), raging: false, shaped: true));
        Assert.True(ClassFeatures.CanCastIn(Data, Make(("druid", 18)), raging: false, shaped: true));   // 獣の呪文
        Assert.False(ClassFeatures.CanCastIn(Data, Make(("druid", 18), ("barbarian", 1)), raging: true, shaped: true));   // 激怒は獣の呪文では外れない
        var rager = new PartyMember(Make(("barbarian", 1)), Data) { Raging = true };
        Assert.NotEmpty(ClassFeatures.Blocking(Data, rager.Character, "no_concentration", rager.Now));
        var beast = new PartyMember(Make(("druid", 2)), Data) { Shape = Data.Shapes["wolf"] };
        Assert.Empty(ClassFeatures.Blocking(Data, beast.Character, "no_concentration", beast.Now));   // 獣の姿では集中は切れない

        // 制限を外したクラス(激怒しても唱えられる試しのクラス)
        var (data, content) = WithPack(("classes.json", """[{"id": "t_calm_rager", "extends": "barbarian", "features": [{"id": "rage", "level": 1}, {"id": "rage_damage", "level": 1}]}]"""));
        Assert.Empty(content.Problems);
        Assert.True(ClassFeatures.CanCastIn(data, Make(("t_calm_rager", 1)), raging: true, shaped: false));
    }

    /// <summary>
    /// 唱えられるが集中できない能力(no_concentration だけ)では、集中の呪文を唱えても集中はすぐ切れる(2 回目の見直し)。
    /// 派生の竜の息の戦闘ログは、その技の名前で出す。
    /// </summary>
    [Fact]
    public void NoConcentrationAlsoStopsNewConcentration()
    {
        var (data, content) = WithPack(
            ("features.json", """
                [{"id": "t_scattered", "kind": "passive", "effects": [{"type": "no_concentration"}]},
                 {"id": "t_frost_breath", "extends": "breath_weapon"}]
                """),
            ("classes.json", """
                [{"id": "t_scatter_priest", "extends": "cleric", "features": [{"id": "t_scattered", "level": 1}, {"id": "t_frost_breath", "level": 1}],
                  "pools": [{"id": "breath_weapon", "max": 1, "recharge": "short"}]}]
                """));
        Assert.Empty(content.Problems);
        var c = Make(("t_scatter_priest", 3));
        c.Scores[Ability.Wis] = 16;
        c.Book("t_scatter_priest").Prepared.Add("bless");
        var engine = new CombatEngine(data, new Core.Rng.Pcg32(1, 1));
        var s = engine.Start([c], [(data.Monsters["ogre"], 1)]);
        s.Surprised = null;
        var p = s.Party[0];
        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new PlayerCommand(CommandKind.Cast, -1, 0, "bless", 1) });
        Assert.Equal(1, c.SlotsUsed.GetValueOrDefault(1));   // 唱えた(スロットは使う)
        Assert.Null(p.Concentration);                         // 集中はすぐ切れる
        Assert.DoesNotContain(p.Modifiers, m => m.SourceId == p.Id && m.Stat.Contains("bless"));

        engine.RunRound(s, new Dictionary<string, PlayerCommand> { [p.Id] = new PlayerCommand(CommandKind.Feature, 0, FeatureId: "t_frost_breath") });
        Assert.Contains(s.Log, e => e.Key == "combat.feature" && e.Args.Contains("feature.t_frost_breath"));
    }
}
