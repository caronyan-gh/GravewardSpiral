using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Combat;

public enum EncounterDifficulty { Easy, Medium, Hard, Deadly }

/// <summary>
/// 遭遇の設定。パックの encounters.json([{"id": "default", ...}])から読む。
/// 深さは割合(0 が最初の階、1 が最下層)。その深さの「目安のレベル」と人数から経験値の予算を出し、予算に合う敵を選ぶ。
/// </summary>
public sealed class EncounterSettings
{
    public const string Collection = "encounters";
    public const string DefaultId = "default";

    public string Id { get; set; } = DefaultId;

    /// <summary>1歩ごとの遭遇の確率。</summary>
    public double Rate { get; set; } = 0.04;

    /// <summary>階に入ったとき、部屋1つに敵がいる確率(Wizardry の玄室)。</summary>
    public double RoomChance { get; set; } = 0.35;

    /// <summary>戦ったあと、次の遭遇まで最低これだけ歩く。</summary>
    public int Cooldown { get; set; } = 8;

    /// <summary>最初の階と最下層の目安のレベル(あいだは深さの割合でならす)。</summary>
    public double FirstLevel { get; set; } = 1;
    public double LastLevel { get; set; } = 13;

    /// <summary>難しさの出やすさ。</summary>
    public Dictionary<EncounterDifficulty, double> Difficulty { get; set; } = new()
    {
        [EncounterDifficulty.Easy] = 3, [EncounterDifficulty.Medium] = 4, [EncounterDifficulty.Hard] = 2, [EncounterDifficulty.Deadly] = 0.3,
    };

    public int MaxGroups { get; set; } = 4;

    /// <summary>キャンプの小休憩で、遭遇を判定する回数(1回ごとに Rate の確率)。</summary>
    public int RestChecks { get; set; } = 6;

    /// <summary>2つ目より後のグループを足す割合。</summary>
    public double ExtraGroupChance { get; set; } = 0.45;

    /// <summary>予算に対して、これより弱い敵は出さない(予算の何分の一か)。</summary>
    public double MinXpFraction { get; set; } = 0.08;

    /// <summary>階ごとの顔ぶれ(その階に出るモンスターの種類)の数。生成のときに抽選する。</summary>
    public int RosterSize { get; set; } = 14;

    /// <summary>目安のレベルがこれを超えた階から、敵を強くする(SRD の数値の外へ。インフレ)。</summary>
    public double ScaleFromLevel { get; set; } = 20;

    /// <summary>目安のレベル 1 つ上がるごとの、HP とダメージの倍率の増え方。</summary>
    public double HpPerLevel { get; set; } = 0.2;
    public double DamagePerLevel { get; set; } = 0.1;

    /// <summary>AC と命中は、この数のレベルごとに +1(上限 max_ac_hit_bonus)。d20 の判定の意味を残すため少しだけ。</summary>
    public int LevelsPerAcHit { get; set; } = 3;
    public int MaxAcHitBonus { get; set; } = 4;

    /// <summary>
    /// 世界のレベル帯をずらしたとき(強いクランの世界)、決まった敵(ボス、イベントの戦闘)に足す強化。ずらした1レベルごとの
    /// HP とダメージの倍率の増え方。AC と命中は levels_per_ac_hit ごとに +1(max_ac_hit_bonus まで)。
    /// </summary>
    public double ShiftHpPerLevel { get; set; } = 0.3;
    public double ShiftDamagePerLevel { get; set; } = 0.12;

    public static EncounterSettings Load(ContentLibrary content, string id = DefaultId) =>
        content.Find(Collection, id)?.As<EncounterSettings>() ?? new EncounterSettings();

    public double TargetLevel(double depth) => FirstLevel + (LastLevel - FirstLevel) * Math.Clamp(depth, 0, 1);

    /// <summary>レベル帯だけ変えた写し(世界のレベル帯。GameSession.WorldSettings)。</summary>
    public EncounterSettings WithLevels(double first, double last)
    {
        var copy = (EncounterSettings)MemberwiseClone();
        copy.FirstLevel = first;
        copy.LastLevel = last;
        return copy;
    }
}

/// <summary>深い階の敵の強化(HP とダメージの倍率、AC と命中の上乗せ)。</summary>
public sealed record MonsterScale(double HpMult, double DamageMult, int AcBonus, int HitBonus)
{
    public static readonly MonsterScale None = new(1, 1, 0, 0);
    public bool IsNone => this == None;

    /// <summary>ボスの戦い(bosses.json のボスと、名のある敵)。instant_kill の no_boss が効かない相手。</summary>
    public bool Boss { get; init; }

