using System.Text.Json;
using System.Text.Json.Nodes;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Monsters;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// 下書きの JSON から、中身の一覧を作る(AI に頼む窓で、JSON の上に出す)。作るのはエディタなので、AI からの報告と違って必ず実データどおり。
/// 品とモンスターは extends を解いて(元の上に書いた項目を重ねて)から読む。JSON を読めない人の最後の確認にも使う。
/// </summary>
public sealed class DraftSummary(EditorContext context)
{
    private static readonly Dictionary<string, Dictionary<string, string>> Words = [];

    /// <summary>標準のパックの文言(状態・ダメージの種類・能力値などの名前)。</summary>
    private string Word(string key, string fallback)
    {
        string lang = En ? "en" : "ja";
        if (!Words.TryGetValue(lang, out var words))
        {
            string file = Path.Combine(context.ContentRoot, "srd", "text", lang + ".json");
            try { words = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [] : []; }
            catch (JsonException) { words = []; }
            Words[lang] = words;
        }
        return words.GetValueOrDefault(key) ?? fallback;
    }

    public string DamageType(string? type) => type == null ? "" : Word("damage." + type, type);
    public string ConditionName(string name) => Word("condition." + name, name);
    private string ConditionName(Condition c) => ConditionName(JsonNamingPolicy.SnakeCaseLower.ConvertName(c.ToString()));
    private string Ability(string? a) => a == null ? "" : Word("ability." + a, a);
    private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();

    private static string KindName(ItemKind k) => k switch
    {
        ItemKind.Weapon => T("武器", "Weapon"), ItemKind.Armor => T("鎧", "Armor"), ItemKind.Shield => T("盾", "Shield"), ItemKind.Backpack => T("袋", "Backpack"),
        ItemKind.Consumable => T("使い切り", "Consumable"), ItemKind.Gear => T("道具", "Gear"), ItemKind.Accessory => T("装身具", "Accessory"),
        ItemKind.Key => T("大事な品", "Key item"), ItemKind.Ammo => T("矢弾", "Ammo"), _ => k.ToString(),
    };

    // ---------------- 品 ----------------

    /// <summary>品の一覧。</summary>
    public List<string> Item(JsonObject draft)
    {
        var lines = new List<string>();
        if (Read<ItemDef>("items", draft) is not { } item) return [T("(読めない)", "(cannot read)")];
        lines.Add($"{Loc.Name(item.Name)}" + ((string?)draft["extends"] is { } ext ? T($"(元: {ItemName(ext)} を写して変える)", $" (based on {ItemName(ext)})") : ""));
        lines.Add(T($"種類: {KindName(item.Kind)}　等級: {Word("rarity." + item.Rarity, item.Rarity)}　値段: {item.Price:0.##} G", $"Kind: {KindName(item.Kind)}  Rarity: {Word("rarity." + item.Rarity, item.Rarity)}  Price: {item.Price:0.##} G")
            + (item.NoRandom ? T("　イベント品(宝の抽選・商店に出ない)", "  Event-only (never in treasure or shops)") : ""));
        var flags = new List<string>();
        if (item.Attunement) flags.Add(T("同調が要る", "needs attunement"));
        if (item.Cursed) flags.Add(T("呪われている(外せない)", "cursed (cannot be removed)"));
        if (item.Only != null) flags.Add(T($"{item.Only} だけが使える", $"usable only by {item.Only}"));
        if (item.LooksLike != null) flags.Add(T("鑑定するまで別の品に見える", "looks like another item until identified"));
        if (flags.Count > 0) lines.Add(string.Join(T("、", ", "), flags));
        if (item.Damage != null)
            lines.Add(T("武器: ", "Weapon: ") + $"{item.Damage} {DamageType(item.DamageType)}" + (item.Bonus != 0 ? T($"、命中とダメージ {Signed(item.Bonus)}", $", {Signed(item.Bonus)} to hit and damage") : "")
                + (item.Properties.Count > 0 ? $"({string.Join(", ", item.Properties)})" : ""));
        if (item.ArmorClass > 0)
            lines.Add(T("守り: ", "Defense: ") + $"AC {item.ArmorClass}" + (item.Bonus != 0 && item.Damage == null ? $" {Signed(item.Bonus)}" : "")
                + (item.StealthDisadvantage ? T("(隠密に不利)", " (stealth disadvantage)") : ""));
        if (item.Kind == ItemKind.Ammo) lines.Add(T($"矢弾: 命中 {Signed(item.AmmoHit)}、ダメージ {Signed(item.AmmoDamage)}", $"Ammo: {Signed(item.AmmoHit)} to hit, {Signed(item.AmmoDamage)} damage"));
        if (item.OnHit.Count > 0) lines.Add(T("当たると: ", "On hit: ") + Effects(item.OnHit));
        if (item.Passive.Count > 0) lines.Add(T("装備すると: ", "While equipped: ") + Effects(item.Passive));
        if (item.UseSpell != null) lines.Add(T($"使うと: 呪文 {SpellName(item.UseSpell)}", $"Use: casts {SpellName(item.UseSpell)}") + (item.Charges is { } c ? T($"(チャージ {c})", $" ({c} charges)") : ""));
        else if (item.Use.Count > 0) lines.Add(T("使うと: ", "Use: ") + Effects(item.Use) + (item.Charges is { } c ? T($"(チャージ {c})", $" ({c} charges)") : ""));
        return lines;
    }

