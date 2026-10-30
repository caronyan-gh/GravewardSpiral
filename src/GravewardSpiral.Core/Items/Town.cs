using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>
/// 小休憩で調べる(5版): 動ける人が1人1個、未鑑定の品を手に取って調べ、休み終わりに正体が分かる。判定は無い。
/// 自分の持ち物から先に選び、無ければ仲間の品を調べる。1つの品を2人で調べることはない。
/// </summary>
public static class RestStudy
{
    /// <param name="defs">品のデータ。渡せば、調べ終わったあとに全員の袋の同じ品を重ねる(正体が分かると、同じ品と重なるようになる)。</param>
    public static List<(Character Who, ItemInstance Item)> Run(IEnumerable<Character> party, IReadOnlyDictionary<string, ItemDef>? defs = null)
    {
        var members = party.ToList();
        var taken = new HashSet<ItemInstance>(ReferenceEqualityComparer.Instance);
        var done = new List<(Character, ItemInstance)>();
        IEnumerable<ItemInstance> Unknown(Character c) => c.Inventory.Items.Concat(c.Inventory.Equipped.Values).Where(i => !i.Identified && !taken.Contains(i));
        foreach (var c in members.Where(c => c.CanAct))
        {
            var item = Unknown(c).FirstOrDefault() ?? members.SelectMany(Unknown).FirstOrDefault();
            if (item == null) break;
            taken.Add(item);
            done.Add((c, item));
        }
        foreach (var (_, item) in done) item.Identified = true;
        if (defs != null && done.Count > 0)
            foreach (var c in members) c.Inventory.MergeStacks(defs);
        return done;
    }
}

/// <summary>パーティの金(買い物や寺院の料金は、パーティの金から払う。足りる限り先頭の人から順に出す)。</summary>
public static class PartyGold
{
    public static long Total(IEnumerable<Character> party) => party.Sum(c => c.Gold);

    /// <summary>払う。足りなければ何もせず false。</summary>
    public static bool Pay(IList<Character> party, long amount)
    {
        if (Total(party) < amount) return false;
        foreach (var c in party)
        {
            long take = Math.Min(c.Gold, amount);
            c.Gold -= take;
            amount -= take;
            if (amount == 0) break;
        }
        return true;
    }

    /// <summary>受け取った金を、パーティで均等に分ける(端数は先頭の人)。</summary>
    public static void Share(IList<Character> party, long amount)
    {
        if (party.Count == 0) return;
        long each = amount / party.Count;
        foreach (var c in party) c.Gold += each;
        party[0].Gold += amount - each * party.Count;
    }
}

/// <summary>
/// 商店。いつも置いている品(ふつうの武器・鎧・盾・袋・道具・水薬)と、売られた品を売る。売値は半額。
/// 鑑定の料金は等級で決まる。呪いを解く料金は1つ 300 G。
/// </summary>
public static class Shop
{
    /// <summary>いつも置いている品。</summary>
    public static IEnumerable<ItemDef> Standard(GameData data) => data.Items.Values
        .Where(i => !i.NoRandom && (!i.IsMagic && i.Base == null && i.Kind != ItemKind.Key || i.Id is "potion_of_healing"))
        .OrderBy(i => i.Kind).ThenBy(i => i.Price).ThenBy(i => i.Id, StringComparer.Ordinal);

    /// <summary>買値。売られた大事なものは、売値の Buyback 倍(うっかり売ると高くつく)。</summary>
    public static long BuyPrice(ItemDef def, ItemInstance item) =>
        def.Kind == ItemKind.Key && def.Buyback is double m ? Math.Max(1, (long)Math.Round(SellPrice(def, item) * m)) : Treasure.Price(def, item) * Math.Max(1, item.Count);

    /// <summary>売れるか。大事なものは、買い戻しの倍率を書いたものだけ。</summary>
    public static bool CanSell(ItemDef def) => def.Kind != ItemKind.Key || def.Buyback != null;

    /// <summary>売値は半額(鑑定していない品は、さらに半分)。</summary>
    public static long SellPrice(ItemDef def, ItemInstance item) =>
        Math.Max(1, Treasure.Price(def, item) / (item.Identified ? 2 : 4)) * item.Count;

    public static long IdentifyPrice(ItemDef def, ItemInstance item) =>
        (item.Bonus > 0 ? Treasure.RarityOf(item.Bonus, def.Kind) : def.Rarity) switch
        {
            "common" => 25, "uncommon" => 100, "rare" => 400, "very_rare" => 1500, "legendary" => 5000, "artifact" => 10000, _ => 10,
        };

    public const long RemoveCursePrice = 300;

    /// <summary>売られた品を店に並べる(未鑑定のものは、店が鑑定してから並べる)。</summary>
    public static void Stock(List<ItemInstance> sold, ItemInstance item)
    {
        item.Identified = true;
        item.Attuned = false;
        sold.Add(item);
        if (sold.Count > 60) sold.RemoveAt(0);   // 古いものから消える
    }
}

/// <summary>
/// 寺院。残っている状態(毒・麻痺など)を治す、死んだ人を蘇生する(Wizardry 風: 失敗すると灰、灰からの失敗で消失)。
/// 成功の割合は耐久力で上がる。厳しさの設定(いつも成功する、など)は M9。
/// </summary>
public static class Temple
{
    public const long CurePrice = 50;

    public static long RevivePrice(Character c) => c.State == LifeState.Ashes ? 250L * c.Level : 100L * c.Level;

    /// <summary>成功の割合(%)。死亡は 50 + 耐久力 × 3、灰は 40 + 耐久力 × 3(上限 95)。sure(やさしい難易度)なら必ず成功。</summary>
    public static int ReviveChance(Character c, bool sure = false) =>
        sure ? 100 : Math.Min(95, (c.State == LifeState.Ashes ? 40 : 50) + c.Scores.Con * 3 - 30);

    public enum ReviveResult { Revived, Ashes, Lost }

    /// <summary>蘇生する。成功すれば HP 1 で生き返る。失敗すると、死亡なら灰に、灰なら消失になる。</summary>
    public static ReviveResult Revive(Character c, IRandom rng, bool sure = false)
    {
        if (sure || rng.Next(0, 100) < ReviveChance(c))
        {
            c.State = LifeState.Alive;
            c.Hp = 1;
            return ReviveResult.Revived;
        }
        if (c.State == LifeState.Dead)
        {
            c.State = LifeState.Ashes;
            return ReviveResult.Ashes;
        }
        c.State = LifeState.Lost;
        return ReviveResult.Lost;
    }

    /// <summary>残っている状態を全部治す。</summary>
    public static void Cure(Character c) => c.Lasting.Clear();
}
