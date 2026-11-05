using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;

namespace GravewardSpiral.Core.Combat;

/// <summary>戦闘のあとも残る状態(毒など)。Minutes は残りの分(1歩 = 1分)。null なら大休憩まで。</summary>
public sealed class LastingCondition
{
    public Condition Condition { get; set; }
    public int? Minutes { get; set; }
}

/// <summary>
/// 戦闘のあとも続く呪文の効き目(修正値・抵抗・状態の耐性)。Minutes は残りの分(1歩 = 1分)。null なら大休憩か町に戻るまで。
/// 集中でない、10 分より長く続く呪文だけ(キャンプで唱えたメイジ・アーマー、フリーダム・オヴ・ムーヴメントなど。2026-11-04、ユーザーが決めた)。
/// </summary>
public sealed class LastingBuff
{
    public string SpellId { get; set; } = "";
    public string Stat { get; set; } = "";
    public int Value { get; set; }
    public string? Dice { get; set; }
    public int? Minutes { get; set; }
}

/// <summary>
/// 戦闘のあとに残る状態の扱い。1分(10 ラウンド)より長く続く毒・盲目・聴覚喪失・恐怖・麻痺だけを残す
/// (麻痺は宝箱のスタンの罠などで、寺院か大休憩で治るまで続く)。
/// </summary>
public static class Lasting
{
    public static readonly Condition[] Kinds = [Condition.Poisoned, Condition.Blinded, Condition.Deafened, Condition.Frightened, Condition.Paralyzed];

    /// <summary>戦闘(か、キャンプで唱える)の始めに、残っている状態をかけ直す。</summary>
    public static void Load(PartyMember p)
    {
        foreach (var l in p.Character.Lasting)
        {
            if (p.Has(l.Condition)) continue;
            p.Conditions.Add(new ActiveCondition { Condition = l.Condition, RoundsLeft = l.Minutes * 10 });
        }
        foreach (var b in p.Character.Buffs)
        {
            if (p.Modifiers.Any(m => m.SpellId == b.SpellId && m.Stat == b.Stat)) continue;
            p.Modifiers.Add(new ActiveModifier { Stat = b.Stat, Value = b.Value, Dice = b.Dice, SpellId = b.SpellId, RoundsLeft = b.Minutes * 10 });
        }
    }

    /// <summary>1時間(60 分)より長い呪文は、歩数で数えずに大休憩か町に戻るまで続ける。</summary>
    public const int UntilRestRounds = 600;

    /// <summary>戦闘のあとに残す呪文か(集中でなく、10 分より長く続く)。</summary>
    private static bool Keeps(GameData? data, string? spellId) =>
        spellId != null && data != null && data.Spells.TryGetValue(spellId, out var sp) && !sp.Concentration && sp.Rounds > 100;

    /// <summary>戦闘の終わりに、長く続く状態だけを残す(呪文の集中でかかったものは消える)。</summary>
    public static void Save(PartyMember p, GameData? data = null)
    {
        data ??= p.Data;
        p.Character.Buffs = p.Modifiers
            .Where(m => Keeps(data, m.SpellId) && m.RoundsLeft is null or > 10)
            .Select(m => new LastingBuff
            {
                SpellId = m.SpellId!, Stat = m.Stat, Value = m.Value, Dice = m.Dice,
                Minutes = data!.Spells[m.SpellId!].Rounds > UntilRestRounds || m.RoundsLeft is null ? null : (m.RoundsLeft.Value + 9) / 10,
            })
            .GroupBy(b => (b.SpellId, b.Stat)).Select(g => g.First()).ToList();
        p.Character.Lasting = p.Conditions
            .Where(x => Kinds.Contains(x.Condition) && x.SpellId == null && (x.RoundsLeft is null or > 10))
            .Select(x => new LastingCondition { Condition = x.Condition, Minutes = x.RoundsLeft is int r ? (r + 9) / 10 : null })
            .GroupBy(x => x.Condition).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// 迷宮を1歩(1分)進んだ。残りの時間を減らし、毒なら HP が 1 減る(1 より下にはしない)。毒で HP が減った人の名前を返す。
    /// </summary>
    public static List<Character> Step(IEnumerable<Character> party)
    {
        var hurt = new List<Character>();
        foreach (var c in party.Where(c => c.IsAlive))
        {
            if (c.Lasting.Any(l => l.Condition == Condition.Poisoned) && c.Hp > 1)
            {
                c.Hp--;
                hurt.Add(c);
            }
            foreach (var l in c.Lasting.Where(l => l.Minutes.HasValue)) l.Minutes--;
            c.Lasting.RemoveAll(l => l.Minutes <= 0);
            Pass(c, 1);
        }
        return hurt;
    }

    /// <summary>呪文の効き目の残りを minutes 分減らし、切れたものを外す(長いもの〔残りが null〕はそのまま)。</summary>
    public static void Pass(Character c, int minutes)
    {
        foreach (var b in c.Buffs.Where(b => b.Minutes.HasValue)) b.Minutes -= minutes;
        c.Buffs.RemoveAll(b => b.Minutes <= 0);
    }
}
