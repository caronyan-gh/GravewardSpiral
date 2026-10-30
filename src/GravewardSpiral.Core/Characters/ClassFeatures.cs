using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

/// <summary>クラスの技の相手。Self は自分、Ally は仲間を1人、EnemyGroup は敵のグループ(攻撃と同じ)、None は選ばない(全部の敵など)。</summary>
public enum FeatureTarget { Self, Ally, EnemyGroup, None }

/// <summary>
/// 戦闘で選ぶクラスの技(「クラスの技」のコマンド)。Pool は回数を数える名前(気のように、いくつかの技で分け合うものもある)、
/// Cost はその技で使う数。Pool が null なら回数を数えない(ディヴァイン・スマイトは呪文スロットを使う)。
/// BeastForm は獣の姿でも使えるか。中身は features.json の kind が action のもの(クラスと種族が features で指す)。
/// </summary>
public sealed record FeatureDef(string Id, FeatureTarget Target, string? Pool, int Cost = 1, bool BeastForm = true)
{
    public static FeatureDef Of(ClassFeatureDef d) => new(d.Id, d.Target, d.Pool, d.Cost, d.BeastForm) { Dice = d.Dice, Part = d.PartId };

    /// <summary>動きの部品の名前(ClassFeatures.ActionParts。データの part、省けば id)。</summary>
    public string Part { get; init; } = Id;

    /// <summary>技のダイス(セカンド・ウィンドの回復など)。</summary>
    public string? Dice { get; init; }
}

/// <summary>
/// クラスの能力(5版の SRD。サブクラスの能力は入れない)。マルチクラスでは5版どおり、どのクラスの能力も、そのクラスのレベルで働く
/// (決めたこと)。重ならないもの(追加攻撃は一番多いもの、鎧なしの守りは最初のクラス、ファイティングスタイルは1つ、オーラは一番高いもの)は重ねない。
/// 回数は Character.FeatureUses に使った数を持ち、大休憩で全部、小休憩で小休憩のものが戻る。
/// </summary>
public static class ClassFeatures
{
    /// <summary>
    /// 動きをコアが知っている技の部品の名前(戦闘で選ぶと CombatEngine が部品の名前で動かす)。kind が action の能力の part(省けば id)は、このどれかにする
    /// (回数・ダイス・数・獣の姿で使えるかはデータで変えられ、別の id で同じ部品を使えば派生の技になる)。新しい動きの技は、コアに部品を足すまで作れない。
    /// </summary>
    public static readonly IReadOnlySet<string> ActionParts = new HashSet<string>(StringComparer.Ordinal)
    {
        "second_wind", "action_surge", "rage", "lay_on_hands", "divine_smite", "flurry_of_blows", "patient_defense",
        "stunning_strike", "turn_undead", "bardic_inspiration", "wild_shape", "breath_weapon",
    };

    /// <summary>その id の技(features.json の kind が action のもの)。無ければ null。</summary>
    public static FeatureDef? Action(GameData data, string id) =>
        data.Features.TryGetValue(id, out var d) && d.Kind == "action" ? FeatureDef.Of(d) : null;

    /// <summary>その人のクラス(取った順)と種族の、回数と数え方の組(クラスはそのクラスのレベル、種族は合計のレベル)。</summary>
    private static IEnumerable<(List<PoolDef> Pools, int Level)> PoolSources(GameData data, Character c)
    {
        foreach (var id in c.ClassIds.Distinct())
            if (data.Classes.TryGetValue(id, out var cls)) yield return (cls.Pools, c.LevelIn(id));
        if (data.Races.TryGetValue(c.RaceId, out var race)) yield return (race.Pools, c.Level);
    }

    /// <summary>その人が、その回数を持つクラスか種族(クラスを取った順、そのあと種族。from_level に届いたもの)と、数えるレベル。</summary>
    private static IEnumerable<(PoolDef Pool, int Level)> PoolOwners(GameData data, Character c, string pool) =>
        PoolSources(data, c).SelectMany(x => x.Pools.Where(p => p.Id == pool && x.Level >= p.FromLevel).Select(p => (p, x.Level)));

