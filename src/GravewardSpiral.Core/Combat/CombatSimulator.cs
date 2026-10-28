using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Combat;

/// <summary>戦闘を自動で何回も回した結果。</summary>
public sealed record SimulationResult(int Fights, int Wins, int Defeats, int Fled, double AverageRounds, double AverageDeaths, double AverageHpLeft)
{
    public double WinRate => Fights == 0 ? 0 : (double)Wins / Fights;
}

/// <summary>
/// 戦闘のシミュレータ。パーティはおまかせの行動(AutoCommands)で戦う。
/// 調整の道具(tools/GravewardSpiral.Sim)とゲームの中の手直しの両方から使う。
/// </summary>
public static class CombatSimulator
{
    public const int MaxRounds = 100;

    /// <summary>
    /// おまかせの行動。術者は、HP が減った味方がいれば回復の呪文、敵が2体以上のグループには範囲の呪文、
    /// それ以外は攻撃の初級呪文を使う。届く武器があれば先頭のグループを攻撃し、何もできなければ身構える。
    /// </summary>
    public static Dictionary<string, PlayerCommand> AutoCommands(CombatState s, GameData data) => AutoCommands(s, data, useSpells: true);

    /// <summary>
    /// おまかせの行動。useSpells が false なら呪文もクラスの技も使わない(スロットと回数を温存する。届く武器が無い人は身構える)。
    /// ソーサラーの呪文の強化は、おまかせでは使わない(決めたこと)。
    /// </summary>
    public static Dictionary<string, PlayerCommand> AutoCommands(CombatState s, GameData data, bool useSpells)
    {
        var d = new Dictionary<string, PlayerCommand>();
        foreach (var p in s.Party.Where(p => p.CanAct))
        {
            var targets = s.AttackableGroups(p).ToList();
            bool canCast = ClassFeatures.CanCastIn(p.Character, p.Raging, p.Shape != null);
            d[p.Id] = (useSpells ? AutoFeature(s, data, p) : null) ?? (useSpells && canCast ? Spread(s, data, AutoSpell(s, data, p)) : null)
                ?? (targets.Count > 0 ? new PlayerCommand(CommandKind.Attack, targets[0]) : PlayerCommand.Dodge);
        }
        return d;
    }

    /// <summary>光線の呪文は、狙うグループに2体以上いれば分散で、1体なら集中で撃つ。</summary>
    private static PlayerCommand? Spread(CombatState s, GameData data, PlayerCommand? cmd) =>
        cmd is { Kind: CommandKind.Cast, SpellId: { } id, TargetGroup: >= 0 and var g } && data.Spells.TryGetValue(id, out var sp) && sp.Beams != null
            && g < s.Groups.Count && s.Groups[g].AliveCount >= 2 ? cmd with { Option = "spread" } : cmd;

    /// <summary>強い戦い(敵の HP の合計が、立っているパーティの HP の合計の半分を超える)。</summary>
    private static bool Dangerous(CombatState s) =>
        s.LiveGroups.Sum(g => g.Alive.Sum(u => u.Hp)) > s.Party.Where(m => m.Standing).Sum(m => m.Hp) * 0.5;

