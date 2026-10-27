using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// シナリオを始める条件の1つ(このシナリオをクリアしたクランだけが入れる)。
/// Guid で見分け、Id はクリアの印を GUID で持つ前のクラン(印を id で持つ)のため。Name は、そのシナリオが置かれていないときに出す名前。
/// </summary>
public sealed class RequiredClear
{
    public string? Guid { get; set; }
    public string Id { get; set; } = "";
    public LocalizedText? Name { get; set; }
}

/// <summary>
/// クランのクリアの印(シナリオ → クリアした世界の数)。印はシナリオの GUID。GUID の無いシナリオと、
/// 印を持つ前にクリアした世界から拾ったものは "id:シナリオの id"。
/// </summary>
public static class ScenarioClears
{
    public const string IdPrefix = "id:";

    public static string Key(PackManifest scenario) => scenario.Guid ?? IdPrefix + scenario.Id;

    public static bool Has(IReadOnlyDictionary<string, int> clears, RequiredClear required) =>
        required.Guid != null && clears.ContainsKey(required.Guid) || required.Id.Length > 0 && clears.ContainsKey(IdPrefix + required.Id);

    /// <summary>scenario を始めるのに足りないクリア(クランを連れていかないなら clears は空)。</summary>
    public static List<RequiredClear> Missing(PackManifest? scenario, IReadOnlyDictionary<string, int>? clears) =>
        scenario?.RequiresClear.Where(r => !Has(clears ?? new Dictionary<string, int>(), r)).ToList() ?? [];

    /// <summary>印を持つ前のクランの世界から拾う(クリアした世界のシナリオを、id の印にする)。</summary>
    public static Dictionary<string, int> FromWorlds(IEnumerable<SaveData> worlds)
    {
        var clears = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in worlds)
        {
            if (w.Scenario is { } id && w.Flags.Contains("cleared")) clears[IdPrefix + id] = clears.GetValueOrDefault(IdPrefix + id) + 1;
        }
        return clears;
    }
}