    /// <summary>小休憩で戻る分の上限(小休憩で戻るクラスか種族の上限の一番大きいもの。short_with の能力を持てば、そのクラスも小休憩)。0 なら小休憩では戻らない。</summary>
    private static int ShortRechargeMax(GameData data, Character c, string pool)
    {
        return PoolOwners(data, c, pool)
            .Where(o => o.Pool.Recharge == "short" || (o.Pool.ShortWith != null && HasPassive(data, c, o.Pool.ShortWith)))
            .Select(o => o.Pool.Max.Eval(c, o.Level)).DefaultIfEmpty(0).Max();
    }

    /// <summary>そのクラスのレベル(持っていなければ 0)。</summary>
    public static int Level(Character c, string classId) => c.LevelIn(classId);

    /// <summary>そのクラスのレベルまでに得た能力値上昇の回数(クラスの progression の ability_increases。知らないクラスは 5版のふつうの表)。</summary>
    public static int AbilityIncreaseCount(GameData data, string classId, int level)
    {
        var at = data.Classes.GetValueOrDefault(classId)?.Progression?.AbilityIncreases ?? ProgressionDef.StandardAbilityIncreases;
        return at.Count(x => level >= x);
    }

    /// <summary>そのクラスを持っていて、レベルが足りているか。</summary>
    /// classId が race:<種族> なら、その種族で、合計のレベルが足りているか(種族の技)。
    public static bool Has(Character c, string classId, int level) =>
        classId.StartsWith("race:") ? c.RaceId == classId[5..] && c.Level >= level : level > 0 && Level(c, classId) >= level;

    /// <summary>その人が持っている戦闘の技(クラスを取った順に各クラスの features の並び、そのあと種族の技。並びがそのまま「クラスの技」の一覧の並び)。</summary>
    public static IEnumerable<FeatureDef> ActiveFor(GameData data, Character c) =>
        Owned(data, c).Where(x => x.Def.Kind == "action").Select(x => x.Def).DistinctBy(d => d.Id).Select(FeatureDef.Of);

    /// <summary>回数の上限(その回数を持つクラスか種族の上限の一番大きいもの。持たなければ 0)。</summary>
    public static int MaxUses(GameData data, Character c, string pool) =>
        PoolOwners(data, c, pool).Select(o => o.Pool.Max.Eval(c, o.Level)).DefaultIfEmpty(0).Max();

    /// <summary>その人が持つ回数(状態の画面に出す順。クラスを取った順に各クラスの pools の並び、そのあと種族の回数)。上限が 0 のものは出さない。</summary>
    public static IReadOnlyList<string> PoolsOf(GameData data, Character c) =>
        PoolSources(data, c).SelectMany(x => x.Pools.Select(p => p.Id)).Distinct().Where(p => MaxUses(data, c, p) > 0).ToList();

    public static int UsesLeft(GameData data, Character c, string pool) => Math.Max(0, MaxUses(data, c, pool) - c.FeatureUses.GetValueOrDefault(pool));

    /// <summary>回数を使う。足りなければ false(使わない)。</summary>
    public static bool Spend(GameData data, Character c, string pool, int n = 1)
    {
        if (UsesLeft(data, c, pool) < n) return false;
        c.FeatureUses[pool] = c.FeatureUses.GetValueOrDefault(pool) + n;
        return true;
    }

    /// <summary>その技を今使えるか(回数の残り。スマイトは呪文スロットの残り)。</summary>
    public static bool CanUse(GameData data, Character c, FeatureDef f)
    {
        if (!ActiveFor(data, c).Any(x => x.Id == f.Id)) return false;
        if (f.Part == "divine_smite") return SmiteSlot(data, c) != null;
        if (f.Part == "lay_on_hands") return f.Pool == null || UsesLeft(data, c, f.Pool) > 0;   // 1 点からでも使える
        if (f.Part == "flurry_of_blows" && !MartialArts(data, c)) return false;   // 鎧か盾を着けていると素手の連打は出ない(気だけ減らないように選べなくする)
        return f.Pool == null || UsesLeft(data, c, f.Pool) >= f.Cost;
    }

