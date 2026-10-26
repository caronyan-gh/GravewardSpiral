using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Monsters;

/// <summary>行動が届く距離。melee は近くのグループからパーティの前列だけ、ranged と any はどこからでも誰にでも。</summary>
public enum ActionRange { Melee, Ranged, Any }

/// <summary>行動の対象。row はパーティの前列(範囲の小さいブレスなど)、party は全員。</summary>
public enum ActionTarget { One, Row, Party }

/// <summary>モンスターの行動1つ。to_hit があれば攻撃ロールで、命中したら effects を起こす。無ければ effects をそのまま起こす(セーヴの行動)。</summary>
public sealed class MonsterAction
{
    /// <summary>行動の id(multiattack の action で指す)。</summary>
    public string Id { get; set; } = "";

    /// <summary>行動の名前({"en": "...", "ja": "..."})。</summary>
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>届く距離: melee(前列だけ)/ ranged / any。</summary>
    public ActionRange Range { get; set; } = ActionRange.Melee;

    /// <summary>対象: one(1人)/ row(前列)/ party(全員)。</summary>
    public ActionTarget Target { get; set; } = ActionTarget.One;

    /// <summary>攻撃ロールのボーナス。書けば攻撃(当たったら effects)、書かなければセーヴの行動(effects をそのまま起こす)。</summary>
    public int? ToHit { get; set; }

    /// <summary>起こす効果(ダメージ・状態など)。</summary>
    public List<EffectPart> Effects { get; set; } = [];

    /// <summary>再使用の目(5 なら、自分の番の始めに d6 で 5 以上が出たらまた使える)。</summary>
    public int? Recharge { get; set; }

    /// <summary>再使用を分け合う組(金属の竜の2つの息など)。どれかを使うと組の全部が使えなくなり、再使用の目は組で1回振る。</summary>
    public string? RechargeGroup { get; set; }

    /// <summary>1 回の戦闘で使える回数(1日 N 回の行動)。</summary>
    public int? Uses { get; set; }

    public bool IsAttack => ToHit.HasValue;
}

/// <summary>モンスターの呪文(スロットで唱える。SRD の Spellcasting)。</summary>
public sealed class MonsterSpellcasting
{
    /// <summary>術者のレベル(初級呪文の強さ)。</summary>
    public int Level { get; set; } = 1;

    /// <summary>呪文の能力値(int / wis / cha)。</summary>
    public string Ability { get; set; } = "int";

    /// <summary>呪文のセーヴの難易度。</summary>
    public int Dc { get; set; } = 10;

    /// <summary>呪文の攻撃ボーナス。</summary>
    public int Attack { get; set; }

    /// <summary>呪文レベルごとのスロットの数。</summary>
    public Dictionary<string, int> Slots { get; set; } = [];

    /// <summary>唱える呪文の id。</summary>
    public List<string> Spells { get; set; } = [];
}

public sealed class MultiattackPiece
{
    /// <summary>行動の id(actions の id)。</summary>
    public string Action { get; set; } = "";

    /// <summary>その行動を何回するか。</summary>
    public int Count { get; set; } = 1;
}

