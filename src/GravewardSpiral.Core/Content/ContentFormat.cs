using System.Text.Json.Nodes;

namespace GravewardSpiral.Core.Content;

/// <summary>
/// パックのデータの形の版(manifest の format)。データの形を変えたら Current を上げ、Upgrade に「前の版 → 次の版」の直し方を足す。
/// 古い版のパックと、セーブに写したオリジナルの品(写したときの版を持つ)は、読むときに Upgrade で今の形に直す。
/// 今のゲームより新しい版のパックは読まない(新しいゲームが要ると知らせる)。
/// </summary>
public static class ContentFormat
{
    /// <summary>このゲームが読み書きするデータの形の版。</summary>
    public const int Current = 1;

    /// <summary>
    /// 物1つ(collection の JSON のオブジェクト)を、from の版から今の版へ直す(書き換えて返す)。
    /// 版を上げたら、ここに 1 → 2、2 → 3 … の直し方を順に足す。
    /// </summary>
    public static JsonObject Upgrade(string collection, JsonObject item, int from)
    {
        for (int v = Math.Max(1, from); v < Current; v++)
        {
            // 例: if (v == 1 && collection == "items") { item["new_name"] = item["old_name"]?.DeepClone(); item.Remove("old_name"); }
        }
        return item;
    }
}
