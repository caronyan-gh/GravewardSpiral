using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Combat;

public enum Side { Party, Enemy }

/// <summary>かかっている状態1つ。</summary>
public sealed class ActiveCondition
{
    public Condition Condition { get; init; }

    /// <summary>残りのラウンド数(自分の番の終わりに 1 減る)。null なら戦闘の終わりまで。</summary>
    public int? RoundsLeft { get; set; }

    /// <summary>自分の番の終わりにセーヴし直す能力値と難易度(null なら、し直さない)。</summary>
    public Ability? SaveAbility { get; init; }

    public int Dc { get; init; }

    /// <summary>かけた人と呪文(集中が切れたら、この2つが同じものを消す)。</summary>
    public string? SourceId { get; init; }
    public string? SpellId { get; init; }
}

/// <summary>かかっている修正値1つ(ブレスの命中 +1d4、シールド・オヴ・フェイスの AC +2 など)。</summary>
public sealed class ActiveModifier
{
    /// <summary>ac / attack / save / damage / attacks(攻撃の回数)、resist:&lt;種類&gt;、immune:&lt;状態&gt;、set:&lt;能力値&gt;。</summary>
    public string Stat { get; init; } = "";
    public int Value { get; init; }

    /// <summary>毎回振るダイス("1d4"、"-1d4")。</summary>
    public string? Dice { get; init; }

    public int? RoundsLeft { get; set; }
    public string? SourceId { get; init; }
    public string? SpellId { get; init; }
}

/// <summary>続くダメージ1つ(番の初めにダメージ。Key が同じものは重ならない)。</summary>
public sealed class ActiveDot
{
    /// <summary>重ならないかの見分け(呪文の id か、敵の行動の id)。</summary>
    public string Key { get; init; } = "";
    public string Dice { get; set; } = "1";
    public string DamageType { get; init; } = "necrotic";
    public int RoundsLeft { get; set; }

    /// <summary>番の終わりのセーヴ(成功で止まる)。null なら止まらない。</summary>
    public Ability? SaveAbility { get; init; }
    public int Dc { get; init; }
    public bool Magical { get; init; }

    /// <summary>かけた人と呪文(集中が切れたら消える)。</summary>
    public string? SourceId { get; init; }
    public string? SpellId { get; init; }
}

/// <summary>戦闘に出ている1人(パーティの1人か、モンスター1体)。</summary>
public abstract class Combatant
{
    public abstract Side Side { get; }
    public abstract string Id { get; }
    public abstract LocalizedText Name { get; }
    public abstract int Hp { get; set; }
    public abstract int MaxHp { get; }

    /// <summary>修正値を足す前の AC。</summary>
    public abstract int BaseAc { get; }

    /// <summary>今の AC。ac の修正値を足し、ac_min(バークスキン)があれば、その値より下げない。</summary>
    public int Ac => Math.Max(BaseAc + Modifiers.Where(m => m.Stat == "ac").Sum(m => m.Value),
        Modifiers.Where(m => m.Stat == "ac_min").Select(m => m.Value).DefaultIfEmpty(0).Max());

    /// <summary>一時的な HP(ダメージはここから減る)。</summary>
    public abstract int TempHp { get; set; }
    public abstract int AbilityMod(Ability a);
    public abstract int SaveBonus(Ability a);
    public abstract bool ImmuneTo(Condition c);

    /// <summary>その状態にならないか(もともとの耐性か、呪文などのしばらくのあいだの耐性 immune:&lt;状態&gt;)。</summary>
    /// <summary>このラウンドにリアクション(カウンタースペルなど)を使ったか。自分の番の始めに戻る。</summary>
    public bool ReactionUsed { get; set; }

    public bool Immune(Condition c) => ImmuneTo(c) || Modifiers.Any(m => m.Stat == "immune:" + c.ToString().ToLowerInvariant());

    /// <summary>魔法への抵抗(呪文と魔法の効果へのセーヴに有利。モンスターの特徴 magic_resistance)。</summary>
    public virtual bool MagicResistant => false;

    /// <summary>もともとの(種族・装備・モンスターの)完全耐性・抵抗・脆弱。倍率にはせず、あるかどうかだけ返す。</summary>
    protected abstract (bool Immune, bool Resist, bool Vulnerable) DamageTraits(string damageType, bool magical);

