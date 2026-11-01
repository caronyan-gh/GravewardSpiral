using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// 酒場の噂の選び方。誰に聞くか(噂の格)で聞ける噂が違い、払うほど役に立つ話になる。
/// 噂を聞く(ただ)・給仕にチップを払う 1G・冒険者に酒を奢る 10G・情報屋に金を払う 50G・旅商人の土産話を買う 100G。
/// どれでも ShiftChance ずつ、1つ上か1つ下の格の噂が聞けることがある。その格に話が無ければ、下の格へ下りて探す。
/// 聞いた相手の格そのものに話が無いときは、お金を取らない。情報屋と旅商人は、町に戻るたびに、いるかいないかが変わる(Present)。
/// </summary>
public static class TavernRumors
{
    /// <summary>格ごとの値段(G)。</summary>
    public static readonly int[] Prices = [0, 1, 10, 50, 100];

    /// <summary>格ごとの話し相手(文言のキー tavern.ask.&lt;id&gt; と、話が無いときの tavern.no_story.&lt;id&gt;)。</summary>
    public static readonly string[] Sources = ["none", "server", "adventurer", "informant", "merchant"];

    /// <summary>格ごとの、酒場にいる割合(情報屋と旅商人だけが、いないことがある)。</summary>
    public static readonly double[] PresentChance = [1, 1, 1, 0.6, 0.4];

    /// <summary>
    /// いま酒場にいるか。世界のシードと、迷宮で歩いた歩数で決める(町にいるあいだは変わらず、迷宮から戻るたびに変わる)。
    /// </summary>
    public static bool Present(int tier, ulong seed, long steps) =>
        PresentChance[tier] >= 1 || ((IRandom)RngStreams.Create(seed, $"tavern-{tier}-{steps}")).NextDouble() < PresentChance[tier];

    public static int MaxTier => Prices.Length - 1;

    /// <summary>1つ上(か下)の格の噂が聞ける割合(それぞれ)。</summary>
    public const double ShiftChance = 0.04;

    /// <summary>聞ける噂の候補。Heard は聞いたときに呼ぶ(伝説の噂をクランが覚える)。</summary>
    public sealed record Candidate(int Tier, string Text, double Weight, Action? Heard = null);

    /// <summary>その酒をおごって聞ける話があるか(その格に候補があるか)。</summary>
    public static bool Has(IEnumerable<Candidate> all, int tier) => all.Any(c => c.Tier == tier && c.Weight > 0);

    /// <summary>
    /// おごった酒(tier)で聞く噂を1つ選ぶ。格をずらすかを振り、その格から下へ、候補のある格を探して重みで選ぶ。
    /// 候補が2つ以上あれば、すぐ前に聞いたもの(last)は選ばない。おごった格に話が無ければ null。
    /// </summary>
    public static Candidate? Pick(IReadOnlyList<Candidate> all, int tier, string? last, IRandom rng)
    {
        if (!Has(all, tier)) return null;
        double x = rng.NextDouble();
        int at = x < ShiftChance && tier < MaxTier ? tier + 1 : x >= 1 - ShiftChance && tier > 0 ? tier - 1 : tier;
        for (; at >= 0; at--)
        {
            var pool = all.Where(c => c.Tier == at && c.Weight > 0).ToList();
            if (pool.Count > 1) pool.RemoveAll(c => c.Text == last);
            if (pool.Count == 0) continue;
            double w = rng.NextDouble() * pool.Sum(c => c.Weight);
            int i = 0;
            while (i < pool.Count - 1 && (w -= pool[i].Weight) >= 0) i++;
            return pool[i];
        }
        return null;
    }
}
