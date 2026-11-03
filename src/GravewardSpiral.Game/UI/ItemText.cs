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
        if (!item.Identified && item.IsMagicItem(def))
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
    /// レアの品は追加効果が多いので、語をつないで作った名前にする(「凶ツ牙のロングソード」。効果は説明の行で見せる)。
    /// </summary>
    private static string WithAffixes(GameServices sv, ItemInstance item, ItemDef def)
    {
        string baseName = sv.Name(def.Name);
        if (def.NoRandom || item.Affixes.Count == 0) return baseName;
        if (item.Rare) return RareName(sv, item, baseName);
        var affixes = item.Affixes.Select(a => sv.Data.Affixes.GetValueOrDefault(a.Id)).OfType<AffixDef>().ToList();
        // 日本語のように、追加効果の語を全部前に付ける言語(text の item.affix_order が "before")
        if (sv.Strings["item.affix_order"] == "before") return string.Concat(affixes.Select(a => sv.Name(a.Name) + " ")) + baseName;
        var pre = affixes.Where(a => a.Position == "prefix").Select(a => sv.Name(a.Name) + " ");
        var suf = affixes.Where(a => a.Position == "suffix").Select(a => " " + sv.Name(a.Name));
        return string.Concat(pre) + baseName + string.Concat(suf);
    }

    /// <summary>
    /// レアの品の名前。前の語と後ろの語(text の item.rare_first / item.rare_second。読点区切り)を、品と追加効果から決まった形で選ぶ
    /// (同じ品はいつも同じ名前。セーブには持たない)。
    /// </summary>
    private static string RareName(GameServices sv, ItemInstance item, string baseName)
    {
        var first = sv.Strings["item.rare_first"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var second = sv.Strings["item.rare_second"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (first.Length == 0 || second.Length == 0) return baseName;
        uint h = 2166136261;   // FNV-1a(string.GetHashCode は起動ごとに変わるので使わない)
        foreach (char ch in item.ItemId + "|" + string.Join(",", item.Affixes.Select(a => $"{a.Id}#{a.Tier}")))
            h = (h ^ ch) * 16777619;
        return sv.Strings.Format("item.rare_name", first[h % (uint)first.Length], second[h / (uint)first.Length % (uint)second.Length], baseName);
    }

    /// <summary>
    /// 段階の通しの番号(T1 は L1、T5 は L13。どの追加効果も同じ段の並びから段階を作るので、出始めるレベルの並びの何番目か)。
    /// </summary>
    private static int TierNumber(Core.Characters.GameData data, int minLevel)
    {
        if (_tierLevels is not { } c || !ReferenceEquals(c.Data, data))
            _tierLevels = c = (data, data.Affixes.Values.SelectMany(a => a.Tiers).Select(t => t.MinLevel).Distinct().Order().ToList());
        return c.Levels.IndexOf(minLevel) + 1;
    }

    private static (Core.Characters.GameData Data, List<int> Levels)? _tierLevels;

    /// <summary>効果の文の中の数字(「+2」「1d6」「15%」など。続くラウンドの数は弱まらないので除く)。弱まった数字に色を付けるのに使う。</summary>
    private static readonly System.Text.RegularExpressions.Regex Numbers = new(@"[+\-−]?\d+(d\d+)?%?(?!\d|\s*(ラウンド|round))");

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
        if (!item.Identified && item.IsMagicItem(def))
        {
            yield return s["item.not_identified"];
            if (WhoCanUse(sv, def) is { } guess) yield return guess;   // 何の武器・鎧かは見えているので、誰が使えるかは分かる
            yield break;
        }
        if (item.IsMagicItem(def)) yield return s["rarity." + Treasure.GradeOf(def, item)];
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
        // マジックとレアの、追加効果の効き方(下の行の数字は、今の効き方で弱めたもの)。レアは同調の行と1行にまとめる
        bool affixed = !def.NoRandom && item.Affixes.Count > 0;
        // 弱まった数字の色(マジックの 2/3 は黄、同調していないレアの 1/3 は橙)。1行目の「2/3」「1/3」も同じ色にして、色の見方にする
        char? weak = affixed ? item.AffixFactor(def) switch { < 0.5 => LinkText.OneThirdColor, < 0.99 => LinkText.TwoThirdsColor, _ => null } : null;
        if (affixed) yield return s[!item.Rare ? "item.magic_affixes" : item.Attuned ? "item.rare_attuned" : "item.rare_affixes"]
            .Replace("2/3", LinkText.Colored("2/3", LinkText.TwoThirdsColor)).Replace("1/3", LinkText.Colored("1/3", LinkText.OneThirdColor));
        else if (item.NeedsAttunement(def)) yield return s[item.Attuned ? "item.attuned" : "item.needs_attunement"];
        if (def.Cursed) yield return s["item.cursed"];
        foreach (var p in def.Passive) yield return Passive(sv, p);
        foreach (var p in def.OnHit) if (OnHit(sv, p) is { } line) yield return line;
        // 追加効果(どの語で、何が付いたか)。持っている仲間がいれば、その人のレベルで届く段階に抑えた数字
        int? level = sv.Session?.Roster.PartyMembers.FirstOrDefault(c => c.Inventory.Items.Contains(item) || c.Inventory.Equipped.ContainsValue(item))?.Level;
        foreach (var roll in item.Affixes)
        {
            if (!sv.Data.Affixes.TryGetValue(roll.Id, out var affix) || roll.Get(sv.Data) is null) continue;
            var parts = item.AffixParts(sv.Data, roll, t => t.Passive, level).Select(p => Passive(sv, p))
                .Concat(item.AffixParts(sv.Data, roll, t => t.OnHit, level).Select(p => OnHit(sv, p)).OfType<string>()).ToList();
            // 段階は「T3」。着ている人のレベルで抑えたときは「T1/T3」(付いた段階の「/T3」は暗く)。まだどの段階も届かなければ「T0/T3」
            int usable = ItemInstance.UsableTier(sv.Data, roll, level);
            string full = "T" + TierNumber(sv.Data, affix.Tiers[roll.Tier].MinLevel);
            string tier = usable >= roll.Tier ? full
                : $"T{(usable < 0 ? 0 : TierNumber(sv.Data, affix.Tiers[usable].MinLevel))}{LinkText.Colored("/" + full, LinkText.DimColor)}";
            if (weak is char w) parts = parts.Select(x => Numbers.Replace(x, m => LinkText.Colored(m.Value, w))).ToList();
            string text;
            if (parts.Count > 0) text = string.Join(s["item.affix_sep"], parts);
            else
            {
                // 効かない(同調していない・弱めて 0・まだどの段階にも届かない)ときは、本来の効果をグレーで(届かないなら一番下の段階の効果)
                int? at = usable < 0 ? affix.Tiers[0].MinLevel : level;
                var whole = item.AffixParts(sv.Data, roll, t => t.Passive, at, full: true).Select(p => Passive(sv, p))
                    .Concat(item.AffixParts(sv.Data, roll, t => t.OnHit, at, full: true).Select(p => OnHit(sv, p)).OfType<string>()).ToList();
                text = whole.Count > 0 ? LinkText.Colored(string.Join(s["item.affix_sep"], whole), LinkText.DimColor) : s["item.affix_none"];
            }
            yield return s.Format("item.affix", sv.Name(affix.Name).Trim(), tier, text);
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
        if (def.Description != null && !(affixed && !def.IsMagic)) yield return sv.Name(def.Description);   // 元の品(ただの指輪など)の説明は出さない
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
        if (p.Type == EffectPart.Kill && p.Chance is int chance) return s.Format("item.kill_chance", chance);   // 首はねの武器
        if (p.Type == EffectPart.Dot) return s.Format("item.on_hit_dot", p.Dice ?? "", s["damage." + p.DamageType], p.Rounds ?? 10);
        if (p.Type != EffectPart.Save) return null;
        var fail = p.Fail.Select(f => f.Type switch
        {
            EffectPart.Damage => s.Format("item.fail_damage", f.Dice ?? "", s["damage." + f.DamageType]),
            EffectPart.ConditionType => s["condition." + f.Condition.ToString()!.ToLowerInvariant()],
            EffectPart.Kill => f.Chance is int c ? s.Format("item.kill_chance", c) : s.Format("item.fail_kill", f.MaxHp ?? 0),
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
