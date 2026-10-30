using System.Text.RegularExpressions;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>
/// 罠1つ(traps.json)。name は罠の名前(宝箱を調べたときや、外す罠を選ぶ一覧に出す)、text は動いたときの文、steps はイベントと同じ部品の手順
/// (damage・condition・teleport・alarm。当たる相手は who、セーヴは save、ダイスは per_tier で罠の等級ごとに増える)。
/// lethal は HP 0 で死ぬか(書けば束の lethal より優先する)。
/// </summary>
public sealed class TrapDef
{
    public const string Collection = "traps";

    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public LocalizedText? Text { get; set; }
    public bool? Lethal { get; set; }
    public List<EventStep> Steps { get; set; } = [];

    /// <summary>罠の手順に書ける部品。</summary>
    public static readonly HashSet<string> StepTypes = ["damage", "condition", "teleport", "alarm"];
}

/// <summary>罠の束(trapsets.json)。lethal は HP 0 で死ぬか、traps はその束から引く罠と重み。宝箱は束 chest から引く。</summary>
public sealed class TrapSetDef
{
    public const string Collection = "trapsets";

    /// <summary>宝箱の罠の束の id。</summary>
    public const string Chest = "chest";

    public string Id { get; set; } = "";
    public bool Lethal { get; set; }
    public List<TrapPick> Traps { get; set; } = [];

    /// <summary>重みで1つ選ぶ(重みの合計で乱数を1つ使う)。空なら null。</summary>
    public string? Pick(IRandom rng)
    {
        int total = Traps.Sum(t => Math.Max(0, t.Weight));
        if (total <= 0) return null;
        int r = rng.Next(0, total);
        foreach (var t in Traps)
        {
            r -= Math.Max(0, t.Weight);
            if (r < 0) return t.Id;
        }
        return null;
    }
}

public sealed class TrapPick
{
    public string Id { get; set; } = "";
    public int Weight { get; set; } = 1;
}

/// <summary>罠が動いたときの結果。テレポーターと警報は、迷宮の画面が処理する。</summary>
public sealed record TrapResult(List<(string Key, object[] Args)> Lines, bool Teleport, bool Alarm);

/// <summary>罠を動かす(宝箱とイベントで共通)。守りは戦闘の外の共通のもの(Hazard)。</summary>
public static class Traps
{
    /// <summary>
    /// 罠の手順を行う。tier は罠の等級(per_tier のダイスに掛ける)、dc は save に dc が書いていないときの難易度、trigger は動かした人。
    /// lethal なら HP 0 で死ぬ。文は lines に足す(prefix の文言で)。
    /// </summary>
    public static TrapResult Spring(GameData data, TrapDef trap, int tier, int dc, IList<Character> party, Character trigger, bool lethal, IRandom rng,
        string prefix, List<(string Key, object[] Args)> lines)
    {
        var hazard = new Hazard(data, party, rng, prefix, (key, args) => lines.Add((key, args)));
        bool teleport = false, alarm = false;
        foreach (var s in trap.Steps)
        {
            switch (s.Type)
            {
                case "damage":
                    foreach (var c in Targets(data, s.Who, party, trigger))
                    {
                        int dmg = DiceExpression.Parse(TierDice(s.Dice ?? "1d6", s.PerTier, tier)).Roll(rng).Total;
                        hazard.Damage(c, dmg, s.DamageType, s.Save is { } sv ? (Abilities.Parse(sv.Ability), sv.Dc ?? dc, sv.Success) : null, lethal);
                    }
                    break;
                case "condition":
                    if (!EventRunner.TryCondition(s.Id, out var cond)) break;
                    foreach (var c in Targets(data, s.Who, party, trigger))
                        hazard.AddCondition(c, cond, s.Value > 0 ? s.Value : null, s.Save is { } sv ? (Abilities.Parse(sv.Ability), sv.Dc ?? dc) : null);
                    break;
                case "teleport":
                    teleport = true;
                    break;
                case "alarm":
                    alarm = true;
                    break;
            }
        }
        return new TrapResult(lines, teleport, alarm);
    }

    /// <summary>
    /// 当たる相手(パーティの並び順)。trigger(省いたときも)は動かした人、party は生きている人全員、tag:&lt;名前&gt; は取ったクラスのどれかがそのタグを持つ人、
    /// race:&lt;id&gt; はその種族の人。死んでいる人には、守りの側(Hazard)で何も起きない。
    /// </summary>
    public static IEnumerable<Character> Targets(GameData data, string? who, IList<Character> party, Character trigger) => who switch
    {
        null or "trigger" => [trigger],
        "party" => party.Where(c => c.IsAlive),
        _ when who.StartsWith("tag:") => party.Where(c => HasTag(data, c, who[4..])),
        _ when who.StartsWith("race:") => party.Where(c => c.RaceId == who[5..]),
        _ => [],
    };

    /// <summary>取ったクラスのどれかが、そのタグ(classes.json の tags)を持つか。</summary>
    public static bool HasTag(GameData data, Character c, string tag) =>
        c.ClassIds.Any(id => data.Classes.TryGetValue(id, out var cls) && cls.Tags.Contains(tag));

    /// <summary>ダイスの数を、罠の等級 1 ごとに perTier 個増やす(1d4 で perTier 1、等級 2 なら 3d4)。</summary>
    public static string TierDice(string dice, int perTier, int tier)
    {
        if (perTier == 0 || tier <= 0) return dice;
        var m = Regex.Match(dice, @"^(\d+)d(.+)$");
        return m.Success ? $"{int.Parse(m.Groups[1].Value) + perTier * tier}d{m.Groups[2].Value}" : dice;
    }

    /// <summary>who に書ける値か。</summary>
    public static bool ValidWho(string? who) =>
        who is null or "trigger" or "party" || who.StartsWith("tag:") && who.Length > 4 || who.StartsWith("race:") && who.Length > 5;
}
