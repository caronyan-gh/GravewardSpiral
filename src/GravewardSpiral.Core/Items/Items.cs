using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;

namespace GravewardSpiral.Core.Items;

/// <summary>アイテムの種類。Ammo は特別な弾(ふつうの矢とボルトは数えず、無限にある)。</summary>
public enum ItemKind { Weapon, Armor, Shield, Backpack, Consumable, Gear, Accessory, Key, Ammo }

public enum ArmorType { Light, Medium, Heavy }

/// <summary>
/// アイテムの種類のデータ(パックの items.json。SRD の武器・鎧・盾は tools/srd_import.py で作り、魔法のアイテムは手で書く)。
/// 魔法の武器・鎧は base に元のアイテムを書くと、ダメージや AC などの書いていない項目を元から写す。
/// </summary>
public sealed class ItemDef
{
    public string Id { get; set; } = "";
    public ItemKind Kind { get; set; }
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public LocalizedText? Description { get; set; }

    /// <summary>値段(金貨)。売値はこの半分。</summary>
    public double Price { get; set; }

    /// <summary>
    /// 大事なもの(kind が key)を売ったときの、買い戻しの値段の倍率(売値の何倍か)。
    /// 書いていない大事なものは売れない。ほかの品では使わない。
    /// </summary>
    public double? Buyback { get; set; }

    /// <summary>等級: mundane(ふつうの品)/ common / uncommon / rare / very_rare / legendary / artifact。</summary>
    public string Rarity { get; set; } = "mundane";

    /// <summary>同調が要るか(要るものは、同調していないと持っているあいだの効果が出ない。同調は3つまで)。</summary>
    public bool Attunement { get; set; }

    /// <summary>使える人(caster = 術者だけ)。</summary>
    public string? Only { get; set; }

    /// <summary>元のアイテム(魔法の武器・鎧が、ダメージや AC を写す元)。</summary>
    public string? Base { get; set; }

    /// <summary>命中とダメージ(武器)か AC(鎧・盾)に足す値(+1 の武器なら 1。呪いのものは負もある)。</summary>
    public int Bonus { get; set; }

    /// <summary>呪われている(装備すると、呪いを解くまで外せない)。</summary>
    public bool Cursed { get; set; }

    /// <summary>
    /// 呪いの品の見せかけ(5版: 鑑定しても呪いは分からない)。鑑定すると、呪いが分かるまではこの品に見える。
    /// 呪いが分かるのは、付けたときと、呪いを解いたとき。
    /// </summary>
    public LooksLike? LooksLike { get; set; }

    // 武器
    public string? Category { get; set; }
    public bool Ranged { get; set; }
    public string? Damage { get; set; }
    public string? DamageType { get; set; }
    public string? VersatileDamage { get; set; }
    public List<string> Properties { get; set; } = [];

    /// <summary>当たったときに足す効果(フレイム・タンの 2d6 の火など)。</summary>
    public List<EffectPart> OnHit { get; set; } = [];

    // 鎧と盾
    public ArmorType? ArmorType { get; set; }
    public int ArmorClass { get; set; }

    /// <summary>敏捷力の修正値を AC に足せる上限(中装鎧は 2、重装鎧は 0)。null なら上限なし。</summary>
    public int? DexCap { get; set; }

    public int StrengthRequired { get; set; }
    public bool StealthDisadvantage { get; set; }

    // 装身具
    /// <summary>付ける場所: ring / neck / cloak / head / hands / feet / waist。</summary>
    public string? Slot { get; set; }

    /// <summary>
    /// 装備しているあいだの効果(modifier の ac / save / attack / damage / stealth、resist、vulnerable、immune_condition、set_ability)。
    /// </summary>
    public List<EffectPart> Passive { get; set; } = [];

    // バックパック
    public int Slots { get; set; }

    // 使うもの(水薬・巻物・ワンドなど)
    /// <summary>使ったときの効果。</summary>
    public List<EffectPart> Use { get; set; } = [];

    /// <summary>使う対象: self / ally / allies / enemy / group。use_spell なら呪文の対象に従う。</summary>
    public string? UseTarget { get; set; }

    /// <summary>使うと唱える呪文(巻物・ワンド・杖)と、そのスロットのレベル。</summary>
    public string? UseSpell { get; set; }
    public int UseSlot { get; set; }

