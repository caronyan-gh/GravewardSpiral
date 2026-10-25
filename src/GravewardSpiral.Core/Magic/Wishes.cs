using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;

namespace GravewardSpiral.Core.Magic;

/// <summary>
/// ウィッシュで選べる大きな効果1つ(パックの wishes.json)。effects はパーティ全員に起こす。
/// special は効果の部品で書けないもの(return_town は町へ帰る。later はあとの段階で入れるので選べない)。
/// </summary>
public sealed class WishDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public List<EffectPart> Effects { get; set; } = [];
    public string? Special { get; set; }
    public string? Note { get; set; }

    public const string ReturnTown = "return_town";
    public bool Available => Special != "later";
}
