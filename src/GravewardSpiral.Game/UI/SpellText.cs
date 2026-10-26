using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Game.Text;

namespace GravewardSpiral.Game.UI;

/// <summary>呪文の説明の文(データから作る。SRD の説明文は写さない)。</summary>
public static class SpellText
{
    public static string Level(Strings s, SpellDef sp) => sp.IsCantrip ? s["spell.cantrip"] : s.Format("spell.level", sp.Level);

    /// <summary>
    /// 唱える一覧の1行。呪文の種類のアイコンを付け、術者のクラスが2つ以上なら、どのクラスの呪文かの印も付ける
    /// (クラスのアイコンが無ければクラスの名前)。
    /// </summary>
    public static MenuItem CastItem(GameServices svc, Character c, SpellDef sp, string? detail, bool enabled)
    {
        var item = new MenuItem(svc.Name(sp.Name), detail, enabled, sp, Icon: svc.SpellIcon(sp));
        if (Spellcasting.CasterClasses(svc.Data, c).Count() < 2 || Spellcasting.ClassFor(svc.Data, c, sp.Id) is not { } cls) return item;
        var badge = svc.ClassIcon(cls.Id);
        return item with { Badge = badge, BadgeText = badge == null ? svc.Name(cls.Name) : null };
    }

    /// <summary>「Lv1 力術 / 敵1グループ / 集中」のような1行。</summary>
    public static string Header(Strings s, SpellDef sp)
    {
        var parts = new List<string> { Level(s, sp), s["school." + sp.School] };
        if (sp.Target is { } t) parts.Add(s["spell.target." + t.ToString().ToLowerInvariant()]);
        if (sp.Concentration) parts.Add(s["spell.concentration"]);
        if (sp.Time == "bonus") parts.Add(s["spell.bonus"]);
        return string.Join(" / ", parts);
    }

    /// <summary>効果を短い文で並べる(レベル level で唱えたときのダイス)。</summary>
    public static IEnumerable<string> Effects(Strings s, SpellDef sp, int level)
    {
        if (sp.Attack != null) yield return s["spell.attack_" + sp.Attack];
        foreach (var line in Parts(s, sp.Effects, level)) yield return line;
    }

    private static IEnumerable<string> Parts(Strings s, IEnumerable<EffectPart> parts, int level)
    {
        foreach (var p in parts)
        {
            string dice = (p.DiceAt(level) ?? "").Replace("MOD", s["spell.mod"]);
            switch (p.Type)
            {
                case EffectPart.Damage:
                    yield return s.Format("spell.fx.damage", dice, s["damage." + p.DamageType]);
                    break;
                case EffectPart.Heal:
                    yield return s.Format("spell.fx.heal", dice);
                    break;
                case EffectPart.TempHp:
                    yield return s.Format("spell.fx.temp_hp", dice);
                    break;
                case EffectPart.Modifier:
                    yield return s.Format("spell.fx.modifier", s["stat." + p.Stat], p.Dice ?? (p.Value >= 0 ? $"+{p.Value}" : $"{p.Value}"));
                    break;
                case EffectPart.ConditionType:
                    yield return s.Format("spell.fx.condition", s["condition." + p.Condition.ToString()!.ToLowerInvariant()]);
                    break;
                case EffectPart.HpPool:
                    yield return s.Format("spell.fx.hp_pool", dice, s["condition." + p.Condition.ToString()!.ToLowerInvariant()]);
                    break;
                case EffectPart.RemoveCondition:
                    yield return s.Format("spell.fx.remove", s["condition." + p.Condition.ToString()!.ToLowerInvariant()]);
                    break;
                case EffectPart.Stabilize:
                    yield return s["spell.fx.stabilize"];
                    break;
                case EffectPart.Kill:
                    yield return s.Format("spell.fx.kill", p.MaxHp ?? 0);
                    break;
                case EffectPart.Wish:
                    yield return s["spell.fx.wish"];
                    break;
                case EffectPart.Save:
                    yield return s.Format(p.Half ? "spell.fx.save_half" : "spell.fx.save", s["ability." + p.Ability]);
                    foreach (var inner in Parts(s, p.Fail, level)) yield return "  " + inner;
                    break;
            }
        }
    }

    /// <summary>スロットの残りの1行(「1: 2/2  2: 1/3」)。術者でなければ null。</summary>
    public static string? Slots(Strings s, GameData data, Character c) =>
        SlotLines(s, data, c, int.MaxValue) is { } lines ? string.Join("  ", lines) : null;

    /// <summary>
    /// スロットの残りを、perLine レベルずつの行に分けたもの(高いレベルの術者で、横にはみ出さないように)。
    /// 1行目に「スロット」を付け、2行目からは字下げする。術者でなければ null。
    /// </summary>
    public static List<string>? SlotLines(Strings s, GameData data, Character c, int perLine = 5)
    {
        if (Spellcasting.CasterClass(data, c) is not { } cls) return null;
        var parts = Enumerable.Range(1, 9).Where(l => Spellcasting.MaxSlots(cls, c, l) > 0)
            .Select(l => $"{l}: {Spellcasting.SlotsLeft(cls, c, l)}/{Spellcasting.MaxSlots(cls, c, l)}").ToList();
        if (parts.Count == 0) return [s["spell.no_slots"]];
        var lines = parts.Chunk(perLine).Select(chunk => string.Join("  ", chunk)).ToList();
        string head = s.Format("spell.slots", lines[0]);
        // 頭の「スロット」の幅だけ字下げ(全角の字は半角の空白2つぶん)
        string indent = string.Concat(head[..(head.Length - lines[0].Length)].Select(ch => ch < 128 ? " " : "  "));
        return [head, .. lines.Skip(1).Select(l => indent + l)];
    }

}
