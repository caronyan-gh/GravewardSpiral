using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Combat;

/// <summary>戦闘の外の守り(Hazard。罠とイベントが使う)。クラスの能力と仲間のオーラも、戦闘と同じく効く(docs/class-data-design.md の Step 6c)。</summary>
public class HazardTests
{
    private static readonly GameData Data = GameData.Load(ContentLibrary.Load(TestPaths.Content));

    /// <summary>いつも同じ目を出す乱数(d20 なら roll)。</summary>
    private sealed class Fixed(int roll) : IRandom
    {
        public uint NextUInt() => 0;
        public int Next(int min, int maxExclusive) => Math.Clamp(roll, min, maxExclusive - 1);
        public double NextDouble() => 0;
    }

    private static Character Make(string cls, int level, string race = "human")
    {
        var c = new Character
        {
            Id = cls, Name = cls, RaceId = race, ClassId = cls, Level = level,
            Classes = [new ClassLevel { ClassId = cls, Level = level }], MaxHp = 40, Hp = 40,
        };
        foreach (var a in Abilities.All) c.Scores[a] = 10;
        return c;
    }

    private static Hazard For(IEnumerable<Character> party, int roll, List<string>? said = null) =>
        new(Data, party, new Fixed(roll), "chest", (key, _) => said?.Add(key));

    [Fact]
    public void ThePaladinsAuraReachesAlliesOutsideCombat()
    {
        var paladin = Make("paladin", 6);
        paladin.Scores[Ability.Cha] = 16;   // オーラは +3
        var fighter = Make("fighter", 1);   // 耐久力のセーヴは習熟の +2 だけ
        Assert.False(For([fighter], 10).Save(fighter, Ability.Con, 14));
        Assert.True(For([fighter, paladin], 10).Save(fighter, Ability.Con, 14));
        paladin.State = LifeState.Dead;
        paladin.Hp = 0;
        Assert.False(For([fighter, paladin], 10).Save(fighter, Ability.Con, 14));   // 倒れた人のオーラは届かない
    }

    [Fact]
    public void EvasionWorksOnTrapsAndEvents()
    {
        var rogue = Make("rogue", 7);
        var fighter = Make("fighter", 7);
        int Hurt(Character c, int dc) => For([c], 10).Damage(c, 20, "fire", (Ability.Dex, dc, SaveSuccess.Half), lethal: true);
        Assert.Equal(0, Hurt(rogue, 1));     // 成功で 0
        Assert.Equal(10, Hurt(rogue, 99));   // 失敗で半分
        Assert.Equal(10, Hurt(fighter, 1));
        Assert.Equal(20, Hurt(fighter, 99));
        Assert.Equal(0, For([fighter], 10).Damage(fighter, 20, null, (Ability.Con, 1, SaveSuccess.None), lethal: true));   // none は成功で受けない
    }

    [Fact]
    public void PurityOfBodyKeepsTrapPoisonAway()
    {
        var monk = Make("monk", 10);
        var fighter = Make("fighter", 10);
        Assert.False(For([monk], 1).AddCondition(monk, Condition.Poisoned, 60, (Ability.Con, 99)));
        Assert.Equal(0, For([monk], 1).Damage(monk, 20, "poison", null, lethal: true));
        Assert.True(For([fighter], 1).AddCondition(fighter, Condition.Poisoned, 60, (Ability.Con, 99)));
        Assert.Contains(fighter.Lasting, l => l.Condition == Condition.Poisoned);
    }

    [Fact]
    public void LethalDecidesBetweenDeathAndOneHp()
    {
        var a = Make("fighter", 1);
        For([a], 10).Damage(a, 30, "piercing", null, lethal: false);
        Assert.Equal((10, LifeState.Alive), (a.Hp, a.State));
        For([a], 10).Damage(a, 30, "piercing", null, lethal: false);
        Assert.Equal(1, a.Hp);   // イベントのダメージでは HP 1 で止まる
        var said = new List<string>();
        For([a], 10, said).Damage(a, 5, "piercing", null, lethal: true);
        Assert.Equal(LifeState.Dead, a.State);
        Assert.Equal(["chest.hurt", "chest.died"], said);

        var orc = Make("fighter", 1, "half_orc");
        said.Clear();
        For([orc], 10, said).Damage(orc, 45, "piercing", null, lethal: true);
        Assert.Equal(1, orc.Hp);   // 不屈の耐久(即死にならない大きさ)
        Assert.Equal(["chest.hurt", "chest.endure"], said);
    }
}
