using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Combat;

public enum CombatOutcome { Ongoing, Victory, Defeat, Fled }

public enum CommandKind { Attack, Cast, Item, Dodge, Help, Hide, Swap, Flee }

/// <summary>
/// パーティの1人の、そのラウンドの行動。Attack は TargetGroup、Help と Swap は TargetMember(パーティの並びの番号)を使う。
/// Cast は SpellId と SlotLevel(初級呪文は 0)と、呪文の対象に合わせて TargetGroup か TargetMember を使う。
/// Item は ItemIndex(袋の中の何番目か)と、対象に合わせて TargetGroup か TargetMember を使う。
/// </summary>
public sealed record PlayerCommand(CommandKind Kind, int TargetGroup = -1, int TargetMember = -1, string? SpellId = null, int SlotLevel = 0,
    int ItemIndex = -1)
{
    public static readonly PlayerCommand Dodge = new(CommandKind.Dodge);
}

/// <summary>
/// 戦闘のログの1行。Key は画面の文言のキー(text/&lt;言語&gt;.json の combat.*)、Args はその埋め込み。
/// 名前は LocalizedText のまま入れ、画面の側で言語を選ぶ。
/// </summary>
public sealed record CombatLogEntry(string Key, params object[] Args)
{
    /// <summary>この行の時点のパーティの HP と状態(並びは CombatState.Party の順)。画面はログを見せた行の値を出す。</summary>
    public IReadOnlyList<(string Id, int Hp, Characters.LifeState State)> Party { get; init; } = [];

    /// <summary>この行の時点の、グループごとの生きている数。</summary>
    public IReadOnlyList<int> GroupAlive { get; init; } = [];
}

/// <summary>勝ったときの取り分。</summary>
public sealed record CombatReward(long XpEach, long Gold, int Survivors);

/// <summary>戦闘の状態。パーティの並び、敵のグループ、行動の順番、ログ。</summary>
public sealed class CombatState
{
    public required List<PartyMember> Party { get; init; }
    public required List<EnemyGroup> Groups { get; init; }

    /// <summary>行動の順番(イニシアチブの高い順。戦闘のあいだ変わらない)。</summary>
    public List<Combatant> Order { get; } = [];

    public int Round { get; set; }

    /// <summary>魔法禁止区域(呪文と、呪文のアイテムが使えない。モンスターも唱えない)。</summary>
    public bool NoMagic { get; set; }
    public CombatOutcome Outcome { get; set; } = CombatOutcome.Ongoing;

    /// <summary>不意を打たれた側(最初のラウンドは行動できない)。</summary>
    public Side? Surprised { get; set; }

    public List<CombatLogEntry> Log { get; } = [];
    public CombatReward? Reward { get; set; }

    /// <summary>倒したモンスター(経験値と金の元)。</summary>
    public List<MonsterUnit> Defeated { get; } = [];

    /// <summary>続いている地帯の呪文と召喚(唱えた人が集中しているあいだ、その人の番ごとに効く)。</summary>
    public List<Lingering> Lingerings { get; } = [];

    /// <summary>近接で殴り合える、近くのグループの数(Wizardry 風に、前の2グループ)。</summary>
    public const int NearGroups = 2;

    public const int FrontRow = 3;

    /// <summary>今の前列(立っている人の先頭から3人。倒れた人は後ろに回る)。</summary>
    public IEnumerable<PartyMember> FrontRowMembers => Party.Where(p => p.Standing).Take(FrontRow);

    public bool InFront(PartyMember p) => FrontRowMembers.Contains(p);

    public IEnumerable<EnemyGroup> LiveGroups => Groups.Where(g => !g.Wiped);

    /// <summary>そのグループが近くにいるか(生きているグループの前から2つ)。</summary>
    public bool IsNear(EnemyGroup g) => LiveGroups.Take(NearGroups).Contains(g);

    public EnemyGroup GroupOf(MonsterUnit u) => Groups.First(g => g.Units.Contains(u));

    public void Add(string key, params object[] args) => Log.Add(new CombatLogEntry(key, args)
    {
        Party = Party.Select(p => (p.Id, p.Hp, p.Character.State)).ToList(),
        GroupAlive = Groups.Select(g => g.AliveCount).ToList(),
    });

    /// <summary>そのラウンドで、パーティの1人が狙えるグループの番号(Groups の添え字)。</summary>
    public IEnumerable<int> AttackableGroups(PartyMember p)
    {
        if (!p.CanAct) yield break;
        for (int i = 0; i < Groups.Count; i++)
        {
            var g = Groups[i];
            if (g.Wiped) continue;
            if (p.HasReach || (InFront(p) && IsNear(g))) yield return i;
        }
    }

    public LocalizedText GroupName(EnemyGroup g) => g.DisplayName;
}

/// <summary>
/// 続く呪文1つ。Targets は番ごとに選び直す(倒れた敵は外れる)。Summons は召喚したもの(敵からは狙われない。集中が切れると消える)。
/// </summary>
public sealed class Lingering
{
    public required Combatant Caster { get; init; }
    public required Magic.SpellDef Spell { get; init; }
    public required int Level { get; init; }
    public required int Mod { get; init; }
    public required int Dc { get; init; }
    public required Func<List<Combatant>> Targets { get; init; }
    public Monsters.MonsterDef? Summoned { get; init; }

    /// <summary>召喚したもの(攻撃されて数が減る)。</summary>
    public SummonUnit? Unit { get; init; }
}