    /// <summary>
    /// 獣の姿でも使える技か。武器や素手の打撃に乗せる技(連打・朦朧打撃・スマイト)は、獣の攻撃では使えない
    /// (使っても気やスロットだけが減るので、選べないようにする)。
    /// </summary>
    public static bool UsableInBeastForm(FeatureDef f) => f.BeastForm;

    /// <summary>スマイトに使うスロット(4 レベルまでの、空いている一番上。5版ではそれより上でもダメージは 5d8 で止まる)。</summary>
    public static int? SmiteSlot(GameData data, Character c)
    {
        for (int lv = 4; lv >= 1; lv--)
            if (Spellcasting.SlotsLeft(data, c, lv) > 0) return lv;
        return null;
    }

    /// <summary>小休憩: 小休憩で戻る回数を戻す。ウィザードは秘術の回復でスロットを戻す(大休憩までに1回)。戻したスロットのレベルの合計を返す。</summary>
    public static int ShortRest(GameData data, Character c)
    {
        // 小休憩で戻る回数は、小休憩で戻るクラスの上限まで残りを満たす(1つのクラスだけなら全部戻る)
        foreach (var pool in c.FeatureUses.Keys.ToList())
        {
            int shortMax = ShortRechargeMax(data, c, pool);
            if (shortMax <= 0) continue;
            int max = MaxUses(data, c, pool);
            int used = Math.Min(c.FeatureUses[pool], max - Math.Min(shortMax, max));
            if (used <= 0) c.FeatureUses.Remove(pool);
            else c.FeatureUses[pool] = used;
        }
        return ArcaneRecovery(data, c);
    }

    /// <summary>
    /// 秘術の回復(ウィザード L1): 小休憩で、合わせてウィザードのレベルの半分(切り上げ)までのスロットを戻す。6 レベル以上のスロットは戻らない。
    /// 上のスロットから戻す。戻したレベルの合計を返す(使わなかったら 0 で、回数も使わない)。
    /// </summary>
    private static int ArcaneRecovery(GameData data, Character c)
    {
        if (UsesLeft(data, c, "arcane_recovery") <= 0) return 0;
        int budget = ArcaneRecoveryBudget(data, c), got = 0;
        for (int lv = Math.Min(5, budget); lv >= 1; lv--)
        {
            while (lv <= budget - got && c.SlotsUsed.GetValueOrDefault(lv) > 0)
            {
                c.SlotsUsed[lv]--;
                if (c.SlotsUsed[lv] == 0) c.SlotsUsed.Remove(lv);
                got += lv;
            }
        }
        if (got > 0) Spend(data, c, "arcane_recovery");
        return got;
    }

    /// <summary>
    /// 休息の歌(バード L2): 小休憩でヒットダイスを使って HP を戻した仲間は、さらに d6(L9 d8、L13 d10、L17 d12)戻る。
    /// パーティにバードが何人いても1回(一番高いもの)。戻った仲間の数を返す。
    /// </summary>
    public static int SongOfRest(GameData data, IEnumerable<(Character C, int DiceUsed)> rested, IEnumerable<Character> party, IRandom rng)
    {
        var singers = party.Where(p => p.CanAct && HasPassive(data, p, "song_of_rest")).ToList();
        if (singers.Count == 0) return 0;
        int die = singers.Max(p => (int)(PassiveValue(data, p, "song_of_rest", "die") ?? 0));
        if (die <= 0) return 0;
        int n = 0;
        foreach (var (c, used) in rested.Where(x => x.DiceUsed > 0 && x.C.IsAlive && x.C.Hp < x.C.MaxHp))
        {
            c.Hp = Math.Min(c.MaxHp, c.Hp + rng.Roll(die));
            n++;
        }
        return n;
    }

