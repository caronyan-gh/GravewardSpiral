using System.Text.Json;
using System.Text.Json.Nodes;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// オリジナルの品(シナリオのパックで定義した品)を、シナリオの外へ持ち出しても使えるようにする。
/// 書くとき(Snapshot): 品の中身をそのまま持ち物に写し、出どころのシナリオの id と GUID、写したときのデータの形の版も入れる。
/// 読むとき(Attach): 出どころのシナリオを読んでいれば、パックの品を使う。読んでいなければ、写しを今の形に直して「GUID:元の id」の名前で入れる
/// (別のシナリオの同じ id の品と混ざらない)。中身がどこにも無い品は「失われた品」で補う(落ちないように)。
/// オリジナルの品が参照してよいのは、標準のパックのもの(呪文・状態・モンスターなど)だけ(エディタの検査で見る)。
/// </summary>
public static class OriginalItems
{
    /// <summary>この世界とクランが持っている品を全部(名簿の持ち物と装備、クランハウスの倉庫、店の売り物。宝箱はその場で開けるのでセーブに無い)。</summary>
    public static IEnumerable<ItemInstance> All(GameSession s)
    {
        foreach (var c in s.Roster.Characters)
        {
            foreach (var i in c.Inventory.Items) yield return i;
            foreach (var i in c.Inventory.Equipped.Values) yield return i;
        }
        foreach (var i in s.ClanHouse.Storage) yield return i;
        foreach (var i in s.ShopSold) yield return i;
    }

    /// <summary>書く前に、オリジナルの品の中身を写す(もう写してあれば、読んでいるパックの今の中身で写し直す)。</summary>
    public static void Snapshot(GameData data, IEnumerable<ItemInstance> items)
    {
        foreach (var it in items)
        {
            if (!data.Items.TryGetValue(it.ItemId, out var def) || def.OriginGuid == null) continue;
            var node = JsonSerializer.SerializeToNode(def, ContentLibrary.JsonOptions)!.AsObject();
            string id = it.Origin?.Id ?? it.ItemId;
            node["id"] = id;
            it.Origin = new ItemOrigin { Scenario = def.OriginScenario ?? "", Guid = def.OriginGuid, Id = id, Def = node, Format = ContentFormat.Current };
        }
    }

    /// <summary>読んだあとに、オリジナルの品と、中身の無い品を、いまのデータで使える形にする。</summary>
    public static void Attach(GameData data, IEnumerable<ItemInstance> items)
    {
        foreach (var it in items)
        {
            if (it.Origin is { Def: not null } o)
            {
                // 出どころのシナリオを読んでいれば、パックの品(作者が直した今の中身)を使う
                if (data.Items.TryGetValue(o.Id, out var live) && live.OriginGuid == o.Guid)
                {
                    it.ItemId = o.Id;
                    continue;
                }
                string key = $"{o.Guid}:{o.Id}";
                it.ItemId = key;
                if (data.Items.ContainsKey(key)) continue;
                try
                {
                    var node = ContentFormat.Upgrade("items", JsonNode.Parse(o.Def.ToJsonString())!.AsObject(), o.Format);
                    node["id"] = key;
                    var def = node.Deserialize<ItemDef>(ContentLibrary.JsonOptions)!;
                    (def.OriginScenario, def.OriginGuid) = (o.Scenario, o.Guid);
                    data.Items[key] = def;
                    continue;
                }
                catch (JsonException) { }   // 写しが読めなければ、失われた品にする
            }
            if (!data.Items.ContainsKey(it.ItemId)) data.Items[it.ItemId] = Lost(it.ItemId);
        }
    }

    /// <summary>中身がどこにも無い品の代わり(装備しても効果は無く、売ると 1 G)。</summary>
    public static ItemDef Lost(string id) => new()
    {
        Id = id,
        Kind = ItemKind.Gear,
        Name = LocalizedText.Of($"Lost item ({id})", $"失われた品({id})"),
        Description = LocalizedText.Of("Something from a world that is no longer here.", "もう無い世界から持ち帰った何か。"),
        Price = 2,
    };
}
