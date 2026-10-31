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

    /// <summary>Damage で与えたダメージの合計(一時的な HP で受けた分も含む)。その攻撃でダメージがあったかを、HP の差でなくこれで見る。</summary>
    private long _damageDealt;

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
        foreach (var p in state.Party) p.Allies = state.Party;
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
        // 野性の勘(バーバリアン L7): イニシアチブが有利
        foreach (var c in all)
            c.Initiative = (FeatureChecks.InitiativeAdvantage(_data, c) ? Math.Max(D20(), D20()) : D20()) + c.AbilityMod(Ability.Dex);
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
                EndForms(p);
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
        if (c is MonsterUnit regen) Regenerate(s, regen);
        Bleed(s, c);
        if (s.Outcome != CombatOutcome.Ongoing || !c.Standing) return;
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

    /// <summary>
    /// 再生(トロルなど): 番の初めに HP を戻す。止める種類のダメージ(Def.RegenerateStop)を受けたあとの番は戻さない。
    /// 5版のトロルは「HP 0 で番を始め、再生しなかったときだけ死ぬ」が、ここでは HP 0 で倒れる(ほかの再生するモンスターと同じ)。
    /// </summary>
    private static void Regenerate(CombatState s, MonsterUnit m)
    {
        bool stopped = m.RegenerationStopped;
        m.RegenerationStopped = false;
        if (m.Def.Regenerate <= 0 || stopped || m.Hp >= m.MaxHp) return;
        int before = m.Hp;
        m.Hp = Math.Min(m.MaxHp, before + m.Def.Regenerate);
        s.Add("combat.regenerate", s.GroupOf(m).DisplayName, m.Hp - before);
    }

    /// <summary>続くダメージ: 番の初めに、受けているものを順に受ける。</summary>
    private void Bleed(CombatState s, Combatant c)
    {
        foreach (var dot in c.Dots.ToList())
        {
            if (!c.Standing) return;
            s.Add("combat.dot", c is MonsterUnit m ? s.GroupOf(m).DisplayName : c.Name, dot.DamageType);
            _damageLabel = "combat.src.dot";
            Damage(s, c, Math.Max(0, RollDice(dot.Dice)), dot.DamageType, false, dot.Magical);
            _damageLabel = null;
            CheckOutcome(s);
        }
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
            case CommandKind.Feature:
                UseFeature(s, p, cmd);
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
    public static int AttacksPerAction(GameData data, Character c) => c.ClassIds.Distinct()
        .Select(id => data.Classes.GetValueOrDefault(id)?.Progression?.Attacks?.Eval(c, c.LevelIn(id)) ?? 1)
        .DefaultIfEmpty(1).Max();

    /// <summary>攻撃に乗せるクラスの技(スマイト、連打、朦朧打撃)。</summary>
    private enum StrikeTechnique { None, Smite, Flurry, Stun }

    /// <summary>1回の攻撃の見込み(使う武器、命中の修正、ダメージ)。</summary>
    private sealed record StrikePlan(ItemDef? Weapon, ItemInstance? Held, bool Shoot, int ToHit, string Dice, int DamageMod, bool Magical, bool Finesse)
    {
        /// <summary>大型武器のファイティングスタイル(ダメージの 1 と 2 を振り直す)。</summary>
        /// <summary>この値以下のダメージの目を振り直す(大型武器。0 なら振り直さない)。</summary>
        public int RerollLow { get; init; }

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
    /// <param name="bonusAction">ボーナスアクションの打撃(武術・連打・二刀流)もするか。アクション・サージの2回目はしない(ボーナスアクションは1ターンに1回)。</param>
    private void PlayerAttack(CombatState s, PartyMember p, int groupIndex, StrikeTechnique technique = StrikeTechnique.None, bool bonusAction = true, FeatureDef? feature = null)
    {
        if (p.Shape != null)
        {
            BeastAttack(s, p, groupIndex);
            return;
        }
        int str = p.AbilityMod(Ability.Str), dex = p.AbilityMod(Ability.Dex);
        var c = p.Character;
        bool martial = ClassFeatures.MartialArts(_data, c);
        bool techniqueUsed = false, monkMelee = false;
        int attacks = AttacksPerAction(_data, p.Character) + p.Modifiers.Where(m => m.Stat == "attacks").Sum(m => m.Value);   // ヘイストなど
        bool usedMainMelee = false;
        var offItem = p.Character.Inventory[EquipSlot.OffHand];

        (ItemInstance, ItemDef)? Ammo(string kind) =>
            p.Character.Inventory[EquipSlot.Ammo] is { Count: > 0 } a && _data.Items.TryGetValue(a.ItemId, out var d) && d.AmmoType == kind ? (a, d) : null;

        StrikePlan Plan(ItemDef? weapon, ItemInstance? held, bool shoot, bool offHand)
        {
            bool proficient = weapon == null || p.Character.WeaponProficient(_data, weapon);   // 魔法の武器は元の武器でも
            bool monk = martial && !shoot && ClassFeatures.MonkWeapon(_data, weapon);   // 武術: モンク武器と素手は敏捷力も使える
            bool finesse = weapon?.Has("finesse") == true || monk;
            int mod = weapon is { Ranged: true } ? dex : finesse ? Math.Max(str, dex) : str;
            int damageMod = offHand && !FeatureChecks.OffhandAbilityDamage(_data, p) ? Math.Min(0, mod) : mod;   // 二刀流
            string dice = weapon == null ? monk ? $"1d{ClassFeatures.MartialArtsDie(_data, c)}" : "1" :
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
            if (weapon == null && monk && FeatureChecks.UnarmedMagical(_data, p)) magical = true;   // 気の込もった打撃
            if (shoot && weapon is { Ranged: true }) bonus += FeatureChecks.RangedShotHitBonus(_data, p);   // 弓術(利き手のスタイルにかかわらず、撃つ番に)
            if (!shoot && !offHand && weapon != null) damageMod += FeatureChecks.MainMeleeDamageBonus(_data, p);   // 決闘
            // 激怒: 筋力で振る近接の攻撃のダメージに上乗せ
            if (!shoot && mod == str) damageMod += PassiveRules.Sum(_data, c, "damage_bonus", "melee_str", p.Now);   // 激怒(能力 rage_damage は激怒のあいだ)
            int toHit = mod + bonus + (proficient ? p.Character.ProficiencyBonus : 0);
            return new StrikePlan(weapon, held, shoot, toHit, dice, damageMod, magical, finesse)
            {
                AffixAverage = held != null && weapon != null && held.ItemId == weapon.Id ? Loot.OnHitAverage(_data, held) : 0,
                RerollLow = !shoot && !offHand && weapon != null ? FeatureChecks.MainMeleeRerollLow(_data, p) : 0,   // 大型武器
            };
        }

        // 期待ダメージ。隣接して撃つときは不利(2回振って低い方)なので、命中率を2乗する
        double Expected(StrikePlan plan, Combatant target, bool adjacent)
        {
            double hit = Math.Clamp((21 - (target.Ac - plan.ToHit)) / 20.0, 0.05, 0.95);
            if (plan.Shoot && adjacent) hit *= hit;
            return hit * plan.Average(plan.Weapon);
        }

        // 1回の攻撃。届く敵がいなければ false。unarmed は武術の素手の打撃(ボーナスアクション)。bonus はボーナスアクションの攻撃(ログに添える)
        bool killTried = false;   // 一撃で倒す判定(instant_kill)は、1回の攻撃の行動で1度だけ(Wizardry: 何回殴っても振るのは1度。効果の当てはまる攻撃だけを数える)
        bool Strike(bool first, bool offHand, bool unarmed = false, bool bonus = false)
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
            if (unarmed) plan = Plan(null, null, false, offHand: false);
            else if (offHand) plan = Plan(offItem != null ? _data.Items.GetValueOrDefault(offItem.ItemId) : null, offItem, false, offHand: true);
            else
            {
                var choices = p.WeaponChoices(adjacent).Select(c => Plan(c.Def, c.Item, c.Ranged, offHand: false)).ToList();
                if (choices.Count == 0) choices.Add(Plan(null, null, false, false));
                plan = choices.OrderByDescending(c => Expected(c, target, adjacent)).First();
            }
            if (!offHand && !plan.Shoot && adjacent) usedMainMelee = true;
            if (!offHand && !unarmed && !plan.Shoot && adjacent && martial && ClassFeatures.MonkWeapon(_data, plan.Weapon)) monkMelee = true;
            var weapon = plan.Weapon;
            bool melee = !plan.Shoot;

            var parts = new List<EffectPart>
            {
                new() { Type = EffectPart.Damage, Dice = $"{plan.Dice}{Signed(plan.DamageMod)}", DamageType = weapon?.DamageType ?? "bludgeoning", Reroll = plan.RerollLow },
            };
            if (melee && weapon != null)
                foreach (var (dice, type, from) in FeatureChecks.MeleeWeaponExtraDamageFrom(_data, p))
                    parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = dice, DamageType = type, Label = "passive." + from });   // 向上したディヴァイン・スマイト
            foreach (var m in p.Modifiers.Where(m => m.Stat == "damage" && m.Dice != null))
                parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = m.Dice, DamageType = "radiant",
                    Label = m.SpellId != null && _data.Spells.TryGetValue(m.SpellId, out var bySpell) ? bySpell.Name : "combat.src.bonus_damage" });
            // 当たったときの効果と属性(装備している武器のとき)
            if (weapon != null && plan.Held is { } held && held.ItemId == weapon.Id)
            {
                // 武器の追加の分は、ログに出どころを添える(元の部品は武器の定義なので、写してから付ける)
                if (!weapon.Attunement || held.Attuned) parts.AddRange(weapon.OnHit.Select(x => Labeled(x, weapon.Name)));
                if (held.ElementDice(weapon) is { } ed) parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = ed, DamageType = held.Element, Label = "combat.src.element" });
                parts.AddRange(held.AffixOnHit(_data).Select(x => Labeled(x, "combat.src.affix")));
            }
            var roll = AttackRoll(s, p, target, plan.ToHit, melee: !plan.Shoot, out bool advantage, extraDisadvantage: plan.Shoot && adjacent);
            int sneak = ClassFeatures.SneakAttackDice(_data, p.Character);
            if (p.SneakRound != s.Round && sneak > 0 && (plan.Finesse || weapon is { Ranged: true }) && roll.Hit   // 1ラウンドに1回(アクション・サージの2回目でも出ない)
                && (advantage || (s.IsNear(group) && s.FrontRowMembers.Any(x => x != p))))
            {
                parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = $"{sneak}d6", DamageType = weapon?.DamageType ?? "piercing", Label = "passive.sneak_attack" });
                p.SneakRound = s.Round;
            }
            // ディヴァイン・スマイト: 最初に当たった近接の攻撃に、スロットを使って光輝のダメージを足す(アンデッドと悪魔には +1d8)
            if (technique == StrikeTechnique.Smite && !techniqueUsed && melee && roll.Hit && ClassFeatures.SmiteSlot(_data, c) is int slot)
            {
                Spellcasting.UseSlot(_data, c, slot);
                int dice = Math.Min(5, slot + 1) + (group.Def.CreatureType is "undead" or "fiend" ? 1 : 0);
                parts.Add(new EffectPart { Type = EffectPart.Damage, Dice = $"{dice}d8", DamageType = "radiant", Label = "combat.src.smite" });
                s.Add("combat.smite", p.Name, slot);
                techniqueUsed = true;
            }
            // 朦朧打撃: 最初に当たった近接の攻撃で気を1使い、耐久力のセーヴに失敗すると次の番まで朦朧
            if (technique == StrikeTechnique.Stun && !techniqueUsed && melee && roll.Hit
                && feature is { } stun && SpendFor(c, stun))
            {
                parts.Add(new EffectPart { Type = EffectPart.Save, Ability = "con", Dc = ClassFeatures.KiDc(_data, c, stun.Id),
                    Fail = [new EffectPart { Type = EffectPart.ConditionType, Condition = Condition.Stunned, Rounds = 1 }] });
                s.Add("combat.stunning_strike", p.Name);
                techniqueUsed = true;
            }
            if (melee && weapon != null && FeatureChecks.MeleeWeaponDrain(_data, p) is int drain and > 0)
                parts.Add(new EffectPart { Type = EffectPart.Drain, Value = drain });   // 命を吸う刃(このゲームでは吸う)
            long dealtBefore = _damageDealt;
            ResolveAttack(s, p, target, weapon?.Name ?? LocalizedText.Of("unarmed strike", "素手"), roll, parts, plan.Magical, label: bonus ? "combat.src.bonus_action" : null);
            // 一撃で倒す(instant_kill): ダメージを1以上与えた(一時的な HP で受けた分も、アンデッドの不屈でこらえた分も数える)最初の攻撃で、1度だけ振る。
            // 不屈でこらえた敵も、首はねは別に振る(不屈はダメージだけをこらえる)
            if (!killTried && roll.Hit && _damageDealt > dealtBefore) killTried = TryInstantKill(s, p, target, unarmed: weapon == null, melee);
            // 残忍な一撃(バーバリアン L9)と獰猛な攻撃(ハーフオーク): 近接のクリティカルで武器のダイスを足す
            if (roll.Crit && melee && FeatureChecks.MeleeCritExtraDice(_data, p) is int brutal and > 0 && target.Standing
                && DiceExpression.Parse(plan.Dice).Terms.FirstOrDefault(t => !t.IsFlat) is { Sides: > 0 } die)
            {
                _damageLabel = "combat.src.crit_extra";
                Damage(s, target, RollDice($"{brutal}d{die.Sides}"), weapon?.DamageType ?? "bludgeoning", false, plan.Magical);
                _damageLabel = null;
            }
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
        // ボーナスアクション: 連打は素手で2回、武術は素手で1回、どちらでもなければ二刀流
        if (s.Outcome != CombatOutcome.Ongoing || !bonusAction) return;
        if (technique == StrikeTechnique.Flurry && martial)
        {
            s.Add("combat.flurry", p.Name);
            for (int n = 0; n < 2 && s.Outcome == CombatOutcome.Ongoing; n++) Strike(false, offHand: false, unarmed: true, bonus: true);
        }
        else if (monkMelee) Strike(false, offHand: false, unarmed: true, bonus: true);
        else if (usedMainMelee && CanTwoWeapon(p.Character)) Strike(false, offHand: true, bonus: true);
    }

    /// <summary>獣の姿の攻撃: 獣の行動(複数回攻撃があればその組)で、近くのグループを殴る。前列からだけ届く。</summary>
    private void BeastAttack(CombatState s, PartyMember p, int groupIndex)
    {
        var beast = p.Shape!;
        var near = s.LiveGroups.Where(g => s.IsNear(g)).ToList();
        if (!s.InFront(p) || near.Count == 0)
        {
            s.Add("combat.no_reach", p.Name);
            return;
        }
        var acts = beast.Multiattack.FirstOrDefault()?.SelectMany(x => Enumerable.Repeat(beast.Action(x.Action), x.Count)).OfType<MonsterAction>().ToList()
            ?? [];
        if (acts.Count == 0 && beast.Actions.Where(a => a.IsAttack).OrderByDescending(a => a.ToHit).FirstOrDefault() is { } one) acts.Add(one);
        foreach (var a in acts.Where(a => a.IsAttack))
        {
            if (s.Outcome != CombatOutcome.Ongoing) return;
            // 狙ったグループが倒れたら、近くの次のグループへ(攻撃のたびに選び直す)
            var group = groupIndex >= 0 && groupIndex < s.Groups.Count && !s.Groups[groupIndex].Wiped && s.IsNear(s.Groups[groupIndex])
                ? s.Groups[groupIndex] : s.LiveGroups.FirstOrDefault(g => s.IsNear(g));
            if (group == null) return;
            var alive = group.Alive.ToList();
            var target = alive[_rng.Next(0, alive.Count)];
            var roll = AttackRoll(s, p, target, a.ToHit!.Value, melee: true, out _);
            ResolveAttack(s, p, target, a.Name, roll, a.Effects, magical: false);
            CheckOutcome(s);
        }
    }

    /// <summary>激怒と獣の姿を解く(戦闘の終わり)。</summary>
    private static void EndForms(PartyMember p)
    {
        p.Dots.Clear();   // 続くダメージは戦闘の終わりで止まる
        p.Raging = false;
        p.Shape = null;
        p.ShapeHp = 0;
        p.StayUpSaves = 0;
    }

    // ---------------- クラスの技 ----------------

    /// <summary>その技の回数を使う(技のデータの pool と cost。amount を渡せばその数。pool が無ければ何も使わず true)。</summary>
    private bool SpendFor(Character c, FeatureDef f, int? amount = null) =>
        f.Pool == null || ClassFeatures.Spend(_data, c, f.Pool, amount ?? f.Cost);

    /// <summary>クラスの技を使う(使えなければ身構える)。</summary>
    private void UseFeature(CombatState s, PartyMember p, PlayerCommand cmd)
    {
        var c = p.Character;
        var f = cmd.FeatureId == null ? null : ClassFeatures.Action(_data, cmd.FeatureId);
        if (f == null || !ClassFeatures.CanUse(_data, c, f) || (p.Shape != null && !ClassFeatures.UsableInBeastForm(f)))
        {
            p.Dodging = true;
            s.Add("combat.dodge", p.Name);
            return;
        }
        var name = "feature." + f.Id;
        switch (f.Part)
        {
            case "second_wind":
                SpendFor(c, f);
                s.Add("combat.feature", p.Name, name);
                Heal(s, p, RollDice(f.Dice ?? "1d10") + ClassFeatures.SecondWindBonus(_data, c, f.Id));
                FollowUp(s, p, cmd.TargetGroup);   // ボーナスアクションなので、そのあと攻撃する
                break;
            case "action_surge":
                SpendFor(c, f);
                s.Add("combat.feature", p.Name, name);
                PlayerAttack(s, p, cmd.TargetGroup);
                if (s.Outcome == CombatOutcome.Ongoing) PlayerAttack(s, p, cmd.TargetGroup, bonusAction: false);
                break;
            case "rage":
                bool started = !p.Raging;
                if (started)
                {
                    SpendFor(c, f);
                    p.Raging = true;
                    if (ClassFeatures.Blocking(_data, c, "no_concentration", p.Now).Any()) EndConcentration(s, p);   // 激怒のあいだは集中できない(能力 rage_limits)
                    s.Add("combat.rage", p.Name);
                }
                PlayerAttack(s, p, cmd.TargetGroup, bonusAction: !started);   // 激怒に入るのはボーナスアクション(その番は二刀流・武術の打撃をしない)
                break;
            case "lay_on_hands":
            {
                var ally = cmd.TargetMember >= 0 && cmd.TargetMember < s.Party.Count ? s.Party[cmd.TargetMember] : p;
                if (ally.Character.State is not (LifeState.Alive or LifeState.Unconscious))
                {
                    s.Add("combat.no_effect", ally.Name);
                    break;
                }
                s.Add("combat.feature_on", p.Name, name, ally.Name);
                // 毒は 5 点で消す。残りで HP を戻す(足りない分だけ)
                if (ally.Has(Condition.Poisoned) && f.Pool != null && SpendFor(c, f, 5))
                {
                    ally.Conditions.RemoveAll(x => x.Condition == Condition.Poisoned);
                    s.Add("combat.condition_end", ally.Name, Condition.Poisoned);
                }
                int need = ally.MaxHp - ally.Hp, pool = f.Pool == null ? 0 : ClassFeatures.UsesLeft(_data, c, f.Pool);
                if (Math.Min(need, pool) is int heal and > 0 && SpendFor(c, f, heal)) Heal(s, ally, heal);
                break;
            }
            case "divine_smite":
                PlayerAttack(s, p, cmd.TargetGroup, StrikeTechnique.Smite);
                break;
            case "flurry_of_blows":
                SpendFor(c, f);
                PlayerAttack(s, p, cmd.TargetGroup, StrikeTechnique.Flurry);
                break;
            case "patient_defense":
                SpendFor(c, f);
                PlayerAttack(s, p, cmd.TargetGroup, bonusAction: false);   // 身構えるのがボーナスアクション
                p.Dodging = true;
                s.Add("combat.dodge", p.Name);
                break;
            case "stunning_strike":
                PlayerAttack(s, p, cmd.TargetGroup, StrikeTechnique.Stun, feature: f);
                break;
            case "turn_undead":
                TurnUndead(s, p, f);
                break;
            case "breath_weapon":
                BreathWeapon(s, p, f, cmd.TargetGroup);
                break;
            case "bardic_inspiration":
            {
                var ally = cmd.TargetMember >= 0 && cmd.TargetMember < s.Party.Count && s.Party[cmd.TargetMember] is { Standing: true } a && a != p ? a : null;
                if (ally == null)
                {
                    s.Add("combat.no_target", p.Name);
                    break;
                }
                SpendFor(c, f);
                ally.Modifiers.RemoveAll(m => m.Stat == "inspire");
                ally.Modifiers.Add(new ActiveModifier { Stat = "inspire", Dice = $"1d{ClassFeatures.InspirationDie(_data, c, f.Id)}", RoundsLeft = 10, SourceId = p.Id });
                s.Add("combat.feature_on", p.Name, name, ally.Name);
                FollowUp(s, p, cmd.TargetGroup);   // ボーナスアクションなので、そのあと攻撃か初級呪文
                break;
            }
            case "wild_shape":
            {
                var beasts = ClassFeatures.WildShapeBeasts(_data, c, f.Id);
                string? id = cmd.Option != null && beasts.Contains(cmd.Option) ? cmd.Option : beasts.LastOrDefault();   // 選ばなければ熊の型
                if (p.Shape != null || id == null || !_data.Shapes.TryGetValue(id, out var beast))
                {
                    s.Add("combat.no_effect", p.Name);
                    break;
                }
                SpendFor(c, f);
                p.Shape = beast;
                p.ShapeHp = beast.HpAverage;
                if (ClassFeatures.Blocking(_data, c, "no_concentration", p.Now).Any()) EndConcentration(s, p);   // 獣の姿では集中は切れない(SRD は no_cast だけ)
                s.Add("combat.wild_shape", p.Name, beast.Name);
                break;
            }
        }
    }

    /// <summary>
    /// アンデッド退散(クレリックのチャネル・ディヴィニティ): すべてのアンデッドが判断力のセーヴ(呪文の難易度)。失敗すると 10 ラウンド何もできない
    /// (ダメージを受けると解ける)。アンデッドの破壊の脅威度以下なら、失敗で滅びる。
    /// </summary>
    /// <summary>竜の息(ドラゴンボーン): 近くのグループに火を吐く。敏捷力のセーヴ(技の dc。8 + 耐久力 + 習熟)、成功で半分。技の dice の数の d6(合計のレベルで 2〜5)。</summary>
    private void BreathWeapon(CombatState s, PartyMember p, FeatureDef f, int groupHint)
    {
        var c = p.Character;
        var near = s.Groups.Select((g, i) => (g, i)).Where(x => !x.g.Wiped && s.IsNear(x.g)).Select(x => x.i).ToList();
        if (near.Count == 0)
        {
            p.Dodging = true;
            s.Add("combat.dodge", p.Name);
            return;
        }
        SpendFor(c, f);
        s.Add("combat.feature", p.Name, "feature." + f.Id);
        var group = s.Groups[near.Contains(groupHint) ? groupHint : near[0]];
        int dc = (int)(ClassFeatures.FeatureValue(_data, c, f.Id, "dc", p.AbilityMod) ?? 0);
        int dice = (int)(ClassFeatures.FeatureValue(_data, c, f.Id, "dice") ?? 0);
        foreach (var u in group.Alive.ToList())
        {
            int dmg = RollDice($"{dice}d6");
            bool saved = SaveOrResist(s, u, Ability.Dex, dc, magical: false);
            s.Add(saved ? "combat.save_ok" : "combat.save_fail", group.DisplayName, Ability.Dex);
            Damage(s, u, saved ? dmg / 2 : dmg, "fire", false, true);
        }
        CheckOutcome(s);
    }

    private void TurnUndead(CombatState s, PartyMember p, FeatureDef f)
    {
        var c = p.Character;
        SpendFor(c, f);
        s.Add("combat.feature", p.Name, "feature.turn_undead");
        var undead = s.Groups.SelectMany(g => g.Alive).Where(u => u.Def.CreatureType == "undead").ToList();
        if (undead.Count == 0)
        {
            s.Add("combat.no_undead");
            return;
        }
        int dc = ClassFeatures.TurnUndeadDc(_data, c, f.Id);
        double destroy = ClassFeatures.DestroyUndeadCr(_data, c);
        foreach (var u in undead)
        {
            if (SaveOrResist(s, u, Ability.Wis, dc, magical: true))
            {
                s.Add("combat.save_ok", s.GroupOf(u).DisplayName, Ability.Wis);
                continue;
            }
            if (u.Def.Cr <= destroy)
            {
                s.Add("combat.undead_destroyed", s.GroupOf(u).DisplayName);
                Damage(s, u, u.Hp + u.MaxHp, "radiant", false, true);
                continue;
            }
            u.Conditions.Add(new ActiveCondition { Condition = Condition.Incapacitated, RoundsLeft = 10, SourceId = p.Id, SpellId = "turn_undead" });
            s.Add("combat.turned", s.GroupOf(u).DisplayName);
        }
        CheckOutcome(s);
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
                ResolveAttack(s, m, target, a.Name, roll, a.Effects, magical: false, actorName: group.DisplayName, ranged: !melee);
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
            foreach (var t in targets.Where(t => !SleepImmune(t, pool)).OrderBy(t => t.Hp))
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
        // 変身した狼の群れの戦術: 前列にほかの仲間が立っていれば有利
        if (attacker is PartyMember { Shape: { } pack } pm && pack.Has("pack_tactics") && s.FrontRowMembers.Any(x => x != pm)) adv++;
        if (attacker.Has(Condition.Poisoned) || attacker.Has(Condition.Frightened) || attacker.Has(Condition.Blinded)
            || attacker.Has(Condition.Restrained) || attacker.Has(Condition.Prone)) dis++;
        if (attacker is PartyMember am && !am.Character.ArmorProficient(_data)) dis++;   // 習熟の無い鎧
        if (target.Dodging && !target.Incapacitated) dis++;
        if (target.Has(Condition.Invisible)) dis++;
        // クラスの能力: 無謀な攻撃(激怒のあいだの近接の攻撃と、激怒している人への攻撃が有利)、とらえどころのなさ(自分への攻撃は有利にならない)
        var (featureAdv, cancelAdv) = FeatureChecks.Attack(_data, attacker, target, melee);
        adv += featureAdv;
        if (cancelAdv) adv = 0;

        advantage = adv > 0 && dis == 0;
        bool disadvantage = dis > 0 && adv == 0;
        int a = D20(), b = D20();
        int nat = advantage ? Math.Max(a, b) : disadvantage ? Math.Min(a, b) : a;
        // 幸運(ハーフリング): 使う d20 が 1 なら、その1つを振り直す
        if (nat == 1 && Lucky(attacker))
        {
            if (a == 1 && (!advantage || b == 1) && (!disadvantage || a <= b)) a = D20(); else b = D20();
            nat = advantage ? Math.Max(a, b) : disadvantage ? Math.Min(a, b) : a;
        }
        int total = nat + toHit + ModifierRoll(attacker, "attack");
        bool hit = nat != 1 && (nat == 20 || total >= target.Ac);
        // 鼓舞: 外れそうなら、もらったダイスを足す(足して当たるときだけ使う)
        if (!hit && nat != 1 && attacker.Modifiers.FirstOrDefault(m => m.Stat == "inspire") is { Dice: { } inspire } im)
        {
            int bonus = RollDice(inspire);
            if (total + bonus >= target.Ac)
            {
                attacker.Modifiers.Remove(im);
                total += bonus;
                hit = true;
                s.Add("combat.inspired", attacker.Name);
            }
        }
        bool crit = hit && (nat == 20 || (melee && (target.Has(Condition.Paralyzed) || target.Has(Condition.Unconscious))));
        return new AttackResult(nat, total, hit, crit);
    }

    private void ResolveAttack(CombatState s, Combatant attacker, Combatant target, LocalizedText weapon, AttackResult roll,
        List<EffectPart> onHit, bool magical, LocalizedText? actorName = null, bool ranged = false, object? label = null)
    {
        var who = actorName ?? attacker.Name;
        var whom = target is MonsterUnit tu ? s.GroupOf(tu).DisplayName : target.Name;
        // label(ボーナスアクションの攻撃など)は、命中の行の4つめに添える(画面が括弧で出す)
        object[] Args() => label == null ? [who, weapon, whom] : [who, weapon, whom, label];
        if (!roll.Hit)
        {
            s.Add("combat.miss", Args());
            return;
        }
        s.Add(roll.Crit ? "combat.crit" : "combat.hit", Args());
        // リアクション: 飛び道具そらし(モンク L3)は飛び道具のダメージを減らし、アンキャニー・ドッジ(ローグ L5)はダメージを半分にする
        if (target is PartyMember tp && attacker.Side != tp.Side && !tp.ReactionUsed && !tp.Incapacitated)
        {
            if (FeatureChecks.ReactionFor(_data, tp, ranged) is { } r)
            {
                if (r.Halve) tp.Halving = true;
                else tp.Deflect = (r.Dice != null ? RollDice(r.Dice) : 0) + (r.Ability is { } ab ? tp.AbilityMod(ab) : 0) + r.Bonus;
                tp.ReactionUsed = true;
                if (r.Message != null) s.Add(r.Message, tp.Name);
            }
        }
        Apply(s, attacker, target, onHit, roll.Crit, magical);
        target.Halving = false;
        target.Deflect = 0;
    }

    /// <summary>呪文を唱えたときの情報(ダイスのレベル、MOD、難易度、続くラウンド数、集中)。モンスターの行動では null。</summary>
    private sealed record CastContext(Combatant Caster, SpellDef Spell, int Level, int Mod, int Dc)
    {
        /// <summary>ソーサラーの呪文の強化(ダメージのダイスの低い目を、魅力の修正の数まで振り直す)。</summary>
        public int Empower { get; set; }
    }

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
        var dealt = new Dictionary<string, int>();   // この並びで与えたダメージ(種類ごと。吸う部品が使う)
        foreach (var part in parts)
        {
            if (part.Type == EffectPart.Drain)
            {
                Drain(s, source, part, dealt);
                continue;
            }
            if (part.Type == EffectPart.Dot)
            {
                AddDot(s, source, target, part, fromSave, ctx, magical || ctx != null);
                continue;
            }
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
                    int rolled = Math.Max(0, RollDamage(expr, part.Reroll, ctx?.Empower ?? 0));
                    // 苦痛の魔撃(ウォーロック L2): エルドリッチ・ブラストに魅力の修正を足す
                    if (ctx is { Caster: PartyMember wl }) rolled += FeatureChecks.SpellDamageBonus(_data, wl, ctx.Spell.Id);
                    if (source is MonsterUnit strong) rolled = (int)(rolled * strong.Scale.DamageMult);   // 深い階の敵はダメージが大きい
                    string type = part.DamageType ?? "bludgeoning";
                    _damageLabel = part.Label;   // 急所攻撃などのおまけの分は、ダメージの行に出どころを添える
                    dealt[type] = dealt.GetValueOrDefault(type) + Damage(s, target, rolled, type, crit, magical || ctx != null);
                    _damageLabel = null;
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
                    if (part.MaxHp is int killCap && target.Hp > killCap) break;
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
                        owner.Character.Inventory.MergeStacks(_data.Items);   // 同じ品と分かったものを重ねる
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
                case EffectPart.Kill when part.Chance is int chance:
                    // 確率で一撃で倒す(首はね): HP に関わらず。くらう人は resist_instant_kill の能力ごとに振って、どれかに当たればこらえる
                    // 同じ攻撃のダメージで気絶した人にも振る(死んだ人と、倒れた敵には振らない)
                    bool gone = target is PartyMember { Character.State: LifeState.Dead or LifeState.Ashes or LifeState.Lost } || target is not PartyMember && !target.Standing;
                    if (gone || target.Hp > (part.MaxHp ?? int.MaxValue) || _rng.Next(0, 100) >= chance) break;
                    if (target is PartyMember guarded && FeatureChecks.InstantKillResists(_data, guarded).Any(r => _rng.Next(0, 100) < r))
                    {
                        s.Add("combat.instant_kill_resisted", target.Name);
                        break;
                    }
                    KillOutright(s, target, part.Message ?? "combat.instant_killed");
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
                    // その状態にする セーヴに有利(save_adv:<状態>。プロテクション・フロム・ポイズンの毒など)
                    bool against = part.Fail.SelectMany(f => f.Flatten()).Any(f => f.Type == EffectPart.ConditionType && f.Condition is { } fc && SaveAdvantage(target, fc));
                    bool ok = SaveOrResist(s, target, ability, dc, magical || ctx != null, against);
                    s.Add(ok ? "combat.save_ok" : "combat.save_fail", target.Name, ability);
                    var withDc = part.Dc == dc ? part : new EffectPart { Ability = part.Ability, Dc = dc };
                    // 回避(ローグ・モンク L7): 敏捷力のセーヴで半分になる効果は、成功で 0、失敗で半分
                    bool evade = part.Half && ability == Ability.Dex && FeatureChecks.Evades(_data, target);
                    if (!ok && evade)
                    {
                        target.Halving = true;
                        Apply(s, source, target, part.Fail, false, magical, withDc, ctx);
                        target.Halving = false;
                    }
                    else if (ok)
                    {
                        if (part.Half && !evade)
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

    /// <summary>
    /// 続くダメージをかける。同じもの(呪文の id か、敵の行動。かけた人では分けない)は重ねず、ダイスの大きいほうとラウンドの長いほうを残す。
    /// </summary>
    private void AddDot(CombatState s, Combatant source, Combatant target, EffectPart part, EffectPart? fromSave, CastContext? ctx, bool magical)
    {
        if (!target.Standing) return;
        string dice = DiceOf(part, ctx);
        string type = part.DamageType ?? "necrotic";
        string key = ctx?.Spell.Id ?? (source is MonsterUnit m ? $"{m.Def.Id}:{type}" : $"{source.Id}:{type}");
        int rounds = RoundsOf(part.Rounds, null) ?? 10;
        var old = target.Dots.FirstOrDefault(d => d.Key == key);
        if (old != null)
        {
            if (DiceExpression.Parse(dice).Average > DiceExpression.Parse(old.Dice).Average) old.Dice = dice;
            old.RoundsLeft = Math.Max(old.RoundsLeft, rounds);
            return;
        }
        int dc = part.Dc != 0 ? part.Dc : fromSave?.Dc ?? ctx?.Dc ?? 10;
        target.Dots.Add(new ActiveDot
        {
            Key = key, Dice = dice, DamageType = type, RoundsLeft = rounds, Magical = magical,
            SaveAbility = part.Ability != null ? Abilities.Parse(part.Ability) : null, Dc = dc,
            SourceId = ctx?.Caster.Id, SpellId = ctx is { Spell.Concentration: true } ? ctx.Spell.Id : null,
        });
        s.Add("combat.dot_on", target is MonsterUnit tm ? s.GroupOf(tm).DisplayName : target.Name, type);
    }

    /// <summary>吸う: 与えたダメージ(damage_type があればその種類だけ)の value % を、使った本人の HP に戻す。</summary>
    private static void Drain(CombatState s, Combatant source, EffectPart part, Dictionary<string, int> dealt)
    {
        int total = part.DamageType is { } only ? dealt.GetValueOrDefault(only) : dealt.Values.Sum();
        int amount = total * (part.Value ?? 100) / 100;
        if (amount <= 0 || !source.Standing || source.Hp >= source.MaxHp) return;
        int before = source.Hp;
        source.Hp = before + amount;
        var who = source is MonsterUnit m ? s.GroupOf(m).DisplayName : source.Name;
        s.Add("combat.drain", who, source.Hp - before);
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
        if (!ClassFeatures.CanCastIn(_data, c, p.Raging, p.Shape != null))
        {
            s.Add(p.Raging ? "combat.rage_no_cast" : "combat.shape_no_cast", p.Name);
            return;
        }
        int slot = spell.IsCantrip ? 0 : Math.Max(Spellcasting.AssignedLevel(_data, c, spell), cmd.SlotLevel);   // クラスで割り当てたレベル(spelllv)のスロットから
        if (!spell.IsCantrip && Spellcasting.SlotsLeft(_data, c, slot) <= 0)
        {
            if (Spellcasting.LowestSlot(_data, c, spell) is int lower) slot = lower;
            else
            {
                s.Add("combat.no_slot", p.Name, spell.Name);
                return;
            }
        }
        if (!spell.IsCantrip) Spellcasting.UseSlot(_data, c, slot);
        s.Add("combat.cast", p.Name, spell.Name);
        if (TryCounter(s, p, spell, slot))   // 敵のカウンタースペル
        {
            if (IsBonusAction(spell)) FollowUp(s, p, cmd.TargetGroup);
            return;
        }

        // 初級呪文はキャラクターの合計のレベルで強くなる(5版)。ほかは、割り当てたレベルから上のスロットの分だけ、もとのレベルより強くなる
        var ctx = new CastContext(p, spell, spell.Scale == "character" ? c.Level : Spellcasting.EffectLevel(_data, c, spell, slot), Spellcasting.Mod(cls, c), Spellcasting.SaveDc(cls, c));
        if (cmd.Empower && CanEmpower(_data, c, spell) && ClassFeatures.Spend(_data, c, "sorcery_points"))
        {
            ctx.Empower = Math.Max(1, c.Scores.Mod(Ability.Cha));
            s.Add("combat.empowered", p.Name);
        }
        Resolve(s, p, spell, cmd, ctx, Spellcasting.AttackBonus(cls, c));
        if (IsBonusAction(spell)) FollowUp(s, p, cmd.TargetGroup);
    }

    /// <summary>
    /// ボーナスアクションで唱える呪文か(唱えたあと、同じ番の行動を自動で行う)。フレイム・ブレードは、このゲームでは唱えたときに
    /// もう斬っている(5版の「行動で斬る」まで含めている)ので外す。
    /// </summary>
    public static bool IsBonusAction(SpellDef spell) => spell.Time == "bonus" && spell.Id != "flame_blade";

    /// <summary>
    /// ボーナスアクションのあとの、同じ番の行動(決めたこと)。前列で近くに敵がいれば武器で攻撃、そうでなければ攻撃の初級呪文
    /// (無ければ届く武器、どちらも無ければ身構える)。狙いは、選んだグループに届けばそこ、届かなければ届く先頭。
    /// </summary>
    private void FollowUp(CombatState s, PartyMember p, int groupHint)
    {
        if (s.Outcome != CombatOutcome.Ongoing || s.Groups.Count == 0 || !p.CanAct) return;   // キャンプで唱えたときは何もしない
        var reach = s.AttackableGroups(p).ToList();
        int Target(IEnumerable<int> groups)
        {
            var list = groups.ToList();
            return list.Contains(groupHint) ? groupHint : list.FirstOrDefault(-1);
        }
        bool melee = s.InFront(p) && s.LiveGroups.Any(s.IsNear);
        var cantrip = melee ? null : FollowUpCantrip(s, p);
        if (cantrip != null)
        {
            CastSpell(s, p, new PlayerCommand(CommandKind.Cast, Target(SpellReach(s, p, cantrip)), -1, cantrip.Id, 0));
            return;
        }
        if (reach.Count > 0)
        {
            PlayerAttack(s, p, Target(reach), bonusAction: false);   // ボーナスアクションはもう使った(二刀流・武術の打撃はしない)
            return;
        }
        p.Dodging = true;
        s.Add("combat.dodge", p.Name);
    }

    /// <summary>あとの行動に使う攻撃の初級呪文(平均のダメージが一番大きいもの)。唱えられなければ null。</summary>
    private SpellDef? FollowUpCantrip(CombatState s, PartyMember p)
    {
        var c = p.Character;
        if (s.NoMagic || !c.ArmorProficient(_data) || !ClassFeatures.CanCastIn(_data, c, p.Raging, p.Shape != null)) return null;
        return Spellcasting.Castable(_data, c)
            .Where(x => x.IsCantrip && x.InCombat && x.TargetsEnemies && SpellReach(s, p, x).Any())
            .Select(x => (x, avg: x.Effects.SelectMany(e => e.Flatten()).Where(e => e.Type == EffectPart.Damage && e.DiceAt(c.Level) != null)
                .Sum(e => DiceExpression.Parse(e.DiceAt(c.Level)!.Replace("MOD", "0")).Average)))
            .Where(x => x.avg > 0).OrderByDescending(x => x.avg).Select(x => x.x).FirstOrDefault();
    }

    /// <summary>呪文の強化ができるか(ソーサラー L3 がメインで、ポイントが残っていて、ダメージのある呪文)。</summary>
    public static bool CanEmpower(GameData data, Character c, SpellDef spell) =>
        ClassFeatures.HasPassive(data, c, "empowered_spell") && ClassFeatures.UsesLeft(data, c, "sorcery_points") > 0
        && spell.Effects.SelectMany(e => e.Flatten()).Any(e => e.Type == EffectPart.Damage);

    /// <summary>
    /// ダメージを振る。reroll があれば、その目以下のダイスを1回だけ振り直す(大型武器)。
    /// empower があれば、平均より低い目のダイスを、低いものから empower 個まで振り直す(呪文の強化)。
    /// </summary>
    private int RollDamage(DiceExpression expr, int reroll, int empower)
    {
        if (reroll <= 0 && empower <= 0) return expr.Roll(_rng).Total;
        int total = 0;
        var dice = new List<(int Value, int Sides, int Sign)>();
        foreach (var t in expr.Terms)
        {
            if (t.IsFlat) total += t.FlatValue;
            else if (t.Keep != KeepMode.All) total += t.Roll(_rng).Value;
            else
                for (int i = 0; i < t.Count; i++)
                {
                    int v = _rng.Roll(t.Sides);
                    if (v <= reroll) v = _rng.Roll(t.Sides);
                    dice.Add((v, t.Sides, t.Sign));
                }
        }
        foreach (var i in dice.Select((d, i) => (d, i)).Where(x => x.d.Sign > 0 && x.d.Value < (x.d.Sides + 1) / 2.0)
                     .OrderBy(x => x.d.Value).Take(empower).Select(x => x.i).ToList())
            dice[i] = dice[i] with { Value = _rng.Roll(dice[i].Sides) };
        return total + dice.Sum(d => d.Sign * d.Value);
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
        if (p.Shape != null)
        {
            s.Add("combat.shape_no_item", p.Name);
            return;
        }
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

    /// <summary>
    /// 呪文(か、呪文のように使うアイテム)の対象を決めて、効果を起こす。集中できない人(no_concentration)は、唱えられても集中がすぐ切れる(5版。
    /// 激怒は唱えることもできないので、唱えられて集中だけできない能力のとき)。
    /// </summary>
    private void Resolve(CombatState s, PartyMember p, SpellDef spell, PlayerCommand cmd, CastContext ctx, int attackBonus)
    {
        ResolveSpell(s, p, spell, cmd, ctx, attackBonus);
        if (spell.Concentration && p.Concentration == spell.Id && ClassFeatures.Blocking(_data, p.Character, "no_concentration", p.Now).Any())
            EndConcentration(s, p);
    }

    private void ResolveSpell(CombatState s, PartyMember p, SpellDef spell, PlayerCommand cmd, CastContext ctx, int attackBonus)
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
        if (spell.Zone && targets.Count > 0)
        {
            // 続く呪文: 同じグループ(1体の呪文はその1体)に、番ごとにもう一度。集中の要らないものはラウンドで数える。
            // 攻撃の目を振るもの(剣・武器)と、集中の要らないもの(番人)は、狙った相手が倒れたら、近くの敵へ移る
            var first = targets[0];
            Func<List<Combatant>> again = spell.Target == SpellTarget.Enemy || first is not MonsterUnit fm
                ? spell.Attack != null || !spell.Concentration ? () => first.Standing ? [first] : NextFoe(s, first) : () => [first]
                : () => s.GroupOf(fm).Alive.Cast<Combatant>().ToList();
            s.Lingerings.RemoveAll(l => l.Caster == p && l.Spell.Id == spell.Id);   // 同じ呪文をかけ直したら、前のものは消える(重ねない)
            s.Lingerings.Add(new Lingering
            {
                Caster = p, Spell = spell, Level = ctx.Level, Mod = ctx.Mod, Dc = ctx.Dc, Targets = again, AttackBonus = attackBonus,
                RoundsLeft = spell.Concentration ? null : Math.Max(1, spell.Rounds),
            });
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
            foreach (var t in targets.Where(t => t.Standing && !(pool.Condition is { } pc && t.Immune(pc)) && !SleepImmune(t, pool)).OrderBy(t => t.Hp))
            {
                if (t.Hp > left) break;
                left -= t.Hp;
                AddCondition(s, t, new EffectPart { Condition = pool.Condition, Rounds = pool.Rounds }, null, ctx);
            }
            return;
        }

        if (spell.Beams != null && spell.Attack != null && targets.FirstOrDefault() is MonsterUnit beamTarget)
        {
            Beams(s, p, spell, ctx, attackBonus, beamTarget, spread: cmd.Option == "spread");
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

    /// <summary>倒れた相手の代わりに狙う敵(同じグループの生き残り、いなければ近いグループ)。</summary>
    private List<Combatant> NextFoe(CombatState s, Combatant gone)
    {
        var pool = gone is MonsterUnit gm && !s.GroupOf(gm).Wiped ? s.GroupOf(gm).Alive.ToList()
            : s.LiveGroups.OrderBy(g => s.IsNear(g) ? 0 : 1).FirstOrDefault()?.Alive.ToList() ?? [];
        return pool.Count == 0 ? [] : [pool[_rng.Next(0, pool.Count)]];
    }

    private void ResolveSpellAttack(CombatState s, Combatant p, Combatant t, SpellDef spell, AttackResult roll, CastContext ctx)
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

    /// <summary>
    /// 味方に向ける呪文で、その人を選べるか(画面の選ぶ一覧もこれで決める)。蘇生の呪文は死んだ人(灰も戻せる呪文なら灰も)だけ、
    /// ほかは立っている人と気絶した人。
    /// </summary>
    public static bool AllyTargetable(SpellDef spell, LifeState state) =>
        spell.Effects.FirstOrDefault(e => e.Type == EffectPart.Revive) is { } revive
            ? state == LifeState.Dead || (revive.AllowAshes && state == LifeState.Ashes)
            : state is LifeState.Alive or LifeState.Unconscious;

    /// <summary>
    /// 光線の呪文。集中は狙った1体に撃ち、倒れたら残りは同じグループの生き残りへランダムに。分散はグループの生き残りから本数分を
    /// ばらばらに選んで1本ずつ(足りなければ余りを生き残りへ)。光線ごとに攻撃ロールとダメージ(余ったダメージは持ち越さない)。
    /// </summary>
    private void Beams(CombatState s, PartyMember p, SpellDef spell, CastContext ctx, int attackBonus, MonsterUnit first, bool spread)
    {
        var group = s.GroupOf(first);
        int n = spell.BeamsAt(ctx.Level);
        var order = new List<MonsterUnit>();
        if (spread)
        {
            var alive = group.Alive.ToList();
            while (alive.Count > 0 && order.Count < n)
            {
                int i = _rng.Next(0, alive.Count);
                order.Add(alive[i]);
                alive.RemoveAt(i);
            }
        }
        else order.Add(first);
        for (int beam = 0; beam < n && s.Outcome == CombatOutcome.Ongoing; beam++)
        {
            var t = beam < order.Count ? order[beam] : null;
            if (t is not { Standing: true })
            {
                var alive = group.Alive.ToList();
                if (alive.Count == 0) break;
                // 集中は同じ相手を撃ち続ける(倒れたらほかの1体へ)。分散の余りは生き残りへ
                t = !spread && order.Count > 0 && order[^1].Standing ? order[^1] : alive[_rng.Next(0, alive.Count)];
                if (!spread) order.Add(t);
            }
            var roll = AttackRoll(s, p, t, attackBonus, spell.Attack == "melee", out _);
            ResolveSpellAttack(s, p, t, spell, roll, ctx);
        }
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
                return group.Alive.Take(spell.Count ?? int.MaxValue).Cast<Combatant>().ToList();   // count があれば、その数まで(チェイン・ライトニング)
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
            c.Dots.RemoveAll(x => x.SourceId == caster.Id && x.SpellId == spell);
        }
        s.Add("combat.concentration_end", caster.Name);
    }

    /// <summary>
    /// セーヴ。呪文と魔法の効果(magical)なら、魔法への抵抗のあるものは有利で振る。
    /// 失敗しても、伝説的抵抗が残っているモンスターは成功に変える(ボスに縛る呪文が素通りしないように)。
    /// </summary>
    private bool SaveOrResist(CombatState s, Combatant c, Ability a, int dc, bool magical, bool advantage = false)
    {
        advantage |= magical && c.MagicResistant;
        advantage |= magical && FeatureChecks.MagicSaveAdvantage(_data, c, a);   // ノームの知恵
        if (SavingThrow(c, a, dc, advantage)) return true;
        // 不屈(ファイター L9): 失敗したセーヴを振り直す
        if (c is PartyMember ip && ClassFeatures.Spend(_data, ip.Character, "indomitable"))
        {
            s.Add("combat.indomitable", c.Name);
            if (SavingThrow(c, a, dc, advantage)) return true;
        }
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
        // 集中が切れたもの、ラウンドの尽きたもの、倒れた召喚は残さない
        s.Lingerings.RemoveAll(l => l.Caster == c && ((l.RoundsLeft == null && c.Concentration != l.Spell.Id) || l.RoundsLeft <= 0 || l.Unit is { Standing: false }));
        foreach (var l in s.Lingerings.Where(l => l.Caster == c).ToList())
        {
            if (l.RoundsLeft != null) l.RoundsLeft--;
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
                if (l.Spell.Attack != null)
                {
                    // 攻撃の目を振る呪文: 毎ターン攻撃する(剣・武器・火の玉)
                    var roll = AttackRoll(s, c, t, l.AttackBonus, l.Spell.Attack == "melee", out _);
                    ResolveSpellAttack(s, c, t, l.Spell, roll, ctx);
                }
                else Apply(s, c, t, l.Spell.Effects, false, true, null, ctx);
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
                int lowest = Math.Max(1, Spellcasting.AssignedLevel(_data, cls, p.Character, counter));   // クラスで割り当てたレベル(5版は 3)から
                var levels = Enumerable.Range(lowest, 10 - lowest).Where(l => Spellcasting.SlotsLeft(_data, p.Character, l) > 0).ToList();
                if (levels.Count == 0) continue;
                use = levels.FirstOrDefault(l => l >= slot, levels.Max());
                Spellcasting.UseSlot(_data, p.Character, use);
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

    /// <summary>その状態にする(か、その状態を解く)セーヴに有利か(修正値 save_adv:&lt;状態&gt; と、能力。FeatureChecks.SaveAdvantageAgainst)。</summary>
    private bool SaveAdvantage(Combatant c, Condition condition) =>
        c.Modifiers.Any(m => m.Stat == "save_adv:" + condition.ToString().ToLowerInvariant()) || FeatureChecks.SaveAdvantageAgainst(_data, c, condition);

    /// <summary>攻撃・セーヴ・死亡のセーヴの d20 の 1 を振り直すか(幸運)。</summary>
    private bool Lucky(Combatant c) => FeatureChecks.Lucky(_data, c);

    /// <summary>魔法の眠りの呪文の状態に、魔法ではならないか(妖精の血)。</summary>
    private bool SleepImmune(Combatant c, EffectPart pool) => pool.Condition is { } pc && FeatureChecks.MagicImmune(_data, c, pc);

    /// <summary>セーヴを振る。advantage なら d20 を2回振って良いほう(拘束の敏捷力のセーヴの不利と打ち消し合う)。</summary>
    public bool SavingThrow(Combatant c, Ability a, int dc, bool advantage = false)
    {
        bool helpless = c.Has(Condition.Paralyzed) || c.Has(Condition.Stunned) || c.Has(Condition.Unconscious) || c.Has(Condition.Petrified);
        if (helpless && a is Ability.Str or Ability.Dex) return false;   // 麻痺などでは筋力と敏捷力のセーヴに必ず失敗する
        int roll = D20();
        bool disadvantage = a == Ability.Dex && c.Has(Condition.Restrained);
        // クラスの能力のセーヴの有利(危険感知は、目が見えていれば敏捷力のセーヴ)
        if (FeatureChecks.SaveAdvantage(_data, c, a)) advantage = true;
        if (advantage != disadvantage)
        {
            int second = D20();
            int used = advantage ? Math.Max(roll, second) : Math.Min(roll, second);
            if (used == 1 && Lucky(c))   // 幸運: 使う方の 1 を振り直す
            {
                if (roll == 1 && (disadvantage || second == 1)) roll = D20(); else second = D20();
                used = advantage ? Math.Max(roll, second) : Math.Min(roll, second);
            }
            roll = used;
        }
        else if (roll == 1 && Lucky(c)) roll = D20();
        int total = roll + c.SaveBonus(a) + ModifierRoll(c, "save");
        // 鼓舞: 失敗しそうなら、もらったダイスを足す(足して成功するときだけ使う)
        if (total < dc && c.Modifiers.FirstOrDefault(m => m.Stat == "inspire") is { Dice: { } inspire } im && total + RollDice(inspire) is int lifted && lifted >= dc)
        {
            c.Modifiers.Remove(im);
            return true;
        }
        return total >= dc;
    }

    /// <summary>
    /// 状態にする。セーヴの失敗から来た状態で repeat_save なら、同じセーヴを毎ターンし直す。
    /// escape_dc が 0 なら唱えた人の呪文の難易度。repeat_save で ability があれば、そのセーヴ(パワー・ワード・スタン)。
    /// </summary>
    private void AddCondition(CombatState s, Combatant target, EffectPart part, EffectPart? fromSave, CastContext? ctx = null)
    {
        if (part.Condition is not { } cond || !target.Standing) return;
        if (target.Immune(cond)) return;
        // 魔法(呪文)ではその状態にならない(妖精の血: 呪文では眠らされない。アイバイトの眠りなども)
        if (ctx != null && FeatureChecks.MagicImmune(_data, target, cond)) return;
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
        // 無力状態になる状態(麻痺・朦朧・気絶・石化・無力)にされたら、集中は切れる(5版)
        if (target.Concentration != null && cond is Condition.Incapacitated or Condition.Paralyzed or Condition.Stunned or Condition.Unconscious or Condition.Petrified)
            EndConcentration(s, target);
    }

    /// <summary>HP に関わらず倒す(kill の chance)。パーティの人は死ぬ(気絶でなく)、敵は倒れ、呼んだ獣は消える。</summary>
    private void KillOutright(CombatState s, Combatant target, string message)
    {
        s.Add(message, target is MonsterUnit mu ? s.GroupOf(mu).DisplayName : target.Name);
        target.TempHp = 0;
        switch (target)
        {
            case MonsterUnit m:
                m.Hp = 0;
                Slain(s, m);
                break;
            case SummonUnit summon:
                summon.Hp = 0;
                s.Add("combat.summon_gone", summon.Name);
                s.Lingerings.RemoveAll(l => l.Unit == summon);
                break;
            case PartyMember p:
                p.Shape = null;
                p.ShapeHp = 0;
                p.Raging = false;
                EndConcentration(s, p);
                p.Conditions.Clear();
                p.Dots.Clear();
                p.Modifiers.Clear();
                Die(s, p);
                break;
        }
    }

    /// <summary>敵が倒れる(HP が 0 になった、一撃で倒された)。</summary>
    private void Slain(CombatState s, MonsterUnit m)
    {
        var g = s.GroupOf(m);
        g.Identified = true;
        EndConcentration(s, m);
        s.Defeated.Add(m);
        s.Add("combat.slain", g.DisplayName);
    }

    /// <summary>
    /// 一撃で倒す(instant_kill。Wizardry の忍者の首はね)。確率に当たり、ボスでなく(no_boss のとき)、敵の抵抗(resist_base・resist_die と CR)を
    /// 越えたら、HP に関わらず倒れる。この攻撃に当てはまる効果があって振ったら true(敵がダメージで倒れていても、振る機会は使ったことにする)。
    /// </summary>
    private bool TryInstantKill(CombatState s, PartyMember p, Combatant target, bool unarmed, bool melee)
    {
        if (target is not MonsterUnit m || FeatureChecks.InstantKill(_data, p, unarmed, melee) is not var (chance, e)) return false;
        if (!m.Standing || _rng.Next(0, 100) >= chance) return true;
        if (e.NoBoss && m.Scale.Boss) return true;
        if (e.ResistDie > 0 && _rng.Next(0, e.ResistDie) <= e.ResistBase + (int)Math.Floor(m.Def.Cr)) return true;
        m.TempHp = 0;
        m.Hp = 0;
        s.Add(e.Message ?? "combat.instant_kill", p.Name, s.GroupOf(m).DisplayName);
        Slain(s, m);
        return true;
    }

    /// <summary>ダメージを与える。抵抗などの倍率をかけ、HP が 0 になったら倒れる。与えたダメージ(一時的な HP で受けた分も含む)を返す。</summary>
    /// <summary>今与えているダメージの出どころ(ログのダメージの行の4つめに添える。おまけの分だけ。空なら添えない)。</summary>
    private object? _damageLabel;

    /// <summary>ダメージの行の引数(出どころがあれば4つめに添える)。</summary>
    private object[] DamageArgs(object who, int amount, string type) => _damageLabel == null ? [who, amount, type] : [who, amount, type, _damageLabel];

    /// <summary>部品を写して、出どころを付ける(武器やアイテムの定義の部品は書き換えない)。</summary>
    private static EffectPart Labeled(EffectPart part, object label) =>
        part.Type == EffectPart.Damage ? part.WithLabel(label) : part;

    public int Damage(CombatState s, Combatant target, int amount, string type, bool crit, bool magical)
    {
        int taken = DealDamage(s, target, amount, type, crit, magical);
        _damageDealt += Math.Max(0, taken);
        return taken;
    }

    private int DealDamage(CombatState s, Combatant target, int amount, string type, bool crit, bool magical)
    {
        if (target is PartyMember { Character.State: LifeState.Dead }) return 0;
        if (target.Halving) amount /= 2;   // アンキャニー・ドッジ、回避
        if (target.Deflect > 0 && DamageTypes.IsPhysical(type))
        {
            int cut = Math.Min(amount, target.Deflect);
            amount -= cut;
            target.Deflect = 0;
            s.Add("combat.deflect", target.Name, cut);
        }
        double mult = target.DamageMultiplier(type, magical);   // 装備・種族・呪文などの抵抗と脆弱(効かないなら 0)
        int dealt = (int)Math.Floor(amount * mult);
        if (dealt > 0) target.Conditions.RemoveAll(x => x.SpellId == "turn_undead");   // 退散は、ダメージを受けると解ける
        if (dealt > 0 && target is MonsterUnit { Def.RegenerateStop: { Count: > 0 } stops } regen && stops.Contains(type)) regen.RegenerationStopped = true;   // 次の番は再生しない
        int taken = dealt;   // 一時的な HP で受けた分も含めた、受けたダメージ(集中の判定に使う)
        bool allAbsorbed = false;
        if (dealt > 0 && target.TempHp > 0)
        {
            int absorbed = Math.Min(target.TempHp, dealt);
            target.TempHp -= absorbed;
            dealt -= absorbed;
            if (dealt == 0)
            {
                s.Add("combat.temp_absorb", target.Name, absorbed);
                allAbsorbed = true;
            }
        }
        // 集中: ダメージを受けたら耐久力のセーヴ(難易度は 10 か、受けたダメージの半分の大きい方。一時的な HP で受けても判定する)
        if (taken > 0 && target.Concentration != null && target.Hp - dealt > 0 && !SavingThrow(target, Ability.Con, Math.Max(10, taken / 2)))
            EndConcentration(s, target);
        if (allAbsorbed) return taken;
        // 獣の姿: 獣の HP から減り、0 になったら元の姿に戻って、あふれた分を受ける
        if (target is PartyMember shaped && shaped.Shape != null && dealt > 0)
        {
            if (dealt < shaped.ShapeHp)
            {
                shaped.ShapeHp -= dealt;
                s.Add("combat.damage", DamageArgs(target.Name, dealt, type));
                return taken;
            }
            s.Add("combat.damage", DamageArgs(target.Name, shaped.ShapeHp, type));
            dealt -= shaped.ShapeHp;
            shaped.Shape = null;
            shaped.ShapeHp = 0;
            s.Add("combat.shape_end", target.Name);
            if (dealt <= 0) return taken;
        }
        if (target is PartyMember dying && dying.Character.State == LifeState.Unconscious)
        {
            if (dealt <= 0) return 0;
            dying.Stable = false;
            dying.DeathFailures += crit ? 2 : 1;
            s.Add("combat.hit_down", target.Name);
            if (dying.DeathFailures >= 3) Die(s, dying);
            return 0;
        }
        int before = target.Hp;
        target.Hp = before - dealt;
        taken = taken - dealt + (before - target.Hp);   // HP は 0 より下がらないので、吸えるのは実際に減った分まで
        s.Add("combat.damage", DamageArgs(target is MonsterUnit tu ? s.GroupOf(tu).DisplayName : target.Name, dealt, type));
        if (target.Hp > 0) return taken;

        if (target is SummonUnit summon)
        {
            s.Add("combat.summon_gone", summon.Name);
            s.Lingerings.RemoveAll(l => l.Unit == summon);
            return taken;
        }

        if (target is MonsterUnit m)
        {
            // アンデッドの不屈: 光輝とクリティカル以外なら、耐久力のセーヴで HP 1 で踏みとどまる
            if (m.Def.Has("undead_fortitude") && type != "radiant" && !crit && SavingThrow(m, Ability.Con, 5 + dealt))
            {
                m.Hp = 1;
                s.Add("combat.endure", s.GroupOf(m).DisplayName);
                return taken;
            }
            Slain(s, m);
            return taken;
        }
        var p = (PartyMember)target;
        // 即死でなければ、踏みとどまる手を順に試す(stay_up)。不屈の激怒(バーバリアン L11)は激怒のあいだに耐久力のセーヴ(10、成功のたびに +5)、
        // 不屈の耐久(ハーフオーク)は大休憩までに1回。どちらかで HP 1 で踏みとどまる
        if (dealt - before < p.MaxHp)
        {
            foreach (var way in FeatureChecks.StayUp(_data, p))
            {
                bool up = way.Pool != null
                    ? ClassFeatures.Spend(_data, p.Character, way.Pool)
                    : SavingThrow(p, way.Ability!.Value, way.Dc + way.Step * p.StayUpSaves);
                if (!up) continue;
                if (way.Pool == null) p.StayUpSaves++;
                p.Hp = 1;
                if (way.Message != null) s.Add(way.Message, p.Name);
                return taken;
            }
        }
        p.Raging = false;
        EndConcentration(s, p);
        p.Conditions.Clear();
        p.Dots.Clear();
        p.Modifiers.Clear();
        if (dealt - before >= p.MaxHp) Die(s, p);   // 大きすぎるダメージは即死
        else
        {
            p.Character.State = LifeState.Unconscious;
            p.DeathSuccesses = p.DeathFailures = 0;
            p.Stable = false;
            s.Add("combat.down", p.Name);
        }
        return taken;
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
        if (r == 1 && Lucky(p)) r = D20();   // 幸運(ハーフリング)
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
            if (x.SaveAbility is { } a && SaveOrResist(s, c, a, x.Dc, magical: x.SpellId != null, SaveAdvantage(c, x.Condition))) end = true;
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
        foreach (var dot in c.Dots.ToList())
        {
            bool saved = dot.SaveAbility is { } a && SaveOrResist(s, c, a, dot.Dc, dot.Magical);
            if (saved || --dot.RoundsLeft <= 0)
            {
                c.Dots.Remove(dot);
                s.Add("combat.dot_end", c is MonsterUnit m ? s.GroupOf(m).DisplayName : c.Name, dot.DamageType);
            }
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
            EndForms(c);
            Lasting.Save(c);   // 毒など、長く続く状態だけを残す
            c.Conditions.Clear();
            c.Modifiers.Clear();
            c.Concentration = null;
        }
        // ACGRW の祝福(冒険者のクランが授かる): 倒した1体につき、生き残った人がそれぞれのレベルで HP を戻す(Lord B の呪いの迷宮では効かない)
        if (s.Defeated.Count > 0 && alive.Count > 0 && !_data.CurseOfLordB)
        {
            foreach (var p in alive) p.Character.Hp = Math.Min(p.Character.MaxHp, p.Character.Hp + s.Defeated.Count * BlessingPerFoe(p.Character.Level));
            s.Add("combat.acgrw_blessing", s.Defeated.Count);
        }
        if (alive.Count > 0) PartyGold.Share(alive.Select(p => p.Character).ToList(), gold);   // 金は生き残った人で分ける(宝箱やイベントと同じ)
        s.Reward = new CombatReward(each, gold, alive.Count);
        s.Add("combat.victory", each, gold);
    }

    /// <summary>ACGRW の祝福で、倒した1体あたりに戻る HP(L1〜4 は 1、L5 から 2、L11 から 3、L17 から 4、そのあとも 6 レベルごとに 1 増える)。</summary>
    public static int BlessingPerFoe(int level) => 1 + (Math.Max(1, level) + 1) / 6;

    /// <summary>金を持っている種類か(獣・不定形・植物は持たない)。</summary>
    public static bool CarriesTreasure(MonsterDef m) => m.CreatureType is not ("beast" or "ooze" or "plant" or "swarm");

    private int D20() => _rng.Roll(20);
    private int Roll(string dice) => DiceExpression.Parse(dice).Roll(_rng).Total;
}
