using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Combat;

/// <summary>
/// 戦闘でクラスの能力が効くかの判定(有利・不利、回避)。能力の when と effects(PassiveRules)から引く。
/// docs/class-data-design.md の Step 4c の2つ目の束。
/// </summary>
public static class FeatureChecks
{
    /// <summary>その人の、その種類の効果で今効いているもの(パーティの人だけ。モンスターはクラスの能力を持たない)。</summary>
    private static IEnumerable<PassiveRules.Active> Effects(GameData data, Combatant c, string type) =>
        c is PartyMember p ? PassiveRules.Effects(data, p.Character, type, p.Now) : [];

    private static bool Advantage(GameData data, Combatant c, string roll) =>
        Effects(data, c, "advantage").Any(e => e.Effect.Roll == roll);

    /// <summary>イニシアチブが有利か(advantage の initiative。野性の勘)。</summary>
    public static bool InitiativeAdvantage(GameData data, Combatant c) => Advantage(data, c, "initiative");

    /// <summary>
    /// 攻撃の有利の、クラスの能力の分。Advantage は足す有利の数(攻める人の melee_attack を近接の攻撃に、受ける人の attacked。無謀な攻撃)、
    /// CancelAdvantage は受ける人への攻撃の有利を消すか(no_advantage_against。とらえどころのなさ)。
    /// </summary>
    public static (int Advantage, bool CancelAdvantage) Attack(GameData data, Combatant attacker, Combatant target, bool melee)
    {
        int adv = 0;
        if (melee && Advantage(data, attacker, "melee_attack")) adv++;
        if (Advantage(data, target, "attacked")) adv++;
        return (adv, Effects(data, target, "no_advantage_against").Any());
    }

    /// <summary>advantage の save で、能力値がそれか、書いていないもの(全部のセーヴ)。</summary>
    private static IEnumerable<PassiveEffectDef> SaveAdvantages(GameData data, Combatant c, Ability a) =>
        Effects(data, c, "advantage").Select(e => e.Effect).Where(e => e.Roll == "save" && (e.Ability == null || Abilities.Parse(e.Ability) == a));

    /// <summary>そのセーヴがいつも有利か(advantage の save で、状態も魔法も絞らないもの。危険感知は敏捷力)。</summary>
    public static bool SaveAdvantage(GameData data, Combatant c, Ability a) =>
        SaveAdvantages(data, c, a).Any(e => e.Condition == null && e.On == null);

    /// <summary>魔法へのそのセーヴが有利か(advantage の save で on: magic。ノームの知恵: 知力・判断力・魅力)。</summary>
    public static bool MagicSaveAdvantage(GameData data, Combatant c, Ability a) =>
        SaveAdvantages(data, c, a).Any(e => e.On == "magic");

    /// <summary>その状態にする(か、その状態を解く)セーヴが有利か(advantage の save で condition。ドワーフの毒、妖精の血の魅了、勇敢の恐怖)。</summary>
    public static bool SaveAdvantageAgainst(GameData data, Combatant c, Condition condition) =>
        Effects(data, c, "advantage").Any(e => e.Effect.Roll == "save" && e.Effect.Condition == condition);

    /// <summary>攻撃・セーヴ・死亡のセーヴの d20 の 1 を振り直すか(reroll_natural_one。ハーフリングの幸運)。</summary>
    public static bool Lucky(GameData data, Combatant c) => Effects(data, c, "reroll_natural_one").Any();

    /// <summary>魔法(呪文や魔法の効果)では、その状態にならないか(immune_magic_condition。妖精の血: 魔法の眠り)。</summary>
    public static bool MagicImmune(GameData data, Combatant c, Condition condition) =>
        Effects(data, c, "immune_magic_condition").Any(e => e.Effect.Condition == condition);

    /// <summary>敏捷力のセーヴで半分になる効果を、成功で 0・失敗で半分にするか(evasion。回避)。</summary>
    public static bool Evades(GameData data, Combatant target) => Effects(data, target, "evasion").Any();

    // ---- ファイティングスタイル(Step 4c の3つ目の束) ----

    // 攻撃の上乗せは、その人の今の様子(激怒など)で効く場面を決める

    /// <summary>もう片方の手の攻撃にも、ダメージに能力値の修正を足すか(offhand_mod。二刀流)。</summary>
    public static bool OffhandAbilityDamage(GameData data, PartyMember p) => Effects(data, p, "offhand_mod").Any();

