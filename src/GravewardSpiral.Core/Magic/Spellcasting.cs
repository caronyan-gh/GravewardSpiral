using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Magic;

/// <summary>
/// 呪文の使い方の種類。Prepared は毎日準備する(クレリック、ドルイド、パラディン)、Book は呪文書から準備する(ウィザード)、
/// Known は覚えた呪文をそのまま使う(バード、ソーサラー、レンジャー)、Pact はウォーロックの協約魔術(小休憩で戻る)。
/// </summary>
public enum CasterKind { None, Prepared, Book, Known, Pact }

/// <summary>
/// 5版の呪文の表と、呪文の数の決まり。レベル 20 より上は 20 の値を使う。
/// 数はクラスごとのレベルで決め、スロットはマルチクラスの術者のレベル(全術者のレベル + 半術者のレベルの半分)で決める。
/// 呪文の一覧(初級・覚える・準備)は今はキャラクターに1つずつで、最初の術者のクラスのものとして扱う(クラスごとに分けるのは M8)。
/// </summary>
public static class Spellcasting
{
    // 全術者の呪文スロット(レベル 1〜20、呪文レベル 1〜9)
    private static readonly int[,] FullSlots =
    {
        { 2, 0, 0, 0, 0, 0, 0, 0, 0 }, { 3, 0, 0, 0, 0, 0, 0, 0, 0 }, { 4, 2, 0, 0, 0, 0, 0, 0, 0 }, { 4, 3, 0, 0, 0, 0, 0, 0, 0 },
        { 4, 3, 2, 0, 0, 0, 0, 0, 0 }, { 4, 3, 3, 0, 0, 0, 0, 0, 0 }, { 4, 3, 3, 1, 0, 0, 0, 0, 0 }, { 4, 3, 3, 2, 0, 0, 0, 0, 0 },
        { 4, 3, 3, 3, 1, 0, 0, 0, 0 }, { 4, 3, 3, 3, 2, 0, 0, 0, 0 }, { 4, 3, 3, 3, 2, 1, 0, 0, 0 }, { 4, 3, 3, 3, 2, 1, 0, 0, 0 },
        { 4, 3, 3, 3, 2, 1, 1, 0, 0 }, { 4, 3, 3, 3, 2, 1, 1, 0, 0 }, { 4, 3, 3, 3, 2, 1, 1, 1, 0 }, { 4, 3, 3, 3, 2, 1, 1, 1, 0 },
        { 4, 3, 3, 3, 2, 1, 1, 1, 1 }, { 4, 3, 3, 3, 3, 1, 1, 1, 1 }, { 4, 3, 3, 3, 3, 2, 1, 1, 1 }, { 4, 3, 3, 3, 3, 2, 2, 1, 1 },
    };

