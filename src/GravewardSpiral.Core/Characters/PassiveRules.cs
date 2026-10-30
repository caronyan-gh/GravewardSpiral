using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

/// <summary>
/// 勝手に効く能力の、効く場面(when)と効果(effects)。docs/class-data-design.md の「いつの一覧」「効果の部品」「重なり方」。
/// 能力は、まとまり(group。書かなければ id)ごとに重なり方(stacks)で選ぶ: first は先に取ったクラスのものだけ(それが効く場面に当たらなければ何も効かない)、
/// own と max は id ごとに1つ(2つのクラスから得ていれば、クラスのレベルが高いほう)。max の「一番大きいものだけ」は、使う側(オーラならパーティ全体)で選ぶ。
/// </summary>
public static class PassiveRules
{
    /// <summary>when に書ける値(コアが持ち、データからは足せない)。</summary>
    public static readonly string[] Whens =
        ["always", "no_armor", "no_armor_no_shield", "wearing_armor", "standing", "raging", "shaped", "not_blinded", "not_incapacitated",
         "grip:ranged", "grip:two_weapons", "grip:two_handed", "grip:one_handed", "grip:none_armored", "grip:none", "has_ranged_weapon"];

    /// <summary>効果の部品の種類。</summary>
    public static readonly string[] EffectTypes =
        ["ac_ability", "save_proficiency", "save_bonus", "immune_condition", "immune_magic_condition", "immune_damage", "resist_damage",
         "advantage", "no_advantage_against", "evasion", "reroll_natural_one",
         "attack_bonus", "damage_bonus", "reroll_damage", "offhand_mod", "ac_bonus",
         "extra_damage", "drain", "crit_extra_dice", "magical", "spell_damage_bonus", "reaction_reduce", "reaction_halve", "stay_up", "no_cast", "no_concentration", "instant_kill", "resist_instant_kill"];

    /// <summary>
    /// 効果の種類ごとの、on に書ける攻撃。ranged_shot は飛び道具で撃つ攻撃、main_melee は利き手の近接武器の攻撃、
    /// melee_weapon は近接の武器の攻撃(もう片方の手も)、melee は近接の攻撃(素手も)、unarmed は武術の素手の攻撃。
    /// </summary>
    public static readonly Dictionary<string, string[]> OnsFor = new()
    {
        ["attack_bonus"] = ["ranged_shot", "main_melee"],
        ["damage_bonus"] = ["ranged_shot", "main_melee", "melee_str"],
        ["reroll_damage"] = ["main_melee"],
        ["extra_damage"] = ["melee_weapon"],
        ["drain"] = ["melee_weapon"],
        ["crit_extra_dice"] = ["melee"],
        ["magical"] = ["unarmed"],
        ["reaction_reduce"] = ["ranged_attacked", "attacked"],
        ["reaction_halve"] = ["ranged_attacked", "attacked"],
        ["instant_kill"] = ["unarmed", "melee", "attack"],
    };

    /// <summary>
    /// advantage の roll に書ける判定。initiative はイニシアチブ、melee_attack は自分の近接の攻撃、attacked は自分への攻撃(相手が有利になる)、
    /// save はセーヴ(ability で能力値を絞る。condition を書けばその状態にする・解くセーヴだけ、on: magic なら魔法へのセーヴだけ)。
    /// </summary>
    public static readonly string[] Rolls = ["initiative", "melee_attack", "attacked", "save"];

    /// <summary>
    /// 効く場面を決める、その人の今の様子。戦闘の外では、立っていて、激怒しておらず、目が見え、行動できるものとする。
    /// </summary>
    public readonly record struct Situation(bool Standing = true, bool Raging = false, bool Blinded = false, bool Incapacitated = false, bool Shaped = false);

    public static readonly string[] StacksValues = ["own", "max", "first"];

    /// <summary>今効いている効果1つ。Level は数を数えるクラスのレベル(能力をくれたクラスのもの)。</summary>
    public readonly record struct Active(ClassFeatureDef Def, int Level, PassiveEffectDef Effect);

    /// <summary>その人の、勝手に効く能力の今効いている効果(戦闘の外の様子で)。</summary>
    public static IEnumerable<Active> Effects(GameData data, Character c) => Effects(data, c, new Situation());

