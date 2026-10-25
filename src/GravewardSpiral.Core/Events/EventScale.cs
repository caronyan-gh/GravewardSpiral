using System.Text.RegularExpressions;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// 低い帯のイベントを、帯より上の世界に置いたときの引き上げ(逆に下げることはしない)。
/// 倍率 f = その階の目安のレベル ÷ イベントの帯の上限(max_level)。f が 1 以下(帯の中)なら、そのまま。
/// 金と経験値(と金の条件)は f²(5版の報酬はレベルより速く増えるため)、ダメージと回復のダイスの数は f、
/// 難易度は帯の上限を超えたレベルの半分だけ足す(20 まで)。
/// </summary>
public static partial class EventScale
{
    /// <summary>帯より上の階に置くときの出やすさの倍率(低い帯のみすぼらしいイベントばかりにならないように)。</summary>
    public const double AboveBandWeight = 0.4;

    public const int MaxDc = 20;

    /// <summary>そのイベントの、その階での倍率 f(帯の上限が無いか、帯の中なら 1)。</summary>
    public static double For(EventDef def, double level, Characters.GameData? data = null)
    {
        double max = MaxLevel(def, data);
        if (max >= 999 || level <= max + 1e-9) return 1;
        return level / Math.Max(1, max);
    }

    /// <summary>難易度に足す値(帯の上限を超えたレベルの半分)。</summary>
    public static int DcBonus(EventDef def, double level, Characters.GameData? data = null)
    {
        double max = MaxLevel(def, data);
        return max < 999 && level > max ? (int)((level - max) / 2) : 0;
    }

    /// <summary>
    /// イベントの帯の上限。迷宮のイベントは scatter の max_level。町の依頼(when で chain:&lt;組&gt; を見る)は、その組の最初の部品の max_level。
    /// </summary>
    private static double MaxLevel(EventDef def, Characters.GameData? data)
    {
        if (def.Scatter is { } sc) return sc.MaxLevel;
        if (data == null) return 999;
        var chain = def.When.FirstOrDefault(c => c.Type == "flag" && !c.Not && c.Id?.StartsWith("chain:") == true)?.Id?["chain:".Length..];
        var first = chain == null ? null : data.Events.Values.Where(e => e.Scatter?.Chain == chain).OrderBy(e => e.Scatter!.Step).FirstOrDefault();
        return first?.Scatter?.MaxLevel ?? 999;
    }

    /// <summary>金・経験値(負の値も)。10 の位で丸める。</summary>
    public static int Reward(int value, double f) =>
        f <= 1 || value == 0 ? value : Math.Sign(value) * Math.Max(1, (int)Math.Round(Math.Abs(value) * f * f / 10.0) * 10);

    public static int Dc(int dc, int bonus) => bonus <= 0 ? dc : Math.Min(MaxDc, dc + bonus);

    /// <summary>ダイスの数(と足す値)に f を掛ける。「2d6+3」→ f が 2 なら「4d6+6」。</summary>
    public static string? Dice(string? dice, double f)
    {
        if (dice == null || f <= 1) return dice;
        return DiceTerm().Replace(dice, m =>
        {
            if (m.Groups["n"].Success && m.Groups["d"].Success)
                return $"{Math.Max(1, (int)Math.Round(int.Parse(m.Groups["n"].Value.Length == 0 ? "1" : m.Groups["n"].Value) * f))}d{m.Groups["d"].Value}";
            return ((int)Math.Round(int.Parse(m.Value) * f)).ToString();
        });
    }

    [GeneratedRegex(@"(?<n>\d*)d(?<d>\d+)|(?<!d)\b\d+\b")]
    private static partial Regex DiceTerm();
}