    /// <summary>飛び道具で撃つ攻撃の命中に足す値(attack_bonus の ranged_shot。弓術)。</summary>
    public static int RangedShotHitBonus(GameData data, PartyMember p) => PassiveRules.Sum(data, p.Character, "attack_bonus", "ranged_shot", p.Now);

    /// <summary>利き手の近接武器の攻撃のダメージに足す値(damage_bonus の main_melee。決闘)。</summary>
    public static int MainMeleeDamageBonus(GameData data, PartyMember p) => PassiveRules.Sum(data, p.Character, "damage_bonus", "main_melee", p.Now);

    // ---- 反応と、倒れかけたとき(Step 4c の5つ目の束) ----

    /// <summary>
    /// 攻撃が当たったときの反応。Halve はダメージを半分に、そうでなければ Dice + 能力値の修正(Ability。戦闘の値)+ Bonus だけ減らす(物理のダメージ)。
    /// Message は使ったときの文言のキー(無ければ出さない)。
    /// </summary>
    public sealed record Reaction(bool Halve, string? Dice, Ability? Ability, int Bonus, string? Message);

    /// <summary>
    /// 当たった攻撃への反応(reaction_reduce と reaction_halve。on が ranged_attacked なら飛び道具に、attacked ならどの攻撃にも)。
    /// 飛び道具には、飛び道具に限る反応を先に選ぶ(飛び道具そらしとアンキャニー・ドッジを両方持てば、飛び道具はそらす)。無ければ null。
    /// </summary>
    public static Reaction? ReactionFor(GameData data, PartyMember tp, bool ranged)
    {
        var candidates = Effects(data, tp, "reaction_reduce").Concat(Effects(data, tp, "reaction_halve"))
            .Where(e => e.Effect.On == "attacked" || (ranged && e.Effect.On == "ranged_attacked"));
        // max のまとまりは、減らす量の見込み(ダイスの平均と value と能力値の修正)が一番大きいもの1つ
        var pick = PassiveRules.PickByGroup(candidates, e => DiceAverage(e.Effect.Dice) + (e.Effect.Value?.Eval(tp.Character, e.Level) ?? 0)
                + (e.Effect.Ability is { } ab ? tp.AbilityMod(Abilities.Parse(ab)) : 0))
            .OrderBy(e => e.Effect.On == "ranged_attacked" ? 0 : 1)
            .Select(e => (PassiveRules.Active?)e).FirstOrDefault();
        if (pick is not { } a) return null;
        var e = a.Effect;
        return e.Type == "reaction_halve"
            ? new Reaction(true, null, null, 0, e.Message)
            : new Reaction(false, e.Dice, e.Ability is { } ab ? Abilities.Parse(ab) : null, e.Value?.Eval(tp.Character, a.Level) ?? 0, e.Message);
    }

    /// <summary>倒れかけたときに踏みとどまる手(stay_up 1つ)。Pool があればセーヴの代わりにその回数を1使う(不屈の耐久)。無ければ Ability のセーヴ(難易度は Dc、成功のたびに Step 上がる)。</summary>
    public sealed record StayUpWay(Ability? Ability, int Dc, int Step, string? Pool, string? Message);

    /// <summary>倒れかけたときに踏みとどまる手を、試す順に(クラスを取った順、そのあと種族)。不屈の激怒は激怒のあいだ。</summary>
    public static IReadOnlyList<StayUpWay> StayUp(GameData data, PartyMember p) =>
        Effects(data, p, "stay_up").Select(e => e.Effect.Pool != null
            ? new StayUpWay(null, 0, 0, e.Effect.Pool, e.Effect.Message)
            : new StayUpWay(Abilities.Parse(e.Effect.Ability!), e.Effect.Value!.Eval(p.Character, e.Level), e.Effect.Step, null, e.Effect.Message)).ToList();

    // ---- 当たったとき(Step 4c の4つ目の束) ----

    /// <summary>近接の武器の攻撃が当たったときに足すダメージ(extra_damage の melee_weapon。ダイスと種類。向上したディヴァイン・スマイトの光輝 1d8)。</summary>
    public static IReadOnlyList<(string Dice, string Type)> MeleeWeaponExtraDamage(GameData data, PartyMember p) =>
        MeleeWeaponExtraDamageFrom(data, p).Select(x => (x.Dice, x.Type)).ToList();

