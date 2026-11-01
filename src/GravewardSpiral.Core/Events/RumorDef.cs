using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// シナリオの噂(rumors.json)。酒場の「噂を聞く」で、名のある敵と伝説の噂に並べて出す。
/// 聞くたびに、when の条件(イベントと同じ部品)を満たすものとほかの噂から、weight の重みで1つ選ぶ。
/// 物語が進むと噂が変わるように、フラグや済んだイベントで出し分ける。
/// tier は、誰に聞くと聞けるか(0 ただ・1 給仕 1G・2 冒険者 10G・3 情報屋 50G・4 旅商人 100G。TavernRumors)。
/// </summary>
public sealed class RumorDef
{
    public const string Collection = "rumors";

    public string Id { get; set; } = "";
    public LocalizedText Text { get; set; } = LocalizedText.Of("");
    public List<EventCondition> When { get; set; } = [];
    public double Weight { get; set; } = 1;

    /// <summary>
    /// 噂の格: 酒場で誰に聞くと聞けるか。0 = 噂を聞く(ただ。世間話。省くとこれ)、1 = 給仕にチップを払う 1G(ちょっとした心得)、
    /// 2 = 冒険者に酒を奢る 10G(名のある敵の居場所のあたり)、3 = 情報屋に金を払う 50G(名のある敵の本当の階。いないこともある)、
    /// 4 = 旅商人の土産話を買う 100G(遠い国の伝説。いないことが多い)。役に立つ話ほど上の格に置く。
    /// </summary>
    public int Tier { get; set; }
    public string? Note { get; set; }

    /// <summary>いま聞ける噂(条件を満たし、出やすさが 0 より大きいもの。id の順)。</summary>
    public static List<RumorDef> Available(GameData data, EventContext ctx) =>
        data.Rumors.Values.Where(r => r.Weight > 0 && EventRunner.Test(r.When, ctx)).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();

    /// <summary>読み込みの検査: 文と条件の部品。</summary>
    public static IEnumerable<string> Check(RumorDef r)
    {
        string where = $"rumors '{r.Id}'";
        if (string.IsNullOrWhiteSpace(r.Text.Texts.GetValueOrDefault("en")) || string.IsNullOrWhiteSpace(r.Text.Texts.GetValueOrDefault("ja")))
            yield return $"{where}: 文が英語か日本語で空です";
        foreach (var c in r.When)
            if (!EventCondition.KnownTypes.Contains(c.Type)) yield return $"{where}: 条件の種類 '{c.Type}' がありません";
        if (r.Tier < 0 || r.Tier > TavernRumors.MaxTier) yield return $"{where}: tier は 0〜{TavernRumors.MaxTier} です({r.Tier})";
    }
}
