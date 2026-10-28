using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>能力値上昇(クラスのレベルで2点ずつ得て、訓練場で振る。20 まで)。</summary>
public class AbilityIncreaseTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static Character Raw(string cls, int level)
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", cls, 1);
        c.Xp = Progression.XpForLevel(level);
        c.LevelUp(Data);
        return c;
    }

    [Fact]
    public void PointsComeAtTheClassLevelsOfEachClass()
    {
        Assert.Equal(0, Raw("wizard", 3).AbilityPoints);
        Assert.Equal(2, Raw("wizard", 4).AbilityPoints);
        Assert.Equal(4, Raw("fighter", 6).AbilityPoints);   // ファイターは 6 にもある
        Assert.Equal(6, Raw("rogue", 10).AbilityPoints);    // ローグは 10 にもある
        Assert.Equal(10, Raw("cleric", 20).AbilityPoints);
        // マルチクラスは、クラスごとのレベルで数える(ファイター 4 / ウィザード 4 なら2回)
        var multi = Raw("fighter", 4);
        multi.Classes.Add(new ClassLevel { ClassId = "wizard", Level = 4 });
        multi.Level = 8;
        Assert.Equal(4, multi.AbilityPoints);
    }

    [Fact]
    public void SpendingRaisesTheScoreUpToTwenty()
    {
        var c = Raw("fighter", 8);
        c.Scores[Ability.Str] = 19;
        Assert.True(c.IncreaseAbility(Ability.Str));
        Assert.Equal(20, c.Scores.Str);
        Assert.False(c.IncreaseAbility(Ability.Str));   // 20 まで
        Assert.Equal(5, c.AbilityPoints);
        while (c.AbilityPoints > 0) Assert.True(c.IncreaseAbility(Ability.Wis));
        Assert.False(c.IncreaseAbility(Ability.Int));   // 点が無い
    }

    [Fact]
    public void RaisingTheConModifierRaisesMaxHpByLevel()
    {
        var c = Raw("barbarian", 8);
        c.Scores[Ability.Con] = 13;
        int max = c.MaxHp, hp = c.Hp;
        Assert.True(c.IncreaseAbility(Ability.Con));   // 13 → 14: 修正値 +1
        Assert.Equal(max + 8, c.MaxHp);
        Assert.Equal(hp + 8, c.Hp);
        Assert.True(c.IncreaseAbility(Ability.Con));   // 14 → 15: 修正値は同じ
        Assert.Equal(max + 8, c.MaxHp);
    }

    [Fact]
    public void ARaiseCanMeetAMulticlassRequirement()
    {
        var c = Raw("fighter", 4);
        c.Scores[Ability.Int] = 12;
        Assert.False(c.CanTakeClass(Data, "wizard"));
        c.IncreaseAbility(Ability.Int);
        Assert.True(c.CanTakeClass(Data, "wizard") || !Data.Classes["fighter"].MeetsMulticlass(c.Scores));
        Assert.True(Data.Classes["wizard"].MeetsMulticlass(c.Scores));
    }

    [Fact]
    public void QuickCreateSpendsThePointsOnThePrimaryAbility()
    {
        var c = CharacterFactory.QuickCreate(Data, "T", "human", "wizard", 8);
        Assert.Equal(0, c.AbilityPoints);
        Assert.Equal(["int", "int", "int", "int"], c.AbilityIncreases);
    }
}