    // ---------------- モンスター ----------------

    /// <summary>モンスターの一覧。</summary>
    public List<string> Monster(JsonObject draft)
    {
        if (Read<MonsterDef>("monsters", draft) is not { } m) return [T("(読めない)", "(cannot read)")];
        var lines = new List<string>
        {
            $"{Loc.Name(m.Name)}" + ((string?)draft["extends"] is { } ext ? T($"(元: {MonsterName(ext)} を写して変える)", $" (based on {MonsterName(ext)})") : ""),
            $"CR {m.Cr:0.###}　HP {m.HpAverage}({m.Hp})　AC {m.Ac}　" + T($"経験値 {m.Xp}　出る数 {m.Appearing}", $"XP {m.Xp}  Appearing {m.Appearing}"),
        };
        if (m.Resist.Count > 0) lines.Add(T("耐性: ", "Resists: ") + string.Join(T("・", ", "), m.Resist.Select(DamageType)));
        if (m.Vulnerable.Count > 0) lines.Add(T("弱点: ", "Vulnerable: ") + string.Join(T("・", ", "), m.Vulnerable.Select(DamageType)));
        if (m.Immune.Count > 0) lines.Add(T("無効: ", "Immune: ") + string.Join(T("・", ", "), m.Immune.Select(DamageType)));
        if (m.ConditionImmune.Count > 0) lines.Add(T("状態無効: ", "Condition immunities: ") + string.Join(T("・", ", "), m.ConditionImmune.Select(ConditionName)));
        if (m.Regenerate > 0)
            lines.Add(T($"再生: 番の初めに HP {m.Regenerate}", $"Regeneration: {m.Regenerate} HP at the start of its turn")
                + (m.RegenerateStop.Count > 0 ? T($"({string.Join("・", m.RegenerateStop.Select(DamageType))}を受けた次の番は止まる)", $" (stopped for a turn by {string.Join(", ", m.RegenerateStop.Select(DamageType))})") : ""));
        if (m.Traits.Count > 0)
            lines.Add(T("特徴: ", "Traits: ") + string.Join(", ", m.Traits.Select(t => MonsterDef.WorkingTraits.Contains(t) ? t
                : t == "regeneration" && m.Regenerate > 0 ? t + T("(上の再生として効く)", " (works as the regeneration above)") : t + T("(名札だけ)", " (label only)"))));
        if (m.Spellcasting is { } sc)
            lines.Add(T($"呪文: 術者レベル {sc.Level}、DC {sc.Dc}、", $"Spells: caster level {sc.Level}, DC {sc.Dc}, ") + string.Join(", ", sc.Spells.Select(SpellName)));
        foreach (var a in m.Actions)
        {
            string head = $"{Loc.Name(a.Name) ?? a.Id}" + (a.ToHit is { } hit ? T($"(命中 {Signed(hit)})", $" ({Signed(hit)} to hit)") : "")
                + (a.Target != ActionTarget.One ? (a.Target == ActionTarget.Party ? T("・全員", ", whole party") : T("・前列", ", front row")) : "")
                + (a.Recharge is { } r ? T($"・{r}〜6 で再使用", $", recharge {r}-6") : "") + (a.Uses is { } u ? T($"・{u} 回まで", $", {u} uses") : "");
            lines.Add(T("行動 ", "Action ") + head + ": " + Effects(a.Effects));
        }
        if (m.Multiattack.Count > 0)
            lines.Add(T("複数回攻撃: ", "Multiattack: ") + string.Join(T(" か ", " or "), m.Multiattack.Select(set => string.Join(" + ", set.Select(p => $"{Loc.Name(m.Actions.FirstOrDefault(a => a.Id == p.Action)?.Name) ?? p.Action}×{p.Count}")))));
        return lines;
    }