    /// <summary>
    /// おまかせのクラスの技。弱った仲間の手当て(レイ・オン・ハンズ、セカンド・ウィンド)、アンデッドの退散、
    /// 前列の激怒と変身は、いつでも使う。アクション・サージ、スマイト、気の技、鼓舞は強い戦いのときだけ。
    /// </summary>
    private static PlayerCommand? AutoFeature(CombatState s, GameData data, PartyMember p)
    {
        var c = p.Character;
        var have = ClassFeatures.ActiveFor(c).Where(f => ClassFeatures.CanUse(data, c, f) && (p.Shape == null || ClassFeatures.UsableInBeastForm(f)))
            .Select(f => f.Id).ToHashSet();
        if (have.Count == 0) return null;
        var targets = s.AttackableGroups(p).ToList();
        int target = targets.Count > 0 ? targets[0] : -1;
        bool dangerous = Dangerous(s);
        PlayerCommand Use(string id, int group = -1, int member = -1) => new(CommandKind.Feature, group, member, FeatureId: id);

        if (have.Contains("second_wind") && p.Hp < p.MaxHp / 2) return Use("second_wind");
        if (have.Contains("lay_on_hands"))
        {
            var hurt = s.Party.Select((m, i) => (m, i)).Where(x => x.m.Character.State is LifeState.Alive or LifeState.Unconscious && x.m.Shape == null
                && x.m.Hp < x.m.MaxHp * 0.4).OrderBy(x => x.m.Hp).FirstOrDefault();
            if (hurt.m != null) return Use("lay_on_hands", member: hurt.i);
        }
        if (have.Contains("turn_undead"))
        {
            var undead = s.LiveGroups.SelectMany(g => g.Alive).Where(u => u.Def.CreatureType == "undead" && !u.Incapacitated).ToList();
            if (undead.Count >= 2 || (dangerous && undead.Any(u => u.LegendaryResistLeft == 0))) return Use("turn_undead");
        }
        // 変身(熊の型)は、呪文スロットを使い切った前列のドルイドだけ(スロットがあるうちは呪文のほうが強い)
        if (have.Contains("wild_shape") && p.Shape == null && s.InFront(p) && s.LiveGroups.Any(g => s.IsNear(g))
            && !Spellcasting.CasterClasses(data, c).Any(cls => Enumerable.Range(1, 9).Any(lv => Spellcasting.SlotsLeft(cls, c, lv) > 0)))
            return Use("wild_shape");
        // 竜の息: 近くに2体以上いるグループに
        if (have.Contains("breath_weapon") && s.Groups.Select((g, i) => (g, i)).FirstOrDefault(x => !x.g.Wiped && s.IsNear(x.g) && x.g.AliveCount >= 2) is { g: not null } br)
            return Use("breath_weapon", br.i);
        if (target < 0) return null;
        if (have.Contains("rage") && !p.Raging && s.InFront(p)) return Use("rage", target);
        if (!dangerous) return null;
        if (have.Contains("action_surge")) return Use("action_surge", target);
        if (have.Contains("divine_smite") && s.InFront(p)) return Use("divine_smite", target);
        if (have.Contains("stunning_strike") && s.InFront(p) && s.Groups[target].Alive.Any(u => !u.Incapacitated && !u.Immune(Condition.Stunned)))
            return Use("stunning_strike", target);
        if (have.Contains("flurry_of_blows") && s.InFront(p)) return Use("flurry_of_blows", target);
        if (have.Contains("bardic_inspiration"))
        {
            var best = s.Party.Select((m, i) => (m, i)).Where(x => x.m != p && x.m.Standing && !x.m.Modifiers.Any(m => m.Stat == "inspire"))
                .OrderByDescending(x => CombatEngine.AttacksPerAction(x.m.Character)).FirstOrDefault();
            if (best.m != null) return Use("bardic_inspiration", member: best.i);
        }
        return null;
    }

