using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Game.Text;

namespace GravewardSpiral.Game.UI;

/// <summary>アイテムの名前と説明の文(未鑑定なら仮の名前)。</summary>
public static class ItemText
{
    /// <summary>
    /// 見える名前。鑑定していない魔法の品は仮の名前(武器・鎧は元の品の名前に「?」、装身具は「指輪?」など)。
    /// 鑑定した +N の品は「+1 ロングソード」。重ねて持っているものは「×3」を付ける。
    /// </summary>
    public static string Name(GameServices sv, ItemInstance item, bool count = true)
    {
        item = item.Apparent(sv.Data.Items);
        if (!sv.Data.Items.TryGetValue(item.ItemId, out var def)) return item.ItemId;
        string name;
        if (!item.Identified && (def.IsMagic || item.Bonus != 0))
        {
            name = def.Base != null && sv.Data.Items.TryGetValue(def.Base, out var b) ? sv.Name(b.Name) + "?"
                : def.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield ? sv.Name(def.Name) + "?"
                : def.Id.StartsWith("scroll_") ? sv.Strings["item.unknown.scroll"]
                : def.Id.StartsWith("potion_") ? sv.Strings["item.unknown.potion"]
                : sv.Strings[def.UnknownNameKey];
        }
        else
        {
            int bonus = item.TotalBonus(def) - def.Bonus;   // 名前つきの品の Bonus は名前に含めない
            name = (bonus > 0 ? $"+{bonus} " : bonus < 0 ? $"{bonus} " : "") + WithAffixes(sv, item, def);
        }
        if (item.Charges is int ch && item.Identified) name += $" [{ch}]";
        return count && item.Count > 1 ? $"{name} ×{item.Count}" : name;
    }

    /// <summary>
    /// 追加効果の語を付けた名前(「燃える 守りの ロングソード」/ "Flaming Longsword of Warding")。
    /// 固有の品(no_random)は、名前をそのまま出す(追加効果は説明の行で見せる)。
    /// </summary>
    private static string WithAffixes(GameServices sv, ItemInstance item, ItemDef def)
    {
        string baseName = sv.Name(def.Name);
        if (def.NoRandom || item.Affixes.Count == 0) return baseName;
        var affixes = item.Affixes.Select(a => sv.Data.Affixes.GetValueOrDefault(a.Id)).OfType<AffixDef>().ToList();
        // 日本語のように、追加効果の語を全部前に付ける言語(text の item.affix_order が "before")
        if (sv.Strings["item.affix_order"] == "before") return string.Concat(affixes.Select(a => sv.Name(a.Name) + " ")) + baseName;
        var pre = affixes.Where(a => a.Position == "prefix").Select(a => sv.Name(a.Name) + " ");
        var suf = affixes.Where(a => a.Position == "suffix").Select(a => " " + sv.Name(a.Name));
        return string.Concat(pre) + baseName + string.Concat(suf);
    }

    /// <summary>武器の持ち方の短い印(片手・両手・片手か両手)。武器でなければ null。</summary>
    public static string? Hands(GameServices sv, ItemInstance item)
    {
        if (!sv.Data.Items.TryGetValue(item.ItemId, out var def) || def.Kind != ItemKind.Weapon) return null;
        return sv.Strings[def.Has("two_handed") ? "item.hands_two_short" : def.VersatileDamage != null ? "item.hands_versatile_short" : "item.hands_one_short"];
    }