    /// <summary>使える回数(ワンドなど。大休憩で戻る)。null なら1回で無くなる(消耗品)か、使えない。</summary>
    public int? Charges { get; set; }

    /// <summary>1 つの枠に重ねて持てる数(消耗品)。</summary>
    public int Stack { get; set; } = 1;

    /// <summary>弾の種類(arrow / bolt / bullet / needle)。弾なら、その種類。弓などなら、使う弾の種類(無ければ名前から決める)。</summary>
    public string? AmmoType { get; set; }

    /// <summary>弾の、魔法でない上乗せ(鉄やミスリルの矢じり)。命中とダメージ。魔法の +N は Bonus。</summary>
    public int AmmoHit { get; set; }
    public int AmmoDamage { get; set; }

    /// <summary>店で買うとき・宝で出るときの本数(弾)。値段は1本あたり。</summary>
    public int Bundle { get; set; } = 1;

    /// <summary>この飛び道具が使う弾の種類(弾を使わない武器は null)。</summary>
    public string? UsesAmmo => !Has("ammunition") ? null : AmmoType ?? (Base ?? Id) switch
    {
        var id when id.Contains("crossbow") => "bolt",
        var id when id.Contains("bow") => "arrow",
        var id when id.Contains("sling") => "bullet",
        var id when id.Contains("blowgun") => "needle",
        _ => null,
    };

    public string? Note { get; set; }

    /// <summary>
    /// 宝箱の等級の抽選では出ず、落とし物の表(drops.json)から決まった品として出るだけのもの(固有のレア品)。
    /// </summary>
    public bool DropOnly { get; set; }

    /// <summary>出たときにランダムに付く追加効果の数(固有のレア品。核の効果は passive と on_hit に書く)。</summary>
    public int RandomAffixes { get; set; }

    public bool Has(string property) => Properties.Contains(property);

    public bool IsEquippable => Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield or ItemKind.Backpack or ItemKind.Accessory or ItemKind.Ammo;
    public bool IsUsable => Use.Count > 0 || UseSpell != null;
    public bool IsMagic => Rarity != "mundane";

    /// <summary>使うと無くなるか(回数の無い、使えるもの)。</summary>
    public bool ConsumedOnUse => IsUsable && Charges == null;
}

