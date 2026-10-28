using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

/// <summary>ファイティングスタイル(持っている人は、装備に合うものが1つ自動で働く)。</summary>
public enum FightingStyle { None, Archery, Defense, Dueling, GreatWeapon, TwoWeapon }

/// <summary>クラスの技の相手。Self は自分、Ally は仲間を1人、EnemyGroup は敵のグループ(攻撃と同じ)、None は選ばない(全部の敵など)。</summary>
public enum FeatureTarget { Self, Ally, EnemyGroup, None }

/// <summary>
/// 戦闘で選ぶクラスの技(「クラスの技」のコマンド)。Pool は回数を数える名前(気のように、いくつかの技で分け合うものもある)、
/// Cost はその技で使う数。Pool が null なら回数を数えない(ディヴァイン・スマイトは呪文スロットを使う)。
/// </summary>
public sealed record FeatureDef(string Id, string ClassId, int MinLevel, FeatureTarget Target, string? Pool, int Cost = 1);

/// <summary>
/// クラスの能力(5版の SRD。サブクラスの能力は入れない)。マルチクラスでは5版どおり、どのクラスの能力も、そのクラスのレベルで働く
/// (決めたこと)。重ならないもの(追加攻撃は一番多いもの、鎧なしの守りは最初のクラス、ファイティングスタイルは1つ、オーラは一番高いもの)は重ねない。
/// 回数は Character.FeatureUses に使った数を持ち、大休憩で全部、小休憩で小休憩のものが戻る。
/// </summary>
public static class ClassFeatures
{
    /// <summary>戦闘で選ぶ技。並びがそのまま「クラスの技」の一覧の並び。</summary>
    public static readonly IReadOnlyList<FeatureDef> Active =
    [
        new("second_wind", "fighter", 1, FeatureTarget.Self, "second_wind"),
        new("action_surge", "fighter", 2, FeatureTarget.EnemyGroup, "action_surge"),
        new("rage", "barbarian", 1, FeatureTarget.EnemyGroup, "rage"),
        new("lay_on_hands", "paladin", 1, FeatureTarget.Ally, "lay_on_hands", 0),
        new("divine_smite", "paladin", 2, FeatureTarget.EnemyGroup, null),
        new("flurry_of_blows", "monk", 2, FeatureTarget.EnemyGroup, "ki"),
        new("patient_defense", "monk", 2, FeatureTarget.EnemyGroup, "ki"),
        new("stunning_strike", "monk", 5, FeatureTarget.EnemyGroup, "ki"),
        new("turn_undead", "cleric", 2, FeatureTarget.None, "channel_divinity"),
        new("bardic_inspiration", "bard", 1, FeatureTarget.Ally, "bardic_inspiration"),
        new("wild_shape", "druid", 2, FeatureTarget.Self, "wild_shape"),
        new("breath_weapon", "race:dragonborn", 1, FeatureTarget.EnemyGroup, "breath_weapon"),   // 種族の技(クラスの代わりに race:<種族>)
    ];

    /// <summary>小休憩で戻る回数(ほかは大休憩で戻る)。呪歌の泉(バード L5)からは鼓舞も小休憩で戻る。</summary>
    private static bool ShortRecharge(Character c, string pool) => pool switch
    {
        "second_wind" or "action_surge" or "ki" or "channel_divinity" or "wild_shape" or "breath_weapon" => true,
        "bardic_inspiration" => Level(c, "bard") >= 5,
        _ => false,
    };

    /// <summary>そのクラスのレベル(持っていなければ 0)。</summary>
    public static int Level(Character c, string classId) => c.LevelIn(classId);

    /// <summary>そのクラスのレベルまでに得た能力値上昇の回数(5版どおり。ファイターは 6・14、ローグは 10 にもある)。</summary>
    public static int AbilityIncreaseCount(string classId, int level)
    {
        int[] at = classId switch
        {
            "fighter" => [4, 6, 8, 12, 14, 16, 19],
            "rogue" => [4, 8, 10, 12, 16, 19],
            _ => [4, 8, 12, 16, 19],
        };
        return at.Count(x => level >= x);
    }

    /// <summary>そのクラスを持っていて、レベルが足りているか。</summary>
    /// classId が race:<種族> なら、その種族で、合計のレベルが足りているか(種族の技)。
    public static bool Has(Character c, string classId, int level) =>
        classId.StartsWith("race:") ? c.RaceId == classId[5..] && c.Level >= level : level > 0 && Level(c, classId) >= level;

    /// <summary>その人が持っている戦闘の技。</summary>
    public static IEnumerable<FeatureDef> ActiveFor(Character c) => Active.Where(f => Has(c, f.ClassId, f.MinLevel));

