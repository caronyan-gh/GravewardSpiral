using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Combat;

/// <summary>
/// 戦闘を進める(5版のルール、Wizardry 風の位置)。
/// 全員の行動を先に受け取り、イニシアチブの順に1人ずつ処理する。パーティの前列は立っている人の先頭3人、
/// 敵は生きているグループの前2つが近く、近接はそのあいだでだけ届く。
/// </summary>
public sealed class CombatEngine(GameData data, IRandom rng)
{
    private readonly GameData _data = data;
    private readonly IRandom _rng = rng;

    /// <summary>戦闘を始める。groups は (モンスター, 数) の並び(最大4グループ)。scale は深い階の強化。</summary>
    public CombatState Start(IEnumerable<Character> party, IEnumerable<(MonsterDef Def, int Count)> groups, MonsterScale? scale = null)
    {
        var state = new CombatState
        {
            Party = party.Select(c => new PartyMember(c, _data)).ToList(),
            Groups = groups.Take(4).Select(g => new EnemyGroup(g.Def,
                Enumerable.Range(0, Math.Max(1, g.Count)).Select(_ => new MonsterUnit(g.Def, Roll(g.Def.Hp), scale)).ToList())).ToList(),
        };
        foreach (var p in state.Party.Where(p => p.Character.State == LifeState.Unconscious)) p.Stable = true;
        foreach (var p in state.Party) Lasting.Load(p);

        // 正体: パーティの誰かが知力の判定(難易度 10 + CR)に成功すれば分かる
        foreach (var g in state.Groups)
        {
            int dc = 10 + (int)Math.Ceiling(g.Def.Cr);
            int best = state.Party.Where(p => p.Standing).Select(p => D20() + p.AbilityMod(Ability.Int)).DefaultIfEmpty(0).Max();
            g.Identified = best >= dc;
        }

        DecideSurprise(state);

        var all = state.Party.Cast<Combatant>().Concat(state.Groups.SelectMany(g => g.Units)).ToList();
        foreach (var c in all) c.Initiative = D20() + c.AbilityMod(Ability.Dex);
        state.Order.AddRange(all.OrderByDescending(c => c.Initiative).ThenByDescending(c => c.AbilityMod(Ability.Dex))
            .ThenBy(c => c.Side));
        foreach (var g in state.Groups) state.Add("combat.appear", g.DisplayName, g.Units.Count);
        if (state.Surprised == Side.Party) state.Add("combat.party_surprised");
        else if (state.Surprised == Side.Enemy) state.Add("combat.enemy_surprised");
        return state;
    }

    /// <summary>不意打ち: 隠れて近づく側の隠密の判定と、気づく側の受動知覚を比べる。</summary>
    private void DecideSurprise(CombatState s)
    {
        var standing = s.Party.Where(p => p.Standing).ToList();
        if (standing.Count == 0) return;
        int enemyStealth = s.Groups.Select(g => D20() + g.Def.Stealth).Max();
        if (enemyStealth > standing.Max(p => p.PassivePerception))
        {
            s.Surprised = Side.Party;
            return;
        }
        int partyStealth = standing.Min(p => (p.StealthDisadvantage ? Math.Min(D20(), D20()) : D20()) + p.StealthBonus);
        if (partyStealth > s.Groups.Max(g => g.Def.PassivePerception)) s.Surprised = Side.Enemy;
    }

    /// <summary>1 ラウンドを進める。commands はパーティの Id ごとの行動(無ければ回避)。</summary>
    public void RunRound(CombatState s, IReadOnlyDictionary<string, PlayerCommand> commands)
    {
        if (s.Outcome != CombatOutcome.Ongoing) return;
        s.Round++;
        foreach (var c in s.Order.ToList())
        {
            if (s.Outcome != CombatOutcome.Ongoing) break;
            if (c is MonsterUnit { Standing: false }) continue;
            if (c is PartyMember { Character.State: LifeState.Dead or LifeState.Ashes or LifeState.Lost }) continue;
            if (s.Round == 1 && s.Surprised == c.Side) continue;
            TakeTurn(s, c, commands);
            CheckOutcome(s);
        }
        if (s.Outcome == CombatOutcome.Victory) Finish(s);
        else if (s.Outcome == CombatOutcome.Fled)
        {
            foreach (var p in s.Party)
            {
                LoseThrown(s, p);
                Lasting.Save(p);   // 逃げても、毒などは残る
                // 倒れていた人は、仲間に担がれて逃げ、HP 1 で起き上がる(勝ったときと同じ。5版の安定状態から回復した扱い)
                if (p.Character.State == LifeState.Unconscious)
                {
                    p.Character.State = LifeState.Alive;
                    p.Character.Hp = Math.Max(1, p.Character.Hp);
                }
            }
        }
    }

    /// <summary>逃げたとき: 投げた武器は拾えずになくす(全部投げた品は装備から外れる)。</summary>
    /// <summary>勝って拾うとき、投げた武器1本ごとにこの確率でなくす(見つからない、折れている)。</summary>
    public const double ThrownLossChance = 0.3;

    /// <summary>
    /// 投げた武器を片づける。逃げたら全部なくす。勝ったら拾うが、1本ごとに ThrownLossChance でなくす。
    /// 全部なくした品は装備から外れる。
    /// </summary>
    private void LoseThrown(CombatState s, PartyMember p, bool won = false)
    {
        foreach (var (item, thrown) in p.Thrown)
        {
            int n = won ? Enumerable.Range(0, thrown).Count(_ => _rng.NextDouble() < ThrownLossChance) : thrown;
            if (n == 0) continue;
            item.Count -= n;
            s.Add("combat.thrown_lost", p.Name, _data.Items[item.ItemId].Name, n);
            if (item.Count > 0) continue;
            foreach (var slot in p.Character.Inventory.Equipped.Where(kv => ReferenceEquals(kv.Value, item)).Select(kv => kv.Key).ToList())
                p.Character.Inventory.Equipped.Remove(slot);
        }
        p.Thrown.Clear();
    }

    private void TakeTurn(CombatState s, Combatant c, IReadOnlyDictionary<string, PlayerCommand> commands)
    {
        c.Dodging = false;
        if (c is PartyMember pm && pm.Character.State == LifeState.Unconscious)
        {
            DeathSave(s, pm);
            return;
        }
        if (!c.Standing) return;
        c.ReactionUsed = false;
        Linger(s, c);
        if (s.Outcome != CombatOutcome.Ongoing || !c.Standing) return;
        var prone = c.Conditions.FirstOrDefault(x => x.Condition == Condition.Prone);
        if (prone != null)
        {
            c.Conditions.Remove(prone);
            s.Add("combat.stand", c.Name);
        }
        if (c is MonsterUnit mu) Recharge(mu);

        if (c.Incapacitated) s.Add("combat.cannot_act", c.Name, ConditionName(c));
        else if (c is PartyMember p) PlayerTurn(s, p, commands.TryGetValue(p.Id, out var cmd) ? cmd : PlayerCommand.Dodge);
        else if (c is MonsterUnit m) MonsterTurn(s, m);

        EndOfTurn(s, c);
    }

    private static object ConditionName(Combatant c) =>
        c.Conditions.First(x => x.Condition is Condition.Paralyzed or Condition.Stunned or Condition.Unconscious
            or Condition.Petrified or Condition.Incapacitated).Condition;

    // ---------------- パーティの行動 ----------------

    private void PlayerTurn(CombatState s, PartyMember p, PlayerCommand cmd)
    {
        switch (cmd.Kind)
        {
            case CommandKind.Attack:
                PlayerAttack(s, p, cmd.TargetGroup);
                break;
            case CommandKind.Cast:
                CastSpell(s, p, cmd);
                break;
            case CommandKind.Item:
                UseItem(s, p, cmd);
                break;
            case CommandKind.Dodge:
                p.Dodging = true;
                s.Add("combat.dodge", p.Name);
                break;
            case CommandKind.Help:
                if (cmd.TargetMember >= 0 && cmd.TargetMember < s.Party.Count && s.Party[cmd.TargetMember] is { Standing: true } ally && ally != p)
                {
                    ally.Helped = true;
                    s.Add("combat.help", p.Name, ally.Name);
                }
                else
                {
                    p.Dodging = true;
                    s.Add("combat.dodge", p.Name);
                }
                break;
            case CommandKind.Hide:
            {
                int roll = (p.StealthDisadvantage ? Math.Min(D20(), D20()) : D20()) + p.StealthBonus;
                int seek = s.LiveGroups.Select(g => g.Def.PassivePerception).DefaultIfEmpty(10).Max();
                p.Hidden = roll > seek;
                s.Add(p.Hidden ? "combat.hide_ok" : "combat.hide_fail", p.Name);
                break;
            }
            case CommandKind.Swap:
                if (cmd.TargetMember >= 0 && cmd.TargetMember < s.Party.Count && s.Party[cmd.TargetMember] != p)
                {
                    if (p.Has(Condition.Grappled) || s.Party[cmd.TargetMember].Has(Condition.Grappled))
                    {
                        s.Add("combat.swap_grappled", p.Name);
                        break;
                    }
                    int i = s.Party.IndexOf(p), j = cmd.TargetMember;
                    var other = s.Party[j];
                    (s.Party[i], s.Party[j]) = (s.Party[j], s.Party[i]);
                    s.Add("combat.swap", p.Name, other.Name);
                }
                break;
            case CommandKind.Flee:
            {
                int near = s.LiveGroups.Take(CombatState.NearGroups).Sum(g => g.AliveCount);
                int dc = 10 + Math.Min(5, near);
                if (D20() + p.AbilityMod(Ability.Dex) + p.Character.PassiveBonus(_data, "flee") >= dc)
                {
                    s.Outcome = CombatOutcome.Fled;
                    s.Add("combat.fled");
                }
                else s.Add("combat.flee_fail", p.Name);
                break;
            }
        }
    }