    /// <summary>2つの強化を重ねる(倍率は掛け、上乗せは足す。どちらかがボスならボス)。</summary>
    public MonsterScale Times(MonsterScale o) => new(HpMult * o.HpMult, DamageMult * o.DamageMult, AcBonus + o.AcBonus, HitBonus + o.HitBonus) { Boss = Boss || o.Boss };
}

/// <summary>決まった遭遇(グループごとのモンスターと数)。</summary>
public sealed record Encounter(EncounterDifficulty Difficulty, int Budget, IReadOnlyList<(MonsterDef Def, int Count)> Groups)
{
    public MonsterScale Scale { get; init; } = MonsterScale.None;

    public int AdjustedXp => Encounters.AdjustedXp(Groups);
}

/// <summary>5版の遭遇の難しさ(経験値の予算)で、敵を選ぶ。</summary>
public static class Encounters
{
    // 5版のキャラクター1人あたりの経験値の閾値(易しい・普通・難しい・致命的)。レベル1〜20
    private static readonly int[,] Thresholds =
    {
        { 25, 50, 75, 100 }, { 50, 100, 150, 200 }, { 75, 150, 225, 400 }, { 125, 250, 375, 500 },
        { 250, 500, 750, 1100 }, { 300, 600, 900, 1400 }, { 350, 750, 1100, 1700 }, { 450, 900, 1400, 2100 },
        { 550, 1100, 1600, 2400 }, { 600, 1200, 1900, 2800 }, { 800, 1600, 2400, 3600 }, { 1000, 2000, 3000, 4500 },
        { 1100, 2200, 3400, 5100 }, { 1250, 2500, 3800, 5700 }, { 1400, 2800, 4300, 6400 }, { 1600, 3200, 4800, 7200 },
        { 2000, 3900, 5900, 8800 }, { 2100, 4200, 6300, 9500 }, { 2400, 4900, 7300, 10900 }, { 2800, 5700, 8500, 12700 },
    };

    /// <summary>1人あたりの閾値。小数のレベルは前後でならし、20 を超えたら 1 レベルごとに 1 割ずつ増やす。</summary>
    public static double Threshold(double level, EncounterDifficulty d)
    {
        level = Math.Max(1, level);
        int col = (int)d;
        if (level >= 20) return Thresholds[19, col] * (1 + 0.1 * (level - 20));
        int lo = (int)Math.Floor(level);
        double t = level - lo;
        double a = Thresholds[lo - 1, col], b = Thresholds[Math.Min(19, lo), col];
        return a + (b - a) * t;
    }

    /// <summary>数による倍率(5版: 1体 ×1、2体 ×1.5、3〜6体 ×2、7〜10体 ×2.5、11〜14体 ×3、15体〜 ×4)。</summary>
    public static double CountMultiplier(int count) => count switch
    {
        <= 1 => 1, 2 => 1.5, <= 6 => 2, <= 10 => 2.5, <= 14 => 3, _ => 4,
    };

    public static int AdjustedXp(IEnumerable<(MonsterDef Def, int Count)> groups)
    {
        var list = groups.ToList();
        int count = list.Sum(g => g.Count);
        return (int)(list.Sum(g => g.Def.Xp * g.Count) * CountMultiplier(count));
    }

    /// <summary>
    /// 深い階の敵の強化。目安のレベルが scale_from_level を超えた分だけ、HP とダメージを大きく、AC と命中を少しだけ上げる。
    /// 強化した階では、経験値の予算は scale_from_level のときのものを使う(強さは倍率で出す)。
    /// </summary>
    /// <summary>世界のレベル帯を shift だけずらしたときの、決まった敵の強化(ずらしていなければ None)。</summary>
    public static MonsterScale ShiftScale(EncounterSettings s, double shift)
    {
        if (shift <= 0) return MonsterScale.None;
        int acHit = Math.Min(s.MaxAcHitBonus, (int)(shift / Math.Max(1, s.LevelsPerAcHit)));
        return new MonsterScale(1 + s.ShiftHpPerLevel * shift, 1 + s.ShiftDamagePerLevel * shift, acHit, acHit);
    }

    public static MonsterScale Scale(EncounterSettings s, double level)
    {
        double over = level - s.ScaleFromLevel;
        if (over <= 0) return MonsterScale.None;
        int acHit = Math.Min(s.MaxAcHitBonus, (int)(over / Math.Max(1, s.LevelsPerAcHit)));
        return new MonsterScale(1 + s.HpPerLevel * over, 1 + s.DamagePerLevel * over, acHit, acHit);
    }

