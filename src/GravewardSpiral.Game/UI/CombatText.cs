using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Magic;
using Microsoft.Xna.Framework;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Game.UI;

/// <summary>戦闘のログの1行を、画面の文言にする(戦闘とキャンプで使う)。</summary>
public static class CombatText
{
    public static string Format(GameServices sv, CombatLogEntry e)
    {
        var s = sv.Strings;
        var args = e.Args.Select((a, i) => a switch
        {
            LocalizedText t => sv.Name(t),
            Condition c => s["condition." + c.ToString().ToLowerInvariant()],
            Ability ab => s["ability." + ab.Code()],
            string str when e.Key == "combat.damage" && i == 2 => s["damage." + str],
            string str when e.Key is "combat.dot" or "combat.dot_on" or "combat.dot_end" && i == 1 => s["damage." + str],
            string str when str.StartsWith("stat.") => s[str],
            string str when str.StartsWith("feature.") || str.StartsWith("passive.") || str.StartsWith("combat.src.") => s[str],
            _ => a,
        }).ToArray();
        // 命中とダメージの行の4つめは出どころ(急所攻撃・ボーナスアクションなど、おまけの分だけ)。括弧で添える形の文言にする
        string key = e.Key is "combat.damage" or "combat.hit" or "combat.crit" or "combat.miss" && args.Length == 4 ? e.Key + "_labeled" : e.Key;
        return s.Format(key, args);
    }

    /// <summary>
    /// 呪文の属性の色(火=赤、冷気=水色、力場=オレンジ、光輝=黄色など)。物理のダメージと、呪文でない行は null。
    /// 唱えた行は、その呪文のダメージの種類(回復の呪文は緑)の色にする。
    /// </summary>
    public static Color? ColorOf(GameData data, CombatLogEntry e) => e.Key switch
    {
        "combat.damage" when e.Args.Length > 2 && e.Args[2] is string type => Element(type),
        "combat.dot" or "combat.dot_on" when e.Args.Length > 1 && e.Args[1] is string dt => Element(dt),
        "combat.heal" or "combat.revive" or "combat.temp_hp" or "combat.drain" => Heal,
        "combat.cast" when e.Args.Length > 1 && e.Args[1] is LocalizedText name => SpellColor(data, name),
        _ => null,
    };

    private static readonly Color Heal = new(140, 235, 150);

    public static Color? Element(string damageType) => damageType switch
    {
        "fire" => new Color(255, 110, 90),
        "cold" => new Color(130, 215, 255),
        "force" => new Color(255, 170, 70),
        "radiant" => new Color(255, 230, 110),
        "lightning" => new Color(170, 170, 255),
        "thunder" => new Color(160, 190, 220),
        "acid" => new Color(190, 235, 90),
        "poison" => new Color(120, 210, 110),
        "necrotic" => new Color(190, 130, 230),
        "psychic" => new Color(250, 140, 210),
        _ => null,   // 物理(斬撃・刺突・殴打)は色を付けない
    };

    private static Color? SpellColor(GameData data, LocalizedText name)
    {
        var sp = data.Spells.Values.FirstOrDefault(x => x.Name.Get("en") == name.Get("en"));
        if (sp == null) return null;
        var parts = sp.Effects.SelectMany(x => x.Flatten()).ToList();
        if (parts.FirstOrDefault(x => x.Type == EffectPart.Damage)?.DamageType is { } t) return Element(t) ?? new Color(220, 200, 255);
        if (parts.Any(x => x.Type is EffectPart.Heal or EffectPart.TempHp)) return Heal;
        return new Color(200, 190, 255);   // ダメージの無い呪文(状態・修正値)は薄い紫
    }
}
