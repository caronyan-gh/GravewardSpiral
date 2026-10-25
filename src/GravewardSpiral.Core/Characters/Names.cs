using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Characters;

/// <summary>
/// 種族ごとの名前の一覧(パックの names.json。id は種族の id)。名前は {"en": ..., "ja": ...} の組で、
/// 名前の入力の「おまかせ」や、おまかせで作るキャラの名前に使う。
/// </summary>
public sealed class NameListDef
{
    public const string Collection = "names";

    public string Id { get; set; } = "";
    public List<LocalizedText> Names { get; set; } = [];
}

public static class RandomNames
{
    /// <summary>
    /// 名前を1つ選ぶ。種族の一覧があればそこから、無ければ全部の一覧から。taken に入っている名前と、長すぎる名前は避ける
    /// (避けられないときは重なってもよい)。一覧が空なら null。
    /// </summary>
    public static string? Pick(GameData data, string? raceId, string language, IRandom rng, IEnumerable<string>? taken = null)
    {
        var pool = raceId != null && data.NameLists.TryGetValue(raceId, out var list) && list.Names.Count > 0
            ? list.Names
            : data.NameLists.Values.SelectMany(l => l.Names).ToList();
        var names = pool.Select(n => n.Get(language)).Where(n => n.Length is > 0 and <= CharacterFactory.MaxNameLength).ToList();
        if (names.Count == 0) return null;
        var used = new HashSet<string>(taken ?? [], StringComparer.OrdinalIgnoreCase);
        var fresh = names.Where(n => !used.Contains(n)).ToList();
        var from = fresh.Count > 0 ? fresh : names;
        return from[rng.Next(0, from.Count)];
    }
}