/// <summary>モンスターの種類のデータ(パックの monsters.json。SRD は tools/srd_import.py で作る)。</summary>
public sealed class MonsterDef
{
    /// <summary>名前だけを変えた写し(名のある敵やボスに使う。正体は初めから分かる。id は元のまま: 絵と落とし物の表を使う)。</summary>
    public MonsterDef WithName(LocalizedText name)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<MonsterDef>(System.Text.Json.JsonSerializer.Serialize(this))!;
        copy.Name = name;
        copy.UnknownName = null;
        return copy;
    }

    /// <summary>モンスターの id(英小文字と _)。</summary>
    public string Id { get; set; } = "";

    /// <summary>名前({"en": "...", "ja": "..."})。</summary>
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>正体が分かる前の名前(「小さな人影」など)。</summary>
    public LocalizedText? UnknownName { get; set; }

    /// <summary>大きさ: tiny / small / medium / large / huge / gargantuan。</summary>
    public string Size { get; set; } = "medium";

    /// <summary>種類(undead / beast / humanoid / dragon など)。</summary>
    public string CreatureType { get; set; } = "monstrosity";

    /// <summary>脅威度(0.125・0.25・0.5・1〜)。</summary>
    public double Cr { get; set; }

    /// <summary>倒したときの経験値。</summary>
    public int Xp { get; set; }

    /// <summary>習熟ボーナス。</summary>
    public int Proficiency { get; set; } = 2;

    /// <summary>AC。</summary>
    public int Ac { get; set; } = 10;

    /// <summary>HP のダイス式。1体ずつ振る。</summary>
    public string Hp { get; set; } = "1d8";

    /// <summary>HP の平均(表示とおすすめに使う)。</summary>
    public int HpAverage { get; set; }

    /// <summary>移動の速さ(フィート。SRD の値)。</summary>
    public int Speed { get; set; } = 30;

    /// <summary>飛ぶか(SRD の値)。</summary>
    public bool Flies { get; set; }

    /// <summary>能力値({"str": 10, "dex": 14, ...})。</summary>
    public Dictionary<string, int> Abilities { get; set; } = [];

    /// <summary>習熟のあるセーヴのボーナス(無い能力値は修正値だけ)。</summary>
    public Dictionary<string, int> Saves { get; set; } = [];

    /// <summary>技能のボーナス({"perception": 3, "stealth": 4})。</summary>
    public Dictionary<string, int> Skills { get; set; } = [];

    /// <summary>受動知覚。</summary>
    public int PassivePerception { get; set; } = 10;

    /// <summary>1 グループに出る数のダイス式。</summary>
    public string Appearing { get; set; } = "1";

    /// <summary>半分になるダメージの種類。</summary>
    public List<string> Resist { get; set; } = [];

    /// <summary>倍になるダメージの種類。</summary>
    public List<string> Vulnerable { get; set; } = [];

    /// <summary>効かないダメージの種類。</summary>
    public List<string> Immune { get; set; } = [];

    /// <summary>かからない状態。</summary>
    public List<Condition> ConditionImmune { get; set; } = [];

    /// <summary>特徴(pack_tactics / undead_fortitude / legendary_resistance など)。ゲームが効き目を持つのは WorkingTraits だけで、ほかは名札。</summary>
    public List<string> Traits { get; set; } = [];

    /// <summary>戦闘で効き目のある特徴(CombatEngine と Combatants が見るもの)。magic_resistance は呪文と魔法の効果へのセーヴに有利。</summary>
    public static readonly string[] WorkingTraits = ["legendary_resistance", "magic_resistance", "pack_tactics", "undead_fortitude"];

    /// <summary>再生: 自分の番の初めに戻す HP(HP が 1 以上のとき。0 なら再生しない)。</summary>
    public int Regenerate { get; set; }

    /// <summary>このダメージの種類を受けると、次の番の初めは再生しない(トロルの酸と火、ヴァンパイアの光輝)。</summary>
    public List<string> RegenerateStop { get; set; } = [];

    /// <summary>行動(攻撃・ブレスなど)。</summary>
    public List<MonsterAction> Actions { get; set; } = [];

    /// <summary>複数回攻撃の組み合わせ(どれか1つを選ぶ)。</summary>
    public List<List<MultiattackPiece>> Multiattack { get; set; } = [];

    /// <summary>取り込めなかった行動の名前(戦闘では使わない)。</summary>
    public List<string> Unsupported { get; set; } = [];

    /// <summary>戦闘で使うのにまだ足りない仕組み(spells など)。空でなければ遭遇に出さない。</summary>
    public List<string> Needs { get; set; } = [];

    /// <summary>伝説のモンスターか(SRD の印)。</summary>
    public bool Legendary { get; set; }

    /// <summary>呪文を唱えるモンスターの、呪文の設定。</summary>
    public MonsterSpellcasting? Spellcasting { get; set; }

    /// <summary>画像の形(square = 512x512、wide = 768x512)。</summary>
    public string Art { get; set; } = "square";

    /// <summary>絵を借りるモンスターの id(自分の絵 art/monsters/&lt;id&gt;.png が無いとき)。extends で写したモンスターは、元を指す。</summary>
    public string? ArtFrom { get; set; }

    /// <summary>分類の印。</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>遭遇の抽選の重み(大きいほど出やすい。0 なら出ない)。</summary>
    public double Weight { get; set; } = 1;

    public bool Usable => Needs.Count == 0 && Actions.Count > 0;

    public int Score(Ability a) => Abilities.TryGetValue(a.Code(), out var v) ? v : 10;
    public int Mod(Ability a) => Rules.Abilities.Modifier(Score(a));
    public int SaveBonus(Ability a) => Saves.TryGetValue(a.Code(), out var v) ? v : Mod(a);
    public int Stealth => Skills.TryGetValue("stealth", out var v) ? v : Mod(Ability.Dex);
    public bool Has(string trait) => Traits.Contains(trait);
    public MonsterAction? Action(string id) => Actions.FirstOrDefault(a => a.Id == id);
}
