using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Monsters;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// データのタブの、品とモンスターの欄(JSON の項目を、名前の付いた欄で直す)。
/// 写して一部を変えたもの(extends)は、書いていない項目に元の値を出し、書いた項目には「元に戻す」を付ける(戻すとその項目を消して、元の値を使う)。
/// 並び(効果・行動など)は、ひとつ書くと全部を書く(ゲームは並びをまとめて置き換えるので、元の並びを写してから直す)。
/// 選んだ種類に関係する欄だけを出す(品なら種類ごと、効果なら効果の種類ごと)。
/// </summary>
public sealed class DataForm(EditorContext context, JsonObject o, JsonObject? baseObj, Action edited, Action rebuild)
{
    private const double LabelWidth = 120;

    private JsonNode? Get(string key) => o.ContainsKey(key) ? o[key] : baseObj?[key];
    private bool Written(string key) => o.ContainsKey(key);

    private void Set(string key, JsonNode? value)
    {
        if (value == null) o.Remove(key);
        else o[key] = value;
        edited();
    }

    // ---------------- 品 ----------------

    /// <summary>品の欄(基本と、種類ごとの欄)。</summary>
    public IEnumerable<UIElement> Item()
    {
        string kind = (string?)Get("kind") ?? "gear";
        yield return Ui.Heading(T("基本", "Basics"));
        yield return Field(T("種類", "Kind"), "kind", Pick("kind", ItemKinds, structure: true));
        yield return Field(T("レア度", "Rarity"), "rarity", Pick("rarity", Rarities));
        yield return Field(T("値段(金貨)", "Price (gold)"), "price", Num("price"));
        yield return Field(T("説明", "Description"), "description", Text2("description", multi: true));
        yield return Field("", "attunement", Bool("attunement", T("同調が要る(同調しないと効果が出ない。3つまで)", "Requires attunement (no effect until attuned; up to 3)")));
        yield return Field("", "cursed", Bool("cursed", T("呪われている(装備すると、呪いを解くまで外せない)", "Cursed (can't be removed until the curse is lifted)")));
        yield return Field(T("使える人", "Usable by"), "only", Pick("only", new PartTable<string>(("", "だれでも", "anyone"), ("caster", "術者だけ", "casters only"))));
        switch (kind)
        {
            case "weapon":
                yield return Ui.Heading(T("武器", "Weapon"));
                if (baseObj == null)   // 写して作ったものは、写す元が元になる
                    yield return Field(T("元の武器", "Base weapon"), "base", Pick("base", Standard(ItemKindKey: "weapon"), allowEmpty: true));
                yield return Field(T("+いくつ", "Bonus (+N)"), "bonus", Int("bonus"));
                yield return Field(T("ダメージ", "Damage"), "damage", Str("damage", T("例 1d8", "e.g. 1d8")));
                yield return Field(T("ダメージの種類", "Damage type"), "damage_type", Pick("damage_type", DamageNames(), allowEmpty: true));
                yield return Field(T("両手のとき", "Two-handed"), "versatile_damage", Str("versatile_damage", T("例 1d10(持ち替えられる武器)", "e.g. 1d10 (versatile)")));
                yield return Field(T("分類", "Category"), "category", Pick("category", new PartTable<string>(("", "なし", "none"), ("simple", "単純武器", "simple"), ("martial", "軍用武器", "martial"))));
                yield return Field("", "ranged", Bool("ranged", T("飛び道具(弓・投げるもの)", "Ranged (bows, thrown)")));
                yield return Field(T("性質", "Properties"), "properties", Multi("properties", WeaponProperties));
                yield return Field(T("当たったとき", "On hit"), "on_hit", Effects("on_hit", ItemEffectTypes));
                break;
            case "armor":
                yield return Ui.Heading(T("鎧", "Armor"));
                if (baseObj == null)   // 写して作ったものは、写す元が元になる
                    yield return Field(T("元の鎧", "Base armor"), "base", Pick("base", Standard(ItemKindKey: "armor"), allowEmpty: true));
                yield return Field(T("+いくつ", "Bonus (+N)"), "bonus", Int("bonus"));
                yield return Field(T("重さ", "Weight class"), "armor_type", Pick("armor_type", new PartTable<string>(("", "なし", "none"), ("light", "軽装", "light"), ("medium", "中装", "medium"), ("heavy", "重装", "heavy"))));
                yield return Field("AC", "armor_class", Int("armor_class"));
                yield return Field(T("敏捷の上限", "Dex cap"), "dex_cap", Int("dex_cap", allowEmpty: true));
                yield return Field(T("要る筋力", "Strength needed"), "strength_required", Int("strength_required"));
                yield return Field("", "stealth_disadvantage", Bool("stealth_disadvantage", T("隠密に不利", "Stealth disadvantage")));
                yield return Field(T("着ているあいだ", "While worn"), "passive", Effects("passive", PassiveTypes));
                break;
            case "shield":
                yield return Ui.Heading(T("盾", "Shield"));
                yield return Field(T("+いくつ", "Bonus (+N)"), "bonus", Int("bonus"));
                yield return Field(T("AC に足す", "AC bonus"), "armor_class", Int("armor_class"));
                yield return Field(T("持っているあいだ", "While held"), "passive", Effects("passive", PassiveTypes));
                break;
            case "accessory":
                yield return Ui.Heading(T("装身具", "Accessory"));
                yield return Field(T("付ける場所", "Slot"), "slot", Pick("slot", Slots));
                yield return Field(T("付けているあいだ", "While worn"), "passive", Effects("passive", PassiveTypes));
                break;
            case "consumable" or "gear":
                yield return Ui.Heading(T("使うと", "When used"));
                yield return Field(T("対象", "Target"), "use_target", Pick("use_target", UseTargets));
                yield return Field(T("効果", "Effects"), "use", Effects("use", ItemEffectTypes));
                yield return Field(T("唱える呪文", "Casts spell"), "use_spell", Pick("use_spell", SpellChoices(), allowEmpty: true));
                yield return Field(T("呪文のレベル", "Spell slot level"), "use_slot", Int("use_slot"));
                yield return Field(T("回数", "Charges"), "charges", Int("charges", allowEmpty: true, hint: T("空なら1回で無くなる", "empty: used up after one use")));
                yield return Field(T("重ねて持てる数", "Stack size"), "stack", Int("stack"));
                break;
            case "ammo":
                yield return Ui.Heading(T("矢弾", "Ammo"));
                yield return Field(T("弾の種類", "Ammo type"), "ammo_type", Pick("ammo_type", new PartTable<string>(("arrow", "矢", "arrow"), ("bolt", "ボルト", "bolt"), ("bullet", "弾", "bullet"), ("needle", "針", "needle"))));
                yield return Field(T("+いくつ", "Bonus (+N)"), "bonus", Int("bonus"));
                yield return Field(T("命中に足す", "Hit bonus"), "ammo_hit", Int("ammo_hit"));
                yield return Field(T("ダメージに足す", "Damage bonus"), "ammo_damage", Int("ammo_damage"));
                yield return Field(T("1つの本数", "Bundle"), "bundle", Int("bundle"));
                break;
            case "backpack":
                yield return Ui.Heading(T("背負い袋", "Pack"));
                yield return Field(T("枠の数", "Slots"), "slots", Int("slots"));
                break;
            case "key":
                yield return Ui.Heading(T("大事な品", "Key item"));
                yield return Field(T("買い戻し(倍)", "Buyback (×)"), "buyback", Num("buyback", allowEmpty: true, hint: T("空なら売れない", "empty: can't be sold")));
                break;
        }
    }

