using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;

namespace GravewardSpiral.Core.Magic;

/// <summary>呪文の対象。Group は敵1グループ、TwoGroups は近い方から2グループ(線の範囲)、Allies は味方の何人か(count。無ければ全員)。</summary>
public enum SpellTarget { Self, Ally, Allies, Enemy, Group, TwoGroups, AllEnemies }

/// <summary>ok は使える、roleplay は会話や演技が中心で外す、unsupported はまだ書けない、later はあとの段階で入れる。</summary>
public enum SpellStatus { Ok, Roleplay, Unsupported, Later }

/// <summary>呪文1つのデータ(パックの spells.json。SRD は tools/srd_import.py で作る)。</summary>
public sealed class SpellDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>0 は初級呪文(キャントリップ)。</summary>
    public int Level { get; set; }

    public string School { get; set; } = "";

    /// <summary>アイコンの種類(fire / heal / debuff など。art/spells/&lt;icon&gt;.png)。同じ種類の呪文は同じアイコン。</summary>
    public string? Icon { get; set; }
    public List<string> Classes { get; set; } = [];
    public bool Ritual { get; set; }
    public bool Concentration { get; set; }

    /// <summary>詠唱時間: action / bonus / reaction / long(1分以上。戦闘では唱えない)。</summary>
    public string Time { get; set; } = "action";

    /// <summary>続くラウンド数(0 は一瞬)。修正値や状態の rounds を書かないときに使う。</summary>
    public int Rounds { get; set; }

    /// <summary>強化のしかた: slot(上のスロットで唱える)/ character(キャラクターのレベル。初級呪文)。</summary>
    public string? Scale { get; set; }

    /// <summary>
    /// 光線の本数(エルドリッチ・ブラスト、スコーチング・レイ)。レベル(scale が character ならキャラクターの合計のレベル、slot なら唱えたスロット)
    /// ごとの本数で、そのレベル以下の一番大きなキーを使う。光線ごとに攻撃ロールとダメージを振る。唱え方は集中(1体に。倒れたら同じグループの別の1体へ)と
    /// 分散(グループのばらばらの相手に1本ずつ)。
    /// </summary>
    public Dictionary<string, int>? Beams { get; set; }

    /// <summary>そのレベルで撃つ光線の本数(光線の呪文でなければ 1)。</summary>
    public int BeamsAt(int level) => Beams == null ? 1
        : Beams.Select(kv => (Level: int.Parse(kv.Key), kv.Value)).Where(x => x.Level <= level).OrderBy(x => x.Level).Select(x => x.Value).DefaultIfEmpty(1).Last();

    /// <summary>呪文の攻撃ロール(melee / ranged)。melee は前列から近くのグループにだけ届く。</summary>
    public string? Attack { get; set; }

    public SpellTarget? Target { get; set; }

    /// <summary>
    /// 地帯の呪文(ムーンビームなど): 集中しているあいだ、唱えた人の番が来るたびに、同じ相手(グループか1体)に効果をもう一度かける。
    /// </summary>
    public bool Zone { get; set; }

    /// <summary>召喚の呪文: 集中しているあいだ、呼んだものが唱えた人の番ごとに敵を攻撃する。</summary>
    public SummonDef? Summon { get; set; }

    /// <summary>Allies の人数の上限(ブレスは 3)と、Group で当たる数の上限(チェイン・ライトニングは 4)。</summary>
    public int? Count { get; set; }

    /// <summary>効く相手の種類(ホールド・パーソンは humanoid)。</summary>
    public string? Only { get; set; }

    public List<EffectPart> Effects { get; set; } = [];

    /// <summary>唱えられる場面(combat / camp)。</summary>
    public List<string> Use { get; set; } = ["combat"];

    public SpellStatus Status { get; set; } = SpellStatus.Unsupported;
    public string? Note { get; set; }

    public bool Usable => Status == SpellStatus.Ok && Target.HasValue;
    public bool IsCantrip => Level == 0;
    public bool InCombat => Usable && Use.Contains("combat") && Time is "action" or "bonus";
    public bool InCamp => Usable && Use.Contains("camp");
    public bool TargetsEnemies => Target is SpellTarget.Enemy or SpellTarget.Group or SpellTarget.TwoGroups or SpellTarget.AllEnemies;
    public bool TargetsAllies => Target is SpellTarget.Self or SpellTarget.Ally or SpellTarget.Allies;
}

/// <summary>召喚するもの(monsters.json の id)と数。上のスロットで唱えると、1 レベルごとに per_level 体増える。</summary>
public sealed class SummonDef
{
    public string Monster { get; set; } = "";
    public int Count { get; set; } = 1;
    public int PerLevel { get; set; }

    public int CountAt(int spellLevel, int slot) => Math.Max(1, Count + PerLevel * Math.Max(0, slot - spellLevel));
}
