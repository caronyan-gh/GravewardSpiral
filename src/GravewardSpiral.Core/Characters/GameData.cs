using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

public sealed class SkillDef
{
    public string Id { get; set; } = "";
    public string Ability { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    public Ability AbilityValue => Abilities.Parse(Ability);
}

public sealed class RaceDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public LocalizedText? Subrace { get; set; }
    public Dictionary<string, int> AbilityBonus { get; set; } = [];

    /// <summary>好きな能力値に 1 ずつ足せる数(ハーフエルフ)。</summary>
    public int FreeAbilityPoints { get; set; }

    /// <summary>好きな技能を覚えられる数(ハーフエルフ)。</summary>
    public int FreeSkills { get; set; }

    public List<string> Skills { get; set; } = [];
    public bool Darkvision { get; set; }
    public int HpPerLevel { get; set; }
    public List<string> Traits { get; set; } = [];
    public LocalizedText? Description { get; set; }
}

public sealed class ClassDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public int HitDie { get; set; } = 8;

    /// <summary>大事な能力値の順(能力値を「おまかせ」で割り当てるときに使う)。</summary>
    public List<string> Primary { get; set; } = [];

    public List<string> Saves { get; set; } = [];
    public List<string> Armor { get; set; } = [];
    public List<string> Weapons { get; set; } = [];
    public List<string> Tools { get; set; } = [];
    public int SkillChoices { get; set; }

    /// <summary>選べる技能。["*"] ならどれでも。</summary>
    public List<string> Skills { get; set; } = [];

    public string? Spellcasting { get; set; }

    /// <summary>
    /// マルチクラスの条件(5版)。どれか1つの組を満たせばよく、組の中は全部の能力値が要る(例: ファイターは 筋力 13 か 敏捷力 13)。
    /// 新しいクラスを取るときは、新しいクラスと今のクラス全部の条件を満たす必要がある。
    /// </summary>
    public List<Dictionary<string, int>> Multiclass { get; set; } = [];

    /// <summary>キャラクターの能力値がマルチクラスの条件を満たすか(条件が無ければ満たす)。</summary>
    public bool MeetsMulticlass(AbilityScores scores) =>
        Multiclass.Count == 0 || Multiclass.Any(group => group.All(kv => scores[Abilities.Parse(kv.Key)] >= kv.Value));

    /// <summary>鎧を着ていないときの AC に足す能力値(バーバリアンは con、モンクは wis)。</summary>
    public string? UnarmoredDefense { get; set; }

    /// <summary>最初の金のダイス式。10 倍して金貨にする(5版の初期の財産と同じ)。</summary>
    public string StartGold { get; set; } = "4d4";

    public List<string> StartItems { get; set; } = [];
    public LocalizedText? Description { get; set; }

    public IEnumerable<Ability> PrimaryAbilities => Primary.Select(Abilities.Parse);
    public bool SavesWith(Ability a) => Saves.Any(s => Abilities.Parse(s) == a);
}

public sealed class BackgroundDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public List<string> Skills { get; set; } = [];
    public LocalizedText? Description { get; set; }
}