    private static readonly int[] BardKnown = [4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 15, 16, 18, 19, 19, 20, 22, 22, 22];
    private static readonly int[] SorcererKnown = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 12, 13, 13, 14, 14, 15, 15, 15, 15];
    private static readonly int[] WarlockKnown = [2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15];
    private static readonly int[] RangerKnown = [0, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11];

    public static CasterKind Kind(ClassDef cls) => Kind(cls.Id);

    public static CasterKind Kind(string classId) => classId switch
    {
        "cleric" or "druid" or "paladin" => CasterKind.Prepared,
        "wizard" => CasterKind.Book,
        "bard" or "sorcerer" or "ranger" => CasterKind.Known,
        "warlock" => CasterKind.Pact,
        _ => CasterKind.None,
    };

    private static bool Half(string classId) => classId is "paladin" or "ranger";

    /// <summary>そのクラスのレベル(1〜20)。</summary>
    private static int L(ClassDef cls, Character c) => Math.Clamp(c.LevelIn(cls.Id), 1, 20);

    /// <summary>最初の術者のクラス(スロットの表示など、クラスを問わないところに使う)。術者でなければ null。</summary>
    public static ClassDef? CasterClass(GameData data, Character c) => CasterClasses(data, c).FirstOrDefault();

    /// <summary>術者のクラス全部(クラスを取った順)。マルチクラスでは、呪文はクラスごとに覚えて準備する。</summary>
    public static IEnumerable<ClassDef> CasterClasses(GameData data, Character c) =>
        c.ClassIds.Distinct().Where(id => Kind(id) != CasterKind.None).Select(id => data.Classes.GetValueOrDefault(id)).OfType<ClassDef>();

    /// <summary>その呪文を唱えるクラス(その呪文を覚えている・準備しているクラス。難易度と攻撃の修正はこのクラスの能力値)。</summary>
    public static ClassDef? ClassFor(GameData data, Character c, string spellId) =>
        CasterClasses(data, c).FirstOrDefault(cls => c.Books.TryGetValue(cls.Id, out var b) && b.Has(spellId)) ?? CasterClass(data, c);

    /// <summary>
    /// スロットの表を引くための術者のレベル。術者のクラスが1つなら 5版の単独の決まり(半術者はレベルの半分の切り上げ、1 レベルは 0)、
    /// 2つ以上ならマルチクラスの決まり(全術者のレベル + 半術者のレベルの半分の切り捨て)。ウォーロックは数えない。
    /// </summary>
    public static int CasterLevel(Character c)
    {
        var casters = c.ClassIds.Distinct().Where(id => Kind(id) is CasterKind.Prepared or CasterKind.Book or CasterKind.Known).ToList();
        if (casters.Count == 0) return 0;
        if (casters.Count == 1)
        {
            int lv = Math.Clamp(c.LevelIn(casters[0]), 1, 20);
            return Half(casters[0]) ? (lv < 2 ? 0 : (lv + 1) / 2) : lv;
        }
        return Math.Min(20, casters.Sum(id => Half(id) ? c.LevelIn(id) / 2 : c.LevelIn(id)));
    }

    /// <summary>
    /// その呪文レベルのスロットの数。術者のスロットと、ウォーロックの協約魔術のスロット(そのレベルにだけ数える)の合計。
    /// cls は呼ぶ側の都合で受け取るだけで、数はキャラクターのクラス全部から決める。
    /// </summary>
    public static int MaxSlots(ClassDef cls, Character c, int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > 9) return 0;
        int n = 0;
        int lv = CasterLevel(c);
        if (lv > 0) n += FullSlots[lv - 1, spellLevel - 1];
        if (c.LevelIn("warlock") > 0 && PactSlotLevel(c) == spellLevel) n += PactSlots(c);
        return n;
    }

    private static int Warlock(Character c) => Math.Clamp(c.LevelIn("warlock"), 1, 20);
    public static int PactSlots(Character c) => Warlock(c) switch { 1 => 1, <= 10 => 2, <= 16 => 3, _ => 4 };
    public static int PactSlotLevel(Character c) => Math.Min(5, (Warlock(c) + 1) / 2);

    /// <summary>今使えるスロットの数(ふつうのスロットの残りと、そのレベルなら協約魔術のスロットの残り)。</summary>
    public static int SlotsLeft(ClassDef cls, Character c, int spellLevel) =>
        Math.Max(0, RegularSlots(c, spellLevel) - c.SlotsUsed.GetValueOrDefault(spellLevel)) + PactLeft(c, spellLevel);

    /// <summary>ふつうの呪文スロットの数(術者のレベルの表から。協約魔術の分は入れない)。</summary>
    public static int RegularSlots(Character c, int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > 9) return 0;
        int lv = CasterLevel(c);
        return lv > 0 ? FullSlots[lv - 1, spellLevel - 1] : 0;
    }

    /// <summary>そのレベルの、協約魔術のスロットの残り(ウォーロックで、協約のスロットのレベルのときだけ)。</summary>
    public static int PactLeft(Character c, int spellLevel) =>
        c.LevelIn("warlock") > 0 && PactSlotLevel(c) == spellLevel ? Math.Max(0, PactSlots(c) - c.PactSlotsUsed) : 0;

    /// <summary>唱えられる一番高い呪文レベル(スロットの残りは見ない)。</summary>
    public static int MaxSpellLevel(ClassDef cls, Character c)
    {
        for (int lv = 9; lv >= 1; lv--)
        {
            if (MaxSlots(cls, c, lv) > 0) return lv;
        }
        return 0;
    }

    /// <summary>スロットを1つ使う。協約魔術のスロットが残っていれば先にそちら(小休憩で戻るので)。</summary>
    public static void UseSlot(Character c, int spellLevel)
    {
        if (PactLeft(c, spellLevel) > 0) c.PactSlotsUsed++;
        else c.SlotsUsed[spellLevel] = c.SlotsUsed.GetValueOrDefault(spellLevel) + 1;
    }

    public static int CantripsKnown(ClassDef cls, Character c)
    {
        int lv = L(cls, c);
        int step = lv >= 10 ? 2 : lv >= 4 ? 1 : 0;
        return cls.Id switch
        {
            "cleric" or "wizard" => 3 + step,
            "sorcerer" => 4 + step,
            "bard" or "druid" or "warlock" => 2 + step,
            _ => 0,
        };
    }

    /// <summary>覚えている呪文の数(Known と Pact)。呪文書(Book)は 6 + 2 × (レベル − 1)。</summary>
    public static int SpellsKnown(ClassDef cls, Character c) => cls.Id switch
    {
        "bard" => BardKnown[L(cls, c) - 1],
        "sorcerer" => SorcererKnown[L(cls, c) - 1],
        "warlock" => WarlockKnown[L(cls, c) - 1],
        "ranger" => RangerKnown[L(cls, c) - 1],
        "wizard" => 6 + 2 * (L(cls, c) - 1),
        _ => 0,
    };

    /// <summary>準備できる呪文の数(Prepared と Book)。能力値の修正値 + レベル(パラディンは半分)、最低 1。</summary>
    public static int PreparedCount(ClassDef cls, Character c)
    {
        var kind = Kind(cls);
        if (kind is not (CasterKind.Prepared or CasterKind.Book)) return 0;
        if (cls.Id == "paladin" && L(cls, c) < 2) return 0;
        int lv = cls.Id == "paladin" ? L(cls, c) / 2 : L(cls, c);
        return Math.Max(1, Mod(cls, c) + lv);
    }

    public static Ability? SpellAbility(ClassDef cls) => cls.Spellcasting is { } code ? Abilities.Parse(code) : null;
    public static int Mod(ClassDef cls, Character c) => SpellAbility(cls) is { } a ? c.Scores.Mod(a) : 0;
    public static int SaveDc(ClassDef cls, Character c) => 8 + c.ProficiencyBonus + Mod(cls, c);
    public static int AttackBonus(ClassDef cls, Character c) => c.ProficiencyBonus + Mod(cls, c);

    /// <summary>
    /// そのクラスで覚えられる(準備できる)呪文の一番高いレベル。マルチクラスでも、そのクラスだけのレベルで決める(5版)。
    /// 全術者はそのクラスのレベルの表、パラディンとレンジャーはレベルの半分の切り上げ(1 レベルは 0)、ウォーロックは協約のスロットのレベル。
    /// </summary>
    public static int ClassMaxSpellLevel(ClassDef cls, Character c)
    {
        int lv = c.LevelIn(cls.Id);
        if (lv <= 0) return 0;
        if (Kind(cls) == CasterKind.Pact) return PactSlotLevel(c);
        int casterLv = Math.Min(20, Half(cls.Id) ? (lv < 2 ? 0 : (lv + 1) / 2) : lv);
        if (casterLv <= 0) return 0;
        for (int l = 9; l >= 1; l--)
            if (FullSlots[casterLv - 1, l - 1] > 0) return l;
        return 0;
    }

    /// <summary>そのクラスで覚えられる(準備できる)呪文。使える呪文だけで、レベルはそのクラスのレベルで覚えられるところまで。</summary>
    public static IEnumerable<SpellDef> ClassSpells(GameData data, ClassDef cls, Character c, bool cantrips)
    {
        int max = ClassMaxSpellLevel(cls, c);
        return data.Spells.Values
            .Where(s => s.Usable && s.Classes.Contains(cls.Id) && (cantrips ? s.IsCantrip : !s.IsCantrip && s.Level <= max))
            .OrderBy(s => s.Level).ThenBy(s => s.Id, StringComparer.Ordinal);
    }

    /// <summary>今唱えられる呪文(術者のクラス全部の、初級呪文と、準備した・覚えた呪文)。</summary>
    public static IEnumerable<SpellDef> Castable(GameData data, Character c)
    {
        var ids = new List<string>();
        foreach (var cls in CasterClasses(data, c))
        {
            if (!c.Books.TryGetValue(cls.Id, out var b)) continue;
            ids.AddRange(b.Cantrips);
            ids.AddRange(Kind(cls) is CasterKind.Prepared or CasterKind.Book ? b.Prepared : b.Known);
        }
        return ids.Distinct().Select(id => data.Spells.GetValueOrDefault(id)).OfType<SpellDef>().Where(s => s.Usable)
            .OrderBy(s => s.Level).ThenBy(s => s.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// 呪文を唱える画面で、その呪文を slotLevel のスロット(0 は初級呪文)の欄に並べるか。そのレベルの呪文と、上のスロットで強くなる呪文。
    /// 強くならない呪文も、自分のレベルのスロットが無い(使い切った、ウォーロックのように持っていない)ときは、払える一番低い上の欄に並べる
    /// (並べないと唱える手段が無くなる)。
    /// </summary>
    public static bool ListedAt(GameData data, Character c, SpellDef sp, int slotLevel)
    {
        if (slotLevel == 0 || sp.IsCantrip) return slotLevel == 0 && sp.IsCantrip;
        if (sp.Level == slotLevel) return true;
        if (sp.Level > slotLevel) return false;
        if (sp.Scale == "slot") return true;
        if (CasterClass(data, c) is not { } cls || SlotsLeft(cls, c, sp.Level) > 0) return false;
        return LowestSlot(cls, c, sp) == slotLevel;
    }

    /// <summary>その呪文を唱えられる一番低いスロット(初級呪文は 0)。無ければ null。</summary>
    public static int? LowestSlot(ClassDef cls, Character c, SpellDef s)
    {
        if (s.IsCantrip) return 0;
        for (int lv = s.Level; lv <= 9; lv++)
        {
            if (SlotsLeft(cls, c, lv) > 0) return lv;
        }
        return null;
    }

    /// <summary>
    /// 足りない分の呪文を、おまかせで選んで足す(作成とレベルアップのとき。選んだものは消さない)。
    /// 戦闘で使える呪文を先に、その中でもダメージと回復を先に選ぶ。準備の呪文は全部選び直す。
    /// </summary>
    public static void AutoPick(GameData data, Character c)
    {
        foreach (var cls in CasterClasses(data, c)) AutoPick(data, c, cls);
    }

    /// <summary>1つのクラスの呪文を、おまかせで足す。</summary>
    public static void AutoPick(GameData data, Character c, ClassDef cls)
    {
        var kind = Kind(cls);
        var book = c.Book(cls.Id);
        Fill(book.Cantrips, ClassSpells(data, cls, c, cantrips: true), CantripsKnown(cls, c));
        if (kind is CasterKind.Known or CasterKind.Pact or CasterKind.Book)
            Fill(book.Known, ClassSpells(data, cls, c, cantrips: false), SpellsKnown(cls, c));
        if (kind is CasterKind.Prepared or CasterKind.Book)
        {
            var pool = kind == CasterKind.Book
                ? book.Known.Select(id => data.Spells.GetValueOrDefault(id)).OfType<SpellDef>()
                : ClassSpells(data, cls, c, cantrips: false);
            book.Prepared.RemoveAll(id => !pool.Any(s => s.Id == id));
            Fill(book.Prepared, pool, PreparedCount(cls, c));
        }
    }

    private static void Fill(List<string> list, IEnumerable<SpellDef> pool, int count)
    {
        var ordered = pool.Where(s => !list.Contains(s.Id))
            .OrderByDescending(Usefulness).ThenByDescending(s => s.Level).ThenBy(s => s.Id, StringComparer.Ordinal);
        foreach (var s in ordered)
        {
            if (list.Count >= count) break;
            list.Add(s.Id);
        }
    }

    private static int Usefulness(SpellDef s)
    {
        var types = s.Effects.SelectMany(e => e.Flatten()).Select(e => e.Type).ToHashSet();
        int score = s.InCombat ? 10 : 0;
        if (s.Time == "reaction" && s.Usable) score += 20;   // カウンタースペルなど(自動で使う。範囲の攻撃の呪文より先に選ぶ)
        if (s.Summon != null) score += 4;
        if (types.Contains(EffectPart.Damage)) score += 5;
        if (types.Contains(EffectPart.Heal)) score += 6;
        if (s.Target is SpellTarget.Group or SpellTarget.TwoGroups) score += 2;
        return score;
    }
}
