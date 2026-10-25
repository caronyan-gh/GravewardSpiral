using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Items;

/// <summary>宝箱の罠(Wizardry 風の9種)。</summary>
public enum ChestTrap { None, PoisonNeedle, GasBomb, CrossbowBolt, ExplodingBox, Stunner, Teleporter, MageBlaster, PriestBlaster, Alarm }

/// <summary>罠が動いたときの結果。テレポーターと警報は、迷宮の画面が処理する。</summary>
public sealed record TrapResult(List<(string Key, object[] Args)> Lines, bool Teleport, bool Alarm);

/// <summary>
/// 宝箱1つ。調べる(知力の判定。失敗すると違う罠の名前が分かったつもりになる)、罠を外す(罠の名前を選ぶ。
/// 違う名前を選ぶと罠が動く。合っていれば敏捷力の判定。盗賊道具の習熟と道具があれば足す)、そのまま開ける(罠があればほぼ動く)。
/// </summary>
public sealed class Chest
{
    public ChestTrap Trap { get; set; }
    public long Gold { get; set; }
    public List<ItemInstance> Items { get; set; } = [];

    /// <summary>罠の強さの段階(目安のレベル / 4)。</summary>
    public int Tier { get; set; }

    public bool Opened { get; set; }

    public int Dc => 12 + Tier;

    /// <summary>宝箱を作る。drops は敵の落とし物(中身に足す)。</summary>
    public static Chest Generate(GameData data, TreasureSettings s, double depth, double targetLevel, IRandom rng, IEnumerable<ItemInstance>? drops = null)
    {
        double trapChance = s.TrapChanceFirst + (s.TrapChanceLast - s.TrapChanceFirst) * Math.Clamp(depth, 0, 1);
        var trap = rng.NextDouble() < trapChance ? (ChestTrap)rng.Next(1, 10) : ChestTrap.None;
        int count = rng.Next(s.MinItems, s.MaxItems + 1);
        var materials = s.Materials.Where(m => depth >= m.MinDepth - 1e-9 && data.Items.ContainsKey(m.Item) && rng.NextDouble() < m.Chance)
            .Select(m => ItemInstance.Of(m.Item)).ToList();
        return new Chest
        {
            Trap = trap,
            Tier = (int)(targetLevel / 4),
            Gold = (long)(Math.Max(1, targetLevel) * s.GoldPerLevel * (0.5 + rng.NextDouble())),
            Items = [.. Treasure.RollMany(data, s, depth, count, rng, targetLevel), .. materials, .. drops ?? []],
        };
    }

    /// <summary>調べる。成功すれば本当の罠、失敗すれば違う罠(か「罠は無い」)が分かったつもりになる。1 が出ると罠が動く。</summary>
    public (ChestTrap Seen, bool Triggered) Inspect(GameData data, Character c, IRandom rng)
    {
        int nat = rng.Roll(20);
        if (nat == 1 && Trap != ChestTrap.None) return (Trap, true);
        int total = nat + c.Stats(data).Mod(Ability.Int) + (c.IsProficient("investigation") ? c.ProficiencyBonus : 0);
        if (total >= Dc) return (Trap, false);
        var wrong = Enum.GetValues<ChestTrap>().Where(t => t != Trap).ToList();
        return (wrong[rng.Next(0, wrong.Count)], false);
    }

    public enum DisarmResult { Disarmed, NoTrap, Failed, Triggered }

    /// <summary>罠を外す。guess が違えば罠が動く。合っていれば敏捷力の判定(盗賊道具の習熟と道具があれば足す。5 以上外すと動く)。</summary>
    public DisarmResult Disarm(GameData data, Character c, ChestTrap guess, IRandom rng)
    {
        if (Trap == ChestTrap.None) return guess == ChestTrap.None ? DisarmResult.NoTrap : DisarmResult.Failed;
        if (guess != Trap) return DisarmResult.Triggered;
        bool tools = c.Inventory.Items.Any(i => i.ItemId == "thieves_tools");
        bool proficient = tools && c.ClassIds.Any(id => data.Classes[id].Tools.Contains("thieves_tools"));
        int total = rng.Roll(20) + c.Stats(data).Mod(Ability.Dex) + (proficient ? c.ProficiencyBonus : 0) + c.PassiveBonus(data, "disarm");
        if (total >= Dc)
        {
            Trap = ChestTrap.None;
            return DisarmResult.Disarmed;
        }
        return total <= Dc - 5 ? DisarmResult.Triggered : DisarmResult.Failed;
    }