    // ---------------- モンスター ----------------

    /// <summary>モンスターの欄(基本・強さ・守り・攻撃・呪文)。</summary>
    public IEnumerable<UIElement> Monster()
    {
        yield return Ui.Heading(T("基本", "Basics"));
        yield return Field(T("正体が分かる前", "Before identified"), "unknown_name", Text2("unknown_name"));
        yield return Field(T("種類", "Type"), "creature_type", Pick("creature_type", CreatureTypes()));
        yield return Field(T("大きさ", "Size"), "size", Pick("size", Sizes));
        yield return Field(T("脅威度(CR)", "Challenge (CR)"), "cr", Num("cr"));
        yield return Field(T("経験値", "XP"), "xp", Int("xp"));
        yield return Field(T("出る数", "Appearing"), "appearing", Str("appearing", T("例 1d6", "e.g. 1d6")));
        yield return Field(T("出やすさ", "Weight"), "weight", Num("weight", hint: T("0 なら遭遇に出ない", "0: never in encounters")));
        yield return Ui.Heading(T("強さ", "Stats"));
        yield return Field("HP", "hp", Str("hp", T("例 2d6+2", "e.g. 2d6+2")));
        yield return Field(T("HP の平均", "Average HP"), "hp_average", Int("hp_average"));
        yield return Field("AC", "ac", Int("ac"));
        yield return Field(T("習熟", "Proficiency"), "proficiency", Int("proficiency"));
        yield return Field(T("能力値", "Abilities"), "abilities", Abilities("abilities"));
        yield return Field(T("受動知覚", "Passive perception"), "passive_perception", Int("passive_perception"));
        yield return Field("", "flies", Bool("flies", T("飛ぶ", "Flies")));
        yield return Ui.Heading(T("守り", "Defenses"));
        yield return Field(T("耐性(被ダメージ半減)", "Resistance (half damage)"), "resist", Multi("resist", DamageNamesWithPhysical()));
        yield return Field(T("弱点(被ダメージ2倍)", "Vulnerability (double damage)"), "vulnerable", Multi("vulnerable", DamageNamesWithPhysical()));
        yield return Field(T("無効(被ダメージ0)", "Immunity (no damage)"), "immune", Multi("immune", DamageNamesWithPhysical()));
        yield return Field(T("状態無効", "Condition immunities"), "condition_immune", Multi("condition_immune", ConditionNames()));
        yield return Field(T("再生(HP)", "Regenerate (HP)"), "regenerate", Int("regenerate"));
        yield return Field(T("再生を止める", "Stopped by"), "regenerate_stop", Multi("regenerate_stop", DamageNames()));
        yield return Field(T("特徴", "Traits"), "traits", Traits());
        yield return Ui.Heading(T("行動", "Actions"));
        yield return Actions();
        yield return Field(T("複数回攻撃", "Multiattack"), "multiattack", Multiattack());
        yield return Ui.Heading(T("呪文", "Spellcasting"));
        yield return Spellcasting();
    }

    // ---------------- 欄の部品 ----------------

