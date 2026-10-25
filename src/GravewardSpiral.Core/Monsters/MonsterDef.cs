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
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public ActionRange Range { get; set; } = ActionRange.Melee;
    public ActionTarget Target { get; set; } = ActionTarget.One;
    public int? ToHit { get; set; }
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

    public string Ability { get; set; } = "int";
    public int Dc { get; set; } = 10;

    /// <summary>呪文の攻撃ボーナス。</summary>
    public int Attack { get; set; }

    /// <summary>呪文レベルごとのスロットの数。</summary>
    public Dictionary<string, int> Slots { get; set; } = [];

    public List<string> Spells { get; set; } = [];
}

public sealed class MultiattackPiece
{
    public string Action { get; set; } = "";
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

    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>正体が分かる前の名前(「小さな人影」など)。</summary>
    public LocalizedText? UnknownName { get; set; }

    public string Size { get; set; } = "medium";
    public string CreatureType { get; set; } = "monstrosity";
    public double Cr { get; set; }
    public int Xp { get; set; }
    public int Proficiency { get; set; } = 2;
    public int Ac { get; set; } = 10;

    /// <summary>HP のダイス式。1体ずつ振る。</summary>
    public string Hp { get; set; } = "1d8";

    public int HpAverage { get; set; }
    public int Speed { get; set; } = 30;
    public bool Flies { get; set; }
    public Dictionary<string, int> Abilities { get; set; } = [];

    /// <summary>習熟のあるセーヴのボーナス(無い能力値は修正値だけ)。</summary>
    public Dictionary<string, int> Saves { get; set; } = [];

    public Dictionary<string, int> Skills { get; set; } = [];
    public int PassivePerception { get; set; } = 10;

    /// <summary>1 グループに出る数のダイス式。</summary>
    public string Appearing { get; set; } = "1";

    public List<string> Resist { get; set; } = [];
    public List<string> Vulnerable { get; set; } = [];
    public List<string> Immune { get; set; } = [];
    public List<Condition> ConditionImmune { get; set; } = [];
    public List<string> Traits { get; set; } = [];
    public List<MonsterAction> Actions { get; set; } = [];

    /// <summary>複数回攻撃の組み合わせ(どれか1つを選ぶ)。</summary>
    public List<List<MultiattackPiece>> Multiattack { get; set; } = [];

    /// <summary>取り込めなかった行動の名前(戦闘では使わない)。</summary>
    public List<string> Unsupported { get; set; } = [];

    /// <summary>戦闘で使うのにまだ足りない仕組み(spells など)。空でなければ遭遇に出さない。</summary>
    public List<string> Needs { get; set; } = [];

    public bool Legendary { get; set; }

    public MonsterSpellcasting? Spellcasting { get; set; }

    /// <summary>画像の形(square = 512x512、wide = 768x512)。</summary>
    public string Art { get; set; } = "square";

    public List<string> Tags { get; set; } = [];
    public double Weight { get; set; } = 1;

    public bool Usable => Needs.Count == 0 && Actions.Count > 0;

    public int Score(Ability a) => Abilities.TryGetValue(a.Code(), out var v) ? v : 10;
    public int Mod(Ability a) => Rules.Abilities.Modifier(Score(a));
    public int SaveBonus(Ability a) => Saves.TryGetValue(a.Code(), out var v) ? v : Mod(a);
    public int Stealth => Skills.TryGetValue("stealth", out var v) ? v : Mod(Ability.Dex);
    public bool Has(string trait) => Traits.Contains(trait);
    public MonsterAction? Action(string id) => Actions.FirstOrDefault(a => a.Id == id);
}
