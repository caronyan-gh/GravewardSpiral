using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>
/// 宝の設定(パックの treasure.json)。SRD には宝の表が無いので、目安のレベルと出どころごとの等級の出やすさを自分で決める。
/// </summary>
public sealed class TreasureSettings
{
    public const string Collection = "treasure";
    public string Id { get; set; } = "default";

    /// <summary>勝ったあとに宝箱が出る確率。</summary>
    public double ChestChance { get; set; } = 0.45;

    /// <summary>宝箱に、ほかの品とは別に入ることがある素材(深さが min_depth 以上で、chance の確率)。</summary>
    public List<MaterialRoll> Materials { get; set; } = [];

    public sealed class MaterialRoll
    {
        public string Item { get; set; } = "";
        public double MinDepth { get; set; }
        public double Chance { get; set; }
    }

    /// <summary>部屋にいた敵を倒したときの、宝箱の出る確率(うろつく敵より出やすい)。</summary>
    public double RoomChestChance { get; set; } = 0.8;

    /// <summary>宝箱に罠がある確率(最初の階と最下層。あいだはならす)。</summary>
    public double TrapChanceFirst { get; set; } = 0.3;
    public double TrapChanceLast { get; set; } = 0.85;

    /// <summary>宝箱の金(目安のレベル × この値 × 0.5〜1.5)。</summary>
    public double GoldPerLevel { get; set; } = 20;

    /// <summary>宝箱のアイテムの数。</summary>
    public int MinItems { get; set; } = 1;
    public int MaxItems { get; set; } = 2;

    /// <summary>
    /// うろつく敵の宝箱の、目安のレベルごとの等級の出やすさ(level の小さい順。目安のレベル以下で一番高い行を使う)。
    /// 深い所ほど +N が上がり、名前つきの品は狙って取りに行く場所(部屋・ボス)から出る、ダンジョンクロウラーの段階にする。
    /// </summary>
    public List<RarityRow> Rarity { get; set; } = [];

    /// <summary>部屋・ねぐら・名のある敵・イベントの宝(無ければ rarity と同じ)。</summary>
    public List<RarityRow> RoomRarity { get; set; } = [];

    /// <summary>ボスの宝箱と、ボスの戦いの落とし物(無ければ room_rarity と同じ)。</summary>
    public List<RarityRow> BossRarity { get; set; } = [];

    /// <summary>魔法の等級が出たとき、名前つきの品の代わりに +N の武器・鎧・盾にする割合。</summary>
    public double BonusChance { get; set; } = 0.5;

    /// <summary>魔法の品が呪いの品に変わる割合。</summary>
    public double CursedChance { get; set; } = 0.08;

    /// <summary>目安のレベルがこれを超えると、+4 より上の強化値が出始める(超えた分の半分まで上乗せ)。</summary>
    public double BonusFromLevel { get; set; } = 13;

    /// <summary>
    /// 宝の武器・鎧・盾・装飾品に付く追加効果の数の重み(等級ごとに、数 → 重み)。アンコモンはマジック(1〜2つ)、
    /// レア以上はレアの品(4・5・6つを 8:3:1。Path of Exile と同じ)。
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> AffixCount { get; set; } = new()
    {
        ["uncommon"] = new() { ["1"] = 1, ["2"] = 1 },
        ["rare"] = Rare(), ["very_rare"] = Rare(), ["legendary"] = Rare(), ["artifact"] = Rare(),
    };

    private static Dictionary<string, double> Rare() => new() { ["4"] = 8, ["5"] = 3, ["6"] = 1 };

    /// <summary>追加効果の段の幅(目安のレベルで出る一番上の段から、下へこの段数の中で均等に選ぶ)。</summary>
    public int AffixTierWindow { get; set; } = 5;

    public static TreasureSettings Load(ContentLibrary content) =>
        content.Find(Collection, "default")?.As<TreasureSettings>() ?? new TreasureSettings();

    public sealed class RarityRow
    {
        public double Level { get; set; }
        public Dictionary<string, double> Weights { get; set; } = [];

        /// <summary>名前つきの品が出る一番上の等級(これより上の等級が出たら +N の武器・鎧・盾にする)。無ければ上限なし。</summary>
        public string? Named { get; set; }
    }
}

/// <summary>宝の出どころ(等級の表を選ぶ)。</summary>
public enum TreasureSource
{
    /// <summary>うろつく敵。</summary>
    Wander,
    /// <summary>部屋・ねぐら・名のある敵・イベント。</summary>
    Room,
    /// <summary>ボス。</summary>
    Boss,
}

