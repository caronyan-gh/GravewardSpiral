using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Monsters;

/// <summary>
/// 名のある敵(パックの named_monsters.json)。ふつうの敵を元にした強めの1体で、倒すと item_chance の確率で決まったユニーク品を落とす。
/// 迷宮にいるかどうか自体が、新しいゲームのたびの抽選(1つの世界に 0〜2 体)。
/// 置き方は、ねぐら(決まったマスにいて、倒すまで来るたびに戦う)か、さまよう(その階の遭遇にまれに混ざる)。
/// </summary>
public sealed class NamedMonsterDef
{
    public const string Collection = "named_monsters";

    /// <summary>さまよう名のある敵が、その階の遭遇に混ざる確率。</summary>
    public const double WanderChance = 0.06;

    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>元にする敵(monsters の id)。</summary>
    public string Monster { get; set; } = "";

    /// <summary>落とす品(items の id。ふつうは no_random のユニーク品)と、落とす確率。</summary>
    public string Item { get; set; } = "";
    public double ItemChance { get; set; } = 0.5;

    /// <summary>出てよい深さ(割合)。</summary>
    public double MinDepth { get; set; }
    public double MaxDepth { get; set; } = 1;

    /// <summary>選ばれやすさ。</summary>
    public double Weight { get; set; } = 1;

    /// <summary>置き方: lair(ねぐら)/ wander(さまよう)/ either(どちらかを抽選)。</summary>
    public string Placement { get; set; } = "either";

    /// <summary>強化(HP とダメージの倍率、AC と命中の上乗せ)。</summary>
    public double HpMult { get; set; } = 2;
    public double DamageMult { get; set; } = 1.25;
    public int AcBonus { get; set; } = 1;
    public int HitBonus { get; set; } = 1;
}

/// <summary>この世界にいる名のある敵(どの階の、どこに)。セーブに入る。</summary>
public sealed class NamedSpawn
{
    public string Id { get; set; } = "";

    /// <summary>いる階。0 から数える(0 が B1、1 が B2)。</summary>
    public int Floor { get; set; }

    /// <summary>ねぐらにいる(false ならさまよう)。</summary>
    public bool Lair { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>酒場の噂に乗っている。</summary>
    public bool Rumored { get; set; }

    public bool Defeated { get; set; }
}