    /// <summary>その人の、勝手に効く能力の今効いている効果。now はその人の今の様子(戦闘なら PartyMember から作る)。</summary>
    public static IEnumerable<Active> Effects(GameData data, Character c, Situation now)
    {
        var owned = ClassFeatures.Owned(data, c).Where(x => x.Def.Kind == "passive")
            .Select(x => (x.Def, x.Level)).ToList();
        foreach (var group in owned.GroupBy(x => x.Def.Group ?? x.Def.Id))   // 並びはクラスを取った順のまま
        {
            var picked = group.First().Def.Stacks == "first"
                ? [group.First()]
                : group.GroupBy(x => x.Def.Id).Select(same => same.OrderByDescending(x => x.Level).First()).ToList();
            foreach (var (def, level) in picked)
            {
                if (!Holds(c, def.When, now, data)) continue;
                foreach (var e in def.Effects) yield return new Active(def, level, e);
            }
        }
    }

    /// <summary>その人に、その種類の効果が今効いているもの。</summary>
    public static IEnumerable<Active> Effects(GameData data, Character c, string type) => Effects(data, c, type, new Situation());

    /// <summary>その人に、その種類の効果が今効いているもの(今の様子で)。</summary>
    public static IEnumerable<Active> Effects(GameData data, Character c, string type, Situation now) =>
        Effects(data, c, now).Where(e => e.Effect.Type == type);

    /// <summary>
    /// target に届く、その種類の効果(target 自身の to: self と、立っている仲間(target も含む)の to: allies)。Source は効果を出す人(数はその人で数える)。
    /// allies は戦闘のパーティ(人と、立っているか)。
    /// </summary>
    public static List<(Character Source, Active Active)> Reaching(GameData data, Character target, Situation targetNow,
        IEnumerable<(Character Who, Situation Now)> allies, string type)
    {
        var got = Effects(data, target, type, targetNow).Where(e => e.Effect.To == "self").Select(e => (target, e)).ToList();
        foreach (var (who, now) in allies.Where(a => a.Now.Standing))
            got.AddRange(Effects(data, who, type, now).Where(e => e.Effect.To == "allies").Select(e => (who, e)));
        return got;
    }

    /// <summary>
    /// 数を足す効果(save_bonus など)の合計。まとまり(group。書かなければ id)ごとに数え、stacks が max のまとまりは一番大きいもの1つ、
    /// ほかは足す(別のまとまりのものや、own のものは重なる。守りのオーラはパーティに何人いても1つ分)。
    /// </summary>
    public static int Total(IEnumerable<(Character Source, Active Active)> effects) =>
        effects.GroupBy(x => x.Active.Def.Group ?? x.Active.Def.Id).Sum(g =>
        {
            var values = g.Select(x => x.Active.Effect.Value?.Eval(x.Source, x.Active.Level) ?? 0).ToList();
            return g.First().Active.Def.Stacks == "max" ? values.Max() : values.Sum();
        });

    /// <summary>
    /// 効果の候補から、まとまりごとの重なり方で効くものを選ぶ。stacks が max のまとまりは、value の一番大きいもの1つ(同じなら先のもの)、
    /// ほかはどれも残す(反応や追加ダメージのように、数を足さずに1つずつ使う効果のため)。
    /// </summary>
    public static IEnumerable<Active> PickByGroup(IEnumerable<Active> effects, Func<Active, double> value) =>
        effects.GroupBy(e => e.Def.Group ?? e.Def.Id).SelectMany(g =>
            g.First().Def.Stacks == "max" ? [g.OrderByDescending(value).First()] : g.AsEnumerable());

    /// <summary>
    /// stacks が max のまとまりの中で、大小を比べられない組み合わせ(種類の違う効果、反応の効く攻撃の違い)を検査で拾う。
    /// </summary>
    public static IEnumerable<string> CheckMaxGroups(IEnumerable<ClassFeatureDef> features)
    {
        foreach (var g in features.Where(f => f.Kind == "passive" && f.Stacks == "max").GroupBy(f => f.Group ?? f.Id).Where(g => g.Count() > 1))
        {
            var kinds = g.Select(f => string.Join("+", f.Effects.Select(e => $"{e.Type}:{e.On}").Order())).Distinct().ToList();
            if (kinds.Count > 1)
                yield return $"features のまとまり '{g.Key}': stacks が max なのに、効果の種類(と on)が違うものが混ざっています({string.Join(" / ", kinds)})。大きいものを選べません";
        }
    }