    /// <summary>説明の行(鑑定していない品は、ほとんど分からない)。</summary>
    public static IEnumerable<string> Lines(GameServices sv, ItemInstance item)
    {
        var s = sv.Strings;
        item = item.Apparent(sv.Data.Items);
        if (!sv.Data.Items.TryGetValue(item.ItemId, out var def)) yield break;
        if (!item.Identified && (def.IsMagic || item.Bonus != 0))
        {
            yield return s["item.not_identified"];
            if (WhoCanUse(sv, def) is { } guess) yield return guess;   // 何の武器・鎧かは見えているので、誰が使えるかは分かる
            yield break;
        }
        if (def.IsMagic || item.Bonus != 0) yield return s["rarity." + (item.Bonus > 0 && !def.IsMagic ? Treasure.RarityOf(item.Bonus, def.Kind) : def.Rarity)];
        int bonus = item.TotalBonus(def);
        switch (def.Kind)
        {
            case ItemKind.Weapon:
                yield return s.Format("item.weapon", def.Damage ?? "-", s["damage." + def.DamageType], s["weapon." + (def.Category ?? "simple")]);
                yield return def.Has("two_handed") ? s["item.hands_two"]
                    : def.VersatileDamage != null ? s.Format("item.hands_versatile", def.VersatileDamage) : s["item.hands_one"];
                if (def.Has("finesse")) yield return s["item.finesse"];   // 技巧: 筋力か敏捷力の高い方
                if (def.Has("light")) yield return s["item.light"];       // 軽い: 両手に持つと二刀流
                if (def.Has("returning")) yield return s["item.returning"];
                else if (def.Has("thrown")) yield return s["item.thrown"];
                if (bonus != 0) yield return s.Format("item.bonus_attack", Signed(ItemInstance.HitAcBonus(bonus)), Signed(bonus));
                break;
            case ItemKind.Armor:
                yield return s.Format("item.armor", def.ArmorClass + ItemInstance.HitAcBonus(bonus), s["armor." + def.ArmorType.ToString()!.ToLowerInvariant()]);
                // 敏捷力の修正の足し方(重装鎧は足さない、中装鎧は上限まで、軽装鎧はそのまま)
                yield return def.DexCap is 0 ? s["item.dex_none"] : def.DexCap is int cap ? s.Format("item.dex_cap", cap) : s["item.dex_full"];
                if (def.StealthDisadvantage) yield return s["item.stealth_disadvantage"];
                break;
            case ItemKind.Shield:
                yield return s.Format("item.shield", Signed(def.ArmorClass + ItemInstance.HitAcBonus(bonus)));
                break;
            case ItemKind.Backpack:
                yield return s.Format("item.slots", def.Slots);
                break;
            case ItemKind.Ammo:
                yield return s.Format("item.ammo", s["ammo." + def.AmmoType]);
                int hit = ItemInstance.HitAcBonus(bonus) + def.AmmoHit, dmg = bonus + def.AmmoDamage;
                if (hit != 0 || dmg != 0) yield return s.Format("item.ammo_bonus", Signed(hit), Signed(dmg));
                break;
        }
        if (WhoCanUse(sv, def) is { } who) yield return who;
        if (def.Attunement) yield return s[item.Attuned ? "item.attuned" : "item.needs_attunement"];
        if (def.Cursed) yield return s["item.cursed"];
        foreach (var p in def.Passive) yield return Passive(sv, p);
        foreach (var p in def.OnHit) if (OnHit(sv, p) is { } line) yield return line;
        // 追加効果(どの語で、何が付いたか)
        foreach (var roll in item.Affixes)
        {
            if (!sv.Data.Affixes.TryGetValue(roll.Id, out var affix) || roll.Get(sv.Data) is not { } tier) continue;
            var parts = tier.Passive.Select(p => Passive(sv, p)).Concat(tier.OnHit.Select(p => OnHit(sv, p)).OfType<string>());
            yield return s.Format("item.affix", sv.Name(affix.Name).Trim(), string.Join(s["item.affix_sep"], parts));
        }
        // 使える回数(残り/最大と戻り方)
        if (def.Charges is int max) yield return s.Format("item.charges", item.Charges ?? max, max);
        if (def.UseSpell != null && sv.Data.Spells.TryGetValue(def.UseSpell, out var sp))
        {
            yield return s.Format("item.use_spell", sv.Name(sp.Name));
            // 唱える呪文の効果(道具が唱えるレベルで。戦闘で使うときと同じ)
            int useLevel = Math.Max(1, Math.Max(sp.Level, def.UseSlot));
            foreach (var line in SpellText.Effects(s, sp, useLevel)) yield return s["item.use"] + line;
        }
        foreach (var line in SpellText.Effects(s, new Core.Magic.SpellDef { Effects = def.Use }, 1)) yield return s["item.use"] + line;
        if (def.Description != null) yield return sv.Name(def.Description);
        yield return s.Format("item.price", Treasure.Price(def, item));
    }

