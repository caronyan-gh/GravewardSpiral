using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>
/// 宝の設定(パックの treasure.json)。SRD には宝の表が無いので、深さ(割合)ごとの等級の出やすさを自分で決める。
/// </summary>
public sealed class TreasureSettings
{
    public const string Collection = "treasure";
    public string Id { get; set; } = "default";

    /// <summary>勝ったあとに宝箱が出る確率。</summary>
    public double ChestChance { get; set; } = 0.35;

    /// <summary>宝箱に、ほかの品とは別に入ることがある素材(深さが min_depth 以上で、chance の確率)。</summary>
    public List<MaterialRoll> Materials { get; set; } = [];

    public sealed class MaterialRoll
    {
        public string Item { get; set; } = "";
        public double MinDepth { get; set; }
        public double Chance { get; set; }
    }

    /// <summary>部屋にいた敵を倒したときの、宝箱の出る確率(うろつく敵より出やすい)。</summary>
    public double RoomChestChance { get; set; } = 0.7;

    /// <summary>宝箱に罠がある確率(最初の階と最下層。あいだはならす)。</summary>
    public double TrapChanceFirst { get; set; } = 0.3;
    public double TrapChanceLast { get; set; } = 0.85;

    /// <summary>宝箱の金(目安のレベル × この値 × 0.5〜1.5)。</summary>
    public double GoldPerLevel { get; set; } = 20;

    /// <summary>宝箱のアイテムの数。</summary>
    public int MinItems { get; set; } = 1;
    public int MaxItems { get; set; } = 2;

    /// <summary>深さごとの等級の出やすさ(depth の小さい順。その深さ以上で一番近いものを使う)。</summary>
    public List<RarityRow> Rarity { get; set; } = [];

    /// <summary>
    /// 目安のレベルで足す等級の重み(5版の宝の表の段階: L11 から very_rare、L17 から legendary が出始める)。
    /// 目安のレベル以下で一番高い行を、深さの重みに足す。強い世界の深い階で、物理に抵抗する敵に効く武器が手に入るように。
    /// </summary>
    public List<LevelRarityRow> LevelRarity { get; set; } = [];

    /// <summary>魔法の等級が出たとき、名前つきの品の代わりに +N の武器・鎧・盾にする割合。</summary>
    public double BonusChance { get; set; } = 0.5;

    /// <summary>魔法の品が呪いの品に変わる割合。</summary>
    public double CursedChance { get; set; } = 0.08;

    /// <summary>目安のレベルがこれを超えると、+4 より上の強化値が出始める(超えた分の半分まで上乗せ)。</summary>
    public double BonusFromLevel { get; set; } = 13;

    /// <summary>+N の武器・鎧・盾に付く追加効果の数(等級ごとの最小と最大)。</summary>
    public Dictionary<string, List<int>> AffixCount { get; set; } = new()
    {
        ["uncommon"] = [0, 1], ["rare"] = [0, 2], ["very_rare"] = [1, 2], ["legendary"] = [1, 3], ["artifact"] = [2, 3],
    };

    public static TreasureSettings Load(ContentLibrary content) =>
        content.Find(Collection, "default")?.As<TreasureSettings>() ?? new TreasureSettings();

    public sealed class RarityRow
    {
        public double Depth { get; set; }
        public Dictionary<string, double> Weights { get; set; } = [];
    }

    public sealed class LevelRarityRow
    {
        public double Level { get; set; }
        public Dictionary<string, double> Weights { get; set; } = [];
    }
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
    public static string RarityOf(int bonus, ItemKind kind) => kind == ItemKind.Armor
        ? bonus switch { <= 0 => "mundane", 1 => "rare", 2 => "very_rare", _ => "legendary" }
        : bonus switch { <= 0 => "mundane", 1 => "uncommon", 2 => "rare", _ => "very_rare" };