    /// <summary>効く場面に当たっているか。</summary>
    public static bool Holds(Character c, string when, Situation now, GameData data) => when switch
    {
        "always" => true,
        "no_armor" => c.Inventory[EquipSlot.Armor] == null,
        "no_armor_no_shield" => c.Inventory[EquipSlot.Armor] == null && !HoldingShield(c, data),
        "wearing_armor" => c.Inventory[EquipSlot.Armor] != null,
        "standing" => now.Standing,
        "raging" => now.Raging,
        "shaped" => now.Shaped,
        "not_blinded" => !now.Blinded,
        "not_incapacitated" => !now.Incapacitated,
        _ when when.StartsWith("grip:") => Grip(data, c) == when[5..],
        "has_ranged_weapon" => HasRangedWeapon(data, c),
        _ => false,
    };

    /// <summary>
    /// 利き手の装備の形(ファイティングスタイルの when の grip:)。次の順で1つに決まる: 利き手に武器が無ければ、遠隔の枠に飛び道具があれば ranged、
    /// 鎧を着ていれば none_armored、どちらも無ければ none。利き手が飛び道具なら ranged、もう片方の手にも武器なら two_weapons、
    /// 両手持ちか片手半をもう片方の手を空けて持つなら two_handed、ほかは one_handed。
    /// </summary>
    public static string Grip(GameData data, Character c)
    {
        var inv = c.Inventory;
        ItemDef? Weapon(EquipSlot slot) => inv[slot] is { } it && data.Items.TryGetValue(it.ItemId, out var d) && d.Kind == ItemKind.Weapon ? d : null;
        var main = Weapon(EquipSlot.MainHand);
        if (main == null) return Weapon(EquipSlot.Ranged) != null ? "ranged" : inv[EquipSlot.Armor] != null ? "none_armored" : "none";
        if (main.Ranged) return "ranged";
        if (Weapon(EquipSlot.OffHand) != null) return "two_weapons";
        if (main.Has("two_handed") || (main.VersatileDamage != null && inv[EquipSlot.OffHand] == null)) return "two_handed";
        return "one_handed";
    }

    /// <summary>利き手か遠隔の枠に、飛び道具を持っているか(弓術の when)。</summary>
    public static bool HasRangedWeapon(GameData data, Character c)
    {
        bool Ranged(EquipSlot slot) => c.Inventory[slot] is { } it && data.Items.TryGetValue(it.ItemId, out var d) && d.Kind == ItemKind.Weapon && d.Ranged;
        return Ranged(EquipSlot.MainHand) || Ranged(EquipSlot.Ranged);
    }

    /// <summary>
    /// その人に今効いている、その種類で on が合う効果の数の合計(ファイティングスタイルの命中・ダメージ、AC など)。Total と同じく、
    /// まとまりごとに数え、stacks が max のまとまりは一番大きいもの1つ。now はその人の今の様子(戦闘の激怒など。省けば戦闘の外)。
    /// </summary>
    public static int Sum(GameData data, Character c, string type, string? on = null, Situation? now = null) =>
        Total(Effects(data, c, type, now ?? new Situation()).Where(e => on == null || e.Effect.On == on).Select(e => (c, e)));

    private static bool HoldingShield(Character c, GameData data) =>
        c.Inventory[EquipSlot.OffHand] is { } held && data.Items.TryGetValue(held.ItemId, out var d) && d.Kind == ItemKind.Shield;