/// <summary>宝を決める(宝箱の中身、敵の落とし物、ウィッシュのアイテム)。</summary>
public static class Treasure
{
    public static readonly string[] Rarities = ["mundane", "common", "uncommon", "rare", "very_rare", "legendary", "artifact"];

    /// <summary>
    /// その等級で出る +N(5版の表: 武器と盾は +1 が uncommon・+2 が rare・+3 が very_rare、
    /// 鎧は +1 が rare・+2 が very_rare・+3 が legendary)。出ない組み合わせは 0。
    /// </summary>
    public static int BonusFor(string rarity, ItemKind kind) => kind == ItemKind.Armor
        ? rarity switch { "rare" => 1, "very_rare" => 2, "legendary" => 3, _ => 0 }
        : rarity switch { "uncommon" => 1, "rare" => 2, "very_rare" => 3, _ => 0 };

    /// <summary>+N の品の等級(BonusFor の逆)。</summary>
    /// <summary>
    /// その品の等級(見せる・鑑定の料金に使う)。+N の品は強化値から、追加効果だけの装飾品はマジックならアンコモン・レアならレア、
    /// それ以外は品の定義の等級。
    /// </summary>
    public static string GradeOf(ItemDef def, ItemInstance item) =>
        def.IsMagic ? def.Rarity : item.Bonus > 0 ? RarityOf(item.Bonus, def.Kind) : item.Affixes.Count > 0 ? (item.Rare ? "rare" : "uncommon") : def.Rarity;

    public static string RarityOf(int bonus, ItemKind kind) => kind == ItemKind.Armor
        ? bonus switch { <= 0 => "mundane", 1 => "rare", 2 => "very_rare", _ => "legendary" }
        : bonus switch { <= 0 => "mundane", 1 => "uncommon", 2 => "rare", _ => "very_rare" };

    public static string PickRarity(TreasureSettings s, double level, IRandom rng, TreasureSource source = TreasureSource.Wander)
    {
        var weights = Weights(s, level, source);
        if (weights.Count == 0) return "mundane";
        double total = weights.Values.Sum(), r = rng.NextDouble() * total;
        foreach (var (k, w) in weights.OrderBy(kv => Array.IndexOf(Rarities, kv.Key)))
        {
            if ((r -= w) < 0) return k;
        }
        return weights.Keys.Last();
    }

    /// <summary>その出どころと目安のレベルで使う行(目安のレベル以下で一番高い行。無ければ一番低い行)。</summary>
    public static TreasureSettings.RarityRow? Row(TreasureSettings s, double level, TreasureSource source = TreasureSource.Wander)
    {
        var rows = source switch
        {
            TreasureSource.Boss when s.BossRarity.Count > 0 => s.BossRarity,
            TreasureSource.Boss or TreasureSource.Room when s.RoomRarity.Count > 0 => s.RoomRarity,
            _ => s.Rarity,
        };
        return rows.OrderBy(r => r.Level).LastOrDefault(r => r.Level <= level + 1e-9) ?? rows.OrderBy(r => r.Level).FirstOrDefault();
    }

    /// <summary>その出どころと目安のレベルでの、等級の重み。</summary>
    public static Dictionary<string, double> Weights(TreasureSettings s, double level, TreasureSource source = TreasureSource.Wander) =>
        new(Row(s, level, source)?.Weights.Where(kv => kv.Value > 0) ?? []);

    /// <summary>その行で出る一番上の等級まで下げる(敵の落とし物の表の等級が、その階には強すぎるとき)。</summary>
    public static string Clamp(TreasureSettings s, string rarity, double level, TreasureSource source = TreasureSource.Wander)
    {
        var weights = Weights(s, level, source);
        if (weights.Count == 0) return rarity;
        int top = weights.Keys.Max(k => Array.IndexOf(Rarities, k));
        return Array.IndexOf(Rarities, rarity) > top ? Rarities[top] : rarity;
    }

    /// <summary>
    /// 強化値の上乗せ。目安のレベルが bonus_from_level を超えた分の半分まで、+N にさらに足す(最大 +20)。
    /// 1周目の最下層(目安 L13)までは 5版どおり +3 まで。
    /// </summary>
    public static int ExtraBonus(TreasureSettings s, double level, IRandom rng)
    {
        int room = (int)((level - s.BonusFromLevel) / 2);
        return room <= 0 ? 0 : rng.Next(0, room + 1);
    }

    private static readonly string[] Elements = ["fire", "cold", "lightning", "acid", "thunder", "radiant", "necrotic", "force"];

