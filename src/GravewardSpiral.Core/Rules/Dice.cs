using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Rules;

/// <summary>
/// ダイス式。書き方: "d20"、"2d6+3"、"1d8+1d6-1"、"4d6kh3"(高い3つを残す)、"2d20kl1"(低い1つを残す)。
/// 空白は無視する。データ(JSON)の中の式はすべてこれで読む。
/// </summary>
public sealed class DiceExpression
{
    public const int MaxDiceCount = 1000;
    public const int MaxSides = 1000;

    private static readonly Regex InnerSpace = new(@"[0-9a-zA-Z]\s+[0-9a-zA-Z]", RegexOptions.Compiled);

    private readonly IReadOnlyList<DiceTerm> _terms;

    private DiceExpression(IReadOnlyList<DiceTerm> terms) => _terms = terms;

    public IReadOnlyList<DiceTerm> Terms => _terms;

    public static DiceExpression Constant(int value) => new([DiceTerm.Flat(value)]);

    public static DiceExpression Parse(string text)
    {
        if (TryParse(text, out var expr, out var error)) return expr;
        throw new FormatException(error);
    }

    public static bool TryParse(string? text, out DiceExpression expression, out string error)
    {
        expression = Constant(0);
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "ダイス式が空です";
            return false;
        }

        // 空白は + と - のまわりだけ許す("2d6 3" を "2d63" と読まないため)
        if (InnerSpace.IsMatch(text))
        {
            error = $"'{text}' の数や d のあいだに空白があります";
            return false;
        }
        string s = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        var terms = new List<DiceTerm>();
        int i = 0;
        while (i < s.Length)
        {
            int sign = 1;
            if (s[i] is '+' or '-')
            {
                sign = s[i] == '-' ? -1 : 1;
                i++;
            }
            else if (terms.Count > 0)
            {
                error = $"'{text}' の {i + 1} 文字目に + か - が要ります";
                return false;
            }

            int? count = ReadInt(s, ref i);
            if (i < s.Length && s[i] == 'd')
            {
                i++;
                int? sides = ReadInt(s, ref i);
                if (sides is null or < 1 || sides > MaxSides)
                {
                    error = $"'{text}' の面の数が正しくありません(1〜{MaxSides})";
                    return false;
                }
                int n = count ?? 1;
                if (n < 1 || n > MaxDiceCount)
                {
                    error = $"'{text}' のダイスの数が正しくありません(1〜{MaxDiceCount})";
                    return false;
                }

                KeepMode keep = KeepMode.All;
                int keepCount = n;
                if (i + 1 < s.Length && s[i] == 'k' && (s[i + 1] is 'h' or 'l'))
                {
                    keep = s[i + 1] == 'h' ? KeepMode.Highest : KeepMode.Lowest;
                    i += 2;
                    int? k = ReadInt(s, ref i);
                    if (k is null || k < 1 || k > n)
                    {
                        error = $"'{text}' の残す数が正しくありません(1〜{n})";
                        return false;
                    }
                    keepCount = k.Value;
                }
                terms.Add(new DiceTerm(sign, n, sides.Value, keep, keepCount, 0));
            }
            else if (count is not null)
            {
                terms.Add(DiceTerm.Flat(sign * count.Value));
            }
            else
            {
                error = $"'{text}' の {i + 1} 文字目が読めません";
                return false;
            }
        }

        expression = new DiceExpression(terms);
        return true;
    }

    private static int? ReadInt(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        if (i == start) return null;
        return int.TryParse(s.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public DiceRoll Roll(IRandom rng)
    {
        var parts = new List<DiceTermRoll>(_terms.Count);
        int total = 0;
        foreach (var term in _terms)
        {
            var part = term.Roll(rng);
            parts.Add(part);
            total += part.Value;
        }
        return new DiceRoll(this, total, parts);
    }

    public int Min => _terms.Sum(t => t.Min);
    public int Max => _terms.Sum(t => t.Max);

    /// <summary>期待値。残す数を指定した項は、全部の組み合わせを数えて求める(小さい式だけ)。</summary>
    public double Average => _terms.Sum(t => t.Average);

    /// <summary>ダイスの数を倍にした式(5版のクリティカル。定数は倍にしない)。</summary>
    public DiceExpression WithDoubledDice() =>
        new(_terms.Select(t => t.IsFlat ? t : t with { Count = t.Count * 2, KeepCount = t.Keep == KeepMode.All ? t.Count * 2 : t.KeepCount }).ToList());

    public DiceExpression Plus(DiceExpression other) => new(_terms.Concat(other._terms).ToList());

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _terms.Count; i++)
        {
            string term = _terms[i].ToString();
            if (i > 0 && !term.StartsWith('-')) sb.Append('+');
            sb.Append(term);
        }
        return sb.ToString();
    }
}

