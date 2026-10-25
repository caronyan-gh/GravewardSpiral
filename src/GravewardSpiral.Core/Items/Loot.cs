using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>
/// 装備の追加効果(パックの affixes.json。5版からの改変)。宝の +N の武器・鎧・盾と、固有のレア品に、ランダムに付く。
/// 同じ名前の品でも追加効果が違うものが出る(掘りの楽しみ)。強さは段階(tiers)で持ち、宝の目安のレベルで上の段階が出る。
/// </summary>
public sealed class AffixDef
{
    public const string Collection = "affixes";

    public string Id { get; set; } = "";

    /// <summary>名前に付ける語(「燃える」「守りの」/ Flaming、of Warding)。</summary>
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>prefix(英語で名前の前)/ suffix(英語で「of ...」として後ろ)。日本語はどちらも前に付ける。</summary>
    public string Position { get; set; } = "prefix";

    /// <summary>付く品の種類: weapon / armor / shield。</summary>
    public List<string> Kinds { get; set; } = [];

    /// <summary>同じ group の追加効果は、1つの品に1つまで(属性のダメージを2つ重ねない、など)。</summary>
    public string? Group { get; set; }

    /// <summary>出やすさ。</summary>
    public double Weight { get; set; } = 10;

    /// <summary>強さの段階(min_level の小さい順)。宝の目安のレベルが min_level 以上の、一番上の段階が出る。</summary>
    public List<AffixTier> Tiers { get; set; } = [];

    public bool Fits(ItemKind kind) => Kinds.Contains(kind.ToString().ToLowerInvariant());

    /// <summary>その目安のレベルで出る段階(まだ出ないなら -1)。</summary>
    public int TierFor(double level)
    {
        int tier = -1;
        for (int i = 0; i < Tiers.Count; i++)
        {
            if (Tiers[i].MinLevel <= level + 1e-9) tier = i;
        }
        return tier;
    }
}

public sealed class AffixTier
{
    public int MinLevel { get; set; } = 1;

    /// <summary>装備しているあいだの効果(modifier、resist、immune_condition など。アイテムの passive と同じ)。</summary>
    public List<EffectPart> Passive { get; set; } = [];

    /// <summary>当たったときに足す効果(武器)。</summary>
    public List<EffectPart> OnHit { get; set; } = [];

    /// <summary>値段の上乗せ(金貨)。</summary>
    public double Price { get; set; }
}

/// <summary>品に付いた追加効果1つ(どの追加効果の、どの段階か)。</summary>
public sealed class AffixRoll
{
    public string Id { get; set; } = "";
    public int Tier { get; set; }

    /// <summary>値段の上乗せ(付いたときの段階の値段。データが後から変わっても、持っている品の値段は変わらない)。</summary>
    public double Price { get; set; }

    public AffixTier? Get(GameData data) =>
        data.Affixes.TryGetValue(Id, out var a) && Tier >= 0 && Tier < a.Tiers.Count ? a.Tiers[Tier] : null;
}

/// <summary>
/// 敵の落とし物の表(パックの drops.json)。決まった敵(monster)の表、種類と CR の表、CR だけの表の順に、
/// 一番細かく合うものを1つ使う。倒した敵1体ごとに chance で落とし、entries から重みで1つ選ぶ。
/// 落とし物は宝箱に入る(落とし物があれば宝箱が必ず出る)。
/// </summary>
public sealed class DropTable
{
    public const string Collection = "drops";

    public string Id { get; set; } = "";

    /// <summary>決まった敵の id。書けば type と CR は見ない。</summary>
    public string? Monster { get; set; }

    /// <summary>ボス(bosses.json の id)を倒したときの表。1つの戦いで1回だけ chance で落とす(敵1体ごとの表とは別に足す)。</summary>
    public string? Boss { get; set; }

    /// <summary>敵の種類(undead / dragon / humanoid など)。無ければどの種類にも合う。</summary>
    public string? Type { get; set; }

    public double CrMin { get; set; }
    public double CrMax { get; set; } = 99;

    /// <summary>1体が落とす確率。</summary>
    public double Chance { get; set; } = 0.05;