    /// <summary>
    /// その等級のアイテムを1つ作る。魔法の品は未鑑定。level は宝の目安のレベル(+4 より上の強化値に使う)。
    /// named は名前つきの品が出る一番上の等級で、それより上の等級なら +N の武器・鎧・盾にする(作れなければ named の等級の品)。
    /// </summary>
    public static ItemInstance Roll(GameData data, TreasureSettings s, string rarity, IRandom rng, double level = 1, string? named = null)
    {
        var all = data.Items.Values.Where(i => !i.NoRandom).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();   // 名指しでだけ出る品は、おまかせでは出さない
        T Pick<T>(List<T> list) => list[rng.Next(0, list.Count)];
        if (rarity == "mundane")
        {
            var mundane = all.Where(i => !i.IsMagic && i.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield or ItemKind.Consumable).ToList();
            return ItemInstance.Of(Pick(mundane).Id);
        }
        bool overNamed = named != null && Array.IndexOf(Rarities, rarity) > Array.IndexOf(Rarities, named);
        if (overNamed || rng.NextDouble() < s.BonusChance)
        {
            // 元の品: ふつうの武器・鎧・盾(+N が付く等級のもの)と、ふつうの装飾品(+N は無く、追加効果だけ)
            int count = Loot.RollAffixCount(s, rarity, rng);
            var bases = all.Where(i => !i.IsMagic && i.Base == null
                && (i.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield && BonusFor(rarity, i.Kind) > 0
                    || i.Kind == ItemKind.Accessory && count > 0 && data.Affixes.Values.Any(a => a.Fits(i) && a.TierFor(level) >= 0 && (!a.RareOnly || Loot.IsRareGrade(rarity))))).ToList();
            if (bases.Count > 0)
            {
                var b = Pick(bases);
                int bonus = b.Kind == ItemKind.Accessory ? 0 : Math.Min(ItemInstance.MaxBonus, BonusFor(rarity, b.Kind) + ExtraBonus(s, level, rng));
                var made = new ItemInstance
                {
                    ItemId = b.Id, Bonus = bonus, Identified = false, Rare = Loot.IsRareGrade(rarity),
                    Element = bonus >= ItemInstance.ElementFrom ? Elements[rng.Next(0, Elements.Length)] : null,
                };
                // 追加効果(等級で数が決まる。マジックは接頭・接尾1つずつ、レアは3つずつまで。同じ名前の品でも違うものが出る)
                if (count > 0) Loot.AddAffixes(data, made, b, count, level, rng, made.Rare ? 3 : 1, s.AffixTierWindow);
                if (made.Affixes.Count == 0) made.Rare = false;
                return made;
            }
            if (overNamed) rarity = named!;
        }
        if (rng.NextDouble() < s.CursedChance)
        {
            // 呪いの品も、出た等級より上のものにはしない(浅い階にデーモン・アーマーを出さない)
            var cursed = all.Where(i => i.Cursed && Array.IndexOf(Rarities, i.Rarity) <= Array.IndexOf(Rarities, rarity)).ToList();
            if (cursed.Count > 0) return Make(Pick(cursed));
        }
        var pool = all.Where(i => i.Rarity == rarity && !i.Cursed && i.MinLevel <= level + 1e-9).ToList();
        if (pool.Count == 0) pool = all.Where(i => i.IsMagic && !i.Cursed).ToList();
        if (pool.Count == 0) return ItemInstance.Of(Pick(all).Id);
        return Make(Pick(pool));
    }

    private static ItemInstance Make(ItemDef def) => new() { ItemId = def.Id, Identified = !def.IsMagic, Charges = def.Charges, Count = Math.Max(1, def.Bundle) };

    /// <summary>目安のレベルと出どころに合った宝を count 個。</summary>
    public static List<ItemInstance> RollMany(GameData data, TreasureSettings s, double level, int count, IRandom rng, TreasureSource source = TreasureSource.Wander)
    {
        string? named = Row(s, level, source)?.Named;
        return Enumerable.Range(0, count).Select(_ => Roll(data, s, PickRarity(s, level, rng, source), rng, level, named)).ToList();
    }

    /// <summary>値段(+N は等級に合わせて上乗せ。追加効果はその段階の値段を足す)。</summary>
    public static long Price(ItemDef def, ItemInstance item)
    {
        double p = def.Price + item.Affixes.Sum(a => a.Price);
        if (item.Bonus > 0)
            p += RarityOf(item.Bonus, def.Kind) switch { "uncommon" => 500, "rare" => 4000, "very_rare" => 20000, _ => 60000 }
                 + Math.Max(0, item.Bonus - 3) * 25000;   // +4 から上は 1 ごとに高くなる
        return Math.Max(1, (long)Math.Round(p));
    }
}