    /// <summary>
    /// ダメージの倍率(完全耐性 0、抵抗 0.5、脆弱 2、両方なら 1)。もともとの分と呪文などの効果(resist:/vulnerable:&lt;種類&gt;)を合わせ、
    /// 抵抗と脆弱は出どころがいくつあっても1回だけ数える(5版)。
    /// </summary>
    public double DamageMultiplier(string damageType, bool magical)
    {
        var (immune, resist, vulnerable) = DamageTraits(damageType, magical);
        if (immune) return 0;
        resist |= Modifiers.Any(m => m.Stat == "resist:" + damageType);
        vulnerable |= Modifiers.Any(m => m.Stat == "vulnerable:" + damageType);
        return (resist ? 0.5 : 1) * (vulnerable ? 2 : 1);
    }

    /// <summary>まだ戦える(HP が 0 でなく、死んでいない)。</summary>
    public abstract bool Standing { get; }

    public int Initiative { get; set; }
    public List<ActiveCondition> Conditions { get; } = [];

    /// <summary>受け続けているダメージ(毒、燃える、地獄の傷など)。</summary>
    public List<ActiveDot> Dots { get; } = [];
    public List<ActiveModifier> Modifiers { get; } = [];

    /// <summary>集中している呪文(1つだけ)。</summary>
    public string? Concentration { get; set; }

    /// <summary>回避の行動(次の自分の番まで、自分への攻撃が不利)。</summary>
    public bool Dodging { get; set; }

    /// <summary>隠れている(次の攻撃が有利。隠れているあいだは、ほかに相手がいれば狙われない)。</summary>
    public bool Hidden { get; set; }

    /// <summary>援護を受けた(次の攻撃が有利)。</summary>
    public bool Helped { get; set; }

    /// <summary>次に受けるダメージを半分にする(アンキャニー・ドッジ、回避。受けたら戻す側で消す)。</summary>
    public bool Halving { get; set; }

    /// <summary>次に受ける物理のダメージから引く量(モンクの飛び道具そらし)。</summary>
    public int Deflect { get; set; }

    public bool Has(Condition c) => Conditions.Any(x => x.Condition == c);

    /// <summary>行動できない状態(麻痺、朦朧、気絶、石化、無力化)。</summary>
    public bool Incapacitated => Conditions.Any(x => x.Condition is Condition.Paralyzed or Condition.Stunned
        or Condition.Unconscious or Condition.Petrified or Condition.Incapacitated);

    public bool CanAct => Standing && !Incapacitated;
}

/// <summary>パーティの1人。HP と状態はキャラクターに直接書く。</summary>
public sealed class PartyMember(Character character, GameData data) : Combatant
{
    public Character Character { get; } = character;
    private readonly GameData _data = data;

    public override Side Side => Side.Party;
    public override string Id => Character.Id;
    public override LocalizedText Name => LocalizedText.Of(Character.Name);

    public override int Hp
    {
        get => Shape != null ? ShapeHp : Character.Hp;
        set
        {
            if (Shape != null) ShapeHp = Math.Clamp(value, 0, Shape.HpAverage);
            else Character.Hp = Math.Clamp(value, 0, Character.MaxHp);
        }
    }

    public override int MaxHp => Shape?.HpAverage ?? Character.MaxHp;
    /// <summary>修正値の前の AC。ac_unarmored(メイジ・アーマー)は、鎧を着ていないときの基本の値(13 + 敏捷力)にする。</summary>
    public override int BaseAc => Shape?.Ac ?? Character.ArmorClass(_data,
        Modifiers.Where(m => m.Stat == "ac_unarmored").Select(m => (int?)m.Value).Max());

    /// <summary>同じパーティの仲間(パラディンのオーラを見る)。戦闘を始めるときに入れる。</summary>
    public IReadOnlyList<PartyMember> Allies { get; set; } = [];

    /// <summary>激怒している(この戦闘の終わりまで)。</summary>
    public bool Raging { get; set; }

    /// <summary>不屈の激怒の難易度(倒れずに踏みとどまるたびに 5 上がる)。</summary>
    public int RelentlessDc { get; set; } = 10;