    /// <summary>名前・欄と、元の値(書いていない項目)か「元に戻す」(書いた項目)。</summary>
    private UIElement Field(string label, string key, UIElement editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0) };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(name);
        grid.Children.Add(editor);
        if (baseObj != null)
        {
            FrameworkElement tag = Written(key)
                ? Ui.Button(T("元に戻す", "Reset"), () => { o.Remove(key); edited(); rebuild(); }, T("元: ", "Base: ") + Short(baseObj[key]))
                : new TextBlock { Text = T("元のまま", "from base"), Foreground = Ui.Dim, Margin = new Thickness(6, 0, 0, 0) };
            tag.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);
        }
        return grid;
    }

    private static string Short(JsonNode? n) => n == null ? T("(無し)", "(none)") : n.ToJsonString() is var s && s.Length > 80 ? s[..80] + "…" : n.ToJsonString();

    private UIElement Str(string key, string hint = "")
    {
        var box = Ui.Text((string?)Get(key), v => Set(key, v.Trim().Length == 0 ? null : JsonValue.Create(v.Trim())));
        if (hint.Length > 0) box.ToolTip = hint;
        return WithHint(box, hint);
    }

    private UIElement Int(string key, bool allowEmpty = false, string hint = "")
    {
        var node = Get(key);
        var box = Ui.Text(node?.ToString() ?? "", v =>
        {
            if (allowEmpty && v.Trim().Length == 0) Set(key, null);
            else if (int.TryParse(v, out int n)) Set(key, JsonValue.Create(n));
        });
        return WithHint(box, hint);
    }

    private UIElement Num(string key, bool allowEmpty = false, string hint = "")
    {
        var node = Get(key);
        var box = Ui.Text(node == null ? "" : ((double)node).ToString(CultureInfo.InvariantCulture), v =>
        {
            if (allowEmpty && v.Trim().Length == 0) Set(key, null);
            else if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) Set(key, JsonValue.Create(d));
        });
        return WithHint(box, hint);
    }

    private static UIElement WithHint(FrameworkElement box, string hint) =>
        hint.Length == 0 ? box : new StackPanel { Children = { box, new TextBlock { Text = hint, Foreground = Ui.Dim, TextWrapping = TextWrapping.Wrap } } };

    private UIElement Bool(string key, string label)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = (bool?)Get(key) == true, VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => Set(key, JsonValue.Create(box.IsChecked == true));
        return box;
    }

    /// <summary>{"en", "ja"} の文。</summary>
    private UIElement Text2(string key, bool multi = false)
    {
        var node = Get(key) as JsonObject;
        var value = node == null ? null : LocalizedText.Of((string?)node["en"] ?? "", (string?)node["ja"]);
        return Ui.Localized(value, v => Set(key, v == null ? null : new JsonObject { ["en"] = v.Texts.GetValueOrDefault("en") ?? "", ["ja"] = v.Texts.GetValueOrDefault("ja") ?? "" }),
            multiline: multi, height: multi ? 54 : 0);
    }

    /// <summary>名前で選ぶ欄(structure なら、選んだあとで欄を出し直す: 種類で欄が変わるとき)。</summary>
    private UIElement Pick(string key, IReadOnlyDictionary<string, string> names, bool allowEmpty = false, bool structure = false)
    {
        var list = new List<Choice>();
        if (allowEmpty && !names.ContainsKey("")) list.Add(new Choice("", T("なし", "none")));
        list.AddRange(names.Select(kv => new Choice(kv.Key, kv.Value)));
        string value = (string?)Get(key) ?? "";
        if (value.Length > 0 && list.All(c => c.Key != value)) list.Add(new Choice(value, ""));
        var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(c => c.Key == value), IsTextSearchEnabled = true };
        box.SetValue(TextSearch.TextPathProperty, nameof(Choice.Display));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not Choice c || c.Key == ((string?)Get(key) ?? "")) return;
            Set(key, c.Key.Length == 0 ? null : JsonValue.Create(c.Key));
            if (structure) rebuild();
        };
        return box;
    }

    /// <summary>いくつでも選べる欄(JSON は文字の並び)。左のコンボで選んで「足す」、右に札(ChipPicker)。</summary>
    private UIElement Multi(string key, IReadOnlyDictionary<string, string> names, string hint = "")
    {
        var chosen = (Get(key) as JsonArray)?.Select(n => (string?)n).OfType<string>() ?? [];
        var picker = new ChipPicker(names.Select(kv => new Choice(kv.Key, kv.Value)), chosen,
            list => Set(key, new JsonArray([.. list.Select(c => (JsonNode)JsonValue.Create(c)!)])));
        return WithHint(picker, hint);
    }

    /// <summary>能力値6つ。</summary>
    private UIElement Abilities(string key)
    {
        var grid = new UniformGrid6();
        foreach (var a in new[] { "str", "dex", "con", "int", "wis", "cha" })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            cell.Children.Add(new TextBlock { Text = PartNames.Name(PartNames.Abilities, a), Foreground = Ui.Dim });
            cell.Children.Add(Ui.Text((Get(key) as JsonObject)?[a]?.ToString() ?? "10", v =>
            {
                if (!int.TryParse(v, out int n)) return;
                var obj = (Get(key) as JsonObject)?.DeepClone().AsObject() ?? [];
                obj[a] = n;
                Set(key, obj);
            }));
            grid.Children.Add(cell);
        }
        return grid;
    }

    private sealed class UniformGrid6 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid6() { Columns = 6; }
    }

    // ---------------- 効果の並び ----------------

    /// <summary>並びの項目を直すための、書く並び(書いていなければ、元の並びを写したもの。直したら書く)。</summary>
    private (JsonArray Array, Action Commit) Working(string key)
    {
        if (o[key] is JsonArray mine) return (mine, edited);
        var copy = (baseObj?[key] as JsonArray)?.DeepClone().AsArray() ?? [];
        return (copy, () =>
        {
            if (!o.ContainsKey(key)) o[key] = copy;
            edited();
        });
    }

    private UIElement Effects(string key, string[] types)
    {
        var (array, commit) = Working(key);
        return EffectList(array, commit, types);
    }

    /// <summary>効果の並び(1つずつ、種類と、その種類の欄)。セーヴの効果は、失敗・成功の並びを中に持つ。</summary>
    private UIElement EffectList(JsonArray array, Action commit, string[] types)
    {
        var panel = new StackPanel();
        foreach (var node in array.ToList())
        {
            if (node is not JsonObject e) continue;
            var box = new Border { BorderBrush = Ui.Dim, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 2, 0, 2), Margin = new Thickness(0, 2, 0, 4) };
            var inner = new StackPanel();
            var head = new DockPanel();
            var remove = Ui.Button("×", () => { array.Remove(e); commit(); rebuild(); }, T("この効果を消す", "Remove this effect"));
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            head.Children.Add(EffectPick(e, types, commit));
            inner.Children.Add(head);
            foreach (var row in EffectFields(e, commit, types)) inner.Children.Add(row);
            box.Child = inner;
            panel.Children.Add(box);
        }
        panel.Children.Add(Ui.Button(T("＋ 効果を足す", "+ Add effect"), () =>
        {
            array.Add(new JsonObject { ["type"] = types[0] });
            commit();
            rebuild();
        }));
        return panel;
    }

    private UIElement EffectPick(JsonObject e, string[] types, Action commit)
    {
        var list = types.Select(t => new Choice(t, EffectNames.GetValueOrDefault(t, t))).ToList();
        string type = (string?)e["type"] ?? "";
        if (list.All(c => c.Key != type)) list.Add(new Choice(type, ""));
        var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(c => c.Key == type) };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not Choice c || c.Key == (string?)e["type"]) return;
            foreach (var k in e.Select(kv => kv.Key).Where(k => k != "type").ToList()) e.Remove(k);   // 種類を替えたら、前の種類の欄は消す
            e["type"] = c.Key;
            commit();
            rebuild();
        };
        return box;
    }

    private IEnumerable<UIElement> EffectFields(JsonObject e, Action commit, string[] types)
    {
        UIElement Row(string label, UIElement editor) => Ui.Row(label, editor, 100);
        UIElement S(string k, string hint = "") =>
            WithHint(Ui.Text((string?)e[k] ?? "", v => { if (v.Trim().Length == 0) e.Remove(k); else e[k] = v.Trim(); commit(); }), hint);
        UIElement I(string k) => Ui.Text(e[k]?.ToString() ?? "", v => { if (v.Trim().Length == 0) e.Remove(k); else if (int.TryParse(v, out int n)) e[k] = n; commit(); });
        UIElement B(string k, string label)
        {
            var c = new CheckBox { Content = label, IsChecked = (bool?)e[k] == true };
            c.Click += (_, _) => { if (c.IsChecked == true) e[k] = true; else e.Remove(k); commit(); };
            return c;
        }
        UIElement P(string k, IReadOnlyDictionary<string, string> names, bool allowEmpty = true)
        {
            var list = new List<Choice>();
            if (allowEmpty) list.Add(new Choice("", T("なし", "none")));
            list.AddRange(names.Select(kv => new Choice(kv.Key, kv.Value)));
            string v = (string?)e[k] ?? "";
            var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(c => c.Key == v) };
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice c) { if (c.Key.Length == 0) e.Remove(k); else e[k] = c.Key; commit(); } };
            return box;
        }
        switch ((string?)e["type"])
        {
            case EffectPart.Damage:
                yield return Row(T("ダイス", "Dice"), S("dice", T("例 2d6、1d8+MOD(MOD は術者の修正値)", "e.g. 2d6, 1d8+MOD")));
                yield return Row(T("種類", "Type"), P("damage_type", DamageNames()));
                yield return Row(T("この相手にだけ", "Only vs"), P("only", CreatureTypes()));
                break;
            case EffectPart.Heal or EffectPart.TempHp:
                yield return Row(T("ダイス", "Dice"), S("dice"));
                break;
            case EffectPart.ConditionType:
                yield return Row(T("状態", "Condition"), P("condition", ConditionNames(), allowEmpty: false));
                yield return Row(T("ラウンド", "Rounds"), I("rounds"));
                yield return Row("", B("repeat_save", T("毎ターン、セーヴし直して解ける", "Save again each turn to end it")));
                yield return Row(T("抜ける難しさ", "Escape DC"), I("escape_dc"));
                yield return Row(T("この HP 以下だけ", "Only at HP ≤"), I("max_hp"));
                break;
            case EffectPart.Save:
                yield return Row(T("能力値", "Ability"), P("ability", PartNames.Abilities, allowEmpty: false));
                yield return Row(T("難しさ(DC)", "DC"), I("dc"));
                yield return Row("", B("half", T("成功ならダメージは半分", "Half damage on success")));
                yield return Row(T("失敗したら", "On failure"), Nested(e, "fail", commit, types));
                yield return Row(T("成功したら", "On success"), Nested(e, "success", commit, types));
                break;
            case EffectPart.Modifier:
                yield return Row(T("何に", "Stat"), P("stat", ModifierStats, allowEmpty: false));
                yield return Row(T("足す値", "Value"), I("value"));
                yield return Row(T("足すダイス", "Dice"), S("dice", T("値の代わりに(例 1d4、-1d4)", "instead of a value (e.g. 1d4, -1d4)")));
                yield return Row(T("ラウンド", "Rounds"), I("rounds"));
                break;
            case EffectPart.HpPool:
                yield return Row(T("ダイス", "Dice"), S("dice"));
                yield return Row(T("状態", "Condition"), P("condition", ConditionNames(), allowEmpty: false));
                break;
            case EffectPart.RemoveCondition or EffectPart.ImmuneCondition:
                yield return Row(T("状態", "Condition"), P("condition", ConditionNames(), allowEmpty: false));
                break;
            case EffectPart.Kill:
                yield return Row(T("この HP 以下", "HP at most"), I("max_hp"));
                break;
            case EffectPart.Resist or EffectPart.Vulnerable:
                yield return Row(T("種類", "Type"), P("damage_type", DamageNamesWithPhysical(), allowEmpty: false));
                break;
            case EffectPart.SetAbility:
                yield return Row(T("能力値", "Ability"), P("ability", PartNames.Abilities, allowEmpty: false));
                yield return Row(T("値にする", "Set to"), I("value"));
                break;
            case EffectPart.Revive:
                yield return Row("", B("allow_ashes", T("灰からも生き返らせる", "Also from ashes")));
                break;
            case EffectPart.Light or EffectPart.FeatherFall:
                yield return Row(T("何分", "Minutes"), I("value"));
                break;
        }
    }

    private UIElement Nested(JsonObject e, string key, Action commit, string[] types)
    {
        if (e[key] is not JsonArray arr) e[key] = arr = [];
        return EffectList(arr, commit, types);
    }

    // ---------------- モンスターの行動 ----------------

    private UIElement Actions()
    {
        var (array, commit) = Working("actions");
        var panel = new StackPanel();
        if (baseObj != null)
        {
            panel.Children.Add(Ui.Note(Written("actions") ? T("行動は、全部を書いている(元の行動は使わない)", "All actions are written (the base's are not used)")
                : T("行動は元のまま。直すと、元の行動を写して全部を書く", "Actions come from the base. Editing copies them and writes them all")));
            if (Written("actions")) panel.Children.Add(Ui.Button(T("行動を元に戻す", "Reset actions to the base"), () => { o.Remove("actions"); edited(); rebuild(); }));
        }
        foreach (var node in array.ToList())
        {
            if (node is not JsonObject a) continue;
            string title = Loc.Name(a["name"] is JsonObject n ? LocalizedText.Of((string?)n["en"] ?? "", (string?)n["ja"]) : null) ?? (string?)a["id"] ?? "?";
            string sum = a["to_hit"] is { } hit ? T($"  命中 +{hit}", $"  to hit +{hit}") : T("  セーヴの行動", "  save action");
            var body = new StackPanel { Margin = new Thickness(12, 2, 0, 6) };
            var ex = new Expander { Header = title + sum, Content = body, Margin = new Thickness(0, 2, 0, 2) };
            body.Children.Add(Ui.Row("id", Ui.Text((string?)a["id"] ?? "", v => { a["id"] = v.Trim(); commit(); }), 100));
            var name = a["name"] as JsonObject;
            body.Children.Add(Ui.Row(T("名前", "Name"), Ui.Localized(name == null ? null : LocalizedText.Of((string?)name["en"] ?? "", (string?)name["ja"]),
                v => { a["name"] = v == null ? null : new JsonObject { ["en"] = v.Texts.GetValueOrDefault("en") ?? "", ["ja"] = v.Texts.GetValueOrDefault("ja") ?? "" }; commit(); }), 100));
            body.Children.Add(Ui.Row(T("届く", "Range"), ActionPick(a, "range", new PartTable<string>(("melee", "近く(前列だけ)", "melee (front row)"), ("ranged", "遠く", "ranged"), ("any", "どこでも", "any")), commit), 100));
            body.Children.Add(Ui.Row(T("対象", "Target"), ActionPick(a, "target", new PartTable<string>(("one", "1人", "one"), ("row", "前列", "front row"), ("party", "全員", "everyone")), commit), 100));
            var attack = new CheckBox { Content = T("攻撃ロールで当てる(外せば、効果をそのまま起こす。セーヴは効果に書く)", "Attack roll (unchecked: effects apply directly; put saves in the effects)"), IsChecked = a["to_hit"] != null };
            attack.Click += (_, _) => { if (attack.IsChecked == true) a["to_hit"] = 4; else a.Remove("to_hit"); commit(); rebuild(); };
            body.Children.Add(Ui.Row("", attack, 100));
            if (a["to_hit"] != null) body.Children.Add(Ui.Row(T("命中 +", "To hit +"), Ui.Text(a["to_hit"]!.ToString(), v => { if (int.TryParse(v, out int n)) { a["to_hit"] = n; commit(); } }), 100));
            body.Children.Add(Ui.Row(T("再使用(d6)", "Recharge (d6)"), Ui.Text(a["recharge"]?.ToString() ?? "", v => { if (v.Trim().Length == 0) a.Remove("recharge"); else if (int.TryParse(v, out int n)) a["recharge"] = n; commit(); }), 100));
            body.Children.Add(Ui.Row(T("1戦で何回", "Uses per fight"), Ui.Text(a["uses"]?.ToString() ?? "", v => { if (v.Trim().Length == 0) a.Remove("uses"); else if (int.TryParse(v, out int n)) a["uses"] = n; commit(); }), 100));
            if (a["effects"] is not JsonArray effects) a["effects"] = effects = [];
            body.Children.Add(Ui.Row(T("効果", "Effects"), EffectList(effects, commit, ActionEffectTypes), 100));
            body.Children.Add(Ui.Button(T("この行動を消す", "Remove this action"), () => { array.Remove(a); commit(); rebuild(); }));
            panel.Children.Add(ex);
        }
        panel.Children.Add(Ui.Button(T("＋ 行動を足す", "+ Add action"), () =>
        {
            int n = array.Count + 1;
            array.Add(new JsonObject
            {
                ["id"] = $"action{n}", ["name"] = new JsonObject { ["en"] = "Attack", ["ja"] = "攻撃" }, ["range"] = "melee", ["target"] = "one", ["to_hit"] = 4,
                ["effects"] = new JsonArray(new JsonObject { ["type"] = "damage", ["dice"] = "1d6", ["damage_type"] = "slashing" }),
            });
            commit();
            rebuild();
        }));
        return panel;
    }

    private static UIElement ActionPick(JsonObject a, string key, IReadOnlyDictionary<string, string> names, Action commit)
    {
        var list = names.Select(kv => new Choice(kv.Key, kv.Value)).ToList();
        string v = (string?)a[key] ?? list[0].Key;
        var box = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(c => c.Key == v) };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice c) { a[key] = c.Key; commit(); } };
        return box;
    }

    /// <summary>複数回攻撃: 組み合わせごとに「行動 × 回数」を並べる(どれか1つの組み合わせを選んで使う)。</summary>
    private UIElement Multiattack()
    {
        var (array, commit) = Working("multiattack");
        var actions = ((Get("actions") as JsonArray) ?? []).OfType<JsonObject>()
            .Select(a => new Choice((string?)a["id"] ?? "", Loc.Name(a["name"] is JsonObject n ? LocalizedText.Of((string?)n["en"] ?? "", (string?)n["ja"]) : null) ?? "")).ToList();
        var panel = new StackPanel();
        int index = 0;
        foreach (var node in array.ToList())
        {
            if (node is not JsonArray option) continue;
            index++;
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = T($"組み合わせ {index}: ", $"Option {index}: "), VerticalAlignment = VerticalAlignment.Center });
            foreach (var pn in option.ToList())
            {
                if (pn is not JsonObject piece) continue;
                var pick = new ComboBox { ItemsSource = actions, SelectedItem = actions.FirstOrDefault(c => c.Key == (string?)piece["action"]), MinWidth = 110 };
                pick.SelectionChanged += (_, _) => { if (pick.SelectedItem is Choice c) { piece["action"] = c.Key; commit(); } };
                var count = new TextBox { Text = piece["count"]?.ToString() ?? "1", Width = 44, Margin = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
                count.TextChanged += (_, _) => { if (int.TryParse(count.Text, out int n) && n > 0) { piece["count"] = n; commit(); } };
                row.Children.Add(pick);
                row.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                row.Children.Add(count);
                row.Children.Add(Ui.Button("−", () => { option.Remove(piece); if (option.Count == 0) array.Remove(option); commit(); rebuild(); }, T("この行動を外す", "Remove this action")));
            }
            row.Children.Add(Ui.Button("＋", () => { option.Add(new JsonObject { ["action"] = actions.FirstOrDefault()?.Key ?? "", ["count"] = 1 }); commit(); rebuild(); }, T("行動を足す", "Add an action")));
            panel.Children.Add(row);
        }
        panel.Children.Add(Ui.Button(T("＋ 組み合わせを足す", "+ Add option"), () =>
        {
            array.Add(new JsonArray(new JsonObject { ["action"] = actions.FirstOrDefault()?.Key ?? "", ["count"] = 2 }));
            commit();
            rebuild();
        }));
        return panel;
    }

    /// <summary>呪文: 唱えるか → 術者のレベル・能力値・難しさ・攻撃・スロット・呪文の一覧。</summary>
    private UIElement Spellcasting()
    {
        var panel = new StackPanel();
        var sc = Get("spellcasting") as JsonObject;
        var on = new CheckBox { Content = T("呪文を唱える", "Casts spells"), IsChecked = sc != null };
        on.Click += (_, _) =>
        {
            if (on.IsChecked == true)
                Set("spellcasting", new JsonObject { ["level"] = 1, ["ability"] = "int", ["dc"] = 11, ["attack"] = 3, ["slots"] = new JsonObject { ["1"] = 2 }, ["spells"] = new JsonArray() });
            else if (baseObj?["spellcasting"] is JsonObject)
            {
                // 写したもので元が呪文を唱えるなら、消すのではなく null を書いて「唱えない」にする(消すと読むときに元の呪文が戻る)
                o["spellcasting"] = null;
                edited();
            }
            else Set("spellcasting", null);
            rebuild();
        };
        panel.Children.Add(on);
        if (sc == null) return panel;
        JsonObject Mine()
        {
            if (o["spellcasting"] is JsonObject m) return m;
            var copy = sc.DeepClone().AsObject();
            o["spellcasting"] = copy;
            return copy;
        }
        void SetSc(string k, JsonNode v) { Mine()[k] = v; edited(); }
        panel.Children.Add(Ui.Row(T("術者のレベル", "Caster level"), Ui.Text(sc["level"]?.ToString() ?? "1", v => { if (int.TryParse(v, out int n)) SetSc("level", n); }), LabelWidth));
        var ability = new ComboBox { ItemsSource = new[] { "int", "wis", "cha" }.Select(a => new Choice(a, PartNames.Name(PartNames.Abilities, a))).ToList() };
        ability.SelectedItem = ((List<Choice>)ability.ItemsSource).FirstOrDefault(c => c.Key == (string?)sc["ability"]);
        ability.SelectionChanged += (_, _) => { if (ability.SelectedItem is Choice c) SetSc("ability", c.Key); };
        panel.Children.Add(Ui.Row(T("能力値", "Ability"), ability, LabelWidth));
        panel.Children.Add(Ui.Row(T("難しさ(DC)", "Save DC"), Ui.Text(sc["dc"]?.ToString() ?? "10", v => { if (int.TryParse(v, out int n)) SetSc("dc", n); }), LabelWidth));
        panel.Children.Add(Ui.Row(T("呪文の攻撃 +", "Spell attack +"), Ui.Text(sc["attack"]?.ToString() ?? "0", v => { if (int.TryParse(v, out int n)) SetSc("attack", n); }), LabelWidth));
        var slots = new UniformGrid6 { Columns = 9 };
        for (int lv = 1; lv <= 9; lv++)
        {
            string key = lv.ToString();
            var cell = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            cell.Children.Add(new TextBlock { Text = T($"{lv}レベル", $"L{lv}"), Foreground = Ui.Dim });
            cell.Children.Add(Ui.Text((sc["slots"] as JsonObject)?[key]?.ToString() ?? "", v =>
            {
                var s = Mine()["slots"] as JsonObject ?? [];
                if (int.TryParse(v, out int n) && n > 0) s[key] = n;
                else s.Remove(key);
                Mine()["slots"] = s;
                edited();
            }));
            slots.Children.Add(cell);
        }
        panel.Children.Add(Ui.Row(T("スロット", "Slots"), slots, LabelWidth));
        var spells = (sc["spells"] as JsonArray)?.Select(n => (string?)n).OfType<string>() ?? [];
        var list = new ChipPicker(SpellChoices().Select(kv => new Choice(kv.Key, kv.Value)), spells, chosen =>
        {
            Mine()["spells"] = new JsonArray([.. chosen.Select(x => (JsonNode)JsonValue.Create(x)!)]);
            edited();
        });
        panel.Children.Add(Ui.Row(T("唱える呪文", "Spells"), list, LabelWidth));
        return panel;
    }

    private string SpellLabel(string id) => context.BaseData.Spells.TryGetValue(id, out var s)
        ? $"{Loc.Name(s.Name) ?? id}({(s.Level == 0 ? T("初級", "cantrip") : T($"{s.Level}レベル", $"level {s.Level}"))})" : id;

    // ---------------- 名前の表 ----------------

    private static readonly PartTable<string> ItemKinds = new(
        ("weapon", "武器", "Weapon"), ("armor", "鎧", "Armor"), ("shield", "盾", "Shield"), ("accessory", "装身具", "Accessory"), ("consumable", "消耗品", "Consumable"),
        ("gear", "道具", "Gear"), ("ammo", "矢弾", "Ammo"), ("backpack", "背負い袋", "Pack"), ("key", "大事な品", "Key item"));

    private static readonly PartTable<string> Rarities = new(
        ("mundane", "ふつう(魔法でない)", "Mundane"), ("common", "コモン", "Common"), ("uncommon", "アンコモン", "Uncommon"), ("rare", "レア", "Rare"),
        ("very_rare", "ベリーレア", "Very rare"), ("legendary", "レジェンダリー", "Legendary"), ("artifact", "アーティファクト", "Artifact"));

    private static readonly PartTable<string> Slots = new(
        ("ring", "指輪", "Ring"), ("neck", "首", "Neck"), ("cloak", "外套", "Cloak"), ("head", "頭", "Head"), ("hands", "手", "Hands"), ("feet", "足", "Feet"), ("waist", "腰", "Waist"));

    private static readonly PartTable<string> UseTargets = new(
        ("self", "使った人", "Self"), ("ally", "味方1人", "One ally"), ("allies", "味方全員", "All allies"), ("enemy", "敵1体", "One enemy"), ("group", "敵の1グループ", "Enemy group"));

    private static readonly PartTable<string> WeaponProperties = new(
        ("light", "軽い", "Light"), ("finesse", "妙技", "Finesse"), ("heavy", "重い", "Heavy"), ("two_handed", "両手", "Two-handed"), ("versatile", "持ち替え", "Versatile"),
        ("thrown", "投てき", "Thrown"), ("reach", "間合い", "Reach"), ("ammunition", "矢弾を使う", "Ammunition"), ("loading", "装填", "Loading"), ("returning", "戻る", "Returning"));

    private static readonly PartTable<string> Sizes = new(
        ("tiny", "超小型", "Tiny"), ("small", "小型", "Small"), ("medium", "中型", "Medium"), ("large", "大型", "Large"), ("huge", "超大型", "Huge"), ("gargantuan", "巨大", "Gargantuan"));

    private static readonly PartTable<string> ModifierStats = new(
        ("ac", "AC", "AC"), ("attack", "命中", "Attack"), ("damage", "ダメージ", "Damage"), ("save", "セーヴ", "Saves"), ("stealth", "隠密", "Stealth"));

    private static readonly Dictionary<string, string> EffectNames = new()
    {
        [EffectPart.Damage] = T("ダメージ", "Damage"), [EffectPart.Heal] = T("回復", "Heal"), [EffectPart.ConditionType] = T("状態にする", "Inflict condition"),
        [EffectPart.Save] = T("セーヴ(失敗・成功で分ける)", "Saving throw"), [EffectPart.Modifier] = T("値を足す", "Modifier"), [EffectPart.TempHp] = T("一時的な HP", "Temporary HP"),
        [EffectPart.HpPool] = T("HP の合計で状態にする", "HP pool condition"), [EffectPart.RemoveCondition] = T("状態を解く", "Remove condition"),
        [EffectPart.Stabilize] = T("容体を落ち着かせる", "Stabilize"), [EffectPart.Kill] = T("倒す(HP が少なければ)", "Kill (low HP)"), [EffectPart.Revive] = T("生き返らせる", "Revive"),
        [EffectPart.Identify] = T("鑑定", "Identify"), [EffectPart.RemoveCurse] = T("呪いを解く", "Remove curse"), [EffectPart.Resist] = T("耐性(被ダメージ半減)", "Resistance (half damage)"),
        [EffectPart.Vulnerable] = T("弱点(被ダメージ2倍)", "Vulnerability (double damage)"), [EffectPart.ImmuneCondition] = T("状態無効", "Condition immunity"),
        [EffectPart.SetAbility] = T("能力値をこの値に", "Set ability"), [EffectPart.Light] = T("明かり", "Light"), [EffectPart.FeatherFall] = T("落下を防ぐ", "Feather fall"),
        [EffectPart.Recall] = T("町へ帰る", "Recall"), [EffectPart.Knock] = T("錠を開ける", "Knock"), [EffectPart.Wish] = T("ウィッシュ", "Wish"),
    };

    private static readonly string[] PassiveTypes = [EffectPart.Modifier, EffectPart.Resist, EffectPart.Vulnerable, EffectPart.ImmuneCondition, EffectPart.SetAbility];
    private static readonly string[] ItemEffectTypes = [EffectPart.Damage, EffectPart.Heal, EffectPart.ConditionType, EffectPart.Save, EffectPart.Modifier, EffectPart.TempHp,
        EffectPart.RemoveCondition, EffectPart.Revive, EffectPart.Identify, EffectPart.RemoveCurse, EffectPart.Light, EffectPart.FeatherFall, EffectPart.Recall];
    private static readonly string[] ActionEffectTypes = [EffectPart.Damage, EffectPart.ConditionType, EffectPart.Save, EffectPart.Heal, EffectPart.Modifier, EffectPart.Kill];

    private IReadOnlyDictionary<string, string> DamageNames()
    {
        var words = new DraftSummary(context);
        return new PartTable<string>([.. DamageTypes.All.Select(t => (t, words.DamageType(t), words.DamageType(t)))]);
    }

    private IReadOnlyDictionary<string, string> DamageNamesWithPhysical() =>
        new PartTable<string>([.. DamageNames().Select(kv => (kv.Key, kv.Value, kv.Value)), (DamageTypes.NonmagicalPhysical, "魔法でない武器の物理", "nonmagical physical")]);

    private IReadOnlyDictionary<string, string> ConditionNames()
    {
        var words = new DraftSummary(context);
        return new PartTable<string>([.. Enum.GetValues<GravewardSpiral.Core.Effects.Condition>().Select(c => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(c.ToString()))
            .Select(id => (id, words.ConditionName(id), words.ConditionName(id)))]);
    }

    private IReadOnlyDictionary<string, string> CreatureTypes() =>
        new PartTable<string>([.. context.BaseData.Monsters.Values.Select(m => m.CreatureType).Distinct().Order(StringComparer.Ordinal).Select(t => (t, t, t))]);

    /// <summary>特徴: 戦闘で効くもの(チェック)と、効かない名札(カンマで並べる文字)。</summary>
    private UIElement Traits()
    {
        var all = (Get("traits") as JsonArray)?.Select(n => (string?)n).OfType<string>().ToList() ?? [];
        var working = all.Where(MonsterDef.WorkingTraits.Contains).ToList();
        var tags = all.Where(t => !MonsterDef.WorkingTraits.Contains(t)).ToList();
        void Save() => Set("traits", new JsonArray([.. working.Concat(tags).Select(t => (JsonNode)JsonValue.Create(t)!)]));
        var panel = new StackPanel();
        var checks = new WrapPanel();
        foreach (var t in MonsterDef.WorkingTraits)
        {
            var box = new CheckBox { Content = new TextBlock { Text = TraitName(t) }, IsChecked = working.Contains(t), Margin = new Thickness(0, 1, 12, 1) };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true) { if (!working.Contains(t)) working.Add(t); }
                else working.Remove(t);
                Save();
            };
            checks.Children.Add(box);
        }
        panel.Children.Add(checks);
        panel.Children.Add(new TextBlock { Text = T("名札(戦闘では効かない。カンマで区切る)", "Labels (no effect in combat; comma-separated)"), Foreground = Ui.Dim, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(Ui.Text(string.Join(", ", tags), v =>
        {
            tags = [.. v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
            Save();
        }));
        return panel;
    }

    private static string TraitName(string t) => t switch
    {
        "legendary_resistance" => T("伝説的抵抗(セーヴに失敗しても、1回の戦闘で3回まで成功にする)", "Legendary resistance"),
        "magic_resistance" => T("魔法抵抗(呪文と魔法へのセーヴに有利)", "Magic resistance"),
        "pack_tactics" => T("群れの戦術(同じグループに仲間が残っていると攻撃に有利)", "Pack tactics"),
        "undead_fortitude" => T("アンデッドの不屈(倒れても、耐久力のセーヴで HP 1 で残る)", "Undead fortitude"),
        _ => t,
    };

    /// <summary>標準の品のうち、その種類のもの(元の武器・元の鎧)。</summary>
    private IReadOnlyDictionary<string, string> Standard(string ItemKindKey) =>
        new PartTable<string>([.. context.BaseData.Items.Values.Where(i => i.Kind.ToString().Equals(ItemKindKey, StringComparison.OrdinalIgnoreCase) && i.Base == null && !i.IsMagic)
            .OrderBy(i => Loc.Name(i.Name), StringComparer.CurrentCulture).Select(i => (i.Id, Loc.Name(i.Name) ?? i.Id, Loc.Name(i.Name) ?? i.Id))]);

    private IReadOnlyDictionary<string, string> SpellChoices() =>
        new PartTable<string>([.. context.BaseData.Spells.Values.OrderBy(s => s.Level).ThenBy(s => Loc.Name(s.Name), StringComparer.CurrentCulture)
            .Select(s => (s.Id, SpellLabel(s.Id), SpellLabel(s.Id)))]);
}