    private static PlayerCommand? AutoSpell(CombatState s, GameData data, PartyMember p)
    {
        var c = p.Character;
        if (Spellcasting.CasterClass(data, c) is not { } cls) return null;
        var spells = Spellcasting.Castable(data, c).Where(x => x.InCombat).ToList();
        bool Has(SpellDef x, string type) => x.Effects.SelectMany(e => e.Flatten()).Any(e => e.Type == type);
        PlayerCommand? Cast(SpellDef x, int group = -1, int member = -1) =>
            Spellcasting.LowestSlot(cls, c, x) is int slot ? new PlayerCommand(CommandKind.Cast, group, member, x.Id, slot) : null;

        // 回復: HP が 4 割を切った(か倒れた)味方
        var hurt = s.Party.Select((m, i) => (m, i)).Where(x => x.m.Character.State is LifeState.Alive or LifeState.Unconscious
            && x.m.Hp < x.m.MaxHp * 0.4).OrderBy(x => x.m.Hp).FirstOrDefault();
        if (hurt.m != null)
        {
            foreach (var x in spells.Where(x => Has(x, EffectPart.Heal)).OrderBy(x => x.Level))
            {
                if (Cast(x, member: hurt.i) is { } cmd) return cmd;
            }
        }
        var reach = s.Groups.Select((g, i) => (g, i)).Where(x => !x.g.Wiped).ToList();
        if (reach.Count == 0) return null;

        // 上のスロットで強くなる呪文は、空いている一番上のスロットで唱える
        int? SlotFor(SpellDef x)
        {
            if (x.IsCantrip) return 0;
            if (x.Scale != "slot") return Spellcasting.LowestSlot(cls, c, x);
            for (int lv = 9; lv >= x.Level; lv--)
            {
                if (Spellcasting.SlotsLeft(cls, c, lv) > 0) return lv;
            }
            return null;
        }
        PlayerCommand? CastAt(SpellDef x, int group) => SlotFor(x) is int slot ? new PlayerCommand(CommandKind.Cast, group, -1, x.Id, slot) : null;
        var reachable = (SpellDef x) => CombatEngine.SpellReach(s, p, x).ToHashSet();

        // 強い戦い(敵の HP の合計がパーティの HP の合計の半分を超える): 即死・縛る・強める呪文も使う
        int enemyHp = reach.Sum(x => x.g.Alive.Sum(u => u.Hp));
        bool dangerous = enemyHp > s.Party.Where(m => m.Standing).Sum(m => m.Hp) * 0.5;
        var biggest = reach.OrderByDescending(x => x.g.Alive.Max(u => u.Hp)).First();
        int biggestHp = biggest.g.Alive.Max(u => u.Hp);
        var stops = new HashSet<Condition> { Condition.Paralyzed, Condition.Stunned, Condition.Unconscious, Condition.Petrified, Condition.Incapacitated, Condition.Restrained };
        IEnumerable<EffectPart> Parts(SpellDef x) => x.Effects.SelectMany(e => e.Flatten());

        // 即死(HP が上限以下の敵)
        foreach (var x in spells.Where(x => !x.IsCantrip && x.TargetsEnemies).OrderByDescending(x => x.Level))
        {
            if (Parts(x).FirstOrDefault(e => e.Type == EffectPart.Kill && e.MaxHp != null) is { } kill
                && reachable(x).Contains(biggest.i) && biggest.g.Alive.Any(u => u.Hp <= kill.MaxHp) && CastAt(x, biggest.i) is { } cmd) return cmd;
        }
        if (dangerous && p.Concentration == null)
        {
            // 縛る: 一番 HP の多い敵が、まだ縛られていなくて、その状態にもなる
            // 伝説的抵抗の残る相手には、縛る呪文を撃たない(抵抗されて、スロットと番が無駄になる)
            var stuck = biggest.g.Alive.Any(u => u.Conditions.Any(k => stops.Contains(k.Condition)) || u.LegendaryResistLeft > 0);
            foreach (var x in spells.Where(x => !x.IsCantrip && x.TargetsEnemies && (x.Only == null || x.Only == biggest.g.Def.CreatureType)).OrderByDescending(x => x.Level))
            {
                var cond = Parts(x).FirstOrDefault(e => e.Type == EffectPart.ConditionType && e.Condition is { } k && stops.Contains(k)
                    && (e.MaxHp == null || biggestHp <= e.MaxHp));
                if (stuck || cond == null || biggest.g.Alive.All(u => u.Immune(cond.Condition!.Value))) continue;
                if (reachable(x).Contains(biggest.i) && CastAt(x, biggest.i) is { } cmd) return cmd;
            }
            // 強める: 攻撃の回数を増やす呪文(ヘイスト)を、攻撃の多い味方に
            var fighter = s.Party.Select((m, i) => (m, i)).Where(x => x.m.Standing && !x.m.Modifiers.Any(m => m.Stat == "attacks"))
                .OrderByDescending(x => CombatEngine.AttacksPerAction(x.m.Character)).FirstOrDefault();
            if (fighter.m != null && CombatEngine.AttacksPerAction(fighter.m.Character) >= 2)
            {
                foreach (var x in spells.Where(x => x.Target is SpellTarget.Ally && Parts(x).Any(e => e.Type == EffectPart.Modifier && e.Stat == "attacks")))
                {
                    if (Cast(x, member: fighter.i) is { } cmd) return cmd;
                }
            }
        }

        // ダメージ: 当たる数 × 平均のダメージが一番大きいもの(初級呪文の 1.5 倍に届かなければ、スロットは使わない)
        double Value(SpellDef x, int slot, int g)
        {
            int level = x.Scale == "character" ? c.Level : slot;
            double each = 0;
            foreach (var e in x.Effects)
            {
                if (e.Type == EffectPart.Damage && e.DiceAt(level) is { } d) each += Rules.DiceExpression.Parse(d.Replace("MOD", "0")).Average;
                if (e.Type == EffectPart.Save)
                {
                    foreach (var f in e.Fail.Where(f => f.Type == EffectPart.Damage && f.DiceAt(level) != null))
                        each += Rules.DiceExpression.Parse(f.DiceAt(level)!.Replace("MOD", "0")).Average * (e.Half ? 0.75 : 0.5);
                }
            }
            if (x.Attack != null) each *= 0.65;
            if (x.Zone && x.Concentration) each *= 2.5;   // 地帯は集中のあいだ、番ごとにもう一度当たる
            int others = reach.Where(r => r.i != g).Select(r => r.g.AliveCount).OrderDescending().FirstOrDefault();
            int hits = x.Target switch
            {
                SpellTarget.Group => s.Groups[g].AliveCount,
                SpellTarget.TwoGroups => s.Groups[g].AliveCount + others,
                SpellTarget.AllEnemies => reach.Sum(r => r.g.AliveCount),
                _ => 1,
            };
            return each * hits;
        }
        // 続いている呪文(スピリチュアル・ウェポンなど)は、かけ直さない
        var options = spells.Where(x => x.TargetsEnemies && Has(x, EffectPart.Damage) && !s.Lingerings.Any(l => l.Caster == p && l.Spell.Id == x.Id))
            .SelectMany(x => reachable(x).Select(g => (x, g, slot: SlotFor(x))))
            .Where(o => o.slot != null).Select(o => (o.x, o.g, slot: o.slot!.Value, v: Value(o.x, o.slot!.Value, o.g))).ToList();
        // 召喚: 呼んだ数 × 1回の攻撃の平均 × 当たる見込み × 続く見込みのラウンド(集中していないときだけ)
        if (p.Concentration == null && reach.Count > 0)
        {
            foreach (var x in spells.Where(x => x.Summon != null && data.Monsters.ContainsKey(x.Summon.Monster)))
            {
                if (SlotFor(x) is not int slot) continue;
                var beast = data.Monsters[x.Summon!.Monster];
                var atk = beast.Actions.Where(a => a.IsAttack).OrderByDescending(a => a.ToHit).FirstOrDefault();
                if (atk == null) continue;
                double hit = atk.Effects.Where(e => e.Type == EffectPart.Damage && e.Dice != null).Sum(e => Rules.DiceExpression.Parse(e.Dice!.Replace("MOD", "0")).Average);
                options.Add((x, reach[0].i, slot, x.Summon.CountAt(x.Level, slot) * hit * 0.6 * 3));
            }
        }
        var cantrip = options.Where(o => o.x.IsCantrip).OrderByDescending(o => o.v).FirstOrDefault();
        var best = options.Where(o => !o.x.IsCantrip).OrderByDescending(o => o.v).FirstOrDefault();
        if (best.x != null && best.v > Math.Max(1, cantrip.v) * 1.5 && (dangerous || s.Groups[best.g].AliveCount >= 2 || best.x.Target != SpellTarget.Enemy)
            && !(best.x.Summon != null && !dangerous))   // 召喚は強い戦いのときだけ
            return new PlayerCommand(CommandKind.Cast, best.g, -1, best.x.Id, best.slot);
        if (cantrip.x != null) return new PlayerCommand(CommandKind.Cast, cantrip.g, -1, cantrip.x.Id, 0);
        return null;
    }