    /// <summary>急所攻撃を最後に使ったラウンド(1ラウンドに1回)。</summary>
    public int SneakRound { get; set; } = -1;

    /// <summary>ワイルドシェイプで変身している獣(この戦闘の終わりまで。獣の HP が 0 になると戻る)。</summary>
    public MonsterDef? Shape { get; set; }

    public int ShapeHp { get; set; }

    /// <summary>倒れずに立っている、オーラを持つパラディンの中で一番高い魅力の修正(守りのオーラ。最低 +1。重ねない)。</summary>
    private int AuraOfProtection() => Allies.Where(a => a.Standing && ClassFeatures.Has(a.Character, "paladin", 6))
        .Select(a => Math.Max(1, a.Character.Scores.Mod(Ability.Cha))).DefaultIfEmpty(0).Max();

    public override int TempHp
    {
        get => Character.TempHp;
        set => Character.TempHp = Math.Max(0, value);
    }
    /// <summary>能力値の修正値(装備の効果と、水薬などのしばらくのあいだの能力値の高い方)。</summary>
    public override int AbilityMod(Ability a)
    {
        if (Shape != null && a is Ability.Str or Ability.Dex or Ability.Con) return Shape.Mod(a);   // 変身中は、体の能力値が獣のもの
        int score = Character.Stats(_data)[a];
        foreach (var m in Modifiers.Where(m => m.Stat == "set:" + a.Code())) score = Math.Max(score, m.Value);
        return Abilities.Modifier(score);
    }
    public override int SaveBonus(Ability a) => Character.SaveBonus(_data, a) + AuraOfProtection();

    /// <summary>状態の耐性: 装備、モンクの体の清浄(L10 毒)、パラディンの勇気のオーラ(L10 恐怖)。</summary>
    public override bool ImmuneTo(Condition c) =>
        Character.Passive(_data).Any(p => p.Type == EffectPart.ImmuneCondition && p.Condition == c)
        || (c == Condition.Poisoned && ClassFeatures.Has(Character, "monk", 10))
        || (c == Condition.Frightened && Allies.Any(a => a.Standing && ClassFeatures.Has(a.Character, "paladin", 10)));

    /// <summary>種族の特性を持っているか(races.json の traits)。</summary>
    public bool Trait(string trait) => Character.HasTrait(_data, trait);

    /// <summary>
    /// 装備の抵抗と脆弱。種族の抵抗: ドワーフの毒(ドワーフの頑健)、ティーフリングの火(地獄の抵抗)、ドラゴンボーンの火(このゲームでは赤い竜の血)。
    /// </summary>
    protected override (bool Immune, bool Resist, bool Vulnerable) DamageTraits(string damageType, bool magical)
    {
        var passive = Character.Passive(_data).ToList();
        bool raceResist = (damageType == "poison" && Trait("dwarven_resilience")) || (damageType == "fire" && (Trait("hellish_resistance") || Trait("damage_resistance")));
        return (damageType == "poison" && ClassFeatures.Has(Character, "monk", 10),
            passive.Any(p => p.Type == EffectPart.Resist && p.DamageType == damageType) || (Raging && DamageTypes.IsPhysical(damageType)) || raceResist,
            passive.Any(p => p.Type == EffectPart.Vulnerable && p.DamageType == damageType));
    }

    public ItemInstance? WeaponItem => Character.Inventory[EquipSlot.MainHand];
    public override bool Standing => Character.State == LifeState.Alive && Character.Hp > 0;

    // 死亡セーヴ
    public int DeathSuccesses { get; set; }
    public int DeathFailures { get; set; }
    public bool Stable { get; set; }

    public bool Dying => Character.State == LifeState.Unconscious && !Stable;

    public ItemDef? Weapon =>
        Character.Inventory[EquipSlot.MainHand] is { } w && _data.Items.TryGetValue(w.ItemId, out var d) && d.Kind == ItemKind.Weapon ? d : null;