public enum KeepMode { All, Highest, Lowest }

public readonly record struct DiceTerm(int Sign, int Count, int Sides, KeepMode Keep, int KeepCount, int FlatValue)
{
    public static DiceTerm Flat(int value) => new(1, 0, 0, KeepMode.All, 0, value);

    public bool IsFlat => Sides == 0;

    public int Min => IsFlat ? FlatValue : Sign > 0 ? KeepCount : -KeepCount * Sides;
    public int Max => IsFlat ? FlatValue : Sign > 0 ? KeepCount * Sides : -KeepCount;

    public double Average
    {
        get
        {
            if (IsFlat) return FlatValue;
            if (Keep == KeepMode.All) return Sign * Count * (Sides + 1) / 2.0;
            return Sign * KeepAverage();
        }
    }

    private double KeepAverage()
    {
        // 組み合わせが多すぎる式は、残す数ぶんの平均で近似する
        if (Math.Pow(Sides, Count) > 2_000_000) return KeepCount * (Sides + 1) / 2.0;
        var dice = new int[Count];
        Array.Fill(dice, 1);
        double sum = 0;
        long combos = 0;
        var sorted = new int[Count];
        while (true)
        {
            dice.CopyTo(sorted, 0);
            Array.Sort(sorted);
            int kept = 0;
            for (int k = 0; k < KeepCount; k++)
                kept += Keep == KeepMode.Highest ? sorted[Count - 1 - k] : sorted[k];
            sum += kept;
            combos++;
            int idx = 0;
            while (idx < Count && ++dice[idx] > Sides)
            {
                dice[idx] = 1;
                idx++;
            }
            if (idx == Count) break;
        }
        return sum / combos;
    }

    public DiceTermRoll Roll(IRandom rng)
    {
        if (IsFlat) return new DiceTermRoll(this, FlatValue, []);
        var rolls = new int[Count];
        for (int i = 0; i < Count; i++) rolls[i] = rng.Roll(Sides);
        int value;
        if (Keep == KeepMode.All)
        {
            value = rolls.Sum();
        }
        else
        {
            var ordered = Keep == KeepMode.Highest ? rolls.OrderByDescending(r => r) : rolls.OrderBy(r => r);
            value = ordered.Take(KeepCount).Sum();
        }
        return new DiceTermRoll(this, Sign * value, rolls);
    }

    public override string ToString()
    {
        if (IsFlat) return FlatValue.ToString(CultureInfo.InvariantCulture);
        string body = $"{Count}d{Sides}";
        if (Keep != KeepMode.All) body += (Keep == KeepMode.Highest ? "kh" : "kl") + KeepCount;
        return Sign < 0 ? "-" + body : body;
    }
}

public sealed record DiceTermRoll(DiceTerm Term, int Value, IReadOnlyList<int> Rolls);

public sealed record DiceRoll(DiceExpression Expression, int Total, IReadOnlyList<DiceTermRoll> Parts)
{
    /// <summary>"2d6+3 = [4, 2] + 3 = 9" のような、ログに出す形。</summary>
    public string Describe()
    {
        var pieces = Parts.Select(p => p.Term.IsFlat ? p.Value.ToString(CultureInfo.InvariantCulture) : $"[{string.Join(", ", p.Rolls)}]");
        return $"{Expression} = {string.Join(" + ", pieces)} = {Total}";
    }
}