    /// <summary>回数の上限。</summary>
    public static int MaxUses(Character c, string pool, GameData? data = null) => pool switch
    {
        "second_wind" => Level(c, "fighter") >= 1 ? 1 : 0,
        "action_surge" => Level(c, "fighter") switch { >= 17 => 2, >= 2 => 1, _ => 0 },
        "indomitable" => Level(c, "fighter") switch { >= 17 => 3, >= 13 => 2, >= 9 => 1, _ => 0 },
        "rage" => Level(c, "barbarian") switch { >= 20 => 99, >= 17 => 6, >= 12 => 5, >= 6 => 4, >= 3 => 3, >= 1 => 2, _ => 0 },
        "lay_on_hands" => Level(c, "paladin") * 5,
        "ki" => Level(c, "monk") >= 2 ? Level(c, "monk") : 0,
        "channel_divinity" => Level(c, "cleric") switch { >= 18 => 3, >= 6 => 2, >= 2 => 1, _ => 0 },
        "bardic_inspiration" => Level(c, "bard") >= 1 ? Math.Max(1, c.Scores.Mod(Ability.Cha)) : 0,
        "wild_shape" => Level(c, "druid") switch { >= 20 => 99, >= 2 => 2, _ => 0 },   // 大ドルイド(L20)は何回でも
        "sorcery_points" => Level(c, "sorcerer") >= 2 ? Level(c, "sorcerer") : 0,
        "arcane_recovery" => Level(c, "wizard") >= 1 ? 1 : 0,
        "breath_weapon" => c.RaceId == "dragonborn" ? 1 : 0,           // 竜の息(小休憩で戻る)
        "relentless_endurance" => c.RaceId == "half_orc" ? 1 : 0,      // 不屈の耐久(大休憩で戻る)
        _ => 0,
    };

    public static int UsesLeft(Character c, string pool) => Math.Max(0, MaxUses(c, pool) - c.FeatureUses.GetValueOrDefault(pool));

    /// <summary>回数を使う。足りなければ false(使わない)。</summary>
    public static bool Spend(Character c, string pool, int n = 1)
    {
        if (UsesLeft(c, pool) < n) return false;
        c.FeatureUses[pool] = c.FeatureUses.GetValueOrDefault(pool) + n;
        return true;
    }

    /// <summary>その技を今使えるか(回数の残り。スマイトは呪文スロットの残り)。</summary>
    public static bool CanUse(GameData data, Character c, FeatureDef f)
    {
        if (!Has(c, f.ClassId, f.MinLevel)) return false;
        if (f.Id == "divine_smite") return SmiteSlot(data, c) != null;
        if (f.Id == "lay_on_hands") return UsesLeft(c, "lay_on_hands") > 0;
        if (f.Id == "flurry_of_blows" && !MartialArts(data, c)) return false;   // 鎧か盾を着けていると素手の連打は出ない(気だけ減らないように選べなくする)
        return f.Pool == null || UsesLeft(c, f.Pool) >= f.Cost;
    }

    /// <summary>
    /// 獣の姿でも使える技か。武器や素手の打撃に乗せる技(連打・朦朧打撃・スマイト)は、獣の攻撃では使えない
    /// (使っても気やスロットだけが減るので、選べないようにする)。
    /// </summary>
    public static bool UsableInBeastForm(FeatureDef f) => f.Id is not ("flurry_of_blows" or "stunning_strike" or "divine_smite" or "breath_weapon");

    /// <summary>スマイトに使うスロット(4 レベルまでの、空いている一番上。5版ではそれより上でもダメージは 5d8 で止まる)。</summary>
    public static int? SmiteSlot(GameData data, Character c)
    {
        if (!data.Classes.TryGetValue("paladin", out var cls)) return null;
        for (int lv = 4; lv >= 1; lv--)
            if (Spellcasting.SlotsLeft(cls, c, lv) > 0) return lv;
        return null;
    }

    /// <summary>小休憩: 小休憩で戻る回数を戻す。ウィザードは秘術の回復でスロットを戻す(大休憩までに1回)。戻したスロットのレベルの合計を返す。</summary>
    public static int ShortRest(GameData data, Character c)
    {
        foreach (var pool in c.FeatureUses.Keys.Where(k => ShortRecharge(c, k)).ToList()) c.FeatureUses.Remove(pool);
        return ArcaneRecovery(data, c);
    }