    /// <summary>能力の中身の検査(知らない when・効果・重なり方、効果に要る項目)。</summary>
    public static IEnumerable<string> Check(ClassFeatureDef f)
    {
        string where = $"features '{f.Id}'";
        if (!Whens.Contains(f.When)) yield return $"{where}: when '{f.When}' はありません({string.Join(" / ", Whens)})";
        if (!StacksValues.Contains(f.Stacks)) yield return $"{where}: stacks '{f.Stacks}' は own / max / first のどれかです";
        foreach (var e in f.Effects)
        {
            if (!EffectTypes.Contains(e.Type))
            {
                yield return $"{where} の effects: '{e.Type}' はありません({string.Join(" / ", EffectTypes)})";
                continue;
            }
            if (e.To is not ("self" or "allies")) yield return $"{where} の effects: to は self か allies です";
            string? bad = e.Type switch
            {
                "ac_ability" when !ValidAbility(e.Ability) => "ac_ability には ability(能力値)が要ります",
                "save_proficiency" when e.Ability != null && !ValidAbility(e.Ability) => "save_proficiency の ability は能力値です(書かなければ全部)",
                "save_bonus" when e.Value == null => "save_bonus には value が要ります",
                "save_bonus" when e.Ability != null && !ValidAbility(e.Ability) => "save_bonus の ability は能力値です(書かなければ全部のセーヴ)",
                "immune_condition" or "immune_magic_condition" when e.Condition == null => $"{e.Type} には condition が要ります",
                "advantage" when (e.Condition != null || e.On != null) && e.Roll != "save" => "advantage の condition と on は、roll が save のときだけ書けます",
                "advantage" when e.On is not (null or "magic") => "advantage の on は magic(魔法へのセーヴ)だけです",
                "advantage" when e.Condition != null && e.On != null => "advantage の condition と on は、どちらか1つです",
                "immune_damage" or "resist_damage" when string.IsNullOrEmpty(e.DamageType) => $"{e.Type} には damage_type が要ります",
                "advantage" when !Rolls.Contains(e.Roll ?? "") => $"advantage の roll は {string.Join(" / ", Rolls)} のどれかです",
                _ when OnsFor.TryGetValue(e.Type, out var ons) && !ons.Contains(e.On ?? "") => $"{e.Type} の on は {string.Join(" / ", ons)} のどれかです",
                "attack_bonus" or "damage_bonus" or "reroll_damage" or "ac_bonus" or "drain" or "crit_extra_dice" or "spell_damage_bonus" when e.Value == null
                    => $"{e.Type} には value が要ります",
                "extra_damage" when string.IsNullOrEmpty(e.Dice) || !Rules.DiceExpression.TryParse(e.Dice, out _, out _) => "extra_damage には dice(ダイスの書き方)が要ります",
                "extra_damage" when string.IsNullOrEmpty(e.DamageType) => "extra_damage には damage_type が要ります",
                "spell_damage_bonus" when string.IsNullOrEmpty(e.Spell) => "spell_damage_bonus には spell(呪文の id)が要ります",
                "reaction_reduce" when e.Dice == null && e.Value == null => "reaction_reduce には dice か value が要ります",
                "reaction_reduce" when e.Dice != null && !Rules.DiceExpression.TryParse(e.Dice, out _, out _) => "reaction_reduce の dice が読めません",
                "reaction_reduce" when e.Ability != null && !ValidAbility(e.Ability) => "reaction_reduce の ability は能力値です",
                "stay_up" when e.Pool == null && (!ValidAbility(e.Ability) || e.Value == null) => "stay_up には ability(セーヴの能力値)と value(最初の難易度)か、pool(使う回数)が要ります",
                "stay_up" when e.Pool != null && e.Ability != null => "stay_up の pool と ability は、どちらか1つです",
                "advantage" when e.Ability != null && (e.Roll != "save" || !ValidAbility(e.Ability)) => "advantage の ability は、roll が save のときの能力値です",
                "instant_kill" when e.Value == null => "instant_kill には value(倒す確率の百分率)が要ります",
                "resist_instant_kill" when e.Value == null => "resist_instant_kill には value(こらえる確率の百分率)が要ります",
                "instant_kill" when e.ResistBase < 0 || e.ResistDie < 0 || e.ResistBase > 0 && e.ResistDie == 0 => "instant_kill の resist_base は 0 以上、resist_die は 1 以上(抵抗させないなら両方とも書かない)です",
                _ when e.Type != "instant_kill" && (e.ResistBase != 0 || e.ResistDie != 0 || e.NoBoss) => "resist_base・resist_die・no_boss は instant_kill だけに書けます",
                _ => null,
            };
            if (bad != null) yield return $"{where} の effects: {bad}";
        }
    }

    internal static bool ValidAbility(string? code)
    {
        if (code == null) return false;
        try { Abilities.Parse(code); return true; }
        catch (FormatException) { return false; }
    }
}