    /// <summary>
    /// 階の顔ぶれを抽選する(その深さで予算に合うモンスターから roster_size 種類。階のテーマがあれば、種類の重みで寄せる)。
    /// 生成のときに決めてセーブに入れる。候補は FloorAdvisor.Monsters(エディタのおすすめと同じ)。
    /// </summary>
    public static List<string> Roster(GameData data, EncounterSettings s, double depth, int partySize, IRandom rng, FloorTheme? theme = null)
    {
        var pool = FloorAdvisor.Monsters(data, s, depth, partySize, theme ?? FloorAdvisor.ThemeFor(data, depth))
            .OrderBy(x => x.Monster.Id, StringComparer.Ordinal).ToList();
        var picked = new List<string>();
        while (picked.Count < s.RosterSize && pool.Count > 0)
        {
            double total = pool.Sum(x => x.Weight), r = rng.NextDouble() * total;
            int i = 0;
            while (i < pool.Count - 1 && (r -= pool[i].Weight) >= 0) i++;
            picked.Add(pool[i].Monster.Id);
            pool.RemoveAt(i);
        }
        return picked;
    }

    /// <summary>
    /// 遭遇を1つ作る。depth は深さの割合、partySize はパーティの人数。roster があれば、その階の顔ぶれから選ぶ。
    /// 予算に合う敵(戦闘で使えるもの)から重みで1種類を選んでグループにし、予算が残れば別の種類のグループを足す。
    /// </summary>
    public static Encounter Build(GameData data, EncounterSettings settings, double depth, int partySize, IRandom rng,
        IReadOnlyCollection<string>? roster = null)
    {
        var diff = PickDifficulty(settings, rng);
        double fullLevel = settings.TargetLevel(depth);
        double level = Math.Min(fullLevel, settings.ScaleFromLevel);
        int budget = (int)Math.Max(10, Threshold(level, diff) * Math.Max(1, partySize));
        var usable = data.Monsters.Values.Where(m => m.Usable && m.Xp > 0).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        if (usable.Count == 0) throw new InvalidOperationException("遭遇に出せるモンスターがありません");
        if (roster is { Count: > 0 })
        {
            var mine = usable.Where(m => roster.Contains(m.Id)).ToList();
            if (mine.Any(m => m.Xp <= budget)) usable = mine;   // 顔ぶれに予算に合う敵がいなければ、全体から
        }

        var pool = usable.Where(m => m.Xp <= budget && m.Xp >= budget * settings.MinXpFraction).ToList();
        if (pool.Count == 0) pool = usable.Where(m => m.Xp <= budget).ToList();
        if (pool.Count == 0) pool = [usable.MinBy(m => m.Xp)!];

        var groups = new List<(MonsterDef Def, int Count)>();
        while (groups.Count < settings.MaxGroups)
        {
            var candidates = pool.Where(m => groups.All(g => g.Def.Id != m.Id)).ToList();
            if (candidates.Count == 0) break;
            var def = PickWeighted(candidates, rng);
            int want = Math.Max(1, DiceExpression.Parse(def.Appearing).Roll(rng).Total);
            int n = want;
            while (n > 1 && AdjustedXp(groups.Append((def, n))) > budget) n--;
            if (AdjustedXp(groups.Append((def, n))) > budget && groups.Count > 0) break;
            groups.Add((def, n));
            if (rng.NextDouble() >= settings.ExtraGroupChance) break;
            pool = pool.Where(m => AdjustedXp(groups.Append((m, 1))) <= budget).ToList();
        }
        return new Encounter(diff, budget, groups) { Scale = Scale(settings, fullLevel) };
    }

    private static EncounterDifficulty PickDifficulty(EncounterSettings s, IRandom rng)
    {
        double total = s.Difficulty.Values.Sum();
        double r = rng.NextDouble() * total;
        foreach (var (d, w) in s.Difficulty.OrderBy(x => x.Key))
        {
            if ((r -= w) < 0) return d;
        }
        return EncounterDifficulty.Medium;
    }

    private static MonsterDef PickWeighted(List<MonsterDef> list, IRandom rng)
    {
        double total = list.Sum(m => m.Weight);
        double r = rng.NextDouble() * total;
        foreach (var m in list)
        {
            if ((r -= m.Weight) < 0) return m;
        }
        return list[^1];
    }

    /// <summary>小休憩のあいだに敵が来るか。</summary>
    public static bool RestCheck(EncounterSettings s, IRandom rng)
    {
        for (int i = 0; i < s.RestChecks; i++)
        {
            if (rng.NextDouble() < s.Rate) return true;
        }
        return false;
    }

    /// <summary>この1歩で遭遇するか。sinceLast は前の戦闘から歩いた数。</summary>
    public static bool Check(EncounterSettings s, long sinceLast, IRandom rng) => sinceLast >= s.Cooldown && rng.NextDouble() < s.Rate;
}