    /// <summary>
    /// 秘術の回復(ウィザード L1): 小休憩で、合わせてウィザードのレベルの半分(切り上げ)までのスロットを戻す。6 レベル以上のスロットは戻らない。
    /// 上のスロットから戻す。戻したレベルの合計を返す(使わなかったら 0 で、回数も使わない)。
    /// </summary>
    private static int ArcaneRecovery(GameData data, Character c)
    {
        if (UsesLeft(c, "arcane_recovery") <= 0) return 0;
        int budget = (Level(c, "wizard") + 1) / 2, got = 0;
        for (int lv = Math.Min(5, budget); lv >= 1; lv--)
        {
            while (lv <= budget - got && c.SlotsUsed.GetValueOrDefault(lv) > 0)
            {
                c.SlotsUsed[lv]--;
                if (c.SlotsUsed[lv] == 0) c.SlotsUsed.Remove(lv);
                got += lv;
            }
        }
        if (got > 0) Spend(c, "arcane_recovery");
        return got;
    }

    /// <summary>
    /// 休息の歌(バード L2): 小休憩でヒットダイスを使って HP を戻した仲間は、さらに d6(L9 d8、L13 d10、L17 d12)戻る。
    /// パーティにバードが何人いても1回(一番高いもの)。戻った仲間の数を返す。
    /// </summary>
    public static int SongOfRest(IEnumerable<(Character C, int DiceUsed)> rested, IEnumerable<Character> party, IRandom rng)
    {
        int bard = party.Where(p => p.CanAct).Select(p => Level(p, "bard")).DefaultIfEmpty(0).Max();
        if (bard < 2) return 0;
        int die = bard >= 17 ? 12 : bard >= 13 ? 10 : bard >= 9 ? 8 : 6;
        int n = 0;
        foreach (var (c, used) in rested.Where(x => x.DiceUsed > 0 && x.C.IsAlive && x.C.Hp < x.C.MaxHp))
        {
            c.Hp = Math.Min(c.MaxHp, c.Hp + rng.Roll(die));
            n++;
        }
        return n;
    }

    /// <summary>回数を数えるもの(状態の画面に残りを出す順)。</summary>
    public static readonly string[] Pools =
        ["second_wind", "action_surge", "indomitable", "rage", "lay_on_hands", "ki", "channel_divinity", "bardic_inspiration", "wild_shape",
         "sorcery_points", "arcane_recovery", "breath_weapon", "relentless_endurance"];

    /// <summary>勝手に効く能力(状態の画面に名前を出す)。id とクラスと覚えるレベル。</summary>
    private static readonly (string Id, string ClassId, int Level)[] PassiveList =
    [
        ("reckless_attack", "barbarian", 2), ("danger_sense", "barbarian", 2), ("feral_instinct", "barbarian", 7),
        ("brutal_critical", "barbarian", 9), ("relentless_rage", "barbarian", 11),
        ("aura_of_protection", "paladin", 6), ("aura_of_courage", "paladin", 10), ("improved_divine_smite", "paladin", 11),
        ("uncanny_dodge", "rogue", 5), ("evasion", "rogue", 7), ("slippery_mind", "rogue", 15), ("elusive", "rogue", 18),
        ("martial_arts", "monk", 1), ("deflect_missiles", "monk", 3), ("ki_empowered_strikes", "monk", 6), ("evasion", "monk", 7),
        ("purity_of_body", "monk", 10), ("diamond_soul", "monk", 14),
        ("destroy_undead", "cleric", 5), ("song_of_rest", "bard", 2), ("font_of_inspiration", "bard", 5),
        ("empowered_spell", "sorcerer", 3), ("beast_spells", "druid", 18), ("archdruid", "druid", 20),
        ("agonizing_blast", "warlock", 2), ("thirsting_blade", "warlock", 5), ("lifedrinker", "warlock", 12),
    ];

    /// <summary>その人に働いている、勝手に効く能力の id。</summary>
    public static IEnumerable<string> Passives(Character c) => PassiveList.Where(x => Has(c, x.ClassId, x.Level)).Select(x => x.Id);

    // ---------------- 勝手に効く能力の数字 ----------------

    /// <summary>激怒のダメージの上乗せ(L1 +2、L9 +3、L16 +4)。</summary>
    public static int RageDamage(Character c) => Level(c, "barbarian") switch { >= 16 => 4, >= 9 => 3, _ => 2 };

    /// <summary>残忍な一撃(バーバリアン L9): クリティカルで足す武器のダイスの数。</summary>
    public static int BrutalCritical(Character c) => Level(c, "barbarian") switch { >= 17 => 3, >= 13 => 2, >= 9 => 1, _ => 0 };

    /// <summary>武術(モンク)の素手のダイス。</summary>
    public static int MartialArtsDie(Character c) => Level(c, "monk") switch { >= 17 => 10, >= 11 => 8, >= 5 => 6, _ => 4 };