    /// <summary>
    /// 追加攻撃の回数(5版: ファイターは 5・11・20 レベルで増え、バーバリアン・パラディン・レンジャー・モンクは 5 レベルで 2 回)。
    /// マルチクラスでは、クラスごとのレベルで一番多いもの(足し合わせない)。
    /// </summary>
    public static int AttacksPerAction(Character c) => c.ClassIds.Distinct().Select(id => (id, lv: c.LevelIn(id))).Select(x => x.id switch
    {
        "fighter" => x.lv >= 20 ? 4 : x.lv >= 11 ? 3 : x.lv >= 5 ? 2 : 1,
        "barbarian" or "paladin" or "ranger" or "monk" => x.lv >= 5 ? 2 : 1,
        _ => 1,
    }).DefaultIfEmpty(1).Max();

    /// <summary>1回の攻撃の見込み(使う武器、命中の修正、ダメージ)。</summary>
    private sealed record StrikePlan(ItemDef? Weapon, ItemInstance? Held, bool Shoot, int ToHit, string Dice, int DamageMod, bool Magical, bool Finesse)
    {
        /// <summary>追加効果の、当たったときの追加のダメージの平均。</summary>
        public double AffixAverage { get; init; }

        /// <summary>1回あたりの平均ダメージ(当たったとき)。</summary>
        public double Average(ItemDef? def) =>
            Math.Max(1, DiceExpression.Parse(Dice).Average + DamageMod)
            + (Held != null && def != null && Held.ElementDice(def) is { } ed ? DiceExpression.Parse(ed).Average : 0)
            + AffixAverage;
    }