    /// <summary>
    /// その人が覚えている技と能力(クラスを取った順に、各クラスの features のうちレベルが届いたもの。そのあと種族の features で、合計のレベルが届いたもの)。
    /// Class は種族のものなら null。Level は数を数えるレベル(クラスはそのクラスのレベル、種族は合計のレベル)。同じ id を2か所から得ていれば両方を返す。
    /// </summary>
    public static IEnumerable<(ClassDef? Class, FeatureRef Ref, ClassFeatureDef Def, int Level)> Owned(GameData data, Character c)
    {
        foreach (var id in c.ClassIds.Distinct())
        {
            if (!data.Classes.TryGetValue(id, out var cls)) continue;
            int lv = c.LevelIn(id);
            foreach (var r in cls.Features)
                if (r.Level <= lv && data.Features.TryGetValue(r.Id, out var def)) yield return (cls, r, def, lv);
        }
        if (data.Races.TryGetValue(c.RaceId, out var race))
            foreach (var r in race.Features)
                if (r.Level <= c.Level && data.Features.TryGetValue(r.Id, out var def)) yield return (null, r, def, c.Level);
    }

    /// <summary>
    /// その人に働いている、勝手に効く能力の id(状態の画面に名前を出す。2つのクラスから得たものも1つ)。
    /// 重なり方が first のまとまり(鎧なしの防御など)は、効く1つ(先に取ったクラスのもの)だけを出す。
    /// </summary>
    public static IEnumerable<string> Passives(GameData data, Character c) =>
        Owned(data, c).Where(x => x.Def.Kind == "passive" && x.Def.Listed)
            .GroupBy(x => x.Def.Group ?? x.Def.Id)
            .SelectMany(g => g.First().Def.Stacks == "first" ? [g.First().Def.Id] : g.Select(x => x.Def.Id))
            .Distinct();

    /// <summary>
    /// その人の、勝手に効く能力の数(features.json の values の key)。持っていなければ null。2つのクラスから得ていれば、
    /// クラスのレベルが高いほう(同じなら先に取ったクラス)のレベルで数える(docs/class-data-design.md の「同じ技を 2 つのクラスから得たとき」)。
    /// </summary>
    public static double? PassiveValue(GameData data, Character c, string id, string key) => FeatureValue(data, c, id, key);

    /// <summary>その人の、技か能力の数(features.json の values の key)。持っていなければ null。数え方は PassiveValue と同じ。</summary>
    /// <remarks>mod を渡すと、数の ability をその修正で引く(戦闘の今の能力値)。</remarks>
    public static double? FeatureValue(GameData data, Character c, string id, string key, Func<Ability, int>? mod = null)
    {
        var from = Owned(data, c).Where(x => x.Def.Id == id).Select(x => (x.Def, x.Level)).ToList();
        if (from.Count == 0) return null;
        var best = from.OrderByDescending(x => x.Level).First();   // 同じレベルなら先に取ったクラス(並びの安定な並べ替え)
        return best.Def.Values.TryGetValue(key, out var n) ? n.EvalValue(c, best.Level, mod) : null;
    }

    /// <summary>その勝手に効く能力を持っているか(どのクラスから得たものでも)。</summary>
    public static bool HasPassive(GameData data, Character c, string id) =>
        Owned(data, c).Any(x => x.Def.Kind == "passive" && x.Def.Id == id);

    // ---------------- 勝手に効く能力の数字 ----------------

    /// <summary>秘術の回復で戻せるスロットのレベルの合計の上限(能力 arcane_recovery の budget。持っていなければ 0)。</summary>
    public static int ArcaneRecoveryBudget(GameData data, Character c) => (int)(FeatureValue(data, c, "arcane_recovery", "budget") ?? 0);

    /// <summary>
    /// その部品を使う、その人の技の id(クラスを取った順の最初のもの)。id を渡さない数の関数(状態の画面や記録)は、これで技を決める。持っていなければ null。
    /// </summary>
    public static string? ActionWithPart(GameData data, Character c, string part) =>
        Owned(data, c).Where(x => x.Def.Kind == "action" && x.Def.PartId == part).Select(x => x.Def.Id).FirstOrDefault();

