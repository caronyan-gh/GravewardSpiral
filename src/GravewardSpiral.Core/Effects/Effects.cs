namespace GravewardSpiral.Core.Effects;

/// <summary>5版の状態。</summary>
public enum Condition
{
    Blinded, Charmed, Deafened, Frightened, Grappled, Incapacitated, Invisible,
    Paralyzed, Petrified, Poisoned, Prone, Restrained, Stunned, Unconscious,
}

/// <summary>
/// 効果の部品1つ(呪文、敵の行動、アイテム、罠の「何が起きるか」)。JSON では "type" で種類を書く。
/// <list type="bullet">
/// <item>damage: dice のダメージ(damage_type の種類)</item>
/// <item>heal: dice だけ HP を戻す</item>
/// <item>condition: condition の状態にする。rounds が無ければ戦闘の終わりまで(倒れた状態は次の自分の番で起き上がる)。
///   repeat_save なら毎ターンの終わりにセーヴし直して解ける。escape_dc はつかみ・拘束から抜ける難易度(筋力のセーヴで扱う)</item>
/// <item>save: ability のセーヴを dc で振らせ、失敗なら fail を、成功なら success を起こす。half ならダメージは成功で半分。
///   dc が 0 なら唱えた人の呪文の難易度</item>
/// <item>modifier: stat(ac / attack / save / damage)に value か dice(負もよい。"-1d4")を rounds のあいだ足す</item>
/// <item>temp_hp: dice だけ一時的な HP を得る(多い方を残す)</item>
/// <item>hp_pool: dice を振った合計の HP ぶん、HP の低い敵から condition にする(スリープなど)</item>
/// <item>remove_condition: condition を解く / stabilize: 倒れている人の容体を落ち着かせる</item>
/// <item>kill: HP が max_hp 以下なら倒す / condition に max_hp があれば、HP がそれ以下のときだけかかる</item>
/// <item>wish: ウィッシュ(決まった大きな効果から選ぶ)</item>
/// <item>revive: 死んだ人を HP 1 で生き返らせる(allow_ashes なら灰からも)</item>
/// <item>identify: 持ち物を1つ鑑定する / remove_curse: 呪われた装備を外せるようにする(キャンプと町で使う)</item>
/// <item>装備の効果(passive)だけで使うもの: resist / vulnerable(damage_type に抵抗・脆弱)、immune_condition(condition にならない)、
///   set_ability(ability の値を value にする。元の値の方が高ければそのまま)</item>
/// </list>
/// only を書いたダメージは、その種類の相手(undead など)にだけ足す。
/// dice の MOD は唱えた人の呪文の能力値の修正値。by_level があれば、上のスロット(初級呪文はキャラクターのレベル)で dice を選ぶ。
/// </summary>
public sealed class EffectPart
{
    public string Type { get; set; } = "";

    // damage / heal
    public string? Dice { get; set; }
    public string? DamageType { get; set; }

    // condition
    public Condition? Condition { get; set; }
    public int? Rounds { get; set; }
    public bool RepeatSave { get; set; }
    public int? EscapeDc { get; set; }

    // modifier
    public string? Stat { get; set; }
    public int? Value { get; set; }

    /// <summary>レベルごとの dice(キーはスロットのレベルか、キャラクターのレベル。その値以下で一番大きいキーを使う)。</summary>
    public Dictionary<string, string> ByLevel { get; set; } = [];

    /// <summary>kill と condition で、これより HP が多い相手には効かない。</summary>
    public int? MaxHp { get; set; }

    /// <summary>この種類の相手にだけ効く(当たったときの追加のダメージなど)。</summary>
    public string? Only { get; set; }

    /// <summary>revive で、灰からも生き返らせる。</summary>
    public bool AllowAshes { get; set; }

    // save
    public string? Ability { get; set; }
    public int Dc { get; set; }
    public bool Half { get; set; }
    public List<EffectPart> Fail { get; set; } = [];
    public List<EffectPart> Success { get; set; } = [];

    public const string Damage = "damage";
    public const string Heal = "heal";
    public const string ConditionType = "condition";
    public const string Save = "save";
    public const string Modifier = "modifier";
    public const string TempHp = "temp_hp";
    public const string HpPool = "hp_pool";
    public const string RemoveCondition = "remove_condition";
    public const string Stabilize = "stabilize";
    public const string Kill = "kill";
    public const string Wish = "wish";
    public const string Revive = "revive";
    public const string Identify = "identify";
    public const string RemoveCurse = "remove_curse";
    public const string Resist = "resist";
    public const string Vulnerable = "vulnerable";
    public const string ImmuneCondition = "immune_condition";
    public const string SetAbility = "set_ability";

    // 迷宮の呪文(キャンプで唱える。value は続く分)。knock は錠の前で使う
    public const string Light = "light";
    public const string FeatherFall = "feather_fall";
    public const string Recall = "recall";
    public const string Knock = "knock";

    public static readonly string[] KnownTypes =
        [Damage, Heal, ConditionType, Save, Modifier, TempHp, HpPool, RemoveCondition, Stabilize, Kill, Wish, Revive, Identify, RemoveCurse,
         Resist, Vulnerable, ImmuneCondition, SetAbility, Light, FeatherFall, Recall, Knock];

    /// <summary>level での dice(by_level が無ければ dice)。</summary>
    public string? DiceAt(int level)
    {
        if (ByLevel.Count == 0) return Dice;
        string? best = null;
        int bestKey = int.MinValue;
        foreach (var (k, v) in ByLevel)
        {
            if (int.TryParse(k, out int key) && key <= level && key > bestKey)
            {
                bestKey = key;
                best = v;
            }
        }
        return best ?? Dice;
    }

    /// <summary>部品と、その中の部品をすべて。</summary>
    public IEnumerable<EffectPart> Flatten()
    {
        yield return this;
        foreach (var p in Fail.Concat(Success).SelectMany(x => x.Flatten())) yield return p;
    }
}

/// <summary>ダメージの種類。</summary>
public static class DamageTypes
{
    public static readonly string[] All =
        ["acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder"];

    public static bool IsPhysical(string type) => type is "bludgeoning" or "piercing" or "slashing";

    /// <summary>魔法でない武器の物理ダメージへの抵抗などを表す印(SRD の「魔法でない武器の殴打・刺突・斬撃」)。</summary>
    public const string NonmagicalPhysical = "nonmagical_physical";
}