    /// <summary>
    /// 攻撃。追加攻撃の回数だけ攻撃する。使う武器は、装備している武器(利き手と遠隔の枠)の中から、
    /// その相手への期待ダメージ(命中率 × 平均ダメージ)が一番高いものを選ぶ。隣接して飛び道具を撃つときは不利(5版)。
    /// 二刀流(5版): 近接で、両手とも軽い武器を持っていれば、もう片方の手の武器でも1回攻撃する(ボーナスアクション)。
    /// もう片方の手の攻撃のダメージには、能力値の修正を足さない(負なら足す)。
    /// </summary>
    private void PlayerAttack(CombatState s, PartyMember p, int groupIndex)
    {
        var classes = p.Character.ClassIds.Select(id => _data.Classes[id]).ToList();
        int str = p.AbilityMod(Ability.Str), dex = p.AbilityMod(Ability.Dex);
        bool sneakUsed = false;
        int attacks = AttacksPerAction(p.Character) + p.Modifiers.Where(m => m.Stat == "attacks").Sum(m => m.Value);   // ヘイストなど
        bool usedMainMelee = false;
        var offItem = p.Character.Inventory[EquipSlot.OffHand];

        (ItemInstance, ItemDef)? Ammo(string kind) =>
            p.Character.Inventory[EquipSlot.Ammo] is { Count: > 0 } a && _data.Items.TryGetValue(a.ItemId, out var d) && d.AmmoType == kind ? (a, d) : null;

        StrikePlan Plan(ItemDef? weapon, ItemInstance? held, bool shoot, bool offHand)
        {
            bool proficient = weapon == null || classes.Any(cls => cls.Weapons.Contains(weapon.Category ?? "") || cls.Weapons.Contains(weapon.Id));
            bool finesse = weapon?.Has("finesse") == true;
            int mod = weapon is { Ranged: true } ? dex : finesse ? Math.Max(str, dex) : str;
            int damageMod = offHand ? Math.Min(0, mod) : mod;
            string dice = weapon == null ? "1" :
                !offHand && !shoot && weapon.VersatileDamage != null && offItem == null ? weapon.VersatileDamage : weapon.Damage ?? "1";
            int bonus = 0;
            bool magical = false;
            if (weapon != null && held != null && held.ItemId == weapon.Id)
            {
                int enchant = held.TotalBonus(weapon);
                bonus = ItemInstance.HitAcBonus(enchant);   // 命中は +7 から増えない。ダメージは全部
                magical = weapon.IsMagic || held.Bonus != 0;
                damageMod += enchant + p.Character.DamageBonus(_data);
            }
            // 弾の枠の弾(その飛び道具に合うものだけ。無ければふつうの弾で、上乗せなし)
            if (shoot && weapon?.UsesAmmo is { } kind && Ammo(kind) is var (ammo, adef))
            {
                int enchant = ammo.TotalBonus(adef);
                bonus += ItemInstance.HitAcBonus(enchant) + adef.AmmoHit;
                damageMod += enchant + adef.AmmoDamage;
                magical |= adef.IsMagic || enchant > 0;
            }
            bonus += p.Character.PassiveBonus(_data, "attack");
            int toHit = mod + bonus + (proficient ? p.Character.ProficiencyBonus : 0);
            return new StrikePlan(weapon, held, shoot, toHit, dice, damageMod, magical, finesse)
            {
                AffixAverage = held != null && weapon != null && held.ItemId == weapon.Id ? Loot.OnHitAverage(_data, held) : 0,
            };
        }

        // 期待ダメージ。隣接して撃つときは不利(2回振って低い方)なので、命中率を2乗する
        double Expected(StrikePlan plan, Combatant target, bool adjacent)
        {
            double hit = Math.Clamp((21 - (target.Ac - plan.ToHit)) / 20.0, 0.05, 0.95);
            if (plan.Shoot && adjacent) hit *= hit;
            return hit * plan.Average(plan.Weapon);
        }

        // 1回の攻撃。届く敵がいなければ false
        bool Strike(bool first, bool offHand)
        {
            var targets = s.AttackableGroups(p).ToList();
            if (targets.Count == 0)
            {
                if (first) s.Add("combat.no_reach", p.Name);
                return false;
            }
            int gi = targets.Contains(groupIndex) ? groupIndex : targets[0];
            var group = s.Groups[gi];
            var alive = group.Alive.ToList();
            var target = alive[_rng.Next(0, alive.Count)];
            bool adjacent = s.InFront(p) && s.IsNear(group);
            StrikePlan plan;
            if (offHand) plan = Plan(offItem != null ? _data.Items.GetValueOrDefault(offItem.ItemId) : null, offItem, false, offHand: true);
            else
            {
                var choices = p.WeaponChoices(adjacent).Select(c => Plan(c.Def, c.Item, c.Ranged, offHand: false)).ToList();
                if (choices.Count == 0) choices.Add(Plan(null, null, false, false));
                plan = choices.OrderByDescending(c => Expected(c, target, adjacent)).First();
            }
            if (!offHand && !plan.Shoot && adjacent) usedMainMelee = true;
            var weapon = plan.Weapon;

            var parts = new List<EffectPart>
            {
                new() { Type = EffectPart.Damage, Dice = $"{plan.Dice}{Signed(plan.DamageMod)}", DamageType = weapon?.DamageType ?? "bludgeoning" },
            };
            foreach (var m in p.Modifiers.Where(m => m.Stat == "damage" && m.Dice != null))
                parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = m.Dice, DamageType = "radiant" });
            // 当たったときの効果と属性(装備している武器のとき)
            if (weapon != null && plan.Held is { } held && held.ItemId == weapon.Id)
            {
                if (!weapon.Attunement || held.Attuned) parts.AddRange(weapon.OnHit);
                if (held.ElementDice(weapon) is { } ed) parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = ed, DamageType = held.Element });
                parts.AddRange(held.AffixOnHit(_data));
            }
            var roll = AttackRoll(s, p, target, plan.ToHit, melee: !plan.Shoot, out bool advantage, extraDisadvantage: plan.Shoot && adjacent);
            int rogue = p.Character.LevelIn("rogue");
            if (!sneakUsed && rogue > 0 && (plan.Finesse || weapon is { Ranged: true }) && roll.Hit
                && (advantage || (s.IsNear(group) && s.FrontRowMembers.Any(x => x != p))))
            {
                parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = $"{(rogue + 1) / 2}d6", DamageType = weapon?.DamageType ?? "piercing" });
                sneakUsed = true;
            }
            ResolveAttack(s, p, target, weapon?.Name ?? LocalizedText.Of("unarmed strike", "素手"), roll, parts, plan.Magical);
            // 投げた武器は、この戦闘では手元から無くなる(勝てば拾う)。戻ってくる武器(returning)は減らない
            if (plan.Shoot && weapon is { Ranged: false } && weapon.Has("thrown") && !weapon.Has("returning") && plan.Held is { } thrown)
            {
                p.Thrown[thrown] = p.Thrown.GetValueOrDefault(thrown) + 1;
                if (p.Left(thrown) <= 0) s.Add("combat.thrown_out", p.Name, weapon.Name);
            }
            // 弾を1本使う(無くなったら枠から外れ、次からはふつうの弾)
            if (plan.Shoot && weapon?.UsesAmmo is { } used && Ammo(used) is var (spent, _) && --spent.Count <= 0)
            {
                p.Character.Inventory.Equipped.Remove(EquipSlot.Ammo);
                s.Add("combat.ammo_out", p.Name, _data.Items[spent.ItemId].Name);
            }
            return true;
        }

        for (int n = 0; n < attacks && s.Outcome == CombatOutcome.Ongoing; n++)
        {
            if (!Strike(n == 0, offHand: false)) return;
        }
        if (s.Outcome == CombatOutcome.Ongoing && usedMainMelee && CanTwoWeapon(p.Character)) Strike(false, offHand: true);
    }

    /// <summary>二刀流ができるか(利き手と、もう片方の手の両方に軽い近接武器)。</summary>
    public bool CanTwoWeapon(Character c)
    {
        bool Light(ItemInstance? it) => it != null && _data.Items.TryGetValue(it.ItemId, out var d) && d.Kind == ItemKind.Weapon && d.Has("light") && !d.Ranged;
        return Light(c.Inventory[EquipSlot.MainHand]) && Light(c.Inventory[EquipSlot.OffHand]);
    }

    private static string Signed(int v) => v == 0 ? "" : v > 0 ? $"+{v}" : v.ToString();

    // ---------------- モンスターの行動 ----------------

    private void Recharge(MonsterUnit m)
    {
        // 組のある行動は組で1回だけ振る(組の最初の行動の目で)
        foreach (var a in m.Def.Actions.Where(a => a.Recharge.HasValue && m.Spent.Contains(a.Id))
                     .GroupBy(a => a.RechargeGroup ?? a.Id).Select(g => g.First()).ToList())
        {
            if (_rng.Roll(6) < a.Recharge!.Value) continue;
            foreach (var x in m.Def.Actions.Where(x => (x.RechargeGroup ?? x.Id) == (a.RechargeGroup ?? a.Id))) m.Spent.Remove(x.Id);
        }
    }

    private void MonsterTurn(CombatState s, MonsterUnit m)
    {
        var group = s.GroupOf(m);
        bool near = s.IsNear(group);
        if (m.Def.Spellcasting != null && !s.NoMagic && MonsterCast(s, m)) return;
        bool Usable(MonsterAction a) => m.Ready(a) && (a.Range != ActionRange.Melee || (near && s.FrontRowMembers.Any()));

        // 再使用のある大技(ブレスなど)は、使えるなら先に使う
        var special = m.Def.Actions.Where(a => !a.IsAttack && Usable(a)).ToList();
        if (special.Count > 0 && (m.Def.Actions.All(a => !a.IsAttack) || _rng.NextDouble() < 0.6))
        {
            UseAction(s, m, special[_rng.Next(0, special.Count)]);
            return;
        }

        foreach (var option in m.Def.Multiattack.OrderBy(_ => _rng.NextUInt()))
        {
            var acts = option.Select(x => (Action: m.Def.Action(x.Action)!, x.Count)).ToList();
            if (acts.All(x => Usable(x.Action)))
            {
                foreach (var (a, count) in acts)
                {
                    for (int i = 0; i < count && s.Outcome == CombatOutcome.Ongoing && s.Party.Any(p => p.Standing); i++) UseAction(s, m, a);
                }
                return;
            }
        }
        var attacks = m.Def.Actions.Where(a => a.IsAttack && Usable(a)).ToList();
        if (attacks.Count == 0)
        {
            s.Add("combat.monster_wait", group.DisplayName);
            return;
        }
        // 近くにいるなら近接を好む
        var melee = attacks.Where(a => a.Range != ActionRange.Ranged).ToList();
        var pick = near && melee.Count > 0 ? melee : attacks;
        UseAction(s, m, pick[_rng.Next(0, pick.Count)]);
    }

    private void UseAction(CombatState s, MonsterUnit m, MonsterAction a)
    {
        var group = s.GroupOf(m);
        if (a.Uses.HasValue) m.UsesLeft[a.Id] = m.UsesLeft.GetValueOrDefault(a.Id) - 1;
        if (a.Recharge.HasValue) m.Spent.Add(a.Id);
        if (a.RechargeGroup != null)
            foreach (var other in m.Def.Actions.Where(o => o.RechargeGroup == a.RechargeGroup)) m.Spent.Add(other.Id);
        bool melee = a.Range == ActionRange.Melee || (a.Range == ActionRange.Any && s.IsNear(group));

        if (a.Target == ActionTarget.One)
        {
            var target = PickPartyTarget(s, melee, m);
            if (target == null) return;
            if (a.IsAttack)
            {
                var roll = AttackRoll(s, m, target, a.ToHit!.Value + m.Scale.HitBonus, melee, out _);
                ResolveAttack(s, m, target, a.Name, roll, a.Effects, magical: false, actorName: group.DisplayName);
            }
            else
            {
                s.Add("combat.monster_uses", group.DisplayName, a.Name, target.Name);
                Apply(s, m, target, a.Effects, crit: false);
            }
            return;
        }
        var targets = (a.Target == ActionTarget.Row ? s.FrontRowMembers : s.Party.Where(p => p.Standing)).ToList();
        s.Add("combat.monster_area", group.DisplayName, a.Name);
        foreach (var t in targets) Apply(s, m, t, a.Effects, crit: false);
    }

    /// <summary>
    /// モンスターが呪文を唱える(敵に効くダメージや状態の呪文だけ)。パーティが2人以上立っていれば範囲の呪文を先に、
    /// スロットが残っていれば高いレベルから。スロットが無ければ攻撃の初級呪文(近くにいて近接の攻撃があれば、そちらを使う)。
    /// 唱えたら true。
    /// </summary>
    private bool MonsterCast(CombatState s, MonsterUnit m)
    {
        var sc = m.Def.Spellcasting!;
        var group = s.GroupOf(m);
        bool near = s.IsNear(group);
        var spells = sc.Spells.Select(id => _data.Spells.GetValueOrDefault(id)).OfType<SpellDef>()
            .Where(x => x.InCombat && x.TargetsEnemies && (x.Attack != "melee" || near)).ToList();
        int standing = s.Party.Count(p => p.Standing);
        SpellDef? pick = null;
        int slot = 0;
        foreach (var x in spells.Where(x => !x.IsCantrip).OrderByDescending(x => x.Target is SpellTarget.Group or SpellTarget.TwoGroups && standing >= 2)
                     .ThenByDescending(x => x.Level))
        {
            int lv = Enumerable.Range(x.Level, 10 - x.Level).FirstOrDefault(l => m.SlotsLeft(l) > 0);
            if (lv == 0 || _rng.NextDouble() > 0.7) continue;
            pick = x;
            slot = lv;
            break;
        }
        if (pick == null)
        {
            bool canMelee = near && m.Def.Actions.Any(a => a.IsAttack && a.Range != ActionRange.Ranged);
            if (canMelee) return false;
            pick = spells.FirstOrDefault(x => x.IsCantrip);
            if (pick == null) return false;
        }
        if (slot > 0) m.SlotsUsed[slot] = m.SlotsUsed.GetValueOrDefault(slot) + 1;
        s.Add("combat.cast", group.DisplayName, pick.Name);
        if (TryCounter(s, m, pick, slot)) return true;   // 味方のカウンタースペル

        var ability = Abilities.Parse(sc.Ability);
        var ctx = new CastContext(m, pick, pick.Scale == "character" ? sc.Level : slot, m.Def.Mod(ability), sc.Dc);
        if (pick.Concentration) StartConcentration(s, m, pick);
        bool melee = pick.Attack == "melee";
        List<Combatant> targets = pick.Target switch
        {
            SpellTarget.Enemy => PickPartyTarget(s, melee, m) is { } t ? [t] : [],
            SpellTarget.AllEnemies => s.Party.Where(p => p.Standing).Cast<Combatant>().ToList(),
            _ => s.FrontRowMembers.Cast<Combatant>().ToList(),   // 範囲は前列に当たる
        };
        if (pick.Zone && pick.Concentration && targets.Count > 0)
        {
            var first = targets[0];
            Func<List<Combatant>> again = pick.Target == SpellTarget.Enemy ? () => [first] : () => s.FrontRowMembers.Cast<Combatant>().ToList();
            s.Lingerings.Add(new Lingering { Caster = m, Spell = pick, Level = ctx.Level, Mod = ctx.Mod, Dc = ctx.Dc, Targets = again });
        }
        var pool = pick.Effects.FirstOrDefault(e => e.Type == EffectPart.HpPool);
        if (pool != null)
        {
            int left = RollDice(DiceOf(pool, ctx));
            foreach (var t in targets.OrderBy(t => t.Hp))
            {
                if (t.Hp > left) break;
                left -= t.Hp;
                AddCondition(s, t, new EffectPart { Condition = pool.Condition, Rounds = pool.Rounds }, null, ctx);
            }
            return true;
        }
        foreach (var t in targets)
        {
            if (pick.Only != null && pick.Only != "humanoid") continue;
            if (pick.Attack != null)
            {
                var roll = AttackRoll(s, m, t, sc.Attack, melee, out _);
                if (!roll.Hit)
                {
                    s.Add("combat.miss", group.DisplayName, pick.Name, t.Name);
                    continue;
                }
                s.Add(roll.Crit ? "combat.crit" : "combat.hit", group.DisplayName, pick.Name, t.Name);
                Apply(s, m, t, pick.Effects, roll.Crit, true, null, ctx);
            }
            else Apply(s, m, t, pick.Effects, false, true, null, ctx);
        }
        return true;
    }

    /// <summary>敵が狙うパーティの1人。近接なら前列から。隠れている人は、ほかに誰かいれば狙わない。</summary>
    /// <summary>知能で狙いを変える境目(知力)。これ以下は目の前の召喚に食いつき、これより上は術者を狙う。</summary>
    public const int DumbInt = 4, SmartInt = 12;

    /// <summary>
    /// 敵が狙う相手。知能で変える。
    /// 知力 4 以下(獣など): 召喚がいれば 7 割でそちらに食いつく。
    /// 知力 5〜11: 前列(遠くからなら全員)と召喚を、区別せずに選ぶ(召喚は数が多いほど選ばれやすい。3 まで)。
    /// 知力 12 以上: 召喚は無視して、集中している術者を狙う。いなければ HP の一番少ない人。召喚しか残っていなければ召喚。
    /// 隠れている人は狙わない(見える人がいれば)。
    /// </summary>
    private Combatant? PickPartyTarget(CombatState s, bool melee, MonsterUnit? attacker = null)
    {
        var pool = (melee ? s.FrontRowMembers : s.Party.Where(p => p.Standing)).Cast<Combatant>().ToList();
        var visible = pool.Where(p => !p.Hidden).ToList();
        if (visible.Count > 0) pool = visible;
        var summons = s.Lingerings.Select(l => l.Unit).OfType<SummonUnit>().Where(u => u.Standing).ToList();
        if (pool.Count == 0) return summons.Count > 0 ? summons[_rng.Next(0, summons.Count)] : null;
        if (summons.Count == 0 || attacker == null) return pool[_rng.Next(0, pool.Count)];
        int intel = attacker.Def.Score(Ability.Int);
        if (intel <= DumbInt)
            return _rng.NextDouble() < 0.7 ? summons[_rng.Next(0, summons.Count)] : pool[_rng.Next(0, pool.Count)];
        if (intel < SmartInt)
        {
            var all = pool.Concat(summons.SelectMany(u => Enumerable.Repeat<Combatant>(u, Math.Min(3, u.Count)))).ToList();
            return all[_rng.Next(0, all.Count)];
        }
        var casters = pool.Where(p => p.Concentration != null).ToList();
        return casters.Count > 0 ? casters[_rng.Next(0, casters.Count)] : pool.OrderBy(p => p.Hp).First();
    }

    // ---------------- 攻撃とダメージ ----------------

    public readonly record struct AttackResult(int Natural, int Total, bool Hit, bool Crit);

    private AttackResult AttackRoll(CombatState s, Combatant attacker, Combatant target, int toHit, bool melee, out bool advantage,
        bool extraDisadvantage = false)
    {
        int adv = 0, dis = extraDisadvantage ? 1 : 0;
        if (attacker.Hidden) { adv++; attacker.Hidden = false; }
        if (attacker.Helped) { adv++; attacker.Helped = false; }
        if (attacker.Has(Condition.Invisible)) adv++;
        if (target.Has(Condition.Blinded) || target.Has(Condition.Restrained) || target.Incapacitated) adv++;
        if (target.Has(Condition.Prone)) { if (melee) adv++; else dis++; }
        if (attacker is MonsterUnit mu && mu.Def.Has("pack_tactics") && s.GroupOf(mu).AliveCount > 1) adv++;
        if (attacker.Has(Condition.Poisoned) || attacker.Has(Condition.Frightened) || attacker.Has(Condition.Blinded)
            || attacker.Has(Condition.Restrained) || attacker.Has(Condition.Prone)) dis++;
        if (attacker is PartyMember am && !am.Character.ArmorProficient(_data)) dis++;   // 習熟の無い鎧
        if (target.Dodging && !target.Incapacitated) dis++;
        if (target.Has(Condition.Invisible)) dis++;

        advantage = adv > 0 && dis == 0;
        bool disadvantage = dis > 0 && adv == 0;
        int a = D20(), b = D20();
        int nat = advantage ? Math.Max(a, b) : disadvantage ? Math.Min(a, b) : a;
        int total = nat + toHit + ModifierRoll(attacker, "attack");
        bool hit = nat != 1 && (nat == 20 || total >= target.Ac);
        bool crit = hit && (nat == 20 || (melee && (target.Has(Condition.Paralyzed) || target.Has(Condition.Unconscious))));
        return new AttackResult(nat, total, hit, crit);
    }

    private void ResolveAttack(CombatState s, Combatant attacker, Combatant target, LocalizedText weapon, AttackResult roll,
        List<EffectPart> onHit, bool magical, LocalizedText? actorName = null)
    {
        var who = actorName ?? attacker.Name;
        var whom = target is MonsterUnit tu ? s.GroupOf(tu).DisplayName : target.Name;
        if (!roll.Hit)
        {
            s.Add("combat.miss", who, weapon, whom);
            return;
        }
        s.Add(roll.Crit ? "combat.crit" : "combat.hit", who, weapon, whom);
        Apply(s, attacker, target, onHit, roll.Crit, magical);
    }

    /// <summary>呪文を唱えたときの情報(ダイスのレベル、MOD、難易度、続くラウンド数、集中)。モンスターの行動では null。</summary>
    private sealed record CastContext(Combatant Caster, SpellDef Spell, int Level, int Mod, int Dc);

    private string DiceOf(EffectPart part, CastContext? ctx)
    {
        string dice = (ctx == null ? part.Dice : part.DiceAt(ctx.Level)) ?? "0";
        return dice.Replace("MOD", ctx == null ? "0" : ctx.Mod >= 0 ? ctx.Mod.ToString() : $"({ctx.Mod})").Replace("+(", "(").Replace("(-", "-").Replace(")", "");
    }

    private static int? RoundsOf(int? rounds, CastContext? ctx) => rounds ?? (ctx is { Spell.Rounds: > 0 } ? ctx.Spell.Rounds : null);

    private int RollDice(string dice) => DiceExpression.Parse(dice).Roll(_rng).Total;

    /// <summary>効果の部品を、対象に起こす。</summary>
    private void Apply(CombatState s, Combatant source, Combatant target, IEnumerable<EffectPart> parts, bool crit, bool magical = false,
        EffectPart? fromSave = null, CastContext? ctx = null)
    {
        foreach (var part in parts)
        {
            if (!target.Standing && target is MonsterUnit) return;
            if (target is PartyMember { Character.State: LifeState.Dead or LifeState.Ashes } && part.Type != EffectPart.Revive) continue;
            // only を書いた部品は、その種類の相手(undead など)にだけ効く
            if (part.Only != null && (target is not MonsterUnit om || om.Def.CreatureType != part.Only)) continue;
            switch (part.Type)
            {
                case EffectPart.Damage:
                {
                    var expr = DiceExpression.Parse(DiceOf(part, ctx));
                    if (crit) expr = expr.WithDoubledDice();
                    int rolled = Math.Max(0, expr.Roll(_rng).Total);
                    if (source is MonsterUnit strong) rolled = (int)(rolled * strong.Scale.DamageMult);   // 深い階の敵はダメージが大きい
                    Damage(s, target, rolled, part.DamageType ?? "bludgeoning", crit, magical || ctx != null);
                    break;
                }
                case EffectPart.Heal:
                    Heal(s, target, Math.Max(1, RollDice(DiceOf(part, ctx))));
                    break;
                case EffectPart.TempHp:
                {
                    int amount = Math.Max(0, RollDice(DiceOf(part, ctx)));
                    if (amount > target.TempHp)
                    {
                        target.TempHp = amount;
                        s.Add("combat.temp_hp", target.Name, amount);
                    }
                    break;
                }
                case EffectPart.Modifier:
                {
                    target.Modifiers.RemoveAll(m => m.Stat == part.Stat && m.SpellId != null && m.SpellId == ctx?.Spell.Id);
                    target.Modifiers.Add(new ActiveModifier
                    {
                        Stat = part.Stat ?? "", Value = part.Value ?? 0, Dice = part.Dice,
                        RoundsLeft = RoundsOf(part.Rounds, ctx), SourceId = ctx?.Caster.Id, SpellId = ctx?.Spell.Id,
                    });
                    s.Add("combat.modifier", target.Name, "stat." + part.Stat);
                    break;
                }
                case EffectPart.ConditionType:
                    if (part.MaxHp is int cap && target.Hp > cap) break;
                    AddCondition(s, target, part, fromSave, ctx);
                    break;
                case EffectPart.Resist or EffectPart.Vulnerable or EffectPart.SetAbility or EffectPart.ImmuneCondition:
                    // 水薬や呪文の、しばらくのあいだの抵抗・脆弱・能力値・状態の耐性
                    target.Modifiers.Add(new ActiveModifier
                    {
                        Stat = part.Type switch
                        {
                            EffectPart.SetAbility => "set:" + part.Ability,
                            EffectPart.ImmuneCondition => "immune:" + part.Condition.ToString()!.ToLowerInvariant(),
                            _ => part.Type + ":" + part.DamageType,
                        },
                        Value = part.Value ?? 0, RoundsLeft = RoundsOf(part.Rounds, ctx), SourceId = ctx?.Caster.Id, SpellId = ctx?.Spell.Id,
                    });
                    s.Add("combat.boon", target.Name);
                    break;
                case EffectPart.RemoveCondition:
                    if (target.Conditions.RemoveAll(x => x.Condition == part.Condition) > 0)
                        s.Add("combat.condition_end", target.Name, part.Condition!.Value);
                    break;
                case EffectPart.Revive:
                    if (target is PartyMember dead && (dead.Character.State == LifeState.Dead || (part.AllowAshes && dead.Character.State == LifeState.Ashes)))
                    {
                        dead.Character.State = LifeState.Alive;
                        dead.Character.Hp = Math.Max(1, part.Value ?? 1);
                        dead.DeathSuccesses = dead.DeathFailures = 0;
                        s.Add("combat.revive", dead.Name);
                    }
                    break;
                case EffectPart.RemoveCurse:
                    if (target is PartyMember cursed && cursed.Character.Inventory.BreakCurses(_data.Items) > 0)
                        s.Add("combat.curse_broken", cursed.Name);
                    else s.Add("combat.no_effect", target.Name);
                    break;
                case EffectPart.Identify:
                    if (target is PartyMember owner && owner.Character.Inventory.Items.Concat(owner.Character.Inventory.Equipped.Values)
                            .FirstOrDefault(i => !i.Identified) is { } unknown)
                    {
                        unknown.Identified = true;
                        s.Add("combat.identified", owner.Name, _data.Items[unknown.ItemId].Name);
                    }
                    else s.Add("combat.nothing_to_identify", target.Name);
                    break;
                case EffectPart.Stabilize:
                    if (target is PartyMember { Dying: true } dying)
                    {
                        dying.Stable = true;
                        s.Add("combat.stable", target.Name);
                    }
                    break;
                case EffectPart.Kill:
                    if (target.Hp <= (part.MaxHp ?? 0)) Damage(s, target, target.Hp + target.TempHp + target.MaxHp, "force", false, true);
                    else s.Add("combat.no_effect", target.Name);
                    break;
                case EffectPart.Wish:
                    // 戦闘の中のウィッシュ: パーティ全員を完全に回復し、状態を解く(ほかの大きな効果は町やキャンプで選ぶ)
                    foreach (var p in s.Party.Where(p => p.Character.State is LifeState.Alive or LifeState.Unconscious))
                    {
                        p.Conditions.Clear();
                        Heal(s, p, p.MaxHp);
                    }
                    break;
                case EffectPart.Save:
                {
                    var ability = Abilities.Parse(part.Ability ?? "con");
                    int dc = part.Dc == 0 && ctx != null ? ctx.Dc : part.Dc;
                    bool ok = SaveOrResist(s, target, ability, dc);
                    s.Add(ok ? "combat.save_ok" : "combat.save_fail", target.Name, ability);
                    var withDc = part.Dc == dc ? part : new EffectPart { Ability = part.Ability, Dc = dc };
                    if (ok)
                    {
                        if (part.Half)
                        {
                            foreach (var d in part.Fail.Where(f => f.Type == EffectPart.Damage))
                                Damage(s, target, RollDice(DiceOf(d, ctx)) / 2, d.DamageType ?? "", false, magical || ctx != null);
                        }
                        Apply(s, source, target, part.Success, false, magical, withDc, ctx);
                    }
                    else Apply(s, source, target, part.Fail, false, magical, withDc, ctx);
                    break;
                }
            }
        }
    }

    /// <summary>効果の部品を、パーティの生きている全員に起こす(ウィッシュなど)。</summary>
    public void ApplyToParty(CombatState s, Combatant source, IEnumerable<EffectPart> parts)
    {
        var list = parts.ToList();
        foreach (var p in s.Party.Where(p => p.Character.State is LifeState.Alive or LifeState.Unconscious)) Apply(s, source, p, list, false, true);
    }

    /// <summary>HP を戻す。倒れていた人は起き上がる。</summary>
    private static void Heal(CombatState s, Combatant target, int amount)
    {
        if (target is PartyMember { Character.State: LifeState.Dead or LifeState.Ashes or LifeState.Lost }) return;
        if (target is PartyMember p && p.Character.State == LifeState.Unconscious)
        {
            p.Character.State = LifeState.Alive;
            p.Stable = false;
            p.DeathSuccesses = p.DeathFailures = 0;
            s.Add("combat.revive", p.Name);
        }
        int before = target.Hp;
        target.Hp = before + amount;
        s.Add("combat.heal", target.Name, target.Hp - before);
    }

    /// <summary>修正値の合計を振る(stat が同じものの value とダイス)。</summary>
    private int ModifierRoll(Combatant c, string stat)
    {
        int total = 0;
        foreach (var m in c.Modifiers.Where(m => m.Stat == stat))
        {
            total += m.Value;
            if (m.Dice != null) total += m.Dice.StartsWith('-') ? -RollDice(m.Dice[1..]) : RollDice(m.Dice);
        }
        return total;
    }

    // ---------------- 呪文 ----------------

    /// <summary>パーティの1人が呪文を唱える(戦闘の中と、キャンプの両方から使う)。</summary>
    public void CastSpell(CombatState s, PartyMember p, PlayerCommand cmd)
    {
        var c = p.Character;
        // 難易度と攻撃の修正は、その呪文を覚えている(準備している)クラスの能力値で決める
        if (cmd.SpellId == null || !_data.Spells.TryGetValue(cmd.SpellId, out var spell) || Spellcasting.ClassFor(_data, c, cmd.SpellId) is not { } cls)
            return;
        if (s.NoMagic)
        {
            s.Add("combat.antimagic", p.Name);
            return;
        }
        if (!c.ArmorProficient(_data))
        {
            s.Add("combat.armor_blocks", p.Name);
            return;
        }
        int slot = spell.IsCantrip ? 0 : Math.Max(spell.Level, cmd.SlotLevel);
        if (!spell.IsCantrip && Spellcasting.SlotsLeft(cls, c, slot) <= 0)
        {
            if (Spellcasting.LowestSlot(cls, c, spell) is int lower) slot = lower;
            else
            {
                s.Add("combat.no_slot", p.Name, spell.Name);
                return;
            }
        }
        if (!spell.IsCantrip) Spellcasting.UseSlot(c, slot);
        s.Add("combat.cast", p.Name, spell.Name);
        if (TryCounter(s, p, spell, slot)) return;   // 敵のカウンタースペル

        var ctx = new CastContext(p, spell, spell.Scale == "character" ? c.Level : slot, Spellcasting.Mod(cls, c), Spellcasting.SaveDc(cls, c));   // 初級呪文はキャラクターの合計のレベルで強くなる(5版)
        Resolve(s, p, spell, cmd, ctx, Spellcasting.AttackBonus(cls, c));
    }

    /// <summary>巻物の難易度(5版: 1〜2 レベル 13、3〜4 は 15、5〜6 は 17、7〜8 は 18、9 は 19)。攻撃のボーナスは難易度 − 8。</summary>
    public static int ItemSpellDc(int slot) => slot switch { <= 2 => 13, <= 4 => 15, <= 6 => 17, <= 8 => 18, _ => 19 };

    /// <summary>
    /// 袋の中のアイテムを使う(水薬・巻物・ワンドなど)。呪文のアイテムは、その呪文をアイテムの難易度で唱える。
    /// 消耗品は1つ減り、回数のあるものは回数が減る。戦闘の中とキャンプの両方から使う。
    /// </summary>
    public void UseItem(CombatState s, PartyMember p, PlayerCommand cmd)
    {
        var inv = p.Character.Inventory;
        if (cmd.ItemIndex < 0 || cmd.ItemIndex >= inv.Items.Count) return;
        var item = inv.Items[cmd.ItemIndex];
        if (_data.Items.TryGetValue(item.ItemId, out var ammoDef) && ammoDef.Kind == ItemKind.Ammo)
        {
            // 弾を付け替える(今の弾はバックパックへ)
            if (inv.Equip(cmd.ItemIndex, EquipSlot.Ammo, _data.Items) == EquipResult.Ok) s.Add("combat.ammo_set", p.Name, ammoDef.Name);
            return;
        }
        if (!_data.Items.TryGetValue(item.ItemId, out var def) || !def.IsUsable) return;
        if (s.NoMagic && (def.IsMagic || def.UseSpell != null))
        {
            s.Add("combat.antimagic", p.Name);
            return;
        }
        if (def.Charges != null && (item.Charges ??= def.Charges) <= 0)
        {
            s.Add("combat.no_charges", p.Name, def.Name);
            return;
        }
        if (def.Only == "caster" && Spellcasting.CasterClass(_data, p.Character) == null)
        {
            s.Add("combat.cannot_use", p.Name, def.Name);
            return;
        }
        if (def.Attunement && !item.Attuned && def.Charges != null)
        {
            // 同調の要る杖やワンドは、装備していなくても、持ち主が同調していれば使える(ここでは使った人が同調の空きを使う)
            if (p.Character.Inventory.AttunedCount >= Inventory.MaxAttuned)
            {
                s.Add("combat.not_attuned", p.Name, def.Name);
                return;
            }
        }
        s.Add("combat.use_item", p.Name, def.Name);
        item.Identified = true;
        if (def.Charges != null) item.Charges--;
        else if (--item.Count <= 0) inv.Items.RemoveAt(cmd.ItemIndex);

        SpellDef spell;
        int level;
        if (def.UseSpell != null && _data.Spells.TryGetValue(def.UseSpell, out var sp))
        {
            spell = sp;
            level = sp.Scale == "character" ? p.Character.Level : Math.Max(sp.Level, def.UseSlot);
        }
        else
        {
            spell = new SpellDef
            {
                Id = "item:" + def.Id, Name = def.Name, Effects = def.Use, Status = SpellStatus.Ok,
                Target = def.UseTarget switch
                {
                    "self" => SpellTarget.Self, "allies" => SpellTarget.Allies, "enemy" => SpellTarget.Enemy, "group" => SpellTarget.Group,
                    _ => SpellTarget.Ally,
                },
            };
            level = 1;
        }
        int dc = ItemSpellDc(Math.Max(1, level));
        int mod = Spellcasting.CasterClass(_data, p.Character) is { } cls ? Spellcasting.Mod(cls, p.Character) : 2;
        Resolve(s, p, spell, cmd, new CastContext(p, spell, level, mod, dc), dc - 8);
    }

    /// <summary>呪文(か、呪文のように使うアイテム)の対象を決めて、効果を起こす。</summary>
    private void Resolve(CombatState s, PartyMember p, SpellDef spell, PlayerCommand cmd, CastContext ctx, int attackBonus)
    {
        if (spell.Concentration) StartConcentration(s, p, spell);
        if (spell.Summon is { } summon && _data.Monsters.TryGetValue(summon.Monster, out var beast))
        {
            // 召喚: 呼んだものは、次の自分の番から攻撃する(集中が切れると消える)
            int n = summon.CountAt(spell.Level, ctx.Level);
            s.Lingerings.Add(new Lingering { Caster = p, Spell = spell, Level = ctx.Level, Mod = ctx.Mod, Dc = ctx.Dc, Targets = () => [], Summoned = beast, Unit = new SummonUnit(beast, n) });
            s.Add("combat.summon", p.Name, beast.Name, n);
            return;
        }
        var targets = SpellTargets(s, p, spell, cmd);
        if (spell.Zone && spell.Concentration && targets.Count > 0)
        {
            // 地帯: 同じグループ(1体の呪文はその1体)に、番ごとにもう一度
            var first = targets[0];
            Func<List<Combatant>> again = spell.Target == SpellTarget.Enemy || first is not MonsterUnit fm
                ? () => [first]
                : () => s.GroupOf(fm).Alive.Cast<Combatant>().ToList();
            s.Lingerings.Add(new Lingering { Caster = p, Spell = spell, Level = ctx.Level, Mod = ctx.Mod, Dc = ctx.Dc, Targets = again });
        }
        if (targets.Count == 0)
        {
            s.Add("combat.no_target", p.Name);
            return;
        }

        // スリープのような「HP の合計ぶん」の呪文は、HP の低い順にかける
        var pool = spell.Effects.FirstOrDefault(e => e.Type == EffectPart.HpPool);
        if (pool != null)
        {
            int left = RollDice(DiceOf(pool, ctx));
            foreach (var t in targets.Where(t => t.Standing && !(pool.Condition is { } pc && t.Immune(pc))).OrderBy(t => t.Hp))
            {
                if (t.Hp > left) break;
                left -= t.Hp;
                AddCondition(s, t, new EffectPart { Condition = pool.Condition, Rounds = pool.Rounds }, null, ctx);
            }
            return;
        }

        foreach (var t in targets)
        {
            if (s.Outcome != CombatOutcome.Ongoing && s.Groups.Count > 0) break;
            if (spell.Only != null && t is MonsterUnit mu && mu.Def.CreatureType != spell.Only)
            {
                s.Add("combat.no_effect", s.GroupOf(mu).DisplayName);
                continue;
            }
            if (spell.Attack != null)
            {
                bool melee = spell.Attack == "melee";
                var roll = AttackRoll(s, p, t, attackBonus, melee, out _);
                ResolveSpellAttack(s, p, t, spell, roll, ctx);
            }
            else Apply(s, p, t, spell.Effects, false, true, null, ctx);
        }
    }

    private void ResolveSpellAttack(CombatState s, PartyMember p, Combatant t, SpellDef spell, AttackResult roll, CastContext ctx)
    {
        var whom = t is MonsterUnit tu ? s.GroupOf(tu).DisplayName : t.Name;
        if (!roll.Hit)
        {
            s.Add("combat.miss", p.Name, spell.Name, whom);
            return;
        }
        s.Add(roll.Crit ? "combat.crit" : "combat.hit", p.Name, spell.Name, whom);
        Apply(s, p, t, spell.Effects, roll.Crit, true, null, ctx);
    }

    /// <summary>呪文の対象。敵は TargetGroup のグループ(届かなければ届く先頭)、味方は TargetMember。</summary>
    private List<Combatant> SpellTargets(CombatState s, PartyMember p, SpellDef spell, PlayerCommand cmd)
    {
        var revive = spell.Effects.FirstOrDefault(e => e.Type == EffectPart.Revive);
        var alliesAlive = s.Party.Where(x => x.Character.State is LifeState.Alive or LifeState.Unconscious
            || (revive != null && (x.Character.State == LifeState.Dead || (revive.AllowAshes && x.Character.State == LifeState.Ashes)))).ToList();
        switch (spell.Target)
        {
            case SpellTarget.Self:
                return [p];
            case SpellTarget.Ally:
                return cmd.TargetMember >= 0 && cmd.TargetMember < s.Party.Count && alliesAlive.Contains(s.Party[cmd.TargetMember])
                    ? [s.Party[cmd.TargetMember]] : [p];
            case SpellTarget.Allies:
                return alliesAlive.Take(spell.Count ?? alliesAlive.Count).Cast<Combatant>().ToList();
        }
        var reach = SpellReach(s, p, spell).ToList();
        if (reach.Count == 0) return [];
        int gi = reach.Contains(cmd.TargetGroup) ? cmd.TargetGroup : reach[0];
        var group = s.Groups[gi];
        switch (spell.Target)
        {
            case SpellTarget.Enemy:
            {
                var alive = group.Alive.ToList();
                return [alive[_rng.Next(0, alive.Count)]];
            }
            case SpellTarget.Group:
                return group.Alive.Cast<Combatant>().ToList();
            case SpellTarget.TwoGroups:
            {
                var next = s.LiveGroups.SkipWhile(g => g != group).Skip(1).FirstOrDefault();
                return group.Alive.Concat(next?.Alive ?? []).Cast<Combatant>().ToList();
            }
            default:
                return s.LiveGroups.SelectMany(g => g.Alive).Cast<Combatant>().ToList();
        }
    }

    /// <summary>その呪文で狙えるグループ(近接の呪文の攻撃は、前列から近くのグループにだけ届く)。</summary>
    public static IEnumerable<int> SpellReach(CombatState s, PartyMember p, SpellDef spell)
    {
        for (int i = 0; i < s.Groups.Count; i++)
        {
            var g = s.Groups[i];
            if (g.Wiped) continue;
            if (spell.Attack != "melee" || (s.InFront(p) && s.IsNear(g))) yield return i;
        }
    }

    private static void StartConcentration(CombatState s, Combatant caster, SpellDef spell)
    {
        if (caster.Concentration != null) EndConcentration(s, caster);
        caster.Concentration = spell.Id;
    }

    /// <summary>集中が切れる: その人がその呪文でかけた状態と修正値を、全員から消す。</summary>
    private static void EndConcentration(CombatState s, Combatant caster)
    {
        string? spell = caster.Concentration;
        if (spell == null) return;
        caster.Concentration = null;
        s.Lingerings.RemoveAll(l => l.Caster == caster);
        var everyone = s.Party.Cast<Combatant>().Concat(s.Groups.SelectMany(g => g.Units));
        foreach (var c in everyone)
        {
            c.Conditions.RemoveAll(x => x.SourceId == caster.Id && x.SpellId == spell);
            c.Modifiers.RemoveAll(x => x.SourceId == caster.Id && x.SpellId == spell);
        }
        s.Add("combat.concentration_end", caster.Name);
    }

    /// <summary>セーヴ。失敗しても、伝説的抵抗が残っているモンスターは成功に変える(ボスに縛る呪文が素通りしないように)。</summary>
    private bool SaveOrResist(CombatState s, Combatant c, Ability a, int dc)
    {
        if (SavingThrow(c, a, dc)) return true;
        if (c is MonsterUnit { LegendaryResistLeft: > 0 } m)
        {
            m.LegendaryResistLeft--;
            s.Add("combat.legendary_resist", s.GroupOf(m).DisplayName, m.LegendaryResistLeft);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 番の始めに、その人が集中している地帯の呪文と召喚を効かせる。地帯は同じ相手に効果をもう一度、
    /// 召喚は呼んだものが前のグループを1回ずつ攻撃する。
    /// </summary>
    private void Linger(CombatState s, Combatant c)
    {
        s.Lingerings.RemoveAll(l => l.Caster == c && (c.Concentration != l.Spell.Id || l.Unit is { Standing: false }));   // 集中していないもの、倒れた召喚は残さない
        foreach (var l in s.Lingerings.Where(l => l.Caster == c && c.Concentration == l.Spell.Id).ToList())
        {
            var ctx = new CastContext(c, l.Spell, l.Level, l.Mod, l.Dc);
            if (l.Summoned is { } beast)
            {
                var attack = beast.Actions.Where(a => a.IsAttack).OrderByDescending(a => a.ToHit).FirstOrDefault();
                if (attack == null) continue;
                for (int i = 0; i < (l.Unit?.Count ?? 0) && s.Outcome == CombatOutcome.Ongoing; i++)
                {
                    var target = s.Groups.Where(g => !g.Wiped).OrderBy(g => s.IsNear(g) ? 0 : 1).SelectMany(g => g.Alive).FirstOrDefault();
                    if (target == null) break;
                    var roll = AttackRoll(s, c, target, attack.ToHit!.Value, melee: true, out _);
                    ResolveAttack(s, c, target, attack.Name, roll, attack.Effects, magical: true, actorName: beast.Name);
                    CheckOutcome(s);
                }
                continue;
            }
            var targets = l.Targets().Where(t => t.Standing).ToList();
            if (targets.Count == 0) continue;
            s.Add("combat.zone", c.Name, l.Spell.Name);
            foreach (var t in targets)
            {
                if (s.Outcome != CombatOutcome.Ongoing) break;
                Apply(s, c, t, l.Spell.Effects, false, true, null, ctx);
                CheckOutcome(s);
            }
        }
    }

    /// <summary>
    /// カウンタースペル(リアクション): 相手の側で、カウンタースペルを持ちスロットの残る人が、3 レベル以上の呪文を打ち消す。
    /// 唱えた呪文のレベル以上のスロットを使えば必ず成功、低いスロットなら 能力値の修正 + d20 が 10 + 呪文のレベル 以上で成功。
    /// 打ち消したら true(唱えた側のスロットは減ったまま)。
    /// </summary>
    private bool TryCounter(CombatState s, Combatant caster, SpellDef spell, int slot)
    {
        if (spell.IsCantrip || slot < 3 || !_data.Spells.TryGetValue("counterspell", out var counter)) return false;
        IEnumerable<Combatant> foes = caster.Side == Side.Party ? s.Groups.SelectMany(g => g.Alive) : s.Party.Where(p => p.Standing);
        foreach (var f in foes.Where(f => !f.ReactionUsed && !f.Incapacitated))
        {
            int use, mod;
            if (f is PartyMember p)
            {
                if (s.NoMagic || !Spellcasting.Castable(_data, p.Character).Any(x => x.Id == counter.Id)
                    || Spellcasting.ClassFor(_data, p.Character, counter.Id) is not { } cls) continue;
                var levels = Enumerable.Range(3, 7).Where(l => Spellcasting.SlotsLeft(cls, p.Character, l) > 0).ToList();
                if (levels.Count == 0) continue;
                use = levels.FirstOrDefault(l => l >= slot, levels.Max());
                Spellcasting.UseSlot(p.Character, use);
                mod = Spellcasting.Mod(cls, p.Character);
            }
            else if (f is MonsterUnit m && m.Def.Spellcasting is { } sc && sc.Spells.Contains(counter.Id))
            {
                var levels = Enumerable.Range(3, 7).Where(l => m.SlotsLeft(l) > 0).ToList();
                if (levels.Count == 0) continue;
                use = levels.FirstOrDefault(l => l >= slot, levels.Max());
                m.SlotsUsed[use] = m.SlotsUsed.GetValueOrDefault(use) + 1;
                mod = m.Def.Mod(Abilities.Parse(sc.Ability));
            }
            else continue;
            f.ReactionUsed = true;
            bool ok = use >= slot || D20() + mod >= 10 + slot;
            if (f is MonsterUnit mu) s.Add(ok ? "combat.counter_ok" : "combat.counter_fail", s.GroupOf(mu).DisplayName, spell.Name);
            else s.Add(ok ? "combat.counter_ok" : "combat.counter_fail", f.Name, spell.Name);
            if (ok) return true;
        }
        return false;
    }

    public bool SavingThrow(Combatant c, Ability a, int dc)
    {
        bool helpless = c.Has(Condition.Paralyzed) || c.Has(Condition.Stunned) || c.Has(Condition.Unconscious) || c.Has(Condition.Petrified);
        if (helpless && a is Ability.Str or Ability.Dex) return false;   // 麻痺などでは筋力と敏捷力のセーヴに必ず失敗する
        int roll = D20();
        if (a == Ability.Dex && c.Has(Condition.Restrained)) roll = Math.Min(roll, D20());
        return roll + c.SaveBonus(a) + ModifierRoll(c, "save") >= dc;
    }

    /// <summary>
    /// 状態にする。セーヴの失敗から来た状態で repeat_save なら、同じセーヴを毎ターンし直す。
    /// escape_dc が 0 なら唱えた人の呪文の難易度。repeat_save で ability があれば、そのセーヴ(パワー・ワード・スタン)。
    /// </summary>
    private void AddCondition(CombatState s, Combatant target, EffectPart part, EffectPart? fromSave, CastContext? ctx = null)
    {
        if (part.Condition is not { } cond || !target.Standing) return;
        if (target.Immune(cond)) return;
        target.Conditions.RemoveAll(x => x.Condition == cond);
        int escape = part.EscapeDc is 0 && ctx != null ? ctx.Dc : part.EscapeDc ?? 0;
        Ability? repeat = part.EscapeDc.HasValue ? Ability.Str
            : part.RepeatSave && fromSave != null ? Abilities.Parse(fromSave.Ability ?? "con")
            : part.RepeatSave && part.Ability != null ? Abilities.Parse(part.Ability) : null;
        target.Conditions.Add(new ActiveCondition
        {
            Condition = cond,
            RoundsLeft = RoundsOf(part.Rounds, ctx),
            SaveAbility = repeat,
            Dc = part.EscapeDc.HasValue ? escape : fromSave?.Dc ?? ctx?.Dc ?? 0,
            SourceId = ctx?.Caster.Id,
            SpellId = ctx is { Spell.Concentration: true } ? ctx.Spell.Id : null,
        });
        s.Add("combat.condition", target.Name, cond);
    }

    /// <summary>ダメージを与える。抵抗などの倍率をかけ、HP が 0 になったら倒れる。</summary>
    public void Damage(CombatState s, Combatant target, int amount, string type, bool crit, bool magical)
    {
        if (target is PartyMember { Character.State: LifeState.Dead }) return;
        double mult = target.DamageMultiplier(type, magical);
        if (target.Modifiers.Any(m => m.Stat == "resist:" + type)) mult *= 0.5;
        if (target.Modifiers.Any(m => m.Stat == "vulnerable:" + type)) mult *= 2;
        int dealt = (int)Math.Floor(amount * mult);
        if (dealt > 0 && target.TempHp > 0)
        {
            int absorbed = Math.Min(target.TempHp, dealt);
            target.TempHp -= absorbed;
            dealt -= absorbed;
            if (dealt == 0)
            {
                s.Add("combat.temp_absorb", target.Name, absorbed);
                return;
            }
        }
        // 集中: ダメージを受けたら耐久力のセーヴ(難易度は 10 か、ダメージの半分の大きい方)
        if (dealt > 0 && target.Concentration != null && target.Hp - dealt > 0 && !SavingThrow(target, Ability.Con, Math.Max(10, dealt / 2)))
            EndConcentration(s, target);
        if (target is PartyMember dying && dying.Character.State == LifeState.Unconscious)
        {
            if (dealt <= 0) return;
            dying.Stable = false;
            dying.DeathFailures += crit ? 2 : 1;
            s.Add("combat.hit_down", target.Name);
            if (dying.DeathFailures >= 3) Die(s, dying);
            return;
        }
        int before = target.Hp;
        target.Hp = before - dealt;
        s.Add("combat.damage", target is MonsterUnit tu ? s.GroupOf(tu).DisplayName : target.Name, dealt, type);
        if (target.Hp > 0) return;

        if (target is SummonUnit summon)
        {
            s.Add("combat.summon_gone", summon.Name);
            s.Lingerings.RemoveAll(l => l.Unit == summon);
            return;
        }

        if (target is MonsterUnit m)
        {
            // アンデッドの不屈: 光輝とクリティカル以外なら、耐久力のセーヴで HP 1 で踏みとどまる
            if (m.Def.Has("undead_fortitude") && type != "radiant" && !crit && SavingThrow(m, Ability.Con, 5 + dealt))
            {
                m.Hp = 1;
                s.Add("combat.endure", s.GroupOf(m).DisplayName);
                return;
            }
            var g = s.GroupOf(m);
            g.Identified = true;
            EndConcentration(s, m);
            s.Defeated.Add(m);
            s.Add("combat.slain", g.DisplayName);
            return;
        }
        var p = (PartyMember)target;
        EndConcentration(s, p);
        p.Conditions.Clear();
        p.Modifiers.Clear();
        if (dealt - before >= p.MaxHp) Die(s, p);   // 大きすぎるダメージは即死
        else
        {
            p.Character.State = LifeState.Unconscious;
            p.DeathSuccesses = p.DeathFailures = 0;
            p.Stable = false;
            s.Add("combat.down", p.Name);
        }
    }

    private static void Die(CombatState s, PartyMember p)
    {
        p.Character.State = LifeState.Dead;
        p.Character.Hp = 0;
        s.Add("combat.dead", p.Name);
    }

    private void DeathSave(CombatState s, PartyMember p)
    {
        if (p.Stable) return;
        int r = D20();
        if (r == 20)
        {
            p.Character.State = LifeState.Alive;
            p.Character.Hp = 1;
            s.Add("combat.revive", p.Name);
            return;
        }
        if (r == 1) p.DeathFailures += 2;
        else if (r >= 10) p.DeathSuccesses++;
        else p.DeathFailures++;
        if (p.DeathFailures >= 3) Die(s, p);
        else if (p.DeathSuccesses >= 3)
        {
            p.Stable = true;
            s.Add("combat.stable", p.Name);
        }
        else s.Add("combat.death_save", p.Name, p.DeathSuccesses, p.DeathFailures);
    }

    private void EndOfTurn(CombatState s, Combatant c)
    {
        foreach (var x in c.Conditions.ToList())
        {
            bool end = false;
            if (x.SaveAbility is { } a && SaveOrResist(s, c, a, x.Dc)) end = true;
            if (x.RoundsLeft is int n)
            {
                x.RoundsLeft = n - 1;
                if (n - 1 <= 0) end = true;
            }
            if (end)
            {
                c.Conditions.Remove(x);
                s.Add("combat.condition_end", c.Name, x.Condition);
            }
        }
        foreach (var m in c.Modifiers.ToList())
        {
            if (m.RoundsLeft is not int n) continue;
            m.RoundsLeft = n - 1;
            if (n - 1 <= 0) c.Modifiers.Remove(m);
        }
    }

    private static void CheckOutcome(CombatState s)
    {
        if (s.Outcome != CombatOutcome.Ongoing) return;
        if (s.Groups.All(g => g.Wiped)) s.Outcome = CombatOutcome.Victory;
        else if (!s.Party.Any(p => p.Standing)) s.Outcome = CombatOutcome.Defeat;
    }

    /// <summary>勝ったあと: 経験値を生き残りで等分し、金を拾う。倒れていた人は HP 1 で起き上がる。状態は消える。</summary>
    private void Finish(CombatState s)
    {
        foreach (var p in s.Party) LoseThrown(s, p, won: true);   // 投げた武器を拾う(いくらかはなくす)
        var alive = s.Party.Where(p => p.Character.State is LifeState.Alive or LifeState.Unconscious).ToList();
        long xp = s.Defeated.Sum(m => (long)m.Def.Xp);
        long each = alive.Count == 0 ? 0 : xp / alive.Count;
        long gold = s.Defeated.Where(m => CarriesTreasure(m.Def)).Sum(m => (long)DiceExpression.Parse($"{Math.Max(1, (int)Math.Ceiling(m.Def.Cr * 4))}d6").Roll(_rng).Total);
        foreach (var p in alive)
        {
            p.Character.Xp += each;
            if (p.Character.State == LifeState.Unconscious)
            {
                p.Character.State = LifeState.Alive;
                p.Character.Hp = Math.Max(1, p.Character.Hp);
            }
        }
        foreach (var c in s.Party)
        {
            Lasting.Save(c);   // 毒など、長く続く状態だけを残す
            c.Conditions.Clear();
            c.Modifiers.Clear();
            c.Concentration = null;
        }
        if (alive.Count > 0) PartyGold.Share(alive.Select(p => p.Character).ToList(), gold);   // 金は生き残った人で分ける(宝箱やイベントと同じ)
        s.Reward = new CombatReward(each, gold, alive.Count);
        s.Add("combat.victory", each, gold);
    }

    /// <summary>金を持っている種類か(獣・不定形・植物は持たない)。</summary>
    public static bool CarriesTreasure(MonsterDef m) => m.CreatureType is not ("beast" or "ooze" or "plant" or "swarm");

    private int D20() => _rng.Roll(20);
    private int Roll(string dice) => DiceExpression.Parse(dice).Roll(_rng).Total;
}
