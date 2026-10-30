using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// クランをシナリオへ連れて入れるか。allow_import ならどのクランでも。外していて series があれば、遊んだ世界が全部同じシリーズのシナリオのクランだけ
/// (シリーズのシナリオは、足したクラスなどを共にする。Wizardry 風の I → II → III)。どちらでもなければ連れて入れない。
/// 連れて入れても、連れていく人のクラス・種族・覚えた呪文が入る先に無ければ始められない(Missing)。
/// </summary>
public static class ClanImport
{
    /// <summary>そのシナリオに、何かのクランを連れて入れるか(新しいゲームでクランを選ぶ行を出すか)。シナリオなし(自由な迷宮)は入れる。</summary>
    public static bool Any(PackManifest? scenario) => scenario == null || scenario.AllowImport || scenario.Series is { Length: > 0 };

    /// <summary>played(そのクランで遊んだ世界のシナリオ)のクランを、scenario へ連れて入れるか。seriesOf はシナリオの id からシリーズ(無い・見つからなければ null)。</summary>
    public static bool Allows(PackManifest? scenario, IReadOnlyCollection<string?> played, Func<string, string?> seriesOf)
    {
        if (scenario == null || scenario.AllowImport) return true;
        if (scenario.Series is not { Length: > 0 } series) return false;
        return played.Count > 0 && played.All(id => id != null && (id == scenario.Id || seriesOf(id) == series));
    }

    /// <summary>連れていく人のうち、入る先のデータに無いもの(「名前: クラス 'samurai'」の形)。全部あれば空。</summary>
    public static List<string> Missing(GameData data, IEnumerable<Character> characters)
    {
        var missing = new List<string>();
        foreach (var c in characters)
        {
            foreach (var id in c.ClassIds.Distinct().Where(id => !data.Classes.ContainsKey(id))) missing.Add($"{c.Name}: class '{id}'");
            if (!data.Races.ContainsKey(c.RaceId)) missing.Add($"{c.Name}: race '{c.RaceId}'");
            var spells = c.Books.Values.SelectMany(b => b.Cantrips.Concat(b.Known).Concat(b.Prepared)).Distinct();
            foreach (var id in spells.Where(id => !data.Spells.ContainsKey(id))) missing.Add($"{c.Name}: spell '{id}'");
        }
        return missing;
    }
}