    public static string PickRarity(TreasureSettings s, double depth, IRandom rng, double level = 1)
    {
        var weights = Weights(s, depth, level);
        if (weights.Count == 0) return "mundane";
        double total = weights.Values.Sum(), r = rng.NextDouble() * total;
        foreach (var (k, w) in weights.OrderBy(kv => Array.IndexOf(Rarities, kv.Key)))
        {
            if ((r -= w) < 0) return k;
        }
        return weights.Keys.Last();
    }

    /// <summary>その深さと目安のレベルでの、等級の重み(深さの行に、レベルの行を足す)。</summary>
    public static Dictionary<string, double> Weights(TreasureSettings s, double depth, double level)
    {
        var row = s.Rarity.OrderBy(r => r.Depth).LastOrDefault(r => r.Depth <= depth + 1e-9) ?? s.Rarity.FirstOrDefault();
        var weights = new Dictionary<string, double>(row?.Weights ?? []);
        if (s.LevelRarity.OrderBy(r => r.Level).LastOrDefault(r => r.Level <= level + 1e-9) is { } add)
            foreach (var (k, w) in add.Weights) weights[k] = weights.GetValueOrDefault(k) + w;
        return weights;
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

    /// <summary>その等級のアイテムを1つ作る。魔法の品は未鑑定。level は宝の目安のレベル(+4 より上の強化値に使う)。</summary>
    public static ItemInstance Roll(GameData data, TreasureSettings s, string rarity, IRandom rng, double level = 1)
    {
        var all = data.Items.Values.Where(i => !i.NoRandom).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();   // 名指しでだけ出る品は、おまかせでは出さない
        T Pick<T>(List<T> list) => list[rng.Next(0, list.Count)];
        if (rarity == "mundane")
        {
            var mundane = all.Where(i => !i.IsMagic && i.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield or ItemKind.Consumable).ToList();
            return ItemInstance.Of(Pick(mundane).Id);
        }
        if (rng.NextDouble() < s.BonusChance)
        {
            var bases = all.Where(i => !i.IsMagic && i.Base == null && i.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield
                && BonusFor(rarity, i.Kind) > 0).ToList();
            if (bases.Count > 0)
            {
                var b = Pick(bases);
                int bonus = Math.Min(ItemInstance.MaxBonus, BonusFor(rarity, b.Kind) + ExtraBonus(s, level, rng));
                var made = new ItemInstance
                {
                    ItemId = b.Id, Bonus = bonus, Identified = false,
                    Element = bonus >= ItemInstance.ElementFrom ? Elements[rng.Next(0, Elements.Length)] : null,
                };
                // 追加効果(等級で数が決まる。同じ名前の品でも違うものが出る)
                var (min, max) = Loot.AffixCount(s, rarity);
                if (max > 0) Loot.AddAffixes(data, made, b, rng.Next(min, max + 1), level, rng);
                return made;
            }
        }
        if (rng.NextDouble() < s.CursedChance)
        {
            var cursed = all.Where(i => i.Cursed).ToList();
            if (cursed.Count > 0) return Make(Pick(cursed));
        }
        var pool = all.Where(i => i.Rarity == rarity && !i.Cursed && i.MinLevel <= level + 1e-9).ToList();
        if (pool.Count == 0) pool = all.Where(i => i.IsMagic && !i.Cursed).ToList();
        if (pool.Count == 0) return ItemInstance.Of(Pick(all).Id);
        return Make(Pick(pool));
    }

    private static ItemInstance Make(ItemDef def) => new() { ItemId = def.Id, Identified = !def.IsMagic, Charges = def.Charges, Count = Math.Max(1, def.Bundle) };

    /// <summary>深さに合った宝を count 個。level は目安のレベル(+4 より上の強化値に使う)。</summary>
    public static List<ItemInstance> RollMany(GameData data, TreasureSettings s, double depth, int count, IRandom rng, double level = 1) =>
        Enumerable.Range(0, count).Select(_ => Roll(data, s, PickRarity(s, depth, rng, level), rng, level)).ToList();

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