    /// <summary>そのまま開ける。罠があれば 9 割で動く。動いたら true。</summary>
    public bool OpenTriggers(IRandom rng) => Trap != ChestTrap.None && rng.NextDouble() < 0.9;

    private static readonly string[] ArcaneClasses = ["wizard", "sorcerer", "warlock", "bard"];
    private static readonly string[] DivineClasses = ["cleric", "druid", "paladin", "ranger"];

    /// <summary>罠を動かす。opener は開けようとした人。HP が 0 になった人は死ぬ(戦闘の外なので死亡セーヴは無い)。</summary>
    public TrapResult Trigger(GameData data, IList<Character> party, Character opener, IRandom rng)
    {
        var lines = new List<(string, object[])> { ("chest.trap." + Trap.ToString().ToLowerInvariant(), []) };
        int Roll(string dice) => DiceExpression.Parse(dice).Roll(rng).Total;
        void Hurt(Character c, int dmg, string type)
        {
            if (!c.IsAlive || dmg <= 0) return;
            if (c.Passive(data).Any(p => p.Type == EffectPart.Resist && p.DamageType == type)) dmg /= 2;
            c.Hp -= dmg;
            lines.Add(("chest.hurt", [c.Name, dmg]));
            if (c.Hp <= 0)
            {
                c.Hp = 0;
                c.State = LifeState.Dead;
                lines.Add(("chest.died", [c.Name]));
            }
        }
        bool Save(Character c, Ability a) => rng.Roll(20) + c.SaveBonus(data, a) >= Dc;
        void Lasting(Character c, Condition cond, int? minutes)
        {
            if (!c.IsAlive || c.Passive(data).Any(p => p.Type == EffectPart.ImmuneCondition && p.Condition == cond)) return;
            c.Lasting.RemoveAll(l => l.Condition == cond);
            c.Lasting.Add(new LastingCondition { Condition = cond, Minutes = minutes });
            lines.Add(("chest.condition", [c.Name, cond]));
        }
        int t = Tier;
        bool teleport = false, alarm = false;
        switch (Trap)
        {
            case ChestTrap.PoisonNeedle:
                Hurt(opener, Roll($"{1 + t}d4"), "piercing");
                if (!Save(opener, Ability.Con)) Lasting(opener, Condition.Poisoned, 60);
                break;
            case ChestTrap.GasBomb:
                foreach (var c in party.Where(c => c.IsAlive && !Save(c, Ability.Con))) Lasting(c, Condition.Poisoned, 30);
                break;
            case ChestTrap.CrossbowBolt:
                Hurt(opener, Roll($"{2 + t}d10"), "piercing");
                break;
            case ChestTrap.ExplodingBox:
                foreach (var c in party.Where(c => c.IsAlive))
                {
                    int dmg = Roll($"{2 + t * 2}d6");
                    Hurt(c, Save(c, Ability.Dex) ? dmg / 2 : dmg, "fire");
                }
                break;
            case ChestTrap.Stunner:
                if (!Save(opener, Ability.Con)) Lasting(opener, Condition.Paralyzed, null);
                break;
            case ChestTrap.Teleporter:
                teleport = true;
                break;
            case ChestTrap.MageBlaster:
                foreach (var c in party.Where(c => c.ClassIds.Any(ArcaneClasses.Contains))) Hurt(c, Roll($"{3 + t}d8"), "lightning");
                break;
            case ChestTrap.PriestBlaster:
                foreach (var c in party.Where(c => c.ClassIds.Any(DivineClasses.Contains))) Hurt(c, Roll($"{3 + t}d8"), "lightning");
                break;
            case ChestTrap.Alarm:
                alarm = true;
                break;
        }
        Trap = ChestTrap.None;
        return new TrapResult(lines, teleport, alarm);
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