    /// <summary>ほかの表とは別に、合う敵1体ごとに振る(クランハウスの素材など)。</summary>
    public bool Extra { get; set; }

    public List<DropEntry> Entries { get; set; } = [];

    /// <summary>合うか。合えば細かさ(決まった敵 2、種類 1、CR だけ 0)、合わなければ -1。</summary>
    public int Match(MonsterDef m)
    {
        if (Boss != null || Extra) return -1;
        if (Monster != null) return Monster == m.Id ? 2 : -1;
        if (m.Cr < CrMin - 1e-9 || m.Cr > CrMax + 1e-9) return -1;
        if (Type != null) return Type == m.CreatureType ? 1 : -1;
        return 0;
    }
}

/// <summary>
/// 落とし物の味付け(倍率)。階のテーマ(floor_themes.json の drops)と、生成で決める階ごとの値(GameSession.FloorDrops)を掛け合わせて使う。
/// chance は落とす確率の倍率、weights は候補の重みの倍率(キーは品の id か等級)。書いていなければ ×1。
/// </summary>
public sealed class DropBoost
{
    public double Chance { get; set; } = 1;
    public Dictionary<string, double> Weights { get; set; } = [];

    public double WeightOf(DropEntry e) =>
        e.Item != null && Weights.TryGetValue(e.Item, out var w) ? w : e.Rarity != null && Weights.TryGetValue(e.Rarity, out var r) ? r : 1;

    /// <summary>2つを掛け合わせる(どちらかが null ならもう片方)。</summary>
    public static DropBoost? Combine(DropBoost? a, DropBoost? b)
    {
        if (a == null || b == null) return a ?? b;
        var weights = new Dictionary<string, double>(a.Weights);
        foreach (var (k, v) in b.Weights) weights[k] = weights.TryGetValue(k, out var w) ? w * v : v;
        return new DropBoost { Chance = a.Chance * b.Chance, Weights = weights };
    }
}

/// <summary>落とし物の候補1つ。item(決まった品。固有のレア品など)か rarity(その等級の宝)のどちらか。</summary>
public sealed class DropEntry
{
    public string? Item { get; set; }
    public string? Rarity { get; set; }
    public double Weight { get; set; } = 1;
}

/// <summary>追加効果と落とし物を決める。</summary>
public static class Loot
{
    /// <summary>等級ごとの追加効果の数(最小と最大)。treasure.json の affix_count で変えられる。</summary>
    public static (int Min, int Max) AffixCount(TreasureSettings s, string rarity) =>
        s.AffixCount.TryGetValue(rarity, out var mm) && mm.Count >= 2 ? (mm[0], mm[1]) : (0, 0);

    /// <summary>品に追加効果を count 個付ける(同じ追加効果・同じ group は重ねない。合うものが無ければ少なくなる)。</summary>
    public static void AddAffixes(GameData data, ItemInstance item, ItemDef def, int count, double level, IRandom rng)
    {
        for (int n = 0; n < count; n++)
        {
            var taken = item.Affixes.Select(a => data.Affixes.GetValueOrDefault(a.Id)).OfType<AffixDef>().ToList();
            // 品がもともと持つ属性のダメージ(獄炎の剣の火など)と同じ属性の追加効果は付けない
            var own = def.OnHit.Where(p => p.Type == EffectPart.Damage && p.Only == null).Select(p => p.DamageType).ToHashSet();
            var pool = data.Affixes.Values.OrderBy(a => a.Id, StringComparer.Ordinal)
                .Where(a => a.Fits(def.Kind) && a.TierFor(level) >= 0 && a.Weight > 0
                    && !taken.Any(t => t.Id == a.Id || (t.Group != null && t.Group == a.Group))
                    && !a.Tiers.SelectMany(t => t.OnHit).Any(p => p.Type == EffectPart.Damage && p.Only == null && own.Contains(p.DamageType)))
                .ToList();
            if (pool.Count == 0) return;
            double r = rng.NextDouble() * pool.Sum(a => a.Weight);
            var pick = pool[^1];
            foreach (var a in pool)
            {
                if ((r -= a.Weight) < 0)
                {
                    pick = a;
                    break;
                }
            }
            // 段階は目安のレベルで出る一番上か、1つ下(少しばらつかせる)
            int top = pick.TierFor(level);
            int tier = top > 0 && rng.NextDouble() < 0.35 ? top - 1 : top;
            item.Affixes.Add(new AffixRoll { Id = pick.Id, Tier = tier, Price = pick.Tiers[tier].Price });
        }
    }