    /// <summary>同じもので、くれた能力の id も(戦闘のログに出どころを添える)。</summary>
    public static IReadOnlyList<(string Dice, string Type, string Feature)> MeleeWeaponExtraDamageFrom(GameData data, PartyMember p) =>
        PassiveRules.PickByGroup(Effects(data, p, "extra_damage").Where(e => e.Effect.On == "melee_weapon"), e => DiceAverage(e.Effect.Dice))   // max は平均の大きいもの1つ
            .Select(e => (e.Effect.Dice!, e.Effect.DamageType!, e.Def.Id)).ToList();

    /// <summary>ダイスの式の平均(読めないか無ければ 0)。</summary>
    private static double DiceAverage(string? dice) =>
        dice != null && DiceExpression.TryParse(dice, out var expr, out _) ? expr.Average : 0;

    /// <summary>近接の武器の攻撃で当てたダメージのうち、HP に戻す割合(drain の melee_weapon。百分率。命を吸う刃は 30。無ければ 0)。</summary>
    public static int MeleeWeaponDrain(GameData data, PartyMember p) => PassiveRules.Sum(data, p.Character, "drain", "melee_weapon", p.Now);

    /// <summary>
    /// 攻撃で敵を一撃で倒す見込み(instant_kill。Wizardry の忍者の首はね)。当てた攻撃の種類(素手・近接・どれでも)に合うもののうち、
    /// 確率(value の百分率。0〜100 に丸める)の一番大きいもの。無ければ null。
    /// </summary>
    public static (int Chance, PassiveEffectDef Effect)? InstantKill(GameData data, PartyMember p, bool unarmed, bool melee) =>
        Effects(data, p, "instant_kill")
            .Where(e => e.Effect.On switch { "unarmed" => unarmed, "melee" => melee, _ => true })
            .Select(e => (Chance: Math.Clamp(e.Effect.Value!.Eval(p.Character, e.Level), 0, 100), e.Effect))
            .Where(x => x.Chance > 0).OrderByDescending(x => x.Chance).Cast<(int, PassiveEffectDef)?>().FirstOrDefault();

    /// <summary>
    /// 一撃で倒されるのをこらえる確率(resist_instant_kill。百分率)。効果ごとに別々に振り、どれか1つに当たればこらえる
    /// (Wizardry: クラスの分と、レベル × 2% の分を別に振る)。
    /// </summary>
    public static IEnumerable<int> InstantKillResists(GameData data, PartyMember p) =>
        PassiveRules.PickByGroup(Effects(data, p, "resist_instant_kill"), e => e.Effect.Value!.Eval(p.Character, e.Level))   // 同じ組(stacks: max)は一番大きいものだけ
            .Select(e => Math.Clamp(e.Effect.Value!.Eval(p.Character, e.Level), 0, 100)).Where(v => v > 0);

    /// <summary>近接の攻撃のクリティカルで足す武器のダイスの数(crit_extra_dice の melee。残忍な一撃と、種族の獰猛な攻撃)。</summary>
    public static int MeleeCritExtraDice(GameData data, PartyMember p) => PassiveRules.Sum(data, p.Character, "crit_extra_dice", "melee", p.Now);

    /// <summary>武術の素手の攻撃が魔法の攻撃になるか(magical の unarmed。気の込もった打撃)。</summary>
    public static bool UnarmedMagical(GameData data, PartyMember p) => Effects(data, p, "magical").Any(e => e.Effect.On == "unarmed");

    /// <summary>その呪文が当たったときに足すダメージ(spell_damage_bonus。苦痛の魔撃はエルドリッチ・ブラストに魅力の修正で、0 より下げない)。</summary>
    public static int SpellDamageBonus(GameData data, PartyMember caster, string spellId) =>
        PassiveRules.Total(Effects(data, caster, "spell_damage_bonus").Where(e => e.Effect.Spell == spellId).Select(e => (caster.Character, e)));

    /// <summary>利き手の近接武器の攻撃で、この値以下のダメージの目を振り直す(reroll_damage の main_melee。大型武器は 2。無ければ 0)。</summary>
    public static int MainMeleeRerollLow(GameData data, PartyMember p) =>
        Effects(data, p, "reroll_damage").Where(e => e.Effect.On == "main_melee").Select(e => e.Effect.Value?.Eval(p.Character, e.Level) ?? 0)
            .DefaultIfEmpty(0).Max();
}