    // ---------------- 効果の部品 ----------------

    /// <summary>効果の部品の並びを、1行の文に。</summary>
    public string Effects(IEnumerable<EffectPart> parts) => string.Join(T("、", ", "), parts.Select(Effect));

    private string Effect(EffectPart p) => p.Type switch
    {
        EffectPart.Damage => T($"{p.Dice ?? DiceText(p)} {DamageType(p.DamageType)}のダメージ", $"{p.Dice ?? DiceText(p)} {DamageType(p.DamageType)} damage") + (p.Only != null ? T($"({p.Only} にだけ)", $" (only vs {p.Only})") : ""),
        EffectPart.Heal => T($"HP を {p.Dice ?? "全部"} 戻す", $"heal {p.Dice ?? "fully"}"),
        EffectPart.Dot => T($"毎ターン {p.Dice ?? DiceText(p)} {DamageType(p.DamageType)}のダメージ({p.Rounds ?? 10} ラウンド)", $"{p.Dice ?? DiceText(p)} {DamageType(p.DamageType)} damage each turn ({p.Rounds ?? 10} rounds)")
            + (p.Ability != null ? T($"、{Ability(p.Ability)}のセーヴで止まる", $", ends on a {Ability(p.Ability)} save") : ""),
        EffectPart.Drain => T($"与えたダメージの {p.Value ?? 100}% を吸う", $"drain {p.Value ?? 100}% of the damage")
            + (p.DamageType != null ? T($"({DamageType(p.DamageType)}の分だけ)", $" ({DamageType(p.DamageType)} only)") : ""),
        EffectPart.ConditionType => T($"{(p.Condition is { } c ? ConditionName(c) : "?")}の状態", $"{(p.Condition is { } c2 ? ConditionName(c2) : "?")} condition")
            + (p.Rounds is { } r ? T($" {r} ラウンド", $" for {r} rounds") : "") + (p.RepeatSave ? T("(毎ターンセーヴし直す)", " (save again each turn)") : "")
            + (p.EscapeDc is { } e ? T($"(抜けるのは DC {e})", $" (escape DC {e})") : ""),
        EffectPart.Save => T($"{Ability(p.Ability)}のセーヴ DC {(p.Dc == 0 ? "呪文の難易度" : p.Dc)}", $"{Ability(p.Ability)} save DC {(p.Dc == 0 ? "spell DC" : p.Dc)}")
            + (p.Fail.Count > 0 ? T($"、失敗 → {Effects(p.Fail)}", $", fail → {Effects(p.Fail)}") : "")
            + (p.Half ? T("(成功で半分)", " (half on success)") : p.Success.Count > 0 ? T($"、成功 → {Effects(p.Success)}", $", success → {Effects(p.Success)}") : ""),
        EffectPart.Modifier => $"{StatName(p.Stat)} {(p.Value is { } v ? Signed(v) : p.Dice)}" + (p.Rounds is { } mr ? T($"({mr} ラウンド)", $" ({mr} rounds)") : ""),
        EffectPart.TempHp => T($"一時的な HP {p.Dice}", $"{p.Dice} temporary HP"),
        EffectPart.RemoveCondition => T($"{(p.Condition is { } rc ? ConditionName(rc) : "?")}を治す", $"cures {(p.Condition is { } rc2 ? ConditionName(rc2) : "?")}"),
        EffectPart.Resist => T($"{DamageType(p.DamageType)}耐性(被ダメージ半減)", $"resists {DamageType(p.DamageType)}"),
        EffectPart.Vulnerable => T($"{DamageType(p.DamageType)}が弱点(被ダメージ2倍)", $"vulnerable to {DamageType(p.DamageType)}"),
        EffectPart.ImmuneCondition => T($"{(p.Condition is { } ic ? ConditionName(ic) : "?")}無効", $"immune to {(p.Condition is { } ic2 ? ConditionName(ic2) : "?")}"),
        EffectPart.SetAbility => T($"{Ability(p.Ability)}が {p.Value} になる(元の方が高ければそのまま)", $"{Ability(p.Ability)} becomes {p.Value} (unless already higher)"),
        _ => p.Type,
    };