/// <summary>持っているアイテム1つ(重ねて持てるものは Count が 2 以上)。</summary>
public sealed class ItemInstance
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; } = 1;

    /// <summary>正体が分かっているか(未鑑定なら false。魔法のアイテムは、拾ったときは分からない)。</summary>
    public bool Identified { get; set; } = true;

    /// <summary>呪いが分かっているか(付けた、呪いを解いた)。鑑定しただけでは分からない。</summary>
    public bool CurseKnown { get; set; }

    /// <summary>
    /// 見える姿。呪いの品で、呪いがまだ分からないなら、見せかけの品(名前・説明・値段はこちらで出す)。それ以外は自分。
    /// </summary>
    public ItemInstance Apparent(IReadOnlyDictionary<string, ItemDef> defs) =>
        !CurseKnown && defs.TryGetValue(ItemId, out var def) && def.Cursed && def.LooksLike is { } look && defs.ContainsKey(look.Id)
            ? new ItemInstance { ItemId = look.Id, Count = Count, Identified = Identified, Bonus = look.Bonus, Attuned = Attuned, CurseKnown = true }
            : this;

    /// <summary>この1つだけの上乗せ(宝で出た +1 のロングソードなど)。ItemDef.Bonus と足す。</summary>
    public int Bonus { get; set; }

    /// <summary>残りの回数(ワンドなど)。</summary>
    public int? Charges { get; set; }

    /// <summary>同調しているか。</summary>
    public bool Attuned { get; set; }

    /// <summary>
    /// 強化値 +7 から付く属性(ダメージの種類)。武器は当たると (強化値 − 5) / 2 個の d6 の、この種類のダメージを足し、
    /// 鎧と盾はこの種類に抵抗する。
    /// </summary>
    public string? Element { get; set; }

    /// <summary>ランダムに付いた追加効果(affixes.json)。鑑定するまで見えない。</summary>
    public List<AffixRoll> Affixes { get; set; } = [];

    /// <summary>追加効果の、装備しているあいだの効果。</summary>
    public IEnumerable<Effects.EffectPart> AffixPassive(Characters.GameData data) =>
        Affixes.Select(a => a.Get(data)).OfType<AffixTier>().SelectMany(t => t.Passive);

    /// <summary>追加効果の、当たったときの効果。</summary>
    public IEnumerable<Effects.EffectPart> AffixOnHit(Characters.GameData data) =>
        Affixes.Select(a => a.Get(data)).OfType<AffixTier>().SelectMany(t => t.OnHit);

    public const int ElementFrom = 7;

    /// <summary>武器の属性の追加ダメージのダイス(+7 で 1d6、+20 で 7d6)。無ければ null。</summary>
    public string? ElementDice(ItemDef def) => Element != null && TotalBonus(def) >= ElementFrom ? $"{(TotalBonus(def) - 5) / 2}d6" : null;

    public static ItemInstance Of(string id, int count = 1) => new() { ItemId = id, Count = count };

    /// <summary>同じ品を1つ(重ねたものから1本取り出すとき)。</summary>
    public ItemInstance SplitOne() => new()
    {
        ItemId = ItemId, Count = 1, Identified = Identified, Bonus = Bonus, Charges = Charges, Element = Element, CurseKnown = CurseKnown,
        Affixes = [.. Affixes],
    };

    /// <summary>重ねて持てるか(同じ種類で、鑑定と上乗せと回数が同じ)。</summary>
    public bool SameKind(ItemInstance o) => ItemId == o.ItemId && Identified == o.Identified && Bonus == o.Bonus && Charges == o.Charges && Element == o.Element
        && Affixes.Count == o.Affixes.Count && Affixes.Zip(o.Affixes).All(x => x.First.Id == x.Second.Id && x.First.Tier == x.Second.Tier);

    public int TotalBonus(ItemDef def) => def.Bonus + Bonus;

    /// <summary>強化値の上限(インフレのため 5版の +3 より大きい。ユーザーの指定)。</summary>
    public const int MaxBonus = 20;

    /// <summary>
    /// 強化値のうち、命中と AC に効く分。+1〜+3 は全部、+4〜+6 は半分、+7 からは増えない(d20 の判定の意味を残すため)。
    /// ダメージには強化値を全部足す。負の値(呪いの品)はそのまま効く。
    /// </summary>
    public static int HitAcBonus(int bonus) => bonus <= 3 ? bonus : 3 + (Math.Min(bonus, 6) - 3) / 2;

    public int HitAcBonus(ItemDef def) => HitAcBonus(TotalBonus(def));
}

/// <summary>装備している場所。Ranged は遠隔の武器(弓・クロスボウ・投げる武器)、Ammo は特別な弾の枠。</summary>
public enum EquipSlot { MainHand, OffHand, Ranged, Ammo, Armor, Backpack, Ring1, Ring2, Neck, Cloak, Head, Hands, Feet, Waist }

/// <summary>キャラクター1人の装備と持ち物。持てる数(枠)は、装備しているバックパックで決まる。</summary>
public sealed class Inventory
{
    public const int MaxAttuned = 3;

    public Dictionary<EquipSlot, ItemInstance> Equipped { get; set; } = [];

    /// <summary>袋の中の持ち物(装備していないもの)。</summary>
    public List<ItemInstance> Items { get; set; } = [];

    public ItemInstance? this[EquipSlot slot] => Equipped.TryGetValue(slot, out var i) ? i : null;

    /// <summary>袋の枠の数。袋を持っていなければ 0。</summary>
    public int Capacity(IReadOnlyDictionary<string, ItemDef> defs) =>
        this[EquipSlot.Backpack] is { } bag && defs.TryGetValue(bag.ItemId, out var d) ? d.Slots : 0;

    public int FreeSlots(IReadOnlyDictionary<string, ItemDef> defs) => Capacity(defs) - Items.Count;

    public int AttunedCount => Equipped.Values.Count(i => i.Attuned);