    /// <summary>
    /// fights 回戦う。makeParty は毎回新しいパーティ(戦うと HP が減るため)、makeGroups は毎回の敵を作る。
    /// </summary>
    public static SimulationResult Run(GameData data, int fights, ulong seed,
        Func<IRandom, List<Character>> makeParty, Func<IRandom, IReadOnlyList<(MonsterDef Def, int Count)>> makeGroups,
        MonsterScale? scale = null)
    {
        var rng = new Pcg32(seed, 54);
        int wins = 0, defeats = 0, fled = 0;
        double rounds = 0, deaths = 0, hpLeft = 0;
        for (int i = 0; i < fights; i++)
        {
            var party = makeParty(rng);
            var engine = new CombatEngine(data, rng);
            var s = engine.Start(party, makeGroups(rng), scale);
            while (s.Outcome == CombatOutcome.Ongoing && s.Round < MaxRounds) engine.RunRound(s, AutoCommands(s, data));
            switch (s.Outcome)
            {
                case CombatOutcome.Victory: wins++; break;
                case CombatOutcome.Defeat: defeats++; break;
                case CombatOutcome.Fled: fled++; break;
            }
            rounds += s.Round;
            deaths += party.Count(c => c.State is LifeState.Dead);
            int max = party.Sum(c => c.MaxHp);
            hpLeft += max == 0 ? 0 : (double)party.Sum(c => c.Hp) / max;
        }
        return new SimulationResult(fights, wins, defeats, fled, rounds / fights, deaths / fights, hpLeft / fights);
    }
}