    private static string DiceText(EffectPart p) => p.ByLevel.Count > 0 ? string.Join("/", p.ByLevel.Values) : "?";

    /// <summary>修正値の stat の名前(resist:fire のような書き方も)。</summary>
    private string StatName(string? stat) => stat switch
    {
        null => "?",
        _ when stat.StartsWith("resist:") => T($"{DamageType(stat[7..])}耐性", $"resists {DamageType(stat[7..])}"),
        _ when stat.StartsWith("vulnerable:") => T($"{DamageType(stat[11..])}に弱い", $"vulnerable to {DamageType(stat[11..])}"),
        "flee" => T("逃走", "flee"),
        _ => Word("stat." + stat, stat),
    };

    // ---------------- イベント ----------------

    /// <summary>イベントの一覧: 起こる場所・一度きり・散らし方と、手順を字下げした木の形で。</summary>
    public List<string> Event(JsonObject draft)
    {
        EventDef e;
        try { e = draft.Deserialize<EventDef>(ContentLibrary.JsonOptions)!; }
        catch (JsonException) { return [T("(読めない)", "(cannot read)")]; }
        var lines = new List<string>
        {
            (Loc.Name(e.Title) is { Length: > 0 } title ? title + "　" : "") + PartNames.Name(PartNames.Places, e.Place) + "　" + PartNames.Name(PartNames.Once, e.Once ?? ""),
        };
        if (e.Scatter is { } sc)
            lines.Add(T($"迷宮に散らす: 深さ {sc.MinDepth:0.##}〜{sc.MaxDepth:0.##}(0 が B1、1 が最下層)、{PartNames.Name(PartNames.Where, sc.Where)}、レベル {sc.MinLevel:0.#}〜{(sc.MaxLevel >= 999 ? "上限なし" : sc.MaxLevel.ToString("0.#"))}",
                $"Scattered: depth {sc.MinDepth:0.##}-{sc.MaxDepth:0.##} (0 = B1, 1 = bottom), {PartNames.Name(PartNames.Where, sc.Where)}, level {sc.MinLevel:0.#}-{(sc.MaxLevel >= 999 ? "no limit" : sc.MaxLevel.ToString("0.#"))}")
                + (sc.Max > 0 ? T($"、1つの迷宮に {sc.Max} つまで", $", at most {sc.Max} per maze") : ""));
        else if (e.Place == "maze") lines.Add(T("散らさない(地図のマスに置く)", "Not scattered (place it on a map cell)"));
        if (e.When.Count > 0) lines.Add(T("起こる条件: ", "Happens when: ") + string.Join(T("、かつ ", " and "), e.When.Select(Condition)));
        Steps(e.Steps, 0, lines);
        return lines;
    }

    private void Steps(List<EventStep> steps, int depth, List<string> lines)
    {
        string pad = new(' ', depth * 2);
        foreach (var s in steps)
        {
            switch (s.Type)
            {
                case "text":
                    break;   // 文は JSON で読む(一覧は、何が起きるかだけ)
                case "choice":
                    lines.Add(pad + T("選択肢:", "Choice:"));
                    foreach (var o in s.Options)
                    {
                        lines.Add(pad + "  ・" + (Loc.Name(o.Label) ?? "?") + (o.When.Count > 0 ? T($"(出る条件: {string.Join("、", o.When.Select(Condition))})", $" (shown when {string.Join(" and ", o.When.Select(Condition))})") : ""));
                        Steps(o.Steps, depth + 2, lines);
                    }
                    break;
                case "random":
                    lines.Add(pad + T("どれか1つ:", "One at random:"));
                    foreach (var o in s.Options)
                    {
                        lines.Add(pad + T($"  ・重み {o.Weight:0.##}", $"  - weight {o.Weight:0.##}"));
                        Steps(o.Steps, depth + 2, lines);
                    }
                    break;
                case "cases":
                    lines.Add(pad + T("値で分ける(上から最初に合う場合だけ):", "Branch by value (first match only):"));
                    for (int i = 0; i < s.Cases.Count; i++)
                    {
                        lines.Add(pad + "  " + CaseLabel(s, i));
                        Steps(s.Cases[i].Then, depth + 2, lines);
                    }
                    lines.Add(pad + T("  それ以外", "  Otherwise"));
                    Steps(s.Else, depth + 2, lines);
                    break;
                case "if":
                    lines.Add(pad + T("もし ", "If ") + string.Join(T("、かつ ", " and "), s.When.Select(Condition)));
                    Steps(s.Then, depth + 1, lines);
                    if (s.Else.Count > 0)
                    {
                        lines.Add(pad + T("でなければ", "Otherwise"));
                        Steps(s.Else, depth + 1, lines);
                    }
                    break;
                case "pick":
                    lines.Add(pad + (s.Value == 1 ? T("くじで一人を選ぶ", "Choose one member by lot") : T("一人を選ぶ", "Choose one member")) + (s.When.Count > 0 ? T($"(選べる人: {string.Join("、", s.When.Select(Condition))})", $" (eligible: {string.Join(" and ", s.When.Select(Condition))})") : ""));
                    Steps(s.Then, depth + 1, lines);
                    if (s.Else.Count > 0)
                    {
                        lines.Add(pad + T("いない・やめたら", "If nobody / cancelled"));
                        Steps(s.Else, depth + 1, lines);
                    }
                    break;
                case "spend_slot":
                    lines.Add(pad + T($"呪文スロットを1つ使う(レベル {Math.Max(1, s.Value)} 以上)", $"Spend a spell slot (level {Math.Max(1, s.Value)}+)"));
                    Steps(s.Then, depth + 1, lines);
                    if (s.Else.Count > 0)
                    {
                        lines.Add(pad + T("残っていなければ", "If none left"));
                        Steps(s.Else, depth + 1, lines);
                    }
                    break;
                case "check":
                    lines.Add(pad + T($"判定: {(s.Skill != null ? SkillName(s.Skill) : Ability(s.Ability))} DC {s.Dc}", $"Check: {(s.Skill != null ? SkillName(s.Skill) : Ability(s.Ability))} DC {s.Dc}"));
                    lines.Add(pad + T("  成功:", "  Success:"));
                    Steps(s.Success, depth + 2, lines);
                    lines.Add(pad + T("  失敗:", "  Failure:"));
                    Steps(s.Fail, depth + 2, lines);
                    break;
                case "combat":
                    lines.Add(pad + T("戦闘: ", "Combat: ") + (s.Boss != null ? T($"ボス {s.Boss}", $"boss {s.Boss}")
                        : s.Groups.Count > 0 ? string.Join(" + ", s.Groups.Select(g => $"{MonsterName(g.Monster)}×{g.Count}")) : T("この階の敵", "this floor's enemies")));
                    if (s.Win.Count > 0)
                    {
                        lines.Add(pad + T("  勝ったら:", "  If won:"));
                        Steps(s.Win, depth + 2, lines);
                    }
                    if (s.Lose.Count > 0)
                    {
                        lines.Add(pad + T("  逃げたら:", "  If fled:"));
                        Steps(s.Lose, depth + 2, lines);
                    }
                    break;
                default:
                    lines.Add(pad + Step(s));
                    break;
            }
        }
    }

    /// <summary>場合の見出し。安全に範囲に直せるときだけ、前の場合を除いた範囲を出す。</summary>
    public string CaseLabel(EventStep s, int index)
    {
        var branch = s.Cases[index];
        if (EventBranches.Ranges(s) is { } ranges)
        {
            var c = branch.When[0];
            string subject = c.Type switch
            {
                "gold" => T("所持金", "Gold"),
                "hp" => "HP(%)",
                "timer" => T($"残り歩数 {c.Id}", $"Steps left {c.Id}"),
                _ => c.Id ?? "?",
            };
            var (min, max) = ranges[index];
            return subject + " " + (min.HasValue ? T($"{min}〜{max}", $"{min}–{max}") : T($"〜{max}", $"up to {max}"));
        }
        return branch.When.Count == 0 ? T("いつでも", "Always") : string.Join(T("、かつ ", " and "), branch.When.Select(Condition));
    }

    public string Step(EventStep s) => s.Type switch
    {
        "gold" => s.Value >= 0 ? T($"金貨 {s.Value} 枚をもらう", $"Receive {s.Value} gold") : T($"金貨 {-s.Value} 枚を払う", $"Pay {-s.Value} gold"),
        "xp" => T($"経験値を {s.Value} 得る(動ける人それぞれ)", $"Each able member gains {s.Value} XP"),
        "give_item" => T($"{ItemName(s.Id)}×{s.Count} を手に入れる", $"Get {ItemName(s.Id)}×{s.Count}"),
        "take_item" => T($"{ItemName(s.Id)}×{s.Count} を失う", $"Lose {ItemName(s.Id)}×{s.Count}"),
        "treasure" => T($"宝を {s.Count} つ手に入れる(この階の表から)", $"Find {s.Count} treasure (this floor's table)"),
        "heal" => T($"全員の HP が {s.Dice ?? "全部"} 回復する", $"Everyone heals {s.Dice ?? "fully"}"),
        "damage" => T($"全員が {s.Dice} の{DamageType(s.DamageType)}ダメージを受ける", $"Everyone takes {s.Dice} {DamageType(s.DamageType)} damage")
            + (s.Ability != null ? T($"({Ability(s.Ability)}のセーヴ DC {s.Dc} で半分)", $" ({Ability(s.Ability)} save DC {s.Dc} for half)") : ""),
        "condition" => T($"{ConditionName(s.Id ?? "?")}になる({(s.Value > 0 ? s.Value + " 分" : "治すまで")})", $"{ConditionName(s.Id ?? "?")} for {(s.Value > 0 ? s.Value + " minutes" : "until cured")}")
            + (s.Ability != null ? T($"({Ability(s.Ability)}のセーヴ DC {s.Dc} で防ぐ)", $" ({Ability(s.Ability)} save DC {s.Dc} to resist)") : ""),
        "cure" => T($"{(s.Id != null ? ConditionName(s.Id) : "状態異常がすべて")}が治る", $"cure {(s.Id != null ? ConditionName(s.Id) : "all conditions")}"),
        "flag" => T($"フラグを立てる: {s.Id}", $"Set flag {s.Id}"),
        "unflag" => T($"フラグを消す: {s.Id}", $"Clear flag {s.Id}"),
        "counter" => T($"変数 {s.Id} に {Signed(s.Value)}", $"Variable {s.Id} {Signed(s.Value)}"),
        "counter_set" => T($"変数 {s.Id} に {s.Value} を代入", $"Variable {s.Id} = {s.Value}"),
        "light" => s.Value <= 0 ? T("明かりが消える", "The light goes out") : T($"明かりがつく({s.Value} 分)", $"A light comes on ({s.Value} minutes)"),
        "open" => T($"門 {s.Id} が開く", $"Gate {s.Id} opens"),
        "close" => T($"門 {s.Id} が閉まる", $"Gate {s.Id} closes"),
        "timer" => T($"{s.Value} 歩あとに、イベント {s.Id} が起こる", $"In {s.Value} steps, event {s.Id} happens"),
        "timer_stop" => T($"あとで起こるはずのイベント {s.Id} を取りやめる", $"Event {s.Id} will no longer happen"),
        "warp" => T($"着地点 {s.Id} へ飛ばされる(イベントはここで終わる)", $"Moved to landing point {s.Id} (the event ends)"),
        "sound" => T($"音が鳴る: {s.Id}", $"Sound: {s.Id}"),
        "run" => T($"イベント {s.Id} へ続く", $"Continue with event {s.Id}"),
        "end" => T("終わる", "End"),
        "again" => T("終わる(また起こる)", "End (can happen again)"),
        "ending" => T("エンディングへ(クリア)", "Go to the ending (clear)"),
        _ => PartNames.Name(PartNames.Steps, s.Type),
    };

    public string Condition(EventCondition c)
    {
        string what = c.Type switch
        {
            "class" => T($"{ClassName(c.Id)}がいる", $"a {ClassName(c.Id)} in the party"),
            "race" => T($"{RaceName(c.Id)}がいる", $"a {RaceName(c.Id)} in the party"),
            "gold" => T($"金貨 {c.Value} 以上", $"{c.Value}+ gold"),
            "item" => T($"{ItemName(c.Id)}を {c.Count} つ以上持つ", $"has {c.Count}+ {ItemName(c.Id)}"),
            "level" => T($"誰かがレベル {c.Value} 以上", $"someone is level {c.Value}+"),
            "chance" => T($"{c.Value}% で", $"{c.Value}% chance"),
            "flag" => c.Not ? T($"フラグ {c.Id} が立っていない", $"flag {c.Id} is not set") : T($"フラグ {c.Id} が立っている", $"flag {c.Id} is set"),
            "done" => T($"イベント {c.Id} が済んだ", $"event {c.Id} done"),
            "counter" => T($"変数 {c.Id} が {c.Value} {PartNames.Name(PartNames.Compare, c.Op)}", $"variable {c.Id} {c.Op} {c.Value}"),
            "turned" => T("向きを変えたとき", "on turning"),
            "entered" => T($"{PartNames.Name(PartNames.Entries, c.Id ?? "")}入った", $"entered by {PartNames.Name(PartNames.Entries, c.Id ?? "")}"),
            "equipped" => T($"{ItemName(c.Id)}を装備している", $"has {ItemName(c.Id)} equipped"),
            "slots" => T($"レベル {Math.Max(1, c.Value)} 以上の呪文スロットが残っている", $"has a level {Math.Max(1, c.Value)}+ spell slot left"),
            "status" => T($"{ConditionName(c.Id ?? "?")}の状態の人がいる", $"someone is {ConditionName(c.Id ?? "?")}"),
            "hp" => T($"HP の割合 {c.Op} {c.Value}%", $"HP {c.Op} {c.Value}%"),
            "armed" => T("武器を手に持っている人がいる", "someone holds a weapon"),
            "lit" => T("明かりがついている", "the light is on"),
            "zone" => T($"このマスが{PartNames.Name(PartNames.Zones, c.Id ?? "")}", $"this cell is {PartNames.Name(PartNames.Zones, c.Id ?? "")}"),
            "cast" => string.IsNullOrEmpty(c.Id) ? T("呪文を唱えた", "a spell was cast") : T($"{SpellName(c.Id)}を唱えた", $"{SpellName(c.Id)} was cast"),
            "rested" => T("休んだ", "rested"),
            "timer" => T($"タイマー {c.Id} の残り {c.Op} {c.Value} 歩", $"timer {c.Id} has {c.Op} {c.Value} steps left"),
            "facing" => T($"{PartNames.Name(PartNames.Facings, c.Id ?? "")}を向いている", $"facing {PartNames.Name(PartNames.Facings, c.Id ?? "")}"),
            _ => PartNames.Name(PartNames.Conditions, c.Type),
        };
        return c.Not && c.Type != "flag" ? T($"{what}ではない", $"not {what}") : what;
    }

    // ---------------- 名前 ----------------

    private string ItemName(string? id) => id == null ? "?" : Loc.Name(context.BaseData.Items.GetValueOrDefault(id)?.Name) ?? OwnName("items", id) ?? id;
    private string MonsterName(string? id) => id == null ? "?" : Loc.Name(context.BaseData.Monsters.GetValueOrDefault(id)?.Name) ?? OwnName("monsters", id) ?? id;
    private string SpellName(string id) => Loc.Name(context.BaseData.Spells.GetValueOrDefault(id)?.Name) ?? id;
    private string SkillName(string id) => Loc.Name(context.BaseData.Skills.GetValueOrDefault(id)?.Name) ?? id;
    private string ClassName(string? id) => id == null ? "?" : Loc.Name(context.BaseData.Classes.GetValueOrDefault(id)?.Name) ?? id;
    private string RaceName(string? id) => id == null ? "?" : Loc.Name(context.BaseData.Races.GetValueOrDefault(id)?.Name) ?? id;

    /// <summary>このシナリオで作った品・モンスターの名前(と、AI がイベントと一緒に書いた新しい品)。</summary>
    private string? OwnName(string collection, string id)
    {
        var o = Own(collection, id);
        return o?["name"] is JsonObject n ? (string?)n[Lang] ?? (string?)n[En ? "ja" : "en"] : null;
    }

    /// <summary>AI がイベントと一緒に書いた新しい品(名前を引くときに使う)。</summary>
    public List<JsonObject> Extra { get; set; } = [];

    private JsonObject? Own(string collection, string id) =>
        (collection == "items" ? Extra.FirstOrDefault(o => (string?)o["id"] == id) : null)
        ?? context.Pack?.DataLists().FirstOrDefault(x => x.Collection == collection).List?.FirstOrDefault(o => (string?)o["id"] == id);

    /// <summary>extends を解いて(元の上に、書いた項目を重ねて)、ゲームの型で読む。元はこのシナリオのものか標準のもの。</summary>
    private T? Read<T>(string collection, JsonObject draft) where T : class
    {
        var merged = draft.DeepClone().AsObject();
        for (int i = 0; i < 8 && (string?)merged["extends"] is { } from; i++)
        {
            var source = (string?)draft["id"] != from ? Own(collection, from) : null;
            JsonObject? baseObj = source?.DeepClone().AsObject()
                ?? (context.BaseContent.Find(collection, from) is { } item ? JsonNode.Parse(item.Data.GetRawText())!.AsObject() : null);
            if (baseObj == null) break;
            merged.Remove("extends");
            foreach (var (k, v) in merged.ToList()) baseObj[k] = v?.DeepClone();
            merged = baseObj;
        }
        try { return merged.Deserialize<T>(ContentLibrary.JsonOptions); }
        catch (JsonException) { return null; }
    }
}
