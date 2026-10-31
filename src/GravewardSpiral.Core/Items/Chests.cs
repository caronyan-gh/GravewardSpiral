using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>
/// 宝箱1つ。調べる(知力の判定。失敗すると違う罠の名前が分かったつもりになる)、罠を外す(罠の名前を選ぶ。
/// 違う名前を選ぶと罠が動く。合っていれば敏捷力の判定。盗賊道具の習熟と道具があれば足す)、そのまま開ける(罠があればほぼ動く)。
/// 罠は罠の束 chest(trapsets.json)から引き、中身は traps.json。
/// </summary>
public sealed class Chest
{
    /// <summary>罠の id(traps.json)。空なら罠は無い。</summary>
    public string Trap { get; set; } = "";

    public bool HasTrap => Trap != "";
    public long Gold { get; set; }
    public List<ItemInstance> Items { get; set; } = [];

    /// <summary>罠の強さの段階(目安のレベル / 4)。</summary>
    public int Tier { get; set; }

    public bool Opened { get; set; }

    /// <summary>調べて分かったつもりになった罠の名前(調べた順。空は「罠は無い」)。外す罠を選ぶ一覧の並びに使う。</summary>
    public List<string> Readings { get; set; } = [];

    public int Dc => DcFor(Tier);

    /// <summary>罠の強さの段階(目安のレベル / 4)。イベントで罠を使うときも、この階の宝箱と同じにする。</summary>
    public static int TierFor(double targetLevel) => (int)(targetLevel / 4);

    /// <summary>罠の難易度(調べる・外す・罠のセーヴ)。</summary>
    public static int DcFor(int tier) => 12 + tier;

    /// <summary>宝箱を作る。drops は敵の落とし物(中身に足す)。</summary>
    public static Chest Generate(GameData data, TreasureSettings s, double depth, double targetLevel, IRandom rng, IEnumerable<ItemInstance>? drops = null)
    {
        double trapChance = s.TrapChanceFirst + (s.TrapChanceLast - s.TrapChanceFirst) * Math.Clamp(depth, 0, 1);
        var trap = rng.NextDouble() < trapChance ? data.TrapSets.GetValueOrDefault(TrapSetDef.Chest)?.Pick(rng) ?? "" : "";
        int count = rng.Next(s.MinItems, s.MaxItems + 1);
        var materials = s.Materials.Where(m => depth >= m.MinDepth - 1e-9 && data.Items.ContainsKey(m.Item) && rng.NextDouble() < m.Chance)
            .Select(m => ItemInstance.Of(m.Item)).ToList();
        return new Chest
        {
            Trap = trap,
            Tier = TierFor(targetLevel),
            Gold = (long)(Math.Max(1, targetLevel) * s.GoldPerLevel * (0.5 + rng.NextDouble())),
            Items = [.. Treasure.RollMany(data, s, depth, count, rng, targetLevel), .. materials, .. drops ?? []],
        };
    }

    /// <summary>調べる判定に足す値(知力の修正 + 調査の習熟)。担当を選ぶ画面にも出す。</summary>
    public static int InspectBonus(GameData data, Character c) => c.Stats(data).Mod(Ability.Int) + (c.IsProficient("investigation") ? c.ProficiencyBonus : 0);

    /// <summary>罠を外す判定に足す値(敏捷力の修正 + 盗賊道具の習熟と道具 + 装備の disarm)と、道具を持っているか。</summary>
    public static (int Bonus, bool Tools) DisarmBonus(GameData data, Character c)
    {
        bool tools = c.Inventory.Items.Any(i => i.ItemId == "thieves_tools");
        bool proficient = tools && c.ClassIds.Any(id => data.Classes[id].Tools.Contains("thieves_tools"));
        return (c.Stats(data).Mod(Ability.Dex) + (proficient ? c.ProficiencyBonus : 0) + c.PassiveBonus(data, "disarm"), tools);
    }