    /// <summary>その敵に使う落とし物の表(無ければ null)。</summary>
    public static DropTable? TableFor(GameData data, MonsterDef m) =>
        data.Drops.Values.OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => (Table: t, Score: t.Match(m))).Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Table.CrMin).Select(x => x.Table).FirstOrDefault();

    /// <summary>
    /// 倒した敵の落とし物。level は宝の目安のレベル(追加効果と +N の強さに使う)。boost は階の味付け(無ければ ×1)。
    /// </summary>
    public static List<ItemInstance> RollDrops(GameData data, TreasureSettings s, IEnumerable<(MonsterDef Def, int Count)> defeated,
        double level, IRandom rng, string? boss = null, DropBoost? boost = null)
    {
        double chanceMult = boost?.Chance ?? 1;
        var drops = new List<ItemInstance>();
        // ボスの表(戦い1回につき1回)
        foreach (var table in data.Drops.Values.Where(t => boss != null && t.Boss == boss).OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            if (rng.NextDouble() < table.Chance * chanceMult && Pick(table.Entries, rng, boost) is { } e) Add(e);
        }
        foreach (var (def, count) in defeated)
        {
            if (TableFor(data, def) is not { Entries.Count: > 0 } table) continue;
            for (int i = 0; i < count; i++)
            {
                if (rng.NextDouble() >= table.Chance * chanceMult) continue;
                if (Pick(table.Entries, rng, boost) is { } entry) Add(entry);
            }
            // 素材の表(別に振る)
            foreach (var extra in data.Drops.Values.Where(t => t.Extra && (t.Type == null || t.Type == def.CreatureType)
                && def.Cr >= t.CrMin - 1e-9 && def.Cr <= t.CrMax + 1e-9).OrderBy(t => t.Id, StringComparer.Ordinal))
            {
                for (int i = 0; i < count; i++)
                    if (rng.NextDouble() < extra.Chance && Pick(extra.Entries, rng, null) is { } entry) Add(entry);
            }
        }
        return drops;

        void Add(DropEntry entry)
        {
            if (entry.Item != null && data.Items.TryGetValue(entry.Item, out var item)) drops.Add(Make(data, s, item, level, rng));
            else if (entry.Rarity != null) drops.Add(Treasure.Roll(data, s, entry.Rarity, rng, level));
        }
    }

    /// <summary>決まった品を1つ作る。魔法の品は未鑑定。固有の品は random_affixes の数だけ追加効果が付く。</summary>
    public static ItemInstance Make(GameData data, TreasureSettings s, ItemDef def, double level, IRandom rng)
    {
        var item = new ItemInstance { ItemId = def.Id, Identified = !def.IsMagic, Charges = def.Charges, Count = Math.Max(1, def.Bundle) };
        if (def.RandomAffixes > 0) AddAffixes(data, item, def, def.RandomAffixes, level, rng);
        return item;
    }

    private static DropEntry? Pick(List<DropEntry> entries, IRandom rng, DropBoost? boost)
    {
        double W(DropEntry e) => Math.Max(0, e.Weight * (boost?.WeightOf(e) ?? 1));
        double total = entries.Sum(W);
        if (total <= 0) return null;
        double r = rng.NextDouble() * total;
        foreach (var e in entries)
        {
            if ((r -= W(e)) < 0) return e;
        }
        return entries[^1];
    }

    /// <summary>当たったときの追加のダメージの平均(装備の見比べに使う。決まった種類の相手にだけ効くものは数えない)。</summary>
    public static double OnHitAverage(GameData data, ItemInstance item) =>
        item.AffixOnHit(data).Where(p => p.Type == EffectPart.Damage && p.Only == null && p.Dice != null)
            .Sum(p => DiceExpression.Parse(p.Dice!).Average);
}
