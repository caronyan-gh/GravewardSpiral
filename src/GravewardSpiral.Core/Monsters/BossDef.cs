using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Monsters;

/// <summary>
/// 決まった場所にいる敵(パックの bosses.json)。迷宮のマスの印 boss:id で置く。
/// 倒すと reward_flag が立つ(最下層のボスの印は、町へ持ち帰るとエンディングになる)。
/// </summary>
public sealed class BossDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public List<BossGroup> Groups { get; set; } = [];
    public string? RewardFlag { get; set; }

    /// <summary>
    /// 敵の組を、世界を作るときに世界のレベル帯から選ぶ(自由な迷宮の主)。選んだ組はセーブに入れ(GameSession.BossPicks)、
    /// groups は使わない。シナリオは同じ id で決まったボスに差し替える。
    /// </summary>
    public bool Auto { get; set; }

    /// <summary>出会ったときと倒したときの文言のキー。</summary>
    public string? Intro { get; set; }
    public string? Victory { get; set; }

    /// <summary>世界の文が書いた出会ったときと倒したときの文(あればキーの文より先に使う。セーブの世界の文から入れる)。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public LocalizedText? IntroText { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public LocalizedText? VictoryText { get; set; }
    public string? Note { get; set; }

    /// <summary>ボスの強化(HP とダメージの倍率、AC と命中の上乗せ)。</summary>
    public double HpMult { get; set; } = 1;
    public double DamageMult { get; set; } = 1;
    public int AcBonus { get; set; }
    public int HitBonus { get; set; }

    public Combat.MonsterScale Scale => new(HpMult, DamageMult, AcBonus, HitBonus);

    public sealed class BossGroup
    {
        public string Monster { get; set; } = "";
        public int Count { get; set; } = 1;

        /// <summary>戦闘で出す名前(省くと元の敵の名前)。</summary>
        public LocalizedText? Name { get; set; }
    }

    /// <summary>敵の組だけを差し替えた写し(auto のボスに、世界で選んだ組を当てる)。</summary>
    public BossDef WithGroups(List<BossGroup> groups)
    {
        var copy = (BossDef)MemberwiseClone();
        copy.Groups = groups;
        return copy;
    }

    /// <summary>
    /// auto のボスの組を選ぶ。主は、世界の最後のレベル帯に近い脅威度(level−1〜level+1)の敵から、最下層のテーマの重みで1体。
    /// 従う者は、最下層の顔ぶれのうち脅威度が level の半分以下で一番強い敵を2体。
    /// </summary>
    public static List<BossGroup> PickGroups(Characters.GameData data, double level, IReadOnlyList<string> deepestRoster, Rng.IRandom rng,
        Combat.FloorTheme? deepestTheme = null)
    {
        var theme = deepestTheme ?? Combat.FloorAdvisor.ThemeFor(data, 1.0);
        var pool = data.Monsters.Values
            .Where(m => m.Usable && m.Xp > 0 && m.CreatureType != "swarm" && m.Cr >= level - 1 && m.Cr <= level + 1)
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => (Monster: m, Weight: (theme?.WeightOf(m) ?? 1) * (1 + m.Cr)))
            .Where(x => x.Weight > 0).ToList();
        if (pool.Count == 0)
        {
            // 合う脅威度の敵がいなければ、脅威度が level に一番近い敵
            var near = data.Monsters.Values.Where(m => m.Usable && m.Xp > 0).OrderBy(m => Math.Abs(m.Cr - level)).ThenBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
            return near == null ? [] : [new BossGroup { Monster = near.Id }];
        }
        double r = rng.NextDouble() * pool.Sum(x => x.Weight);
        int i = 0;
        while (i < pool.Count - 1 && (r -= pool[i].Weight) >= 0) i++;
        var groups = new List<BossGroup> { new() { Monster = pool[i].Monster.Id } };
        var escort = deepestRoster.Select(id => data.Monsters.GetValueOrDefault(id)).OfType<MonsterDef>()
            .Where(m => m.Usable && m.Cr > 0 && m.Cr <= level / 2 && m.Id != pool[i].Monster.Id)
            .OrderByDescending(m => m.Cr).ThenBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
        if (escort != null) groups.Add(new BossGroup { Monster = escort.Id, Count = 2 });
        return groups;
    }

    /// <summary>戦う敵の組(名前の付いた組は、名前を変えた写し)。monsters に無い敵は飛ばす。</summary>
    public List<(MonsterDef Def, int Count)> Fighters(IReadOnlyDictionary<string, MonsterDef> monsters) =>
        Groups.Where(g => monsters.ContainsKey(g.Monster))
            .Select(g => (g.Name is { } n ? monsters[g.Monster].WithName(n) : monsters[g.Monster], g.Count)).ToList();
}