    /// <summary>調べる。成功すれば本当の罠、失敗すれば違う罠(か「罠は無い」)が分かったつもりになる。1 が出ると罠が動く。</summary>
    public (string Seen, bool Triggered) Inspect(GameData data, Character c, IRandom rng)
    {
        int nat = rng.Roll(20);
        if (nat == 1 && HasTrap) return (Trap, true);
        int total = nat + InspectBonus(data, c);
        string seen = Trap;
        if (total < Dc)
        {
            var wrong = Names(data).Where(t => t != Trap).ToList();
            seen = wrong[rng.Next(0, wrong.Count)];
        }
        Readings.Add(seen);
        return (seen, false);
    }

    /// <summary>外す罠を選ぶ一覧の並び。調べて出た名前を、出た回数の多い順(同じなら先に出た順)に上へ置き、残りは元の並び。</summary>
    public IReadOnlyList<string> GuessOrder(GameData data)
    {
        var names = Names(data);
        var seen = Readings.Where(names.Contains).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        return [.. seen, .. names.Where(t => !seen.Contains(t))];
    }

    /// <summary>宝箱の罠として名前を選べるもの(「罠は無い」の空と、束 chest の罠の並び)。調べ違いと、外す罠を選ぶ一覧に使う。</summary>
    public static IReadOnlyList<string> Names(GameData data) =>
        [.. (string[])[""], .. data.TrapSets.GetValueOrDefault(TrapSetDef.Chest)?.Traps.Select(t => t.Id).Distinct() ?? []];

    public enum DisarmResult { Disarmed, NoTrap, Failed, Triggered }

    /// <summary>罠を外す。guess が違えば罠が動く。合っていれば敏捷力の判定(盗賊道具の習熟と道具があれば足す。5 以上外すと動く)。</summary>
    public DisarmResult Disarm(GameData data, Character c, string guess, IRandom rng)
    {
        if (!HasTrap) return guess == "" ? DisarmResult.NoTrap : DisarmResult.Failed;
        if (guess != Trap) return DisarmResult.Triggered;
        int total = rng.Roll(20) + DisarmBonus(data, c).Bonus;
        if (total >= Dc)
        {
            Trap = "";
            return DisarmResult.Disarmed;
        }
        return total <= Dc - 5 ? DisarmResult.Triggered : DisarmResult.Failed;
    }

    /// <summary>そのまま開ける。罠があれば 9 割で動く。動いたら true。</summary>
    public bool OpenTriggers(IRandom rng) => HasTrap && rng.NextDouble() < 0.9;

    /// <summary>
    /// 罠を動かす。opener は開けようとした人。死ぬか耐えるかは、罠の lethal、なければ束 chest の lethal(今は HP 0 で死ぬ。戦闘の外なので死亡セーヴは無い)。
    /// 最初の文は罠の text(文言 chest.sprung に入れる)。
    /// </summary>
    public TrapResult Trigger(GameData data, IList<Character> party, Character opener, IRandom rng)
    {
        var lines = new List<(string Key, object[] Args)>();
        if (!data.Traps.TryGetValue(Trap, out var trap))
        {
            Trap = "";
            return new TrapResult(lines, false, false);
        }
        if (trap.Text != null) lines.Add(("chest.sprung", [trap.Text]));
        bool lethal = trap.Lethal ?? data.TrapSets.GetValueOrDefault(TrapSetDef.Chest)?.Lethal ?? false;
        var r = Traps.Spring(data, trap, Tier, Dc, party, opener, lethal, rng, "chest", lines);
        Trap = "";
        return r;
    }

    /// <summary>中身を受け取る。金はパーティで均等に分け、アイテムは入る人の袋に入れる。入らなかったものを返す。</summary>
    public List<ItemInstance> Loot(GameData data, IList<Character> party)
    {
        Opened = true;
        var alive = party.Where(c => c.IsAlive).ToList();
        if (alive.Count > 0)
        {
            long each = Gold / alive.Count;
            foreach (var c in alive) c.Gold += each;
            alive[0].Gold += Gold - each * alive.Count;
        }
        var left = new List<ItemInstance>();
        foreach (var item in Items)
        {
            if (!alive.Any(c => c.Inventory.TryAdd(item, data.Items))) left.Add(item);
        }
        return left;
    }
}