    /// <summary>
    /// その攻撃で使える武器(装備しているものだけ。バックパックの武器は使わない)。
    /// 近接で届くなら: 利き手の近接武器(無ければ素手)と、遠隔の枠・利き手の飛び道具(隣接して撃つので不利)。
    /// 届かないなら: 遠隔の枠の武器と、利き手の飛び道具か投げられる武器。
    /// どれを使うかは、戦闘が期待ダメージで選ぶ。Item は null なら素手。
    /// </summary>
    public IEnumerable<(ItemDef? Def, ItemInstance? Item, bool Ranged)> WeaponChoices(bool melee)
    {
        (ItemDef? Def, ItemInstance? Item) Of(EquipSlot slot) =>
            Character.Inventory[slot] is { } it && Left(it) > 0 && _data.Items.TryGetValue(it.ItemId, out var d) && d.Kind == ItemKind.Weapon ? (d, it) : (null, null);
        var main = Of(EquipSlot.MainHand);
        var ranged = Of(EquipSlot.Ranged);
        if (melee)
        {
            yield return main.Def is { Ranged: false } ? (main.Def, main.Item, false) : (null, null, false);
            if (main.Def is { Ranged: true }) yield return (main.Def, main.Item, true);
        }
        else if (main.Def is { } md && (md.Ranged || md.Has("thrown"))) yield return (md, main.Item, true);
        if (ranged.Def != null) yield return (ranged.Def, ranged.Item, true);
    }

    /// <summary>
    /// この戦闘で投げた本数(装備の品ごと)。投げた武器は戦闘の終わりまで手元に無い。
    /// 勝てば拾って戻る。逃げたら、投げたぶんをなくす(5版: 投げたら拾うまで使えない)。
    /// </summary>
    public Dictionary<ItemInstance, int> Thrown { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>その品の、まだ手元にある本数。</summary>
    public int Left(ItemInstance item) => item.Count - Thrown.GetValueOrDefault(item);

    /// <summary>飛び道具か投げられる武器を装備している(後列や遠くのグループに届く)。</summary>
    public bool HasReach => Shape == null && WeaponChoices(melee: false).Any();   // 獣の姿では近接だけ

    public int PassivePerception => 10 + Character.SkillBonus(_data, "perception");
    public int StealthBonus => Character.SkillBonus(_data, "stealth");

    public bool StealthDisadvantage =>
        Character.Inventory[EquipSlot.Armor] is { } a && _data.Items.TryGetValue(a.ItemId, out var d) && d.StealthDisadvantage;
}

/// <summary>モンスター1体。</summary>
public sealed class MonsterUnit : Combatant
{
    private static int _next;

    public MonsterUnit(MonsterDef def, int hp, MonsterScale? scale = null)
    {
        Def = def;
        Scale = scale ?? MonsterScale.None;
        _hp = MaxHpValue = Math.Max(1, (int)(hp * Scale.HpMult));
        _id = $"{def.Id}#{Interlocked.Increment(ref _next)}";
        foreach (var a in def.Actions.Where(a => a.Uses.HasValue)) UsesLeft[a.Id] = a.Uses!.Value;
        LegendaryResistLeft = def.Has("legendary_resistance") ? 3 : 0;
    }

    private readonly string _id;
    private int _hp;

    public MonsterDef Def { get; }
    public int MaxHpValue { get; }

    /// <summary>伝説的抵抗の残り(5版: 1日に3回、セーヴの失敗を成功に変える。この戦いのあいだで数える)。</summary>
    public int LegendaryResistLeft { get; set; }

    /// <summary>止める種類のダメージ(Def.RegenerateStop)を受けたので、次の番の初めは再生しない。</summary>
    public bool RegenerationStopped { get; set; }

    public override bool MagicResistant => Def.Has("magic_resistance");

    /// <summary>深い階の強化。</summary>
    public MonsterScale Scale { get; }

    /// <summary>1回の戦闘で使える行動の残り。</summary>
    public Dictionary<string, int> UsesLeft { get; } = [];

    /// <summary>使ったあと、再使用の目を待っている行動。</summary>
    public HashSet<string> Spent { get; } = [];

    /// <summary>使った呪文スロット(呪文レベルごと)。</summary>
    public Dictionary<int, int> SlotsUsed { get; } = [];

    public int SlotsLeft(int level) =>
        Def.Spellcasting is { } sc && sc.Slots.TryGetValue(level.ToString(), out int n) ? n - SlotsUsed.GetValueOrDefault(level) : 0;

