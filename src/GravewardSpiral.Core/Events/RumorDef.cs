using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// シナリオの噂(rumors.json)。酒場の「噂を聞く」で、名のある敵と伝説の噂に並べて出す。
/// 聞くたびに、when の条件(イベントと同じ部品)を満たすものとほかの噂から、weight の重みで1つ選ぶ。
/// 物語が進むと噂が変わるように、フラグや済んだイベントで出し分ける。
/// </summary>
public sealed class RumorDef
{
    public const string Collection = "rumors";

    public string Id { get; set; } = "";
    public LocalizedText Text { get; set; } = LocalizedText.Of("");
    public List<EventCondition> When { get; set; } = [];
    public double Weight { get; set; } = 1;
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
    }
}
