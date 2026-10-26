using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Combat;

/// <summary>
/// シナリオが決める階の顔ぶれ(dungeons.json の rosters の1階ぶん)。並びの1つずつが、次のどちらか。
/// - モンスターの id: その敵は必ず顔ぶれに入る。
/// - type:&lt;種類&gt;:&lt;数&gt;(例 type:undead:4): 新しいゲームのたびに、その種類(creature_type)でこの階に合う敵から、重みで数だけ選ぶ。
/// 空の階は、決めない(階のテーマから自動で選ぶ。Encounters.Roster)。
/// </summary>
public static class RosterSpec
{
    public const string TypePrefix = "type:";

    /// <summary>種類の項目を作る。</summary>
    public static string Type(string type, int count) => $"{TypePrefix}{type}:{Math.Max(1, count)}";

    /// <summary>種類の項目なら、その種類と数。</summary>
    public static bool IsType(string entry, out string type, out int count)
    {
        type = "";
        count = 0;
        if (!entry.StartsWith(TypePrefix, StringComparison.Ordinal)) return false;
        var parts = entry[TypePrefix.Length..].Split(':');
        type = parts[0];
        count = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 1;
        return true;
    }

    /// <summary>
    /// 階の顔ぶれにする。決めた敵は全部入れ、種類の項目は、その深さで予算に合う(FloorAdvisor.Monsters と同じ)その種類の敵から、
    /// まだ入っていないものを重みで数だけ選ぶ(足りなければ、あるだけ)。
    /// </summary>
    public static List<string> Resolve(GameData data, EncounterSettings s, double depth, int partySize, IReadOnlyList<string> spec, IRandom rng)
    {
        var picked = spec.Where(e => !IsType(e, out _, out _)).Distinct().ToList();
        var fit = FloorAdvisor.Monsters(data, s, depth, partySize);
        foreach (var e in spec)
        {
            if (!IsType(e, out string type, out int count)) continue;
            var pool = fit.Where(x => x.Monster.CreatureType == type && !picked.Contains(x.Monster.Id))
                .OrderBy(x => x.Monster.Id, StringComparer.Ordinal).ToList();
            for (int n = 0; n < count && pool.Count > 0; n++)
            {
                double r = rng.NextDouble() * pool.Sum(x => x.Weight);
                int i = 0;
                while (i < pool.Count - 1 && (r -= pool[i].Weight) >= 0) i++;
                picked.Add(pool[i].Monster.Id);
                pool.RemoveAt(i);
            }
        }
        return picked;
    }

    /// <summary>その深さで、種類ごとに合う敵の数(エディタが「この階に合うアンデッド 12 種」と見せる)。</summary>
    public static Dictionary<string, int> FitCounts(GameData data, EncounterSettings s, double depth, int partySize) =>
        FloorAdvisor.Monsters(data, s, depth, partySize).GroupBy(x => x.Monster.CreatureType).ToDictionary(g => g.Key, g => g.Count());
}
