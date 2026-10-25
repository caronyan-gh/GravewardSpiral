using System.Text.Json.Serialization;

namespace GravewardSpiral.Core.Rules;

/// <summary>能力値。JSON では "str" "dex" "con" "int" "wis" "cha" と書く。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Ability>))]
public enum Ability
{
    [JsonStringEnumMemberName("str")] Str,
    [JsonStringEnumMemberName("dex")] Dex,
    [JsonStringEnumMemberName("con")] Con,
    [JsonStringEnumMemberName("int")] Int,
    [JsonStringEnumMemberName("wis")] Wis,
    [JsonStringEnumMemberName("cha")] Cha,
}

public static class Abilities
{
    public static readonly Ability[] All = [Ability.Str, Ability.Dex, Ability.Con, Ability.Int, Ability.Wis, Ability.Cha];

    /// <summary>修正値。(値 - 10) / 2 の切り捨て(8 → -1、9 → -1、10 → 0、11 → 0、12 → 1)。</summary>
    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    public static string Code(this Ability a) => a switch
    {
        Ability.Str => "str",
        Ability.Dex => "dex",
        Ability.Con => "con",
        Ability.Int => "int",
        Ability.Wis => "wis",
        _ => "cha",
    };

    public static Ability Parse(string code) => code.ToLowerInvariant() switch
    {
        "str" => Ability.Str,
        "dex" => Ability.Dex,
        "con" => Ability.Con,
        "int" => Ability.Int,
        "wis" => Ability.Wis,
        "cha" => Ability.Cha,
        _ => throw new FormatException($"能力値 '{code}' はありません(str/dex/con/int/wis/cha)"),
    };
}

/// <summary>6 つの能力値。</summary>
public sealed class AbilityScores
{
    public int Str { get; set; } = 10;
    public int Dex { get; set; } = 10;
    public int Con { get; set; } = 10;
    public int Int { get; set; } = 10;
    public int Wis { get; set; } = 10;
    public int Cha { get; set; } = 10;

    public int this[Ability a]
    {
        get => a switch
        {
            Ability.Str => Str,
            Ability.Dex => Dex,
            Ability.Con => Con,
            Ability.Int => Int,
            Ability.Wis => Wis,
            _ => Cha,
        };
        set
        {
            switch (a)
            {
                case Ability.Str: Str = value; break;
                case Ability.Dex: Dex = value; break;
                case Ability.Con: Con = value; break;
                case Ability.Int: Int = value; break;
                case Ability.Wis: Wis = value; break;
                default: Cha = value; break;
            }
        }
    }

    public int Mod(Ability a) => Abilities.Modifier(this[a]);

    public AbilityScores Clone() => new() { Str = Str, Dex = Dex, Con = Con, Int = Int, Wis = Wis, Cha = Cha };
}

/// <summary>レベル・習熟ボーナス・経験値の表(5版。L21 より先はエピックレベル)。</summary>
public static class Progression
{
    /// <summary>そのレベルになるのに要る経験値(L1〜L20 は 5版の表)。</summary>
    private static readonly int[] Thresholds =
        [0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000, 85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000, 305000, 355000];

    /// <summary>習熟ボーナスの上限(d20 を振る意味を残すため、L21 より先は伸ばさない)。</summary>
    public const int MaxProficiency = 6;

    public static int ProficiencyBonus(int level) => Math.Min(MaxProficiency, 2 + (Math.Max(1, level) - 1) / 4);

    /// <summary>そのレベルになるのに要る経験値の合計。L21 より先は、1 レベルごとに 60000 から 5000 ずつ増える。</summary>
    public static long XpForLevel(int level)
    {
        if (level <= 1) return 0;
        if (level <= 20) return Thresholds[level - 1];
        long xp = Thresholds[^1];
        for (int l = 21; l <= level; l++) xp += 60000 + 5000L * (l - 21);
        return xp;
    }

    /// <summary>今の経験値で上がれるレベル(上限は無い)。</summary>
    public static int LevelForXp(long xp)
    {
        int level = 1;
        while (XpForLevel(level + 1) <= xp) level++;
        return level;
    }
}