    /// <summary>その技の数(id を渡せばその技、省けばその部品を使う最初の技)。</summary>
    private static double? ActionValue(GameData data, Character c, string? id, string part, string key) =>
        (id ?? ActionWithPart(data, c, part)) is { } x ? FeatureValue(data, c, x, key) : null;

    /// <summary>セカンド・ウィンドの回復の、ダイスに足す数(技の bonus。id を省けば部品 second_wind の技。持っていなければ 0)。</summary>
    public static int SecondWindBonus(GameData data, Character c, string? id = null) => (int)(ActionValue(data, c, id, "second_wind", "bonus") ?? 0);

    /// <summary>急所攻撃のダイス(d6)の数(能力 sneak_attack の dice。持っていなければ 0)。</summary>
    public static int SneakAttackDice(GameData data, Character c) => (int)(FeatureValue(data, c, "sneak_attack", "dice") ?? 0);

    /// <summary>アンデッド退散の難易度(その技をくれたクラスの呪文の難易度。id を省けば部品 turn_undead の技。持っていなければ 0)。</summary>
    public static int TurnUndeadDc(GameData data, Character c, string? id = null) =>
        Owned(data, c).FirstOrDefault(x => id != null ? x.Def.Id == id : x.Def.Kind == "action" && x.Def.PartId == "turn_undead") is { Class: { } cls } ? Spellcasting.SaveDc(cls, c) : 0;

    /// <summary>激怒のダメージの上乗せ(L1 +2、L9 +3、L16 +4)。</summary>
    public static int RageDamage(GameData data, Character c) => PassiveRules.Sum(data, c, "damage_bonus", "melee_str", new PassiveRules.Situation(Raging: true));

    /// <summary>クリティカルで足す近接の武器のダイスの数(残忍な一撃と、種族の獰猛な攻撃)。</summary>
    public static int BrutalCritical(GameData data, Character c) => PassiveRules.Sum(data, c, "crit_extra_dice", "melee");

    /// <summary>武術(モンク)の素手のダイス。</summary>
    public static int MartialArtsDie(GameData data, Character c) => (int)(PassiveValue(data, c, "martial_arts", "die") ?? 4);

    /// <summary>気の技の難易度(8 + 習熟 + 判断力)。</summary>
    public static int KiDc(GameData data, Character c, string? id = null) => (int)(ActionValue(data, c, id, "stunning_strike", "dc") ?? 0);

    /// <summary>鼓舞のダイス(L1 d6、L5 d8、L10 d10、L15 d12)。</summary>
    public static int InspirationDie(GameData data, Character c, string? id = null) => (int)(ActionValue(data, c, id, "bardic_inspiration", "die") ?? 0);

    /// <summary>アンデッドの破壊(クレリック L5〜): この脅威度以下のアンデッドは、退散に失敗すると滅びる。無ければ -1。</summary>
    public static double DestroyUndeadCr(GameData data, Character c) => PassiveValue(data, c, "destroy_undead", "cr") ?? -1;

    /// <summary>
    /// ワイルドシェイプでなれる変身先の id(技の shapes。その技をくれたクラスのレベル以下の一番大きなキーの一覧)。id を省けば部品 wild_shape の最初の技。
    /// 2つのクラスから得ていれば、クラスのレベルが高いほう。持っていなければ空。
    /// </summary>
    public static IReadOnlyList<string> WildShapeBeasts(GameData data, Character c, string? id = null)
    {
        id ??= ActionWithPart(data, c, "wild_shape");
        var from = Owned(data, c).Where(x => x.Def.Id == id).Select(x => (x.Def, x.Level))
            .OrderByDescending(x => x.Level).FirstOrDefault();
        if (from.Def == null) return [];
        return from.Def.Shapes.Select(kv => (Lv: int.TryParse(kv.Key, out int n) ? n : 99, Ids: kv.Value))
            .Where(x => x.Lv <= from.Level).OrderByDescending(x => x.Lv).Select(x => x.Ids).FirstOrDefault() ?? [];
    }