    /// <summary>気の技の難易度(8 + 習熟 + 判断力)。</summary>
    public static int KiDc(Character c) => 8 + c.ProficiencyBonus + c.Scores.Mod(Ability.Wis);

    /// <summary>鼓舞のダイス(L1 d6、L5 d8、L10 d10、L15 d12)。</summary>
    public static int InspirationDie(Character c) => Level(c, "bard") switch { >= 15 => 12, >= 10 => 10, >= 5 => 8, _ => 6 };

    /// <summary>アンデッドの破壊(クレリック L5〜): この脅威度以下のアンデッドは、退散に失敗すると滅びる。無ければ -1。</summary>
    public static double DestroyUndeadCr(Character c) => Level(c, "cleric") switch { >= 17 => 4, >= 14 => 3, >= 11 => 2, >= 8 => 1, >= 5 => 0.5, _ => -1 };

    /// <summary>ワイルドシェイプでなれる獣(L2〜3 狼、L4〜7 狼か黒熊、L8 からダイア・ウルフかヒグマ)。狼の型と熊の型から選ぶ。</summary>
    public static IReadOnlyList<string> WildShapeBeasts(Character c) => Level(c, "druid") switch
    {
        >= 8 => ["dire_wolf", "brown_bear"],
        >= 4 => ["wolf", "black_bear"],
        >= 2 => ["wolf"],
        _ => [],
    };

    /// <summary>ファイティングスタイルを持っているか(ファイター L1、パラディン・レンジャー L2)。</summary>
    public static bool HasFightingStyle(Character c) => Has(c, "fighter", 1) || Has(c, "paladin", 2) || Has(c, "ranger", 2);

    /// <summary>
    /// 働くファイティングスタイル。選ばせずに、利き手の装備から1つ決める: 飛び道具なら弓術(命中 +2)、
    /// 両手に軽い武器なら二刀流(もう片方の手にも能力値の修正)、両手持ちか片手半を両手で持つなら大型武器(ダメージの 1 と 2 を振り直す)、
    /// 片手の近接武器なら決闘(ダメージ +2)、武器が無ければ防御(鎧を着ていれば AC +1)。
    /// </summary>
    public static FightingStyle StyleOf(GameData data, Character c)
    {
        if (!HasFightingStyle(c)) return FightingStyle.None;
        var inv = c.Inventory;
        ItemDef? Weapon(EquipSlot slot) => inv[slot] is { } it && data.Items.TryGetValue(it.ItemId, out var d) && d.Kind == ItemKind.Weapon ? d : null;
        var main = Weapon(EquipSlot.MainHand);
        var off = Weapon(EquipSlot.OffHand);
        if (main == null) return Weapon(EquipSlot.Ranged) != null ? FightingStyle.Archery : FightingStyle.Defense;
        if (main.Ranged) return FightingStyle.Archery;
        if (off != null) return FightingStyle.TwoWeapon;
        if (main.Has("two_handed") || (main.VersatileDamage != null && inv[EquipSlot.OffHand] == null)) return FightingStyle.GreatWeapon;
        return FightingStyle.Dueling;
    }

    /// <summary>元の武器(魔法の武器は base の品)。</summary>
    public static ItemDef BaseWeapon(GameData data, ItemDef def) => def.Base != null && data.Items.TryGetValue(def.Base, out var b) ? b : def;

    /// <summary>モンク武器(5版: ショートソードと、両手持ち・重いでない単純な近接武器)。素手は null で、これも含む。</summary>
    public static bool MonkWeapon(GameData data, ItemDef? def)
    {
        if (def == null) return true;
        var b = BaseWeapon(data, def);
        return !b.Ranged && (b.Id == "shortsword" || (b.Category == "simple" && !b.Has("two_handed") && !b.Has("heavy")));
    }

    /// <summary>武術が働くか(モンクで、鎧も盾も着けていない)。</summary>
    public static bool MartialArts(GameData data, Character c) =>
        Level(c, "monk") >= 1 && c.Inventory[EquipSlot.Armor] == null
        && !(c.Inventory[EquipSlot.OffHand] is { } off && data.Items.TryGetValue(off.ItemId, out var d) && d.Kind == ItemKind.Shield);

    /// <summary>呪文を唱えられる姿か(激怒のあいだは唱えられない。獣の姿は、獣の呪文 ドルイド L18 があれば唱えられる)。</summary>
    public static bool CanCastIn(Character c, bool raging, bool shaped) => !raging && (!shaped || Has(c, "druid", 18));

    /// <summary>回避(ローグ L7、モンク L7): 敏捷力のセーヴで半分になる効果を、成功で 0、失敗で半分にする。</summary>
    public static bool Evasion(Character c) => Has(c, "rogue", 7) || Has(c, "monk", 7);
}
