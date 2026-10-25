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

    /// <summary>出会ったときと倒したときの文言のキー。</summary>
    public string? Intro { get; set; }
    public string? Victory { get; set; }
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

    /// <summary>戦う敵の組(名前の付いた組は、名前を変えた写し)。monsters に無い敵は飛ばす。</summary>
    public List<(MonsterDef Def, int Count)> Fighters(IReadOnlyDictionary<string, MonsterDef> monsters) =>
        Groups.Where(g => monsters.ContainsKey(g.Monster))
            .Select(g => (g.Name is { } n ? monsters[g.Monster].WithName(n) : monsters[g.Monster], g.Count)).ToList();
}
