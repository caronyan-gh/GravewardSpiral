using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Tests.Characters;

/// <summary>クラスの伸び方をデータで持つ形(classes.json の progression)。docs/class-data-design.md の Step 3。</summary>
public class ProgressionTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    private static Character Multi(params (string Id, int Level)[] levels) => new()
    {
        RaceId = "human",
        ClassId = levels[0].Id,
        Level = levels.Sum(x => x.Level),
        Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(),
    };

    private static (GameData Data, ContentLibrary Content) WithPack(string classesJson)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/classes.json", classesJson);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    [Fact]
    public void ACopiedFighterAttacksAndIncreasesLikeTheOriginal()
    {
        var (data, _) = WithPack("""[{"id": "test_fighter", "extends": "fighter"}]""");
        for (int lv = 1; lv <= 20; lv++)
        {
            Assert.Equal(CombatEngine.AttacksPerAction(data, Multi(("fighter", lv))), CombatEngine.AttacksPerAction(data, Multi(("test_fighter", lv))));
            Assert.Equal(Multi(("fighter", lv)).AbilityPoints(data), Multi(("test_fighter", lv)).AbilityPoints(data));
        }
        // マルチクラスでは、攻撃の回数は一番多いもの(足さない)、能力値上昇はクラスごとに数えて足す
        var m = Multi(("fighter", 11), ("test_fighter", 6));
        Assert.Equal(3, CombatEngine.AttacksPerAction(data, m));
        Assert.Equal(2 * (3 + 2), m.AbilityPoints(data));   // ファイター 11 で 4・6・8 の3回、写し 6 で 4・6 の2回
    }

    [Fact]
    public void AClassWithoutProgressionAttacksOnceAndUsesTheStandardTable()
    {
        var (data, content) = WithPack("""[{"id": "plain", "extends": "rogue", "progression": null}]""");
        Assert.Empty(content.Problems);
        Assert.Equal(1, CombatEngine.AttacksPerAction(data, Multi(("plain", 20))));
        Assert.Equal(2 * 5, Multi(("plain", 20)).AbilityPoints(data));   // 4・8・12・16・19
        Assert.Equal(2 * 6, Multi(("rogue", 20)).AbilityPoints(data));   // ローグは 10 にもある
    }

    [Fact]
    public void MartialArtsFollowsTheFeatureNotTheMonkId()
    {
        // 別の id で写したモンクも武術が働き、武術を外したモンクは働かない(Step 4a の取り残しの直し)
        var (data, content) = WithPack("""
            [{"id": "test_monk", "extends": "monk"},
             {"id": "monk_without_arts", "extends": "monk", "features": [{"id": "deflect_missiles", "level": 3}]}]
            """);
        Assert.Empty(content.Problems);
        Assert.True(ClassFeatures.MartialArts(data, Multi(("test_monk", 1))));
        Assert.False(ClassFeatures.MartialArts(data, Multi(("monk_without_arts", 1))));
        Assert.True(ClassFeatures.MartialArts(Data, Multi(("monk", 1))));   // 標準のモンクは今までどおり
        // 武術のダイスも、写したクラスのレベルで能力の数から引く(Step 4b)
        Assert.Equal(8, ClassFeatures.MartialArtsDie(data, Multi(("test_monk", 11))));
        Assert.Equal(4, ClassFeatures.MartialArtsDie(data, Multi(("fighter", 11))));   // 持っていなければ d4(素手のふつうのダイスの代わり)
    }

    /// <summary>技の回数(Step 5a): 写したクラスの回数、鼓舞の泉で小休憩、2つのクラスの同じ回数(上限は大きいほう、小休憩は小休憩のクラスの上限まで)。</summary>
    [Fact]
    public void PoolsFollowTheClassData()
    {
        var (data, content) = WithPack("""
            [{"id": "test_bard", "extends": "bard"},
             {"id": "big_rager", "extends": "fighter", "pools": [{"id": "rage", "max": 5, "recharge": "short"}]}]
            """);
        Assert.Empty(content.Problems);
        var bard = Multi(("test_bard", 5));
        bard.Scores[Core.Rules.Ability.Cha] = 16;
        Assert.Equal(3, ClassFeatures.MaxUses(data, bard, "bardic_inspiration"));
        bard.FeatureUses["bardic_inspiration"] = 3;
        ClassFeatures.ShortRest(data, bard);
        Assert.Equal(3, ClassFeatures.UsesLeft(data, bard, "bardic_inspiration"));   // 鼓舞の泉で小休憩に戻る

        // バーバリアン 17(激怒 6 回、大休憩で戻る)と、激怒 5 回を小休憩で戻す自作のクラス: 上限は 6、小休憩では残りを 5 まで満たす
        var mixed = Multi(("barbarian", 17), ("big_rager", 1));
        Assert.Equal(6, ClassFeatures.MaxUses(data, mixed, "rage"));
        mixed.FeatureUses["rage"] = 6;
        ClassFeatures.ShortRest(data, mixed);
        Assert.Equal(5, ClassFeatures.UsesLeft(data, mixed, "rage"));
        ClassFeatures.ShortRest(data, mixed);
        Assert.Equal(5, ClassFeatures.UsesLeft(data, mixed, "rage"));   // 何度休んでも 6 にはならない
    }

    /// <summary>戦闘の技(Step 5b): 写したモンクも技を持ち、獣の姿で使えるかはデータの beast_form。回数を持つクラスの無い pool は検査で拾う。</summary>
    [Fact]
    public void ActionsFollowTheClassData()
    {
        var (data, content) = WithPack("""[{"id": "test_monk", "extends": "monk"}]""");
        Assert.Empty(content.Problems);
        var monk = Multi(("test_monk", 5));
        Assert.Equal(["flurry_of_blows", "patient_defense", "stunning_strike"], ClassFeatures.ActiveFor(data, monk).Select(f => f.Id));
        Assert.False(ClassFeatures.UsableInBeastForm(ClassFeatures.Action(data, "flurry_of_blows")!));
        Assert.True(ClassFeatures.UsableInBeastForm(ClassFeatures.Action(data, "patient_defense")!));

        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/features.json", """[{"id": "shout", "kind": "action", "target": "none", "pool": "no_such_pool"}]""");
        dir.Write("p/classes.json", """[{"id": "t_shouter", "extends": "fighter", "features": [{"id": "shout", "level": 1}]}]""");   // 回数の持ち主は、誰かが持つ技だけ確かめる
        var bad = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        GameData.Load(bad);
        Assert.Contains(bad.Problems, p => p.Contains("'no_such_pool'"));
        Assert.Contains(bad.Problems, p => p.Contains("features 'shout': 技(action)の動きはコアの部品"));   // 動きの無い技は作れない(Step 7)
    }

    /// <summary>技に付く数(Step 5c-1): 写したクラスでも、データの数でクラスのレベルから数える。</summary>
    [Fact]
    public void ActionNumbersFollowTheClassData()
    {
        var (data, content) = WithPack("""
            [{"id": "t_rogue", "extends": "rogue"}, {"id": "t_fighter", "extends": "fighter"}, {"id": "t_cleric", "extends": "cleric"},
             {"id": "t_barb", "extends": "barbarian"}, {"id": "t_bard", "extends": "bard"}, {"id": "t_wizard", "extends": "wizard"}]
            """);
        Assert.Empty(content.Problems);
        Assert.Equal(5, ClassFeatures.SneakAttackDice(data, Multi(("t_rogue", 9))));
        Assert.Equal(7, ClassFeatures.SecondWindBonus(data, Multi(("t_fighter", 7))));
        var cleric = Multi(("t_cleric", 5));
        cleric.Scores[Core.Rules.Ability.Wis] = 16;
        Assert.Equal(8 + 3 + 3, ClassFeatures.TurnUndeadDc(data, cleric));
        Assert.Equal(3, ClassFeatures.RageDamage(data, Multi(("t_barb", 9))));
        Assert.Equal(10, ClassFeatures.InspirationDie(data, Multi(("t_bard", 10))));
        Assert.Equal(4, ClassFeatures.ArcaneRecoveryBudget(data, Multi(("t_wizard", 7))));
        Assert.Equal(0, ClassFeatures.SneakAttackDice(data, Multi(("t_fighter", 9))));   // 持たなければ 0
    }

    /// <summary>変身先(Step 5c-2): shapes.json から引き、モンスターの数値を直しても変身は変わらない。写したドルイドも同じ変身先。知らない変身先は検査で拾う。</summary>
    [Fact]
    public void WildShapesComeFromShapesNotMonsters()
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/monsters.json", """[{"id": "wolf", "extends": "wolf", "hp_average": 99}]""");
        dir.Write("p/classes.json", """[{"id": "t_druid", "extends": "druid"}]""");
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        var data = GameData.Load(content);
        Assert.Equal(["wolf", "black_bear"], ClassFeatures.WildShapeBeasts(data, Multi(("t_druid", 4))));
        Assert.Equal(99, data.Monsters["wolf"].HpAverage);
        Assert.NotEqual(99, data.Shapes["wolf"].HpAverage);   // 変身の狼は変わらない

        using var dir2 = new TestPaths.TempDir();
        dir2.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir2.Write("p/features.json", """[{"id": "beastform", "kind": "action", "target": "self", "shapes": {"1": ["no_such_beast"]}}]""");
        var bad = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir2.Path, "p"));
        GameData.Load(bad);
        Assert.Contains(bad.Problems, x => x.Contains("'no_such_beast'"));
    }

    [Fact]
    public void BadAbilityIncreaseLevelsAreReported()
    {
        var (_, content) = WithPack("""[{"id": "bad", "extends": "fighter", "progression": {"ability_increases": [4, 4, 25]}}]""");
        Assert.Contains(content.Problems, p => p.Contains("ability_increases"));
    }
}