    /// <summary>ファイティングスタイルを持っているか(まとまり fighting_style の能力を1つでも)。</summary>
    public static bool HasFightingStyle(GameData data, Character c) =>
        Owned(data, c).Any(x => x.Def.Kind == "passive" && x.Def.Group == "fighting_style");

    /// <summary>飛び道具で撃つ攻撃に、命中の上乗せ(弓術。attack_bonus の ranged_shot)が付く能力を持っているか(装備は問わない)。</summary>
    public static bool ArcheryOnShot(GameData data, Character c) =>
        Owned(data, c).Any(x => x.Def.Kind == "passive" && x.Def.Effects.Any(e => e.Type == "attack_bonus" && e.On == "ranged_shot"));

    /// <summary>
    /// 状態の画面に出す、今の装備で働くファイティングスタイル(まとまり fighting_style の能力で、効く場面に当たっているものの id)。
    /// 持っていない能力や、装備が合わない能力は出さない。利き手の装備の形で決まるもの(grip:)を先に、弓術などをあとに並べる。
    /// </summary>
    public static IReadOnlyList<string> ActiveStyles(GameData data, Character c) =>
        PassiveRules.Effects(data, c).Where(e => e.Def.Group == "fighting_style").Select(e => e.Def).DistinctBy(d => d.Id)
            .OrderBy(d => d.When.StartsWith("grip:") ? 0 : 1).Select(d => d.Id).ToList();

    /// <summary>元の武器(魔法の武器は base の品)。</summary>
    public static ItemDef BaseWeapon(GameData data, ItemDef def) => def.Base != null && data.Items.TryGetValue(def.Base, out var b) ? b : def;

    /// <summary>モンク武器(5版: ショートソードと、両手持ち・重いでない単純な近接武器)。素手は null で、これも含む。</summary>
    public static bool MonkWeapon(GameData data, ItemDef? def)
    {
        if (def == null) return true;
        var b = BaseWeapon(data, def);
        return !b.Ranged && (b.Id == "shortsword" || (b.Category == "simple" && !b.Has("two_handed") && !b.Has("heavy")));
    }

    /// <summary>武術が働くか(武術の能力を持っていて、鎧も盾も着けていない)。</summary>
    public static bool MartialArts(GameData data, Character c) =>
        HasPassive(data, c, "martial_arts") && c.Inventory[EquipSlot.Armor] == null
        && !(c.Inventory[EquipSlot.OffHand] is { } off && data.Items.TryGetValue(off.ItemId, out var d) && d.Kind == ItemKind.Shield);

    /// <summary>
    /// 呪文を唱えられる姿か(no_cast が効いていない)。激怒のあいだは唱えられない(能力 rage_limits)。獣の姿も唱えられない(能力 wild_shape_limits)が、
    /// 獣の呪文(ドルイド L18)があれば外れる(unless)。
    /// </summary>
    public static bool CanCastIn(GameData data, Character c, bool raging, bool shaped) =>
        !Blocking(data, c, "no_cast", new PassiveRules.Situation(Raging: raging, Shaped: shaped)).Any();

    /// <summary>その種類の制限(no_cast・no_concentration)で今効いているもの(unless の能力を持っていれば外す)。</summary>
    public static IEnumerable<PassiveRules.Active> Blocking(GameData data, Character c, string type, PassiveRules.Situation now) =>
        PassiveRules.Effects(data, c, type, now).Where(e => e.Effect.Unless is not { } u || !HasPassive(data, c, u));

    /// <summary>回避(ローグ L7、モンク L7): 敏捷力のセーヴで半分になる効果を、成功で 0、失敗で半分にする。</summary>
    public static bool Evasion(GameData data, Character c) => PassiveRules.Effects(data, c, "evasion").Any();
}
