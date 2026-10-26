using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Monsters;

namespace GravewardSpiral.Core.Combat;

/// <summary>
/// 階のテーマ(パックの floor_themes.json)。深さの範囲に当てはまる階に、名前と入ったときの文を付け、
/// 出る敵を種類 (creature_type) の重みで寄せる(墓所ならアンデッドを多く)。"*" はほかの種類の重み(省くと 1)。
/// </summary>
public sealed class FloorTheme
{
    public const string Collection = "floor_themes";

    public string Id { get; set; } = "";
    public double MinDepth { get; set; }
    public double MaxDepth { get; set; } = 1;
    public LocalizedText? Name { get; set; }
    public LocalizedText? Enter { get; set; }
    public Dictionary<string, double> Types { get; set; } = [];

    /// <summary>この階の落とし物の味付け(確率と候補の重みの倍率)。無ければ ×1。</summary>
    public Items.DropBoost? Drops { get; set; }

    public bool Covers(double depth) => depth >= MinDepth - 1e-9 && depth <= MaxDepth + 1e-9;

    public double WeightOf(MonsterDef m) =>
        Types.TryGetValue(m.CreatureType, out var w) ? w : Types.TryGetValue("*", out var other) ? other : 1;
}

/// <summary>
/// 階ごとのおすすめ(その深さの目安のレベルとテーマに合うモンスター、宝の等級の割合)。
/// ゲームは階の顔ぶれの抽選に使い、シナリオエディタは「この階のおすすめ」の一覧に使う。
/// </summary>
public static class FloorAdvisor
{
    public sealed record MonsterSuggestion(MonsterDef Monster, double Weight);

    /// <summary>その深さのテーマ。範囲がいちばん狭いものを選ぶ(同じなら id の順)。無ければ null。</summary>
    public static FloorTheme? ThemeFor(GameData data, double depth) => data.FloorThemes.Values
        .Where(t => t.Covers(depth)).OrderBy(t => t.MaxDepth - t.MinDepth).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// おすすめのモンスター(重みの大きい順)。その深さの目安のレベルで、弱すぎず(予算の min_xp_fraction 以上)、
    /// 強すぎない(1体で「とても危険」の予算まで)もの。重みはモンスターの重み × テーマの種類の重み。
    /// </summary>
    public static List<MonsterSuggestion> Monsters(GameData data, EncounterSettings s, double depth, int partySize, FloorTheme? theme = null)
    {
        double level = Math.Min(s.TargetLevel(depth), s.ScaleFromLevel);
        double lo = Encounters.Threshold(level, EncounterDifficulty.Easy) * partySize * s.MinXpFraction;
        double hi = Encounters.Threshold(level, EncounterDifficulty.Deadly) * partySize;
        return data.Monsters.Values
            .Where(m => m.Usable && m.Xp > 0 && m.Xp >= lo && m.Xp <= hi)
            .Select(m => new MonsterSuggestion(m, m.Weight * (theme?.WeightOf(m) ?? 1)))
            .Where(x => x.Weight > 0)
            .OrderByDescending(x => x.Weight).ThenBy(x => x.Monster.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>その深さの宝の等級の割合(合計 1)。</summary>
    public static Dictionary<string, double> ItemRarities(TreasureSettings t, double depth)
    {
        var row = t.Rarity.OrderBy(r => r.Depth).LastOrDefault(r => r.Depth <= depth + 1e-9) ?? t.Rarity.FirstOrDefault();
        if (row == null || row.Weights.Count == 0) return new() { ["mundane"] = 1 };
        double total = row.Weights.Values.Sum();
        return row.Weights.ToDictionary(kv => kv.Key, kv => kv.Value / total);
    }

    /// <summary>おすすめの魔法のアイテム(その深さで出やすい等級のもの。出やすい等級の順、同じなら id の順)。</summary>
    public static List<ItemDef> Items(GameData data, TreasureSettings t, double depth)
    {
        var rarities = ItemRarities(t, depth);
        return data.Items.Values.Where(i => i.IsMagic && !i.NoRandom && rarities.ContainsKey(i.Rarity))
            .OrderByDescending(i => rarities[i.Rarity]).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
    }
}
