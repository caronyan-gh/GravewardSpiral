using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Combat;

/// <summary>ダメージのセーヴに成功したときの扱い。Half は半分(敏捷力なら、回避で成功 0・失敗で半分)、None は受けない。</summary>
public enum SaveSuccess { Half, None }

/// <summary>
/// 戦闘の外で、ダメージや状態を受けたときの守り(宝箱の罠とイベント)。戦闘と同じ仕組みで見る: パーティの人を PartyMember に包み、
/// 装備・種族・クラスの能力・仲間のオーラから、セーヴの修正と有利(状態へのセーヴの有利も)・幸運・回避・ダメージの無効と抵抗と脆弱・効かない状態・
/// 踏みとどまる手(不屈の耐久)を決める。戦闘の外では、立っていて、激怒しておらず、目が見え、行動できるものとする。
/// 文は prefix の文言(prefix.hurt・prefix.endure・prefix.died・prefix.condition)で出す。
/// </summary>
public sealed class Hazard
{
    private readonly GameData _data;
    private readonly IRandom _rng;
    private readonly string _prefix;
    private readonly Action<string, object[]> _say;
    private readonly Dictionary<Character, PartyMember> _members = new(ReferenceEqualityComparer.Instance);

    public Hazard(GameData data, IEnumerable<Character> party, IRandom rng, string prefix, Action<string, object[]> say)
    {
        _data = data;
        _rng = rng;
        _prefix = prefix;
        _say = say;
        var all = party.Select(c => new PartyMember(c, data)).ToList();
        foreach (var m in all)
        {
            m.Allies = all;
            _members[m.Character] = m;
        }
    }

    /// <summary>その人(パーティにいなければ、その人だけの仲間として包む)。</summary>
    private PartyMember Member(Character c)
    {
        if (_members.TryGetValue(c, out var m)) return m;
        m = new PartyMember(c, _data);
        m.Allies = [m];
        return _members[c] = m;
    }

    /// <summary>d20 を振る(幸運なら 1 を振り直す)。</summary>
    private int D20(PartyMember m)
    {
        int r = _rng.Roll(20);
        return r == 1 && FeatureChecks.Lucky(_data, m) ? _rng.Roll(20) : r;
    }

    /// <summary>セーヴ。against はその状態にするセーヴ(ドワーフの毒などの有利を見る)。</summary>
    public bool Save(Character c, Ability a, int dc, Condition? against = null)
    {
        var m = Member(c);
        bool advantage = FeatureChecks.SaveAdvantage(_data, m, a) || against is { } k && FeatureChecks.SaveAdvantageAgainst(_data, m, k);
        int roll = D20(m);
        if (advantage) roll = Math.Max(roll, D20(m));
        return roll + m.SaveBonus(a) >= dc;
    }

    /// <summary>
    /// ダメージを受ける。save があれば先にセーヴする。type があれば無効・抵抗・脆弱を掛ける。lethal なら HP 0 で死ぬ
    /// (その前に踏みとどまる手を試す。即死になる大きなダメージは除く)。lethal でなければ HP は 1 より下がらない。受けたダメージを返す。
    /// </summary>
    public int Damage(Character c, int rolled, string? type, (Ability Ability, int Dc, SaveSuccess Success)? save, bool lethal)
    {
        if (!c.IsAlive) return 0;
        var m = Member(c);
        int dmg = rolled;
        if (save is var (a, dc, how))
        {
            bool ok = Save(c, a, dc);
            bool evade = how == SaveSuccess.Half && a == Ability.Dex && FeatureChecks.Evades(_data, m);
            dmg = ok ? (how == SaveSuccess.None || evade ? 0 : dmg / 2) : evade ? dmg / 2 : dmg;
        }
        if (type != null) dmg = (int)Math.Floor(dmg * m.DamageMultiplier(type, false));
        if (dmg <= 0) return 0;
        int before = c.Hp;
        if (!lethal)
        {
            c.Hp = Math.Max(1, c.Hp - dmg);
            _say(_prefix + ".hurt", [c.Name, dmg]);
            return dmg;
        }
        c.Hp -= dmg;
        _say(_prefix + ".hurt", [c.Name, dmg]);
        if (c.Hp <= 0 && dmg - before < c.MaxHp && StayUp(m))
        {
            c.Hp = 1;
            _say(_prefix + ".endure", [c.Name]);
            return dmg;
        }
        if (c.Hp <= 0)
        {
            c.Hp = 0;
            c.State = LifeState.Dead;
            _say(_prefix + ".died", [c.Name]);
        }
        return dmg;
    }

    /// <summary>倒れかけたときに踏みとどまる手を順に試す(回数を使うもの、セーヴのもの)。</summary>
    private bool StayUp(PartyMember m)
    {
        foreach (var way in FeatureChecks.StayUp(_data, m))
        {
            if (way.Pool != null ? ClassFeatures.Spend(_data, m.Character, way.Pool) : Save(m.Character, way.Ability!.Value, way.Dc)) return true;
        }
        return false;
    }

    /// <summary>
    /// 状態にする。save があれば先にセーヴ(成功で防ぐ)。効かない状態(装備・能力・仲間のオーラ)なら付かない。minutes は分(null なら治療するまで)。
    /// 付いたら true。
    /// </summary>
    public bool AddCondition(Character c, Condition cond, int? minutes, (Ability Ability, int Dc)? save)
    {
        if (!c.IsAlive) return false;
        if (save is var (a, dc) && Save(c, a, dc, cond)) return false;
        if (Member(c).ImmuneTo(cond)) return false;
        c.Lasting.RemoveAll(l => l.Condition == cond);
        c.Lasting.Add(new LastingCondition { Condition = cond, Minutes = minutes });
        _say(_prefix + ".condition", [c.Name, cond]);
        return true;
    }
}
