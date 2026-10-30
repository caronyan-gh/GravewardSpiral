using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>種族の技・能力・回数(races.json の features と pools)。docs/class-data-design.md の Step 6。</summary>
public class RaceFeatureTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    private static Character Make(string race, params (string Id, int Level)[] levels) => new()
    {
        RaceId = race,
        ClassId = levels[0].Id,
        Level = levels.Sum(x => x.Level),
        Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(),
        MaxHp = 10,
        Hp = 10,
    };

    private static (GameData Data, ContentLibrary Content) WithPack(string file, string json, params (string File, string Json)[] more)
    {
        using var dir = new TestPaths.TempDir();
        dir.Write("p/manifest.json", """{"id": "p", "depends": ["srd"]}""");
        dir.Write("p/" + file, json);
        foreach (var (f, j) in more) dir.Write("p/" + f, j);
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(dir.Path, "p"));
        return (GameData.Load(content), content);
    }

    /// <summary>別の id で写した種族も、竜の息・火への抵抗・不屈の耐久・獰猛な攻撃を持つ(種族の id でなく、データで決まる)。</summary>
    [Fact]
    public void ACopiedRaceKeepsItsFeatures()
    {
        var (data, content) = WithPack("races.json", """[{"id": "t_dragon", "extends": "dragonborn"}, {"id": "t_orc", "extends": "half_orc"}]""");
        Assert.Empty(content.Problems);
        var dragon = Make("t_dragon", ("fighter", 1));
        Assert.Contains(ClassFeatures.ActiveFor(data, dragon), f => f.Id == "breath_weapon");
        Assert.Equal(1, ClassFeatures.MaxUses(data, dragon, "breath_weapon"));
        Assert.Equal(0.5, new PartyMember(dragon, data).DamageMultiplier("fire", false));

        var orc = Make("t_orc", ("fighter", 1));
        Assert.Equal("relentless_endurance", ClassFeatures.PoolsOf(data, orc)[^1]);   // クラスの回数のあとに種族の回数
        Assert.Equal(1, ClassFeatures.BrutalCritical(data, orc));
        Assert.Equal("relentless_endurance", Assert.Single(FeatureChecks.StayUp(data, new PartyMember(orc, data))).Pool);

        Assert.DoesNotContain(ClassFeatures.ActiveFor(data, Make("human", ("fighter", 1))), f => f.Id == "breath_weapon");   // 人間は持たない
    }

    /// <summary>種族の数は合計のレベルで数える(ファイター 3 / ウィザード 3 のドラゴンボーンは、竜の息が 6 レベルの 3d6)。竜の息は小休憩で戻る。</summary>
    [Fact]
    public void RaceNumbersUseTheTotalLevel()
    {
        var c = Make("dragonborn", ("fighter", 3), ("wizard", 3));
        Assert.Equal(3, ClassFeatures.FeatureValue(Data, c, "breath_weapon", "dice"));
        Assert.Equal(2, ClassFeatures.FeatureValue(Data, Make("dragonborn", ("fighter", 5)), "breath_weapon", "dice"));
        c.Scores[Ability.Con] = 14;
        Assert.Equal(8 + 3 + 2, ClassFeatures.FeatureValue(Data, c, "breath_weapon", "dc"));   // 8 + 習熟 + 耐久力
        Assert.Equal(8 + 3 + 4, ClassFeatures.FeatureValue(Data, c, "breath_weapon", "dc", _ => 4));   // 戦闘の今の能力値を渡せる

        Assert.True(ClassFeatures.Spend(Data, c, "breath_weapon"));
        ClassFeatures.ShortRest(Data, c);
        Assert.Equal(1, ClassFeatures.UsesLeft(Data, c, "breath_weapon"));
        var orc = Make("half_orc", ("fighter", 1));
        Assert.True(ClassFeatures.Spend(Data, orc, "relentless_endurance"));
        ClassFeatures.ShortRest(Data, orc);
        Assert.Equal(0, ClassFeatures.UsesLeft(Data, orc, "relentless_endurance"));   // 不屈の耐久は大休憩まで戻らない
    }

    /// <summary>ハーフオークのバーバリアンが激怒のあいだに倒れかけたら、先に不屈の激怒のセーヴ、だめなら不屈の耐久(クラス、種族の順)。</summary>
    [Fact]
    public void StayUpTriesTheClassSaveBeforeTheRacePool()
    {
        var pm = new PartyMember(Make("half_orc", ("barbarian", 11)), Data) { Raging = true };
        var ways = FeatureChecks.StayUp(Data, pm);
        Assert.Equal(2, ways.Count);
        Assert.Equal((Ability.Con, 10, (string?)null), (ways[0].Ability!.Value, ways[0].Dc, ways[0].Pool));
        Assert.Equal("relentless_endurance", ways[1].Pool);
    }

    /// <summary>Step 6b: 写した種族も、状態へのセーヴの有利・魔法へのセーヴの有利・幸運・魔法の眠りを持つ。ふつうのセーヴの有利にはしない。</summary>
    [Fact]
    public void CopiedRacesKeepTheirSaves()
    {
        var (data, content) = WithPack("races.json", """
            [{"id": "t_dwarf", "extends": "dwarf"}, {"id": "t_elf", "extends": "elf"}, {"id": "t_halfling", "extends": "halfling"}, {"id": "t_gnome", "extends": "gnome"}]
            """);
        Assert.Empty(content.Problems);
        PartyMember M(string race) => new(Make(race, ("fighter", 1)), data);
        Assert.True(FeatureChecks.SaveAdvantageAgainst(data, M("t_dwarf"), Core.Effects.Condition.Poisoned));
        Assert.False(FeatureChecks.SaveAdvantage(data, M("t_dwarf"), Ability.Con));   // 毒のセーヴだけ
        Assert.True(FeatureChecks.SaveAdvantageAgainst(data, M("t_elf"), Core.Effects.Condition.Charmed));
        Assert.True(FeatureChecks.MagicImmune(data, M("t_elf"), Core.Effects.Condition.Unconscious));
        Assert.False(FeatureChecks.MagicImmune(data, M("t_elf"), Core.Effects.Condition.Charmed));
        Assert.True(FeatureChecks.Lucky(data, M("t_halfling")));
        Assert.True(FeatureChecks.SaveAdvantageAgainst(data, M("t_halfling"), Core.Effects.Condition.Frightened));
        Assert.True(FeatureChecks.MagicSaveAdvantage(data, M("t_gnome"), Ability.Wis));
        Assert.False(FeatureChecks.MagicSaveAdvantage(data, M("t_gnome"), Ability.Dex));
        Assert.False(FeatureChecks.SaveAdvantage(data, M("t_gnome"), Ability.Wis));   // 魔法でないセーヴは有利にならない
    }

    [Fact]
    public void BadRaceFeaturesAreReported()
    {
        var (_, content) = WithPack("features.json", """
            [{"id": "t_resist", "kind": "passive", "effects": [{"type": "resist_damage"}]},
             {"id": "t_stay", "kind": "passive", "effects": [{"type": "stay_up", "pool": "no_such_pool"}]},
             {"id": "t_scale", "kind": "passive", "values": {"n": {"class_level": 1}}},
             {"id": "t_adv", "kind": "passive", "effects": [{"type": "advantage", "roll": "initiative", "condition": "charmed"}]},
             {"id": "t_magic", "kind": "passive", "effects": [{"type": "immune_magic_condition"}]}]
            """, ("races.json", """[{"id": "t_stayer", "extends": "human", "features": [{"id": "t_stay", "level": 1}]}]"""));   // 回数の持ち主は、誰かが持つ能力だけ確かめる
        Assert.Contains(content.Problems, p => p.Contains("resist_damage には damage_type"));
        Assert.Contains(content.Problems, p => p.Contains("'no_such_pool'"));
        Assert.Contains(content.Problems, p => p.Contains("roll が save のときだけ"));
        Assert.Contains(content.Problems, p => p.Contains("immune_magic_condition には condition"));

        var (_, races) = WithPack("races.json", """[{"id": "t_race", "extends": "human", "features": [{"id": "second_wind", "level": 1}, {"id": "nothing_here", "level": 1}]}]""");
        Assert.Contains(races.Problems, p => p.Contains("'nothing_here'"));
        Assert.Contains(races.Problems, p => p.Contains("races 't_race' の features 'second_wind'") && p.Contains("total_level"));
    }
}
