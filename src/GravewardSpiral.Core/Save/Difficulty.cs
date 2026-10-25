namespace GravewardSpiral.Core.Save;

/// <summary>
/// 難易度(新規ゲームで選び、セーブに入る)。Wizardry 風(厳しい)と 5版風(やさしい)と、項目ごとに選ぶカスタム。
/// </summary>
public sealed class Difficulty
{
    public const string Wizardry = "wizardry", Fifth = "fifth", Custom = "custom";
    public static readonly string[] Presets = [Wizardry, Fifth, Custom];

    public string Preset { get; set; } = Wizardry;

    /// <summary>全滅したら死体が迷宮に残る(別のパーティで回収する)。false なら町へ戻され、金を半分失う。</summary>
    public bool BodiesRemain { get; set; } = true;

    /// <summary>蘇生に失敗することがある(死亡 → 灰 → 消失)。false なら寺院の蘇生は必ず成功する(代金は要る)。</summary>
    public bool RiskyRevive { get; set; } = true;

    /// <summary>迷宮でもこまめに自動で保存し、やり直しをさせない(戦闘の途中でやめると、その戦闘の始めから)。false なら保存は町と中断のときだけ。</summary>
    public bool StrictSave { get; set; } = true;

    public static Difficulty Of(string preset) => preset switch
    {
        Fifth => new Difficulty { Preset = Fifth, BodiesRemain = false, RiskyRevive = false, StrictSave = false },
        Custom => new Difficulty { Preset = Custom },
        _ => new Difficulty(),
    };
}

/// <summary>
/// 始まっていて終わっていない、迷宮を歩いていて出会った敵との戦闘(厳しいセーブで、戦闘の途中でやめたとき、続きからその戦闘を始め直す)。
/// ボスやイベントの戦闘は、続きから始めると同じマスの印でもう一度起こるので、ここには入れない。
/// </summary>
public sealed class PendingCombat
{
    public List<PendingGroup> Groups { get; set; } = [];
    public double HpMult { get; set; } = 1;
    public double DamageMult { get; set; } = 1;
    public int AcBonus { get; set; }
    public int HitBonus { get; set; }

    public sealed class PendingGroup
    {
        public string Monster { get; set; } = "";
        public int Count { get; set; }
    }
}
