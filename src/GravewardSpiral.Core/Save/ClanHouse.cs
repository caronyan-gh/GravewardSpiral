using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// クランハウスの段階(パックの clan_house.json)。金を投資すると次の段階になり、倉庫が広がり、設備が付く。
/// 段階の数と中身はデータで決める。最後の段階からは、倉庫だけを何度でも広げられる(expand_*)。
/// </summary>
public sealed class ClanHouseLevel
{
    public const string Collection = "clan_house";

    public string Id { get; set; } = "";
    public int Level { get; set; } = 1;
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public LocalizedText? Description { get; set; }

    /// <summary>この段階にするのに要る金(1段階目は 0)。</summary>
    public long Cost { get; set; }

    /// <summary>倉庫の枠の数(重ねられる品は1枠に重なる)。</summary>
    public int StorageSlots { get; set; }

    /// <summary>付いている設備(その段階で使えるもの全部。下の段階のものも書く)。</summary>
    public List<string> Facilities { get; set; } = [];

    /// <summary>この段階にするのに要る素材(品の id → 数)。パーティの持ち物と倉庫から集めて使う。</summary>
    public Dictionary<string, int> Materials { get; set; } = [];

    /// <summary>この段階から、倉庫を広げられる: 1回で増える枠、最初の費用、回ごとに増える費用。</summary>
    public int ExpandSlots { get; set; }
    public long ExpandCost { get; set; }
    public long ExpandCostStep { get; set; }

    /// <summary>設備の名前。</summary>
    public const string Bed = "bed", BedFull = "bed_full", Library = "library", Chapel = "chapel", Hall = "hall";

    public static List<ClanHouseLevel> Load(ContentLibrary content) =>
        content.All(Collection).Select(i => i.As<ClanHouseLevel>()).OrderBy(l => l.Level).ToList();
}

/// <summary>
/// クランハウス(クランで1つ。新しいゲームで建ち、クランファイルと一緒に持ち歩く)。
/// 倉庫は、パーティのだれでも預けたり引き出したりできる共有の袋。
/// </summary>
public sealed class ClanHouse
{
    public int Level { get; set; } = 1;
    public List<ItemInstance> Storage { get; set; } = [];

    /// <summary>倉庫を広げた回数(最後の段階から)。</summary>
    public int Expansions { get; set; }

    public ClanHouseLevel? Def(IReadOnlyList<ClanHouseLevel> levels) => levels.LastOrDefault(l => l.Level <= Level) ?? levels.FirstOrDefault();

    public int Capacity(IReadOnlyList<ClanHouseLevel> levels) =>
        Def(levels) is { } d ? d.StorageSlots + Expansions * d.ExpandSlots : 0;

    /// <summary>次に倉庫を広げる費用。広げられないなら null(まだ最後の段階でない、広げる設定が無い)。</summary>
    public long? ExpandCost(IReadOnlyList<ClanHouseLevel> levels) =>
        Next(levels) == null && Def(levels) is { ExpandSlots: > 0 } d ? d.ExpandCost + Expansions * d.ExpandCostStep : null;

    /// <summary>倉庫を広げる(パーティの金から払う)。できなければ false。</summary>
    public bool Expand(IList<Characters.Character> party, IReadOnlyList<ClanHouseLevel> levels)
    {
        if (ExpandCost(levels) is not long cost || !PartyGold.Pay(party, cost)) return false;
        Expansions++;
        return true;
    }

    /// <summary>名簿に載せられる人数(大広間で増える)。</summary>
    public int RosterLimit(IReadOnlyList<ClanHouseLevel> levels) => Has(levels, ClanHouseLevel.Hall) ? 30 : 20;

    /// <summary>設備による値段(書庫で鑑定、礼拝堂で寺院と呪い解きが半額)。</summary>
    public long Price(IReadOnlyList<ClanHouseLevel> levels, string facility, long price) =>
        Has(levels, facility) ? Math.Max(1, price / 2) : price;

    /// <summary>次の段階(無ければ null)。</summary>
    public ClanHouseLevel? Next(IReadOnlyList<ClanHouseLevel> levels) => levels.FirstOrDefault(l => l.Level > Level);

    public bool Has(IReadOnlyList<ClanHouseLevel> levels, string facility) =>
        Def(levels)?.Facilities.Contains(facility) == true;

    /// <summary>倉庫に入れる。重ねられる品は同じものに重ねる(本数の上限は品の stack)。入らなければ false。</summary>
    public bool Deposit(ItemInstance item, IReadOnlyDictionary<string, ItemDef> defs, IReadOnlyList<ClanHouseLevel> levels)
    {
        if (!defs.TryGetValue(item.ItemId, out var def)) return false;
        if (def.Stack > 1)
        {
            var same = Storage.FirstOrDefault(i => i.SameKind(item) && i.Count + item.Count <= def.Stack);
            if (same != null)
            {
                same.Count += item.Count;
                return true;
            }
        }
        if (Storage.Count >= Capacity(levels)) return false;
        Storage.Add(item);
        return true;
    }

    /// <summary>投資して次の段階にする(パーティの金から払う)。足りないか、次が無ければ false。</summary>
    public bool Upgrade(IList<Characters.Character> party, IReadOnlyList<ClanHouseLevel> levels)
    {
        if (Next(levels) is not { } next || next.Materials.Any(m => Count(party, m.Key) < m.Value)) return false;
        if (!PartyGold.Pay(party, next.Cost)) return false;
        foreach (var (id, n) in next.Materials) Take(party, id, n);
        Level = next.Level;
        return true;
    }

    /// <summary>その素材を、パーティの持ち物(袋)と倉庫にいくつ持っているか。</summary>
    public int Count(IEnumerable<Characters.Character> party, string itemId) =>
        Storage.Concat(party.SelectMany(c => c.Inventory.Items)).Where(i => i.ItemId == itemId).Sum(i => i.Count);

    /// <summary>素材を n 個使う(倉庫から先に、足りなければ持ち物から)。</summary>
    private void Take(IEnumerable<Characters.Character> party, string itemId, int n)
    {
        foreach (var bag in new[] { Storage }.Concat(party.Select(c => c.Inventory.Items)))
        {
            foreach (var it in bag.Where(i => i.ItemId == itemId).ToList())
            {
                int take = Math.Min(n, it.Count);
                it.Count -= take;
                n -= take;
                if (it.Count <= 0) bag.Remove(it);
                if (n == 0) return;
            }
        }
    }
}