/// <summary>キャラクター・アイテム・モンスターのデータ(パックの races / classes / skills / backgrounds / items / monsters)。</summary>
public sealed class GameData
{
    public Dictionary<string, SkillDef> Skills { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RaceDef> Races { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ClassDef> Classes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, BackgroundDef> Backgrounds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ItemDef> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, MonsterDef> Monsters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SpellDef> Spells { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, WishDef> Wishes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, BossDef> Bosses { get; } = new(StringComparer.Ordinal);

    /// <summary>名のある敵(1つの世界に 0〜2 体。決まったユニーク品を必ず落とす)。</summary>
    public Dictionary<string, NamedMonsterDef> NamedMonsters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Events.EventDef> Events { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Combat.FloorTheme> FloorThemes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AffixDef> Affixes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DropTable> Drops { get; } = new(StringComparer.Ordinal);
    public List<string> WishOrder { get; } = [];

    /// <summary>自由な迷宮の由来の部品(場所・災い・底・町の頼み・町の名前)。</summary>
    public Dictionary<string, Ai.WorldPart> WorldParts { get; } = new(StringComparer.Ordinal);

    /// <summary>種族ごとの名前の一覧(おまかせの名前)。</summary>
    public Dictionary<string, NameListDef> NameLists { get; } = new(StringComparer.Ordinal);

    /// <summary>パックの順番どおりの並び(画面に出す順)。</summary>
    public List<string> RaceOrder { get; } = [];
    public List<string> ClassOrder { get; } = [];
    public List<string> BackgroundOrder { get; } = [];

    public static GameData Load(ContentLibrary content)
    {
        var d = new GameData();
        void Read<T>(string collection, Dictionary<string, T> into, List<string>? order, Func<T, string> id) where T : class
        {
            foreach (var item in content.All(collection))
            {
                try
                {
                    var v = item.As<T>();
                    into[id(v)] = v;
                    order?.Add(id(v));
                }
                catch (System.Text.Json.JsonException ex)
                {
                    content.Problems.Add($"{item.Pack.Id}/{collection}.json の '{item.Id}' が読めません: {ex.Message}");
                }
            }
        }
        Read("skills", d.Skills, null, s => s.Id);
        Read("races", d.Races, d.RaceOrder, r => r.Id);
        Read("classes", d.Classes, d.ClassOrder, c => c.Id);
        Read("backgrounds", d.Backgrounds, d.BackgroundOrder, b => b.Id);
        Read("items", d.Items, null, i => i.Id);
        Read("monsters", d.Monsters, null, m => m.Id);
        Read("spells", d.Spells, null, s => s.Id);
        Read("wishes", d.Wishes, d.WishOrder, w => w.Id);
        Read("bosses", d.Bosses, null, b => b.Id);
        Read(NamedMonsterDef.Collection, d.NamedMonsters, null, n => n.Id);
        Read(Core.Events.EventDef.Collection, d.Events, null, e => e.Id);
        Read(Combat.FloorTheme.Collection, d.FloorThemes, null, t => t.Id);
        Read(AffixDef.Collection, d.Affixes, null, a => a.Id);
        Read(DropTable.Collection, d.Drops, null, t => t.Id);
        Read(NameListDef.Collection, d.NameLists, null, n => n.Id);
        Read(Ai.WorldPart.Collection, d.WorldParts, null, p => p.Id);
        d.Check(content);
        return d;
    }

    private void Check(ContentLibrary content)
    {
        void Need(string where, string collection, IEnumerable<string> ids, Func<string, bool> exists)
        {
            foreach (var id in ids)
            {
                if (!exists(id)) content.Problems.Add($"{where}: {collection} に '{id}' がありません");
            }
        }
        foreach (var s in Skills.Values)
        {
            try { _ = s.AbilityValue; }
            catch (FormatException ex) { content.Problems.Add($"skills '{s.Id}': {ex.Message}"); }
        }
        foreach (var r in Races.Values)
        {
            Need($"races '{r.Id}' の skills", "skills", r.Skills, Skills.ContainsKey);
            foreach (var k in r.AbilityBonus.Keys)
            {
                try { Abilities.Parse(k); }
                catch (FormatException ex) { content.Problems.Add($"races '{r.Id}': {ex.Message}"); }
            }
        }
        foreach (var c in Classes.Values)
        {
            Need($"classes '{c.Id}' の skills", "skills", c.Skills.Where(s => s != "*"), Skills.ContainsKey);
            Need($"classes '{c.Id}' の start_items", "items", c.StartItems, Items.ContainsKey);
            if (!DiceExpression.TryParse(c.StartGold, out _, out var err))
                content.Problems.Add($"classes '{c.Id}' の start_gold: {err}");
        }
        foreach (var b in Backgrounds.Values)
            Need($"backgrounds '{b.Id}' の skills", "skills", b.Skills, Skills.ContainsKey);
        ResolveItemBases(content);
        foreach (var i in Items.Values)
        {
            CheckEffects(content, $"items '{i.Id}' の passive", i.Passive);
            CheckEffects(content, $"items '{i.Id}' の use", i.Use);
            CheckEffects(content, $"items '{i.Id}' の on_hit", i.OnHit);
            if (i.UseSpell != null && !(Spells.TryGetValue(i.UseSpell, out var us) && us.Usable))
                content.Problems.Add($"items '{i.Id}' の use_spell: 使える呪文 '{i.UseSpell}' がありません");
            if (i.Kind == ItemKind.Accessory && i.Slot is not ("ring" or "neck" or "cloak" or "head" or "hands" or "feet" or "waist"))
                content.Problems.Add($"items '{i.Id}': slot '{i.Slot}' は ring / neck / cloak / head / hands / feet / waist のどれかです");
            if (i.Damage != null && !DiceExpression.TryParse(i.Damage, out _, out var err))
                content.Problems.Add($"items '{i.Id}' の damage: {err}");
        }
        foreach (var m in Monsters.Values) CheckMonster(content, m);
        foreach (var s in Spells.Values)
        {
            CheckEffects(content, $"spells '{s.Id}'", s.Effects);
            if (s.Summon != null && !Monsters.ContainsKey(s.Summon.Monster))
                content.Problems.Add($"spells '{s.Id}' の summon: monsters に '{s.Summon.Monster}' がありません");
        }
        foreach (var b in Bosses.Values)
            Need($"bosses '{b.Id}' の groups", "monsters", b.Groups.Select(g => g.Monster), Monsters.ContainsKey);
        foreach (var n in NamedMonsters.Values)
        {
            Need($"named_monsters '{n.Id}' の monster", "monsters", [n.Monster], Monsters.ContainsKey);
            Need($"named_monsters '{n.Id}' の item", "items", [n.Item], Items.ContainsKey);
            if (n.Placement is not ("lair" or "wander" or "either")) content.Problems.Add($"named_monsters '{n.Id}' の placement は lair / wander / either です");
        }
        foreach (var w in Wishes.Values)
            CheckEffects(content, $"wishes '{w.Id}'", w.Effects);
        foreach (var e in Events.Values) content.Problems.AddRange(Core.Events.EventCheck.Check(this, e, content));
        foreach (var e in Events.Values.Where(e => e.Scatter != null && !Core.Events.EventScatter.RarityWeights.ContainsKey(e.Scatter.Rarity)))
            content.Problems.Add($"events '{e.Id}': rarity '{e.Scatter!.Rarity}' は common / uncommon / rare / legendary のどれかです");
        // 連動する組: 部品が2つ以上(1つなら組にする意味が無い)、step が重ならない
        foreach (var g in Events.Values.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!))
        {
            if (g.Count() < 2) content.Problems.Add($"events の組 '{g.Key}': 部品が1つしかありません");
            foreach (var dup in g.GroupBy(e => e.Scatter!.Step).Where(x => x.Count() > 1))
                content.Problems.Add($"events の組 '{g.Key}': step {dup.Key} が重なっています({string.Join(", ", dup.Select(e => e.Id))})");
        }
        foreach (var a in Affixes.Values)
        {
            if (a.Tiers.Count == 0) content.Problems.Add($"affixes '{a.Id}': tiers がありません");
            if (a.Kinds.Count == 0 || a.Kinds.Any(k => k is not ("weapon" or "armor" or "shield")))
                content.Problems.Add($"affixes '{a.Id}': kinds は weapon / armor / shield です");
            if (a.Position is not ("prefix" or "suffix")) content.Problems.Add($"affixes '{a.Id}': position は prefix / suffix です");
            for (int i = 0; i < a.Tiers.Count; i++)
            {
                CheckEffects(content, $"affixes '{a.Id}' の tiers[{i}] の passive", a.Tiers[i].Passive);
                CheckEffects(content, $"affixes '{a.Id}' の tiers[{i}] の on_hit", a.Tiers[i].OnHit);
            }
        }
        foreach (var t in Drops.Values)
        {
            if (t.Monster != null && !Monsters.ContainsKey(t.Monster)) content.Problems.Add($"drops '{t.Id}': monsters に '{t.Monster}' がありません");
            if (t.Boss != null && !Bosses.ContainsKey(t.Boss)) content.Problems.Add($"drops '{t.Id}': bosses に '{t.Boss}' がありません");
            foreach (var e in t.Entries)
            {
                if (e.Item != null && !Items.ContainsKey(e.Item)) content.Problems.Add($"drops '{t.Id}': items に '{e.Item}' がありません");
                if (e.Rarity != null && !Treasure.Rarities.Contains(e.Rarity)) content.Problems.Add($"drops '{t.Id}': 等級 '{e.Rarity}' がありません");
                if ((e.Item == null) == (e.Rarity == null)) content.Problems.Add($"drops '{t.Id}': 候補には item か rarity のどちらか1つを書きます");
            }
        }
    }

    /// <summary>魔法の武器・鎧の base から、書いていない項目(ダメージ、AC、特性など)を写す。</summary>
    private void ResolveItemBases(ContentLibrary content)
    {
        foreach (var i in Items.Values.Where(i => i.Base != null))
        {
            if (!Items.TryGetValue(i.Base!, out var b) || b.Base != null)
            {
                content.Problems.Add($"items '{i.Id}' の base: ふつうの品 '{i.Base}' がありません");
                continue;
            }
            i.Category ??= b.Category;
            if (!i.Ranged) i.Ranged = b.Ranged;
            i.Damage ??= b.Damage;
            i.DamageType ??= b.DamageType;
            i.VersatileDamage ??= b.VersatileDamage;
            if (i.Properties.Count == 0) i.Properties = [.. b.Properties];
            i.ArmorType ??= b.ArmorType;
            if (i.ArmorClass == 0) i.ArmorClass = b.ArmorClass;
            i.DexCap ??= b.DexCap;
            if (i.StrengthRequired == 0) i.StrengthRequired = b.StrengthRequired;
            if (!i.StealthDisadvantage) i.StealthDisadvantage = b.StealthDisadvantage;
        }
    }

    /// <summary>効果の部品の種類・ダイス式・能力値を確かめる。MOD(呪文の能力値)は 0 として読む。</summary>
    /// <summary>modifier の stat に書けるもの(技能は 5版の 18 個)。</summary>
    private static readonly HashSet<string> ModifierStats =
    [
        "ac", "attack", "save", "damage", "attacks", "disarm", "flee",
        "acrobatics", "animal_handling", "arcana", "athletics", "deception", "history", "insight", "intimidation", "investigation",
        "medicine", "nature", "perception", "performance", "persuasion", "religion", "sleight_of_hand", "stealth", "survival",
    ];

    private static void CheckEffects(ContentLibrary content, string where, IEnumerable<EffectPart> parts)
    {
        void Dice(string? text)
        {
            if (text == null) return;
            if (!DiceExpression.TryParse(text.Replace("MOD", "0"), out _, out var err)) content.Problems.Add($"{where} の dice '{text}': {err}");
        }
        foreach (var p in parts.SelectMany(e => e.Flatten()))
        {
            if (!EffectPart.KnownTypes.Contains(p.Type)) content.Problems.Add($"{where}: 効果の種類 '{p.Type}' がありません");
            Dice(p.Dice);
            foreach (var v in p.ByLevel.Values) Dice(v);
            if (p.Type == EffectPart.Save || p.Ability != null)
            {
                try { Abilities.Parse(p.Ability ?? ""); }
                catch (FormatException ex) { content.Problems.Add($"{where}: {ex.Message}"); }
            }
            if (p.Type is EffectPart.ConditionType or EffectPart.RemoveCondition && p.Condition == null)
                content.Problems.Add($"{where}: condition がありません");
            if (p.Type == EffectPart.Modifier && !ModifierStats.Contains(p.Stat ?? ""))
                content.Problems.Add($"{where}: modifier の stat '{p.Stat}' は ac / attack / save / damage / attacks / disarm / flee か、技能の id です");
            if (p.Type is EffectPart.Resist or EffectPart.Vulnerable && !DamageTypes.All.Contains(p.DamageType ?? ""))
                content.Problems.Add($"{where}: ダメージの種類 '{p.DamageType}' がありません");
            if (p.Type == EffectPart.ImmuneCondition && p.Condition == null) content.Problems.Add($"{where}: condition がありません");
        }
    }

    private static void CheckMonster(ContentLibrary content, MonsterDef m)
    {
        string where = $"monsters '{m.Id}'";
        void Dice(string what, string? text)
        {
            if (text != null && !DiceExpression.TryParse(text, out _, out var err)) content.Problems.Add($"{where} の {what}: {err}");
        }
        Dice("hp", m.Hp);
        Dice("appearing", m.Appearing);
        foreach (var a in m.Actions)
        {
            foreach (var p in a.Effects.SelectMany(e => e.Flatten()))
            {
                if (!EffectPart.KnownTypes.Contains(p.Type)) content.Problems.Add($"{where} の {a.Id}: 効果の種類 '{p.Type}' がありません");
                Dice($"{a.Id} の dice", p.Dice);
                if (p.Type == EffectPart.Save)
                {
                    try { Abilities.Parse(p.Ability ?? ""); }
                    catch (FormatException ex) { content.Problems.Add($"{where} の {a.Id}: {ex.Message}"); }
                }
                if (p.Type == EffectPart.ConditionType && p.Condition == null) content.Problems.Add($"{where} の {a.Id}: condition がありません");
            }
        }
        foreach (var opt in m.Multiattack)
        {
            foreach (var piece in opt.Where(x => m.Action(x.Action) == null))
                content.Problems.Add($"{where} の multiattack: 行動 '{piece.Action}' がありません");
        }
    }

    /// <summary>そのクラスで選べる技能。</summary>
    public IEnumerable<string> SkillChoicesFor(ClassDef c) =>
        c.Skills.Contains("*") ? Skills.Keys.Order(StringComparer.Ordinal) : c.Skills;
}