    public override Side Side => Side.Enemy;
    public override string Id => _id;
    public override LocalizedText Name => Def.Name;

    public override int Hp
    {
        get => _hp;
        set => _hp = Math.Clamp(value, 0, MaxHpValue);
    }

    public override int MaxHp => MaxHpValue;
    public override int BaseAc => Def.Ac + Scale.AcBonus;
    public override int TempHp { get; set; }
    public override int AbilityMod(Ability a) => Def.Mod(a);
    public override int SaveBonus(Ability a) => Def.SaveBonus(a);
    public override bool ImmuneTo(Condition c) => Def.ConditionImmune.Contains(c);
    public override bool Standing => _hp > 0;

    protected override (bool Immune, bool Resist, bool Vulnerable) DamageTraits(string damageType, bool magical)
    {
        bool nonmagicalPhysical = !magical && DamageTypes.IsPhysical(damageType);
        bool In(List<string> list) => list.Contains(damageType) || (nonmagicalPhysical && list.Contains(DamageTypes.NonmagicalPhysical));
        return (In(Def.Immune), In(Def.Resist), In(Def.Vulnerable));
    }

    public bool Ready(MonsterAction a) =>
        !Spent.Contains(a.Id) && (!a.Uses.HasValue || UsesLeft.GetValueOrDefault(a.Id) > 0);
}

/// <summary>敵の1グループ(同じ種類のモンスター)。</summary>
public sealed class EnemyGroup(MonsterDef def, List<MonsterUnit> units)
{
    public MonsterDef Def { get; } = def;
    public List<MonsterUnit> Units { get; } = units;

    /// <summary>正体が分かっているか(分かるまでは「小さな人影」などと出す)。</summary>
    public bool Identified { get; set; }

    public IEnumerable<MonsterUnit> Alive => Units.Where(u => u.Standing);
    public int AliveCount => Units.Count(u => u.Standing);
    public bool Wiped => AliveCount == 0;

    public LocalizedText DisplayName => Identified || Def.UnknownName == null ? Def.Name : Def.UnknownName;
}

/// <summary>
/// 召喚したもの(同じ種類の何体かをまとめて1つで持つ)。HP は 1 体の HP × 数で、減ると数が減る(HP の残りを 1 体の HP で割って切り上げ)。
/// パーティの側で、敵から狙われる(知能の低い敵ほど、目の前の召喚に食いつく)。唱えた人の集中が切れると消える。
/// </summary>
public sealed class SummonUnit : Combatant
{
    private static int _next;
    private readonly string _id;
    private int _hp;

    public SummonUnit(MonsterDef def, int count)
    {
        Def = def;
        HpEach = Math.Max(1, def.HpAverage);
        _hp = HpEach * Math.Max(1, count);
        MaxHpValue = _hp;
        _id = $"summon:{def.Id}#{Interlocked.Increment(ref _next)}";
    }

    public MonsterDef Def { get; }
    public int HpEach { get; }
    public int MaxHpValue { get; }

    public override bool MagicResistant => Def.Has("magic_resistance");

    /// <summary>残っている数。</summary>
    public int Count => _hp <= 0 ? 0 : (_hp + HpEach - 1) / HpEach;

    public override Side Side => Side.Party;
    public override string Id => _id;
    public override LocalizedText Name => Def.Name;
    public override int Hp { get => _hp; set => _hp = Math.Max(0, value); }
    public override int MaxHp => MaxHpValue;
    public override int BaseAc => Def.Ac;
    public override int TempHp { get; set; }
    public override int AbilityMod(Ability a) => Def.Mod(a);
    public override int SaveBonus(Ability a) => Def.SaveBonus(a);
    public override bool ImmuneTo(Condition c) => Def.ConditionImmune.Contains(c);
    public override bool Standing => _hp > 0;

    protected override (bool Immune, bool Resist, bool Vulnerable) DamageTraits(string damageType, bool magical)
    {
        bool nonmagicalPhysical = !magical && DamageTypes.IsPhysical(damageType);
        bool In(List<string> list) => list.Contains(damageType) || (nonmagicalPhysical && list.Contains(DamageTypes.NonmagicalPhysical));
        return (In(Def.Immune), In(Def.Resist), In(Def.Vulnerable));
    }
}
