using System.Globalization;
using System.Text;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Characters;

/// <summary>
/// クラスのエンジンとデータの分離(docs/class-data-design.md)の Step 1: 今のクラスと種族の動きを、全部書き出して固定する。
/// 全クラス × 全レベル、マルチクラスの組、種族ごとに、技・能力・回数・小休憩で戻るもの・能力の数字・呪文の数とスロット・選べる呪文・
/// おまかせで選ぶ呪文を、能力値の2つの組で書き出し、Characters/Records/class_behavior.txt と比べる。
/// 分離の各段で中身が同じなら、この記録は変わらない。決めた動きの変更や不具合の直しで変わるときは、理由を書いてから
/// 環境変数 GW_UPDATE_RECORDS=1 で書き直す。
/// </summary>
public class ClassBehaviorRecordTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData DefaultData = GameData.Load(Content);

    /// <summary>書き出しに使うデータ(ふつうは SRD。写したクラスの比べ方では、試しのパックを重ねたもの)。</summary>
    private static readonly AsyncLocal<GameData?> UsingData = new();
    private static GameData Data => UsingData.Value ?? DefaultData;

    /// <summary>能力値の組の名前と値(写したクラスの比べ方でも使う)。</summary>
    internal static IReadOnlyList<(string Name, int[] Scores)> ProfileList => Profiles;

    /// <summary>
    /// data のもとで、その人を作って、記録と同じ行を書き出す(写したクラスの比べ方に使う。Step 7)。
    /// </summary>
    internal static string Describe(GameData data, string profile, int[] scores, string race, (string Id, int Level)[] levels)
    {
        UsingData.Value = data;
        try
        {
            var sb = new StringBuilder();
            Write(sb, profile, Make(race, scores, levels));
            return sb.ToString();
        }
        finally { UsingData.Value = null; }
    }

    private static string RecordPath =>
        Path.Combine(TestPaths.Root, "tests", "GravewardSpiral.Tests", "Characters", "Records", "class_behavior.txt");

    /// <summary>能力値の組。上は筋力が高く魅力が低い(下限の min を見る)、下はその逆。</summary>
    private static readonly (string Name, int[] Scores)[] Profiles =
    [
        ("str", [18, 16, 14, 12, 10, 8]),
        ("cha", [8, 10, 12, 14, 16, 18]),
    ];

    /// <summary>マルチクラスの組(先に書いたクラスを先に取る)。</summary>
    private static readonly (string Id, int Level)[][] Multis =
    [
        [("fighter", 5), ("wizard", 5)],
        [("wizard", 5), ("fighter", 5)],
        [("monk", 5), ("barbarian", 5)],
        [("barbarian", 5), ("monk", 5)],
        [("paladin", 6), ("sorcerer", 4)],
        [("cleric", 3), ("wizard", 3)],
        [("warlock", 5), ("paladin", 2)],
        [("ranger", 4), ("druid", 4)],
        [("bard", 5), ("rogue", 7)],
        [("rogue", 7), ("monk", 7)],
        [("fighter", 2), ("ranger", 2), ("paladin", 2)],
        [("sorcerer", 3), ("warlock", 3), ("bard", 3)],
    ];

    private static readonly int[] RaceLevels = [1, 5, 11, 17, 20];

    /// <summary>種族の特性の id の全部(どの種族のものでも)。</summary>
    private static readonly string[] AllTraits = Data.Races.Values.SelectMany(r => r.Traits).Distinct().Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheClassAndRaceBehaviorMatchesTheRecord()
    {
        string now = Build();
        if (Environment.GetEnvironmentVariable("GW_UPDATE_RECORDS") == "1" || !File.Exists(RecordPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath)!);
            File.WriteAllText(RecordPath, now, new UTF8Encoding(false));
        }
        string was = File.ReadAllText(RecordPath).ReplaceLineEndings("\n");
        if (was == now) return;
        // 最初に違う行を出す(全部を出すと長すぎる)
        var a = was.Split('\n');
        var b = now.Split('\n');
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        Assert.Fail($"クラスの動きが記録と違う({i + 1} 行目)\n記録: {(i < a.Length ? a[i] : "(無し)")}\n今  : {(i < b.Length ? b[i] : "(無し)")}");
    }

    /// <summary>全部の書き出し。</summary>
    private static string Build()
    {
        var sb = new StringBuilder();
        var classes = Data.Classes.Keys.Order(StringComparer.Ordinal).ToList();
        foreach (var (pname, scores) in Profiles)
        {
            foreach (var cls in classes)
                for (int lv = 1; lv <= 20; lv++)
                    Write(sb, pname, Make("human", scores, [(cls, lv)]));
            foreach (var multi in Multis)
                Write(sb, pname, Make("human", scores, multi));
            foreach (var race in Data.Races.Keys.Order(StringComparer.Ordinal))
                foreach (int lv in RaceLevels)
                    Write(sb, pname, Make(race, scores, [("fighter", lv)]));
        }
        // クラスで選べる呪文の全部(呪文のレベルごと)。ClassSpells はこれをクラスの呪文の上限レベルで切る
        foreach (var cls in classes.Select(id => Data.Classes[id]).Where(c => Spellcasting.Kind(c) != CasterKind.None))
        {
            var top = Make("human", Profiles[0].Scores, [(cls.Id, 20)]);
            sb.Append("spells ").Append(cls.Id).Append(" cantrips: ")
              .AppendJoin(",", Spellcasting.ClassSpells(Data, cls, top, cantrips: true).Select(s => s.Id)).Append('\n');
            foreach (var g in Spellcasting.ClassSpells(Data, cls, top, cantrips: false).GroupBy(s => s.Level).OrderBy(g => g.Key))
                sb.Append("spells ").Append(cls.Id).Append(' ').Append(g.Key).Append(": ").AppendJoin(",", g.Select(s => s.Id)).Append('\n');
        }
        return sb.ToString();
    }

    private static Character Make(string race, int[] scores, (string Id, int Level)[] levels)
    {
        var c = new Character
        {
            Id = "t",
            Name = "T",
            RaceId = race,
            ClassId = levels[0].Id,
            Level = levels.Sum(x => x.Level),
            Classes = levels.Select(x => new ClassLevel { ClassId = x.Id, Level = x.Level }).ToList(),
        };
        var all = Abilities.All.ToList();
        for (int i = 0; i < all.Count; i++) c.Scores[all[i]] = scores[i];
        Spellcasting.AutoPick(Data, c);
        return c;
    }

    private static void Write(StringBuilder sb, string profile, Character c)
    {
        string who = $"{profile} {c.RaceId} " + string.Join("/", c.Classes.Select(x => $"{x.ClassId}{x.Level}"));
        void Line(string key, string value) => sb.Append(who).Append(" | ").Append(key).Append(": ").Append(value).Append('\n');

        Line("actions", string.Join(",", ClassFeatures.ActiveFor(Data, c).Select(f => f.Id)));
        Line("passives", string.Join(",", ClassFeatures.Passives(Data, c)));
        Line("traits", string.Join(",", AllTraits.Where(t => c.HasTrait(Data, t))));
        Line("pools", string.Join(",", ClassFeatures.PoolsOf(Data, c).Select(p => (p, n: ClassFeatures.MaxUses(Data, c, p))).Select(x => $"{x.p}={x.n}")));
        Line("short_rest", ShortRestPools(c));
        Line("numbers", string.Join(" ", new[]
        {
            $"attacks={CombatEngine.AttacksPerAction(Data, c)}",
            $"asi={c.AbilityPoints(Data)}",
            $"prof={c.ProficiencyBonus}",
            $"ac={c.ArmorClass(Data)}",
            $"rage_dmg={ClassFeatures.RageDamage(Data, c)}",
            $"brutal={ClassFeatures.BrutalCritical(Data, c)}",
            $"ma_die={ClassFeatures.MartialArtsDie(Data, c)}",
            $"ki_dc={ClassFeatures.KiDc(Data, c)}",
            $"insp_die={ClassFeatures.InspirationDie(Data, c)}",
            $"destroy_cr={ClassFeatures.DestroyUndeadCr(Data, c).ToString(CultureInfo.InvariantCulture)}",
            $"beasts={string.Join("+", ClassFeatures.WildShapeBeasts(Data, c))}",
            $"style={ClassFeatures.HasFightingStyle(Data, c)}/{string.Join("+", ClassFeatures.ActiveStyles(Data, c))}",
            $"evasion={ClassFeatures.Evasion(Data, c)}",
            $"martial={ClassFeatures.MartialArts(Data, c)}",
            $"cast_raging={ClassFeatures.CanCastIn(Data, c, raging: true, shaped: false)}",
            $"cast_shaped={ClassFeatures.CanCastIn(Data, c, raging: false, shaped: true)}",
            $"smite={ClassFeatures.SmiteSlot(Data, c)?.ToString() ?? "-"}",
        }));
        Line("defense", Defense(c));
        Line("combat", CombatChecks(c));
        Line("styles", Styles(c));
        Line("onhit", OnHit(c));
        Line("react", Reactions(c));
        Line("race", RaceChecks(c));
        Line("trap", Traps(c));
        Line("action_nums", $"second_wind_bonus={ClassFeatures.SecondWindBonus(Data, c)} sneak_d6={ClassFeatures.SneakAttackDice(Data, c)}"
            + $" arcane_budget={ClassFeatures.ArcaneRecoveryBudget(Data, c)} turn_dc={ClassFeatures.TurnUndeadDc(Data, c)}");
        var casters = Spellcasting.CasterClasses(Data, c).ToList();
        if (casters.Count == 0) return;
        var any = casters[0];
        Line("slots", $"caster_lv={Spellcasting.CasterLevel(Data, c)} " + string.Join(",", Enumerable.Range(1, 9).Select(l => Spellcasting.MaxSlots(Data, c, l)))
            + $" regular={string.Join(",", Enumerable.Range(1, 9).Select(l => Spellcasting.RegularSlots(Data, c, l)))}"
            + $" pact={(Spellcasting.HasPactSlots(Data, c) ? $"{Spellcasting.PactSlots(Data, c)}@{Spellcasting.PactSlotLevel(Data, c)}" : "-")}"
            + $" max_lv={Spellcasting.MaxSpellLevel(Data, c)}");
        foreach (var cls in casters)
        {
            var b = c.Book(cls.Id);
            Line("cast " + cls.Id, string.Join(" ", new[]
            {
                $"kind={Spellcasting.Kind(cls)}",
                $"cantrips={Spellcasting.CantripsKnown(cls, c)}",
                $"known={Spellcasting.SpellsKnown(cls, c)}",
                $"prepared={Spellcasting.PreparedCount(cls, c)}",
                $"class_max={Spellcasting.ClassMaxSpellLevel(Data, cls, c)}",
                $"dc={Spellcasting.SaveDc(cls, c)}",
                $"atk={Spellcasting.AttackBonus(cls, c)}",
            }));
            Line("book " + cls.Id, $"cantrips={string.Join(",", b.Cantrips)} known={string.Join(",", b.Known)} prepared={string.Join(",", b.Prepared)}");
        }
        Line("castable", string.Join(",", Spellcasting.Castable(Data, c).Select(s => $"{s.Id}@{Spellcasting.ClassFor(Data, c, s.Id)?.Id}")));
    }

    /// <summary>
    /// 守りの能力: セーヴの習熟、戦闘で効かない状態、毒のダメージ、オーラでセーヴに足す値(立っているときと倒れたとき)。
    /// 戦闘の1人のパーティ(仲間は自分だけ)として見る。
    /// </summary>
    private static string Defense(Character c)
    {
        var copy = new Character { RaceId = c.RaceId, ClassId = c.ClassId, Level = c.Level, Classes = c.Classes, Scores = c.Scores, MaxHp = 10, Hp = 10 };
        var pm = new PartyMember(copy, Data);
        pm.Allies = [pm];
        var saves = Abilities.All.Where(a => copy.SaveProficient(Data, a)).Select(a => a.Code()).ToList();
        var immune = new[] { Condition.Poisoned, Condition.Frightened, Condition.Charmed }.Where(pm.ImmuneTo).Select(x => x.ToString().ToLowerInvariant()).ToList();   // 倒れる前に数える(オーラは立っているときだけ)
        var traits = typeof(Combatant).GetMethod("DamageTraits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var (poisonImmune, poisonResist, _) = ((bool, bool, bool))traits.Invoke(pm, ["poison", false])!;
        var (fireImmune, fireResist, _) = ((bool, bool, bool))traits.Invoke(pm, ["fire", false])!;
        int aura = pm.SaveBonus(Ability.Wis) - copy.SaveBonus(Data, Ability.Wis);
        copy.Hp = 0;
        int auraDown = pm.SaveBonus(Ability.Wis) - copy.SaveBonus(Data, Ability.Wis);
        return $"saves={string.Join("+", saves)} immune={string.Join("+", immune)} poison={(poisonImmune ? "immune" : poisonResist ? "resist" : "-")} fire={(fireImmune ? "immune" : fireResist ? "resist" : "-")} aura={aura} aura_down={auraDown}";
    }

    /// <summary>
    /// 戦闘でのクラスの能力の判定: イニシアチブの有利、攻撃の有利(激怒の有無 × 近接か)、自分への攻撃の有利が消えるか、
    /// 敏捷力のセーヴの有利(目が見えるか)、回避(行動不能か)。相手はクラスの能力の無い戦士 1。
    /// </summary>
    private static string CombatChecks(Character c)
    {
        PartyMember Pm(Character ch) => new(new Character
        {
            RaceId = ch.RaceId, ClassId = ch.ClassId, Level = ch.Level, Classes = ch.Classes, Scores = ch.Scores, MaxHp = 10, Hp = 10,
        }, Data);
        var me = Pm(c);
        var plain = Pm(new Character { RaceId = "human", ClassId = "fighter", Level = 1, Classes = [new ClassLevel { ClassId = "fighter", Level = 1 }] });
        string Atk(bool raging, bool melee)
        {
            me.Raging = raging;
            var (a, _) = FeatureChecks.Attack(Data, me, plain, melee);
            var (d, cancel) = FeatureChecks.Attack(Data, plain, me, melee);
            me.Raging = false;
            return $"{a}/{d}{(cancel ? "x" : "")}";
        }
        var parts = new List<string>
        {
            $"init={FeatureChecks.InitiativeAdvantage(Data, me)}",
            $"atk_calm_melee={Atk(false, true)}",
            $"atk_rage_melee={Atk(true, true)}",
            $"atk_rage_ranged={Atk(true, false)}",
            $"dex_save={FeatureChecks.SaveAdvantage(Data, me, Ability.Dex)}",
            $"evade={FeatureChecks.Evades(Data, me)}",
        };
        me.Conditions.Add(new ActiveCondition { Condition = Condition.Blinded });
        parts.Add($"dex_save_blind={FeatureChecks.SaveAdvantage(Data, me, Ability.Dex)}");
        me.Conditions.Clear();
        me.Conditions.Add(new ActiveCondition { Condition = Condition.Stunned });
        parts.Add($"evade_stunned={FeatureChecks.Evades(Data, me)}");
        var (_, stunnedCancel) = FeatureChecks.Attack(Data, plain, me, true);
        parts.Add($"elusive_stunned={stunnedCancel}");
        return string.Join(" ", parts);
    }

    /// <summary>装備の組(名前と、利き手・もう片方の手・遠隔の枠・鎧)。ファイティングスタイルの記録に使う。</summary>
    private static readonly (string Name, string? Main, string? Off, string? Ranged, string? Armor)[] Loadouts =
    [
        ("none", null, null, null, null),
        ("mail", null, null, null, "chain_mail"),
        ("sword_shield", "longsword", "shield", null, "chain_mail"),
        ("great", "greatsword", null, null, "chain_mail"),
        ("versatile", "longsword", null, null, null),
        ("dual", "shortsword", "shortsword", null, null),
        ("bow", null, null, "longbow", null),
        ("sword_bow", "longsword", "shield", "longbow", "chain_mail"),
    ];

    /// <summary>
    /// ファイティングスタイル: 装備の組ごとに、AC・働くスタイル・状態の画面に出すスタイル・二刀流・弓術の命中・決闘のダメージ・大型武器の振り直し。
    /// </summary>
    private static string Styles(Character c)
    {
        var parts = new List<string>();
        foreach (var (name, main, off, ranged, armor) in Loadouts)
        {
            var copy = new Character { RaceId = c.RaceId, ClassId = c.ClassId, Level = c.Level, Classes = c.Classes, Scores = c.Scores, MaxHp = 10, Hp = 10 };
            var pm = new PartyMember(copy, Data);
            void Put(EquipSlot slot, string? id)
            {
                if (id != null) copy.Inventory.Equipped[slot] = new Core.Items.ItemInstance { ItemId = id, Identified = true };
            }
            Put(EquipSlot.MainHand, main);
            Put(EquipSlot.OffHand, off);
            Put(EquipSlot.Ranged, ranged);
            Put(EquipSlot.Armor, armor);
            parts.Add($"{name}:{copy.ArmorClass(Data)}/{string.Join("+", ClassFeatures.ActiveStyles(Data, copy))}"
                + $"/{(FeatureChecks.OffhandAbilityDamage(Data, pm) ? "off" : "-")}/{FeatureChecks.RangedShotHitBonus(Data, pm)}"
                + $"/{FeatureChecks.MainMeleeDamageBonus(Data, pm)}/{FeatureChecks.MainMeleeRerollLow(Data, pm)}");
        }
        return string.Join(" ", parts);
    }

    /// <summary>当たったときのクラスの能力: 近接の武器で足すダメージ、吸う割合、クリティカルで足すダイス、素手が魔法か(鎧なし)、エルドリッチ・ブラストに足すダメージ。</summary>
    private static string OnHit(Character c)
    {
        var copy = new Character { RaceId = c.RaceId, ClassId = c.ClassId, Level = c.Level, Classes = c.Classes, Scores = c.Scores, MaxHp = 10, Hp = 10 };
        var pm = new PartyMember(copy, Data);
        return $"extra={string.Join("+", FeatureChecks.MeleeWeaponExtraDamage(Data, pm).Select(x => $"{x.Dice}:{x.Type}"))}"
            + $" drain={FeatureChecks.MeleeWeaponDrain(Data, pm)} crit_dice={FeatureChecks.MeleeCritExtraDice(Data, pm)}"
            + $" unarmed_magic={FeatureChecks.UnarmedMagical(Data, pm)} eb={FeatureChecks.SpellDamageBonus(Data, pm, "eldritch_blast")}"
            + $" fire_bolt={FeatureChecks.SpellDamageBonus(Data, pm, "fire_bolt")}";
    }

    /// <summary>
    /// 宝箱の罠(9 種)を、その人が開けたときの結果(残りの HP・付いた状態・死んだか)。HP 30、罠の等級 2、決まった乱数で 3 回ずつ。
    /// 仲間に、魔術師と僧侶の両方を狙われるクラスを持たない人間のファイターを1人置く(全員に当たる罠の当たり方も見る)。
    /// </summary>
    private static string Traps(Character c)
    {
        var parts = new List<string>();
        foreach (var (trap, n) in Chest.Names(Data).Where(t => t != "").Select((t, i) => (t, i + 1)))
        {
            var runs = new List<string>();
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var me = new Character { Id = "t", Name = "T", RaceId = c.RaceId, ClassId = c.ClassId, Level = c.Level, Classes = c.Classes, Scores = c.Scores, MaxHp = 30, Hp = 30 };
                var ally = new Character { Id = "a", Name = "A", RaceId = "human", ClassId = "fighter", Level = 1, Classes = [new ClassLevel { ClassId = "fighter", Level = 1 }], MaxHp = 30, Hp = 30 };
                var chest = new Chest { Trap = trap, Tier = 2 };
                var r = chest.Trigger(Data, [me, ally], me, new Core.Rng.Pcg32(seed, (ulong)n));
                string Of(Character x) => x.State == LifeState.Dead ? "dead" : $"{x.Hp}{string.Concat(x.Lasting.Select(l => "+" + l.Condition.ToString().ToLowerInvariant()))}";
                runs.Add($"{Of(me)}/{Of(ally)}{(r.Teleport ? "/tp" : "")}{(r.Alarm ? "/alarm" : "")}");
            }
            parts.Add($"{trap}={string.Join(",", runs)}");
        }
        return string.Join(" ", parts);
    }

    /// <summary>種族の守り(状態ごとのセーヴの有利、魔法へのセーヴの有利、幸運、魔法でならない状態)。</summary>
    private static string RaceChecks(Character c)
    {
        var pm = new PartyMember(c, Data);
        var conds = Enum.GetValues<Core.Effects.Condition>();
        string against = string.Join(",", conds.Where(x => FeatureChecks.SaveAdvantageAgainst(Data, pm, x)));
        string magic = string.Join(",", Enum.GetValues<Ability>().Where(a => FeatureChecks.MagicSaveAdvantage(Data, pm, a)));
        string immune = string.Join(",", conds.Where(x => FeatureChecks.MagicImmune(Data, pm, x)));
        return $"adv_against={against} magic_save_adv={magic} lucky={FeatureChecks.Lucky(Data, pm)} magic_immune={immune}";
    }

    /// <summary>反応(近接と飛び道具)と、倒れかけたときのセーヴ(激怒していないときと、しているとき)。</summary>
    private static string Reactions(Character c)
    {
        var copy = new Character { RaceId = c.RaceId, ClassId = c.ClassId, Level = c.Level, Classes = c.Classes, Scores = c.Scores, MaxHp = 10, Hp = 10 };
        var pm = new PartyMember(copy, Data);
        static string R(FeatureChecks.Reaction? r) => r == null ? "-" : r.Halve ? $"halve:{r.Message}" : $"cut:{r.Dice}+{r.Ability}+{r.Bonus}:{r.Message}";
        static string S(IReadOnlyList<FeatureChecks.StayUpWay> ways) => ways.Count == 0 ? "-"
            : string.Join(",", ways.Select(w => w.Pool != null ? $"pool:{w.Pool}:{w.Message}" : $"{w.Ability}:{w.Dc}+{w.Step}:{w.Message}"));
        string calm = S(FeatureChecks.StayUp(Data, pm));
        pm.Raging = true;
        string raging = S(FeatureChecks.StayUp(Data, pm));
        return $"melee={R(FeatureChecks.ReactionFor(Data, pm, false))} ranged={R(FeatureChecks.ReactionFor(Data, pm, true))} stay={calm} stay_raging={raging}";
    }

    /// <summary>小休憩で戻る回数(全部を1回使ったことにして、小休憩のあとに消えたもの)。秘術の回復は別に数えるので除く。</summary>
    private static string ShortRestPools(Character c)
    {
        var pools = ClassFeatures.PoolsOf(Data, c).Where(p => p != "arcane_recovery").ToList();
        var copy = new Character { RaceId = c.RaceId, ClassId = c.ClassId, Level = c.Level, Classes = c.Classes, Scores = c.Scores };
        foreach (var p in pools) copy.FeatureUses[p] = 1;
        ClassFeatures.ShortRest(Data, copy);
        return string.Join(",", pools.Where(p => !copy.FeatureUses.ContainsKey(p)));
    }
}