    /// <summary>
    /// 効いている装備(同調が要るものは同調しているときだけ)。持っているあいだの効果や、当たったときの効果を集めるのに使う。
    /// </summary>
    public IEnumerable<(EquipSlot Slot, ItemInstance Item, ItemDef Def)> Active(IReadOnlyDictionary<string, ItemDef> defs)
    {
        foreach (var (slot, item) in Equipped)
        {
            if (!defs.TryGetValue(item.ItemId, out var def)) continue;
            if (def.Attunement && !item.Attuned) continue;
            yield return (slot, item, def);
        }
    }

    /// <summary>袋の中の、重ねて持てる同じ品をまとめる(重ねられるようになる前のセーブの品など)。</summary>
    public void MergeStacks(IReadOnlyDictionary<string, ItemDef> defs)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var it = Items[i];
            if (!defs.TryGetValue(it.ItemId, out var def) || def.Stack <= 1) continue;
            var into = Items.Take(i).FirstOrDefault(x => x.SameKind(it) && x.Count + it.Count <= def.Stack);
            if (into == null) continue;
            into.Count += it.Count;
            Items.RemoveAt(i);
        }
    }

    /// <summary>袋に入れる。重ねて持てるものは同じものに重ねる。入らなければ false。</summary>
    public bool TryAdd(ItemInstance item, IReadOnlyDictionary<string, ItemDef> defs)
    {
        if (!defs.TryGetValue(item.ItemId, out var def)) return false;
        if (def.Stack > 1)
        {
            var same = Items.FirstOrDefault(i => i.SameKind(item) && i.Count < def.Stack);
            if (same != null && same.Count + item.Count <= def.Stack)
            {
                same.Count += item.Count;
                return true;
            }
        }
        if (FreeSlots(defs) <= 0) return false;
        Items.Add(item);
        return true;
    }

    /// <summary>
    /// 袋の中のものを装備する。今装備しているものは袋に戻す(呪われていれば外せない)。
    /// バックパックの付け替えは、入っている数が新しい袋の枠より多いとできない。
    /// 同調が要るものは、空きがあれば同調する(3つまで)。
    /// </summary>
    public EquipResult Equip(int index, EquipSlot slot, IReadOnlyDictionary<string, ItemDef> defs)
    {
        if (index < 0 || index >= Items.Count) return EquipResult.NotFound;
        var item = Items[index];
        if (!defs.TryGetValue(item.ItemId, out var def) || !Fits(def, slot)) return EquipResult.WrongSlot;
        if (slot == EquipSlot.Backpack && Items.Count > def.Slots) return EquipResult.BagTooSmall;
        if (slot == EquipSlot.OffHand && this[EquipSlot.MainHand] is { } main && defs.TryGetValue(main.ItemId, out var md) && md.Has("two_handed"))
            return EquipResult.HandsFull;
        if (this[slot] is { } current && IsStuck(current, defs)) return EquipResult.Cursed;
        if (slot == EquipSlot.MainHand && def.Has("two_handed") && this[EquipSlot.OffHand] is { } offHand && IsStuck(offHand, defs))
            return EquipResult.Cursed;
        // 重ねて持っている品(投げる武器)を手に持つときは1本だけ。束で付けるのは遠隔と弾の枠だけ
        if (item.Count > 1 && slot is not (EquipSlot.Ranged or EquipSlot.Ammo))
        {
            if (FreeSlots(defs) <= 0 && this[slot] != null) return EquipResult.BagFull;   // 今の装備を戻す場所が無い
            item.Count--;
            item = item.SplitOne();
        }
        else Items.RemoveAt(index);
        if (this[slot] is { } old)
        {
            old.Attuned = false;
            if (!TryAdd(old, defs)) Items.Add(old);   // 同じ品が袋にあれば重ねる
        }
        Equipped[slot] = item;
        if (def.Attunement && !item.Attuned && AttunedCount < MaxAttuned) item.Attuned = true;
        if (slot == EquipSlot.MainHand && def.Has("two_handed") && this[EquipSlot.OffHand] is { } off)
        {
            Equipped.Remove(EquipSlot.OffHand);
            off.Attuned = false;
            Items.Add(off);
        }
        if (def.Cursed) item.Identified = item.CurseKnown = true;   // 付けてみて、呪いと分かる
        return EquipResult.Ok;
    }

    /// <summary>呪われていて外せないか。</summary>
    public static bool IsStuck(ItemInstance item, IReadOnlyDictionary<string, ItemDef> defs) =>
        defs.TryGetValue(item.ItemId, out var d) && d.Cursed;

    /// <summary>外して袋に入れる。袋に空きが無いと外せない。バックパックと、呪われたものは外せない。</summary>
    public EquipResult Unequip(EquipSlot slot, IReadOnlyDictionary<string, ItemDef> defs)
    {
        if (this[slot] is not { } item) return EquipResult.NotFound;
        if (slot == EquipSlot.Backpack) return EquipResult.WrongSlot;
        if (IsStuck(item, defs)) return EquipResult.Cursed;
        item.Attuned = false;
        if (!TryAdd(item, defs)) return EquipResult.BagFull;   // 同じ品が袋にあれば重ねる
        Equipped.Remove(slot);
        return EquipResult.Ok;
    }

    /// <summary>呪いを解く: 呪われた装備を全部外して袋に入れる(袋に入らなければ、その場に捨てる)。外した数を返す。</summary>
    public int BreakCurses(IReadOnlyDictionary<string, ItemDef> defs)
    {
        int n = 0;
        foreach (var slot in Equipped.Where(kv => IsStuck(kv.Value, defs)).Select(kv => kv.Key).ToList())
        {
            var item = Equipped[slot];
            Equipped.Remove(slot);
            item.Attuned = false;
            item.Identified = item.CurseKnown = true;
            if (FreeSlots(defs) > 0) Items.Add(item);
            n++;
        }
        return n;
    }

    /// <summary>そのアイテムを付けられる場所(指輪は空いている方)。</summary>
    public EquipSlot? SlotFor(ItemDef def) => def.Kind switch
    {
        ItemKind.Weapon => def.Ranged ? EquipSlot.Ranged : EquipSlot.MainHand,
        ItemKind.Shield => EquipSlot.OffHand,
        ItemKind.Ammo => EquipSlot.Ammo,
        ItemKind.Armor => EquipSlot.Armor,
        ItemKind.Backpack => EquipSlot.Backpack,
        ItemKind.Accessory => def.Slot switch
        {
            "ring" => this[EquipSlot.Ring1] == null ? EquipSlot.Ring1 : EquipSlot.Ring2,
            "neck" => EquipSlot.Neck,
            "cloak" => EquipSlot.Cloak,
            "head" => EquipSlot.Head,
            "hands" => EquipSlot.Hands,
            "feet" => EquipSlot.Feet,
            "waist" => EquipSlot.Waist,
            _ => null,
        },
        _ => null,
    };

    public static bool Fits(ItemDef def, EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => def.Kind == ItemKind.Weapon,
        EquipSlot.OffHand => def.Kind == ItemKind.Shield || (def.Kind == ItemKind.Weapon && def.Has("light")),
        EquipSlot.Ranged => def.Kind == ItemKind.Weapon && (def.Ranged || def.Has("thrown")),
        EquipSlot.Ammo => def.Kind == ItemKind.Ammo,
        EquipSlot.Armor => def.Kind == ItemKind.Armor,
        EquipSlot.Backpack => def.Kind == ItemKind.Backpack,
        EquipSlot.Ring1 or EquipSlot.Ring2 => def.Kind == ItemKind.Accessory && def.Slot == "ring",
        EquipSlot.Neck => def.Kind == ItemKind.Accessory && def.Slot == "neck",
        EquipSlot.Cloak => def.Kind == ItemKind.Accessory && def.Slot == "cloak",
        EquipSlot.Head => def.Kind == ItemKind.Accessory && def.Slot == "head",
        EquipSlot.Hands => def.Kind == ItemKind.Accessory && def.Slot == "hands",
        EquipSlot.Feet => def.Kind == ItemKind.Accessory && def.Slot == "feet",
        EquipSlot.Waist => def.Kind == ItemKind.Accessory && def.Slot == "waist",
        _ => false,
    };
}

public enum EquipResult { Ok, NotFound, WrongSlot, BagTooSmall, BagFull, HandsFull, Cursed }

/// <summary>呪いの品の見せかけ(id の品に、bonus の上乗せで見える)。</summary>
public sealed record LooksLike(string Id, int Bonus = 0);
