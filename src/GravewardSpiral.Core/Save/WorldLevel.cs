using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Items;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// 世界のレベル帯(新しいゲームの迷宮の目安のレベルの、最初の階と最下層)。
/// ふつうはシナリオ(encounters.json)の帯を使う。強いクランを連れて始めたら、クランの強さに合わせて帯をずらす
/// (1階はクランより少し下から。敵の顔ぶれ・倍率、宝の +N と追加効果、散らすイベントが、ずらした帯で決まる)。
/// 周回はこれで回る: 強くなったクランで新しい世界を始めると、世界も強くなる。
/// </summary>
public static class WorldLevel
{
    /// <summary>1階の目安を、クランの強さからいくつ下げるか。</summary>
    public const double StartBelow = 1;

    /// <summary>
    /// クランの強さ(レベルに直した値)。パーティ(いなければ名簿のレベルの高い6人)の平均レベルに、
    /// 武器と鎧の強化値の平均の半分を足す。
    /// </summary>
    public static double ClanPower(GameData data, IEnumerable<Character> members)
    {
        var list = members.ToList();
        if (list.Count == 0) return 0;
        double gear = list.Average(c => new[] { EquipSlot.MainHand, EquipSlot.Ranged, EquipSlot.Armor }
            .Select(slot => c.Inventory[slot] is { } it && data.Items.TryGetValue(it.ItemId, out var d) ? Math.Max(0, it.TotalBonus(d)) : 0)
            .DefaultIfEmpty(0).Max());
        return list.Average(c => c.Level) + gear / 2;
    }

    /// <summary>クランの強さを見る人(パーティ。いなければ名簿のレベルの高い6人)。</summary>
    public static IEnumerable<Character> Strongest(Roster roster) =>
        roster.PartyMembers.Any() ? roster.PartyMembers : roster.Characters.OrderByDescending(c => c.Level).Take(Roster.MaxParty);

    /// <summary>
    /// 世界のレベル帯。クランの強さ − StartBelow が、元の帯の最初の階より上なら、その分だけ帯全体をずらす(幅は変えない)。
    /// クランが無いか弱ければ、元の帯のまま。
    /// </summary>
    public static (double First, double Last) Band(double first, double last, double? power)
    {
        double shift = power is double p ? Math.Max(0, p - StartBelow - first) : 0;
        shift = Math.Floor(shift);
        return (first + shift, last + shift);
    }
}