    /// <summary>
    /// 武器・鎧・盾を、今のパーティの誰が習熟ありで使えるか(鎧と盾は、習熟が無いと着られない。武器は持てるが、習熟ボーナスが付かない)。
    /// パーティがいなければ(タイトルの前など) null。
    /// </summary>
    public static string? WhoCanUse(GameServices sv, ItemDef def)
    {
        if (def.Kind is not (ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield) || sv.Session?.Roster.PartyMembers.ToList() is not { Count: > 0 } party) return null;
        var s = sv.Strings;
        var ok = party.Where(c => c.Proficient(sv.Data, def)).Select(c => c.Name).ToList();
        string key = def.Kind == ItemKind.Weapon ? "item.proficient_members" : "item.can_wear_members";
        return s.Format(key, ok.Count == 0 ? s["item.nobody_in_party"] : ok.Count == party.Count ? s["item.whole_party"] : s.List(ok));
    }

    /// <summary>その人に習熟の無い武器・鎧・盾なら、その注意の1行(武器は持てるが命中に習熟ボーナスが付かない。鎧と盾は付けられない)。</summary>
    public static string? ProficiencyNote(GameServices sv, ItemInstance item, Core.Characters.Character who)
    {
        if (!sv.Data.Items.TryGetValue(item.ItemId, out var def)) return null;
        if (def.Kind == ItemKind.Weapon && !who.WeaponProficient(sv.Data, def)) return sv.Strings.Format("item.no_weapon_prof_for", who.Name);
        if (def.Kind is ItemKind.Armor or ItemKind.Shield && !who.CanWear(sv.Data, def)) return sv.Strings.Format("item.no_armor_prof_for", who.Name);
        return null;
    }

    /// <summary>当たったときの効果の1行(ダメージと、セーヴに失敗したときの効果)。書けないものは null。</summary>
    private static string? OnHit(GameServices sv, EffectPart p)
    {
        var s = sv.Strings;
        if (p.Type == EffectPart.Damage)
            return s.Format(p.Only != null ? "item.on_hit_only" : "item.on_hit", p.Dice ?? "", s["damage." + p.DamageType], p.Only != null ? s["creature." + p.Only] : "");
        if (p.Type == EffectPart.Drain) return s.Format("item.on_hit_drain", p.Value ?? 100);
        if (p.Type == EffectPart.Dot) return s.Format("item.on_hit_dot", p.Dice ?? "", s["damage." + p.DamageType], p.Rounds ?? 10);
        if (p.Type != EffectPart.Save) return null;
        var fail = p.Fail.Select(f => f.Type switch
        {
            EffectPart.Damage => s.Format("item.fail_damage", f.Dice ?? "", s["damage." + f.DamageType]),
            EffectPart.ConditionType => s["condition." + f.Condition.ToString()!.ToLowerInvariant()],
            EffectPart.Kill => s.Format("item.fail_kill", f.MaxHp ?? 0),
            _ => null,
        }).OfType<string>();
        return s.Format("item.on_hit_save", s["ability." + p.Ability], p.Dc, string.Join(s["item.affix_sep"], fail));
    }

    private static string Passive(GameServices sv, EffectPart p)
    {
        var s = sv.Strings;
        return p.Type switch
        {
            EffectPart.Modifier => s.Format("spell.fx.modifier", s["stat." + p.Stat], p.Dice ?? Signed(p.Value ?? 0)),
            EffectPart.Resist => s.Format("item.resist", s["damage." + p.DamageType]),
            EffectPart.Vulnerable => s.Format("item.vulnerable", s["damage." + p.DamageType]),
            EffectPart.ImmuneCondition => s.Format("item.immune", s["condition." + p.Condition.ToString()!.ToLowerInvariant()]),
            EffectPart.SetAbility => s.Format("item.set_ability", s["ability." + p.Ability], p.Value ?? 0),
            _ => p.Type,
        };
    }

    private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();
}
