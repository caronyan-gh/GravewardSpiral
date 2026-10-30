using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Magic;

/// <summary>
/// 呪文の使い方の種類(クラスの spellcasting の style)。Prepared は毎日準備する(クレリック、ドルイド、パラディン)、Book は呪文書から準備する(ウィザード)、
/// Known は覚えた呪文をそのまま使う(バード、ソーサラー、レンジャー)、Pact はウォーロックの協約魔術(小休憩で戻る)。
/// </summary>
public enum CasterKind { None, Prepared, Book, Known, Pact }

/// <summary>
/// 呪文の数とスロットの決まり。レベル 20 より上は 20 の値を使う。
/// 数はクラスごとのレベルで決め(クラスの spellcasting のデータ)、スロットはマルチクラスの術者のレベル(伸び方の multiclass の合計)で決める。
/// 呪文の一覧(初級・覚える・準備)はクラスごとに持つ(Character.Books)。スロットの表は spell_slots.json、伸び方と協約のスロットは spell_progressions.json。
/// </summary>
public static class Spellcasting
{
    public static CasterKind Kind(ClassDef cls) => cls.Spellcasting?.Style ?? CasterKind.None;

    /// <summary>そのクラスのレベル(1〜20)。</summary>
    private static int L(ClassDef cls, Character c) => Math.Clamp(c.LevelIn(cls.Id), 1, 20);

    /// <summary>そのクラスのスロットの伸び方(spell_progressions.json)。無ければ null。</summary>
    private static SpellProgressionDef? Prog(GameData data, ClassDef cls) =>
        cls.Spellcasting is { } sc ? data.SpellProgressions.GetValueOrDefault(sc.Progression) : null;

    /// <summary>協約魔術のクラスか(伸び方に pact がある)。</summary>
    private static bool IsPact(GameData data, ClassDef cls) => Prog(data, cls)?.Pact != null;

    /// <summary>そのクラスのレベルが、呪文を使い始めるレベル(from_level)に届いているか。</summary>
    private static bool Started(ClassDef cls, Character c) => cls.Spellcasting is { } sc && c.LevelIn(cls.Id) >= sc.FromLevel;

    /// <summary>最初の術者のクラス(スロットの表示など、クラスを問わないところに使う)。術者でなければ null。</summary>
    public static ClassDef? CasterClass(GameData data, Character c) => CasterClasses(data, c).FirstOrDefault();

    /// <summary>術者のクラス全部(クラスを取った順)。マルチクラスでは、呪文はクラスごとに覚えて準備する。</summary>
    public static IEnumerable<ClassDef> CasterClasses(GameData data, Character c) =>
        c.ClassIds.Distinct().Select(id => data.Classes.GetValueOrDefault(id)).OfType<ClassDef>().Where(cls => Kind(cls) != CasterKind.None);

    /// <summary>
    /// その呪文を唱えるクラス(難易度と攻撃の修正はこのクラスの能力値)。そのクラスで今唱えられるものだけを候補にする:
    /// 初級呪文は Cantrips、準備(prepared・book)は Prepared、覚える(known・pact)は Known にあるもの。呪文書にあるだけの未準備の呪文は候補にしない。
    /// 候補がいくつもあれば先に取ったクラス。どこにも無ければ(巻物など)、最初の術者のクラス。
    /// </summary>
    public static ClassDef? ClassFor(GameData data, Character c, string spellId) =>
        CasterClasses(data, c).FirstOrDefault(cls => CanCastAs(cls, c, spellId)) ?? CasterClass(data, c);

    /// <summary>そのクラスとして今その呪文を唱えられるか(覚えた・準備した呪文の一覧にある)。</summary>
    public static bool CanCastAs(ClassDef cls, Character c, string spellId)
    {
        if (!c.Books.TryGetValue(cls.Id, out var b)) return false;
        if (b.Cantrips.Contains(spellId)) return true;
        return Kind(cls) is CasterKind.Prepared or CasterKind.Book ? b.Prepared.Contains(spellId) : b.Known.Contains(spellId);
    }

    /// <summary>
    /// スロットの表を引くための術者のレベル。術者のクラスが1つなら、そのクラスの伸び方の caster_level(5版の単独の決まり。半術者はレベルの半分の切り上げ)、
    /// 2つ以上なら、各クラスの伸び方の multiclass の合計(5版のマルチクラスの決まり。半術者はレベルの半分の切り捨て)。協約魔術のクラスと、
    /// まだ呪文を使い始めていない(from_level に届いていない)クラスは数えない。
    /// </summary>
    public static int CasterLevel(GameData data, Character c)
    {
        var casters = CasterClasses(data, c).Where(cls => !IsPact(data, cls) && Started(cls, c)).ToList();
        if (casters.Count == 0) return 0;
        if (casters.Count == 1) return OwnCasterLevel(data, casters[0], c);
        return Math.Min(20, casters.Sum(cls => Prog(data, cls)?.Multiclass?.Eval(c, c.LevelIn(cls.Id)) ?? 0));
    }

    /// <summary>そのクラスだけのときの術者のレベル(伸び方の caster_level をそのクラスのレベルで数える。20 で止まる)。</summary>
    private static int OwnCasterLevel(GameData data, ClassDef cls, Character c) =>
        Math.Clamp(Prog(data, cls)?.CasterLevel?.Eval(c, L(cls, c)) ?? 0, 0, 20);

    /// <summary>協約魔術のクラス(呪文を使い始めたもの)のレベルの合計(0 なら協約のスロットは無い)。スロットの数とレベルはこれで決める。</summary>
    private static int PactLevel(GameData data, Character c) =>
        CasterClasses(data, c).Where(cls => IsPact(data, cls) && Started(cls, c)).Sum(cls => c.LevelIn(cls.Id));

    /// <summary>協約のスロットの数え方(呪文を使い始めた、最初の協約魔術のクラスの伸び方の pact)。</summary>
    private static PactSlotsDef? PactDef(GameData data, Character c) =>
        CasterClasses(data, c).Where(cls => Started(cls, c)).Select(cls => Prog(data, cls)?.Pact).FirstOrDefault(p => p != null);

    /// <summary>ふつうのスロットの表の、その術者のレベルの、その呪文レベルの数(行が無ければ 0)。</summary>
    private static int TableSlots(GameData data, int casterLevel, int spellLevel) =>
        data.SpellSlots.TryGetValue(casterLevel.ToString(), out var row) && spellLevel <= row.Slots.Count ? row.Slots[spellLevel - 1] : 0;

    /// <summary>
    /// その呪文レベルのスロットの数。術者のスロットと、協約魔術のスロット(そのレベルにだけ数える)の合計。数はキャラクターのクラス全部から決める。
    /// </summary>
    public static int MaxSlots(GameData data, Character c, int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > 9) return 0;
        int n = RegularSlots(data, c, spellLevel);
        if (PactLevel(data, c) > 0 && PactSlotLevel(data, c) == spellLevel) n += PactSlots(data, c);
        return n;
    }

    private static int Pact(GameData data, Character c) => Math.Clamp(PactLevel(data, c), 1, 20);
    public static int PactSlots(GameData data, Character c) => PactDef(data, c)?.Slots.Eval(c, Pact(data, c)) ?? 0;
    public static int PactSlotLevel(GameData data, Character c) => PactDef(data, c)?.Level.Eval(c, Pact(data, c)) ?? 0;

    /// <summary>今使えるスロットの数(ふつうのスロットの残りと、そのレベルなら協約魔術のスロットの残り)。</summary>
    public static int SlotsLeft(GameData data, Character c, int spellLevel) =>
        Math.Max(0, RegularSlots(data, c, spellLevel) - c.SlotsUsed.GetValueOrDefault(spellLevel)) + PactLeft(data, c, spellLevel);

    /// <summary>ふつうの呪文スロットの数(術者のレベルの表から。協約魔術の分は入れない)。</summary>
    public static int RegularSlots(GameData data, Character c, int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > 9) return 0;
        int lv = CasterLevel(data, c);
        return lv > 0 ? TableSlots(data, lv, spellLevel) : 0;
    }

    /// <summary>そのレベルの、協約魔術のスロットの残り(協約魔術のクラスがあって、協約のスロットのレベルのときだけ)。</summary>
    public static int PactLeft(GameData data, Character c, int spellLevel) =>
        PactLevel(data, c) > 0 && PactSlotLevel(data, c) == spellLevel ? Math.Max(0, PactSlots(data, c) - c.PactSlotsUsed) : 0;

    /// <summary>協約魔術のスロットを持つか。</summary>
    public static bool HasPactSlots(GameData data, Character c) => PactLevel(data, c) > 0;

    /// <summary>唱えられる一番高い呪文レベル(スロットの残りは見ない)。</summary>
    public static int MaxSpellLevel(GameData data, Character c)
    {
        for (int lv = 9; lv >= 1; lv--)
        {
            if (MaxSlots(data, c, lv) > 0) return lv;
        }
        return 0;
    }

    /// <summary>スロットを1つ使う。協約魔術のスロットが残っていれば先にそちら(小休憩で戻るので)。</summary>
    public static void UseSlot(GameData data, Character c, int spellLevel)
    {
        if (PactLeft(data, c, spellLevel) > 0) c.PactSlotsUsed++;
        else c.SlotsUsed[spellLevel] = c.SlotsUsed.GetValueOrDefault(spellLevel) + 1;
    }

    /// <summary>覚える初級呪文の数(クラスの cantrips)。</summary>
    public static int CantripsKnown(ClassDef cls, Character c) =>
        Started(cls, c) && cls.Spellcasting!.Cantrips is { } n ? n.Eval(c, L(cls, c)) : 0;

    /// <summary>覚えている呪文の数(known・pact・book。book は呪文書に載せる数)。</summary>
    public static int SpellsKnown(ClassDef cls, Character c) =>
        Started(cls, c) && Kind(cls) is CasterKind.Known or CasterKind.Pact or CasterKind.Book && cls.Spellcasting!.Known is { } n ? n.Eval(c, L(cls, c)) : 0;

    /// <summary>準備できる呪文の数(prepared・book)。</summary>
    public static int PreparedCount(ClassDef cls, Character c) =>
        Started(cls, c) && Kind(cls) is CasterKind.Prepared or CasterKind.Book && cls.Spellcasting!.Prepared is { } n ? n.Eval(c, L(cls, c)) : 0;

    public static Ability? SpellAbility(ClassDef cls) => cls.Spellcasting is { } sc ? Abilities.Parse(sc.Ability) : null;
    public static int Mod(ClassDef cls, Character c) => SpellAbility(cls) is { } a ? c.Scores.Mod(a) : 0;
    public static int SaveDc(ClassDef cls, Character c) => 8 + c.ProficiencyBonus + Mod(cls, c);
    public static int AttackBonus(ClassDef cls, Character c) => c.ProficiencyBonus + Mod(cls, c);

    /// <summary>
    /// そのクラスで覚えられる(準備できる)呪文の一番高いレベル。マルチクラスでも、そのクラスだけのレベルで決める(5版)。
    /// そのクラスの伸び方の caster_level で術者のレベルを出し、スロットの表のその行で数のある一番上のレベル。協約魔術は、そのクラスのレベルでの協約のスロットのレベル
    /// (協約魔術のクラスが2つあっても、スロットは合わせて数えるが、覚えられる呪文はそれぞれのレベルで決める)。呪文を使い始める前は 0。
    /// </summary>
    public static int ClassMaxSpellLevel(GameData data, ClassDef cls, Character c)
    {
        int lv = c.LevelIn(cls.Id);
        if (lv <= 0 || cls.Spellcasting == null || !Started(cls, c)) return 0;
        if (Prog(data, cls)?.Pact is { } pact) return pact.Level.Eval(c, Math.Clamp(lv, 1, 20));
        int casterLv = OwnCasterLevel(data, cls, c);
        if (casterLv <= 0) return 0;
        for (int l = 9; l >= 1; l--)
            if (TableSlots(data, casterLv, l) > 0) return l;
        return 0;
    }

    /// <summary>
    /// そのクラスで、その呪文を何レベルの呪文として扱うか(spell_access の spelllv。そのクラスのレベルで開いた束のうち一番低いもの)。
    /// どの束にも無ければ(巻物など)、呪文のもとのレベル。初級呪文は 0。
    /// </summary>
    public static int AssignedLevel(GameData data, ClassDef? cls, Character c, SpellDef sp)
    {
        if (sp.IsCantrip || cls?.Spellcasting is not { } sc) return sp.Level;
        int lv = c.LevelIn(cls.Id);
        var levels = sc.SpellAccess.Where(a => a.Lv <= lv && a.Spelllv > 0 && data.SpellSets.TryGetValue(a.Set, out var set) && set.Spells.Contains(sp.Id))
            .Select(a => a.Spelllv).ToList();
        return levels.Count > 0 ? levels.Min() : sp.Level;
    }

    /// <summary>その人が唱えるときの、その呪文のレベル(唱えるクラス ClassFor の割り当て)。要るスロットと、呪文の並びはこれで数える。</summary>
    public static int AssignedLevel(GameData data, Character c, SpellDef sp) => AssignedLevel(data, ClassFor(data, c, sp.Id), c, sp);

    /// <summary>
    /// slot のスロットで唱えたときの、効果の強さのレベル(ダイスの表・召喚の数などは、もとのレベルを基準に書いてある)。
    /// 割り当てたレベルのスロットなら、もとのレベルの強さ。そこから上のスロットの分だけ強くなる(もとは 3 の呪文を 2 に割り当てて 3 で唱えたら 4 の強さ)。
    /// </summary>
    public static int EffectLevel(GameData data, Character c, SpellDef sp, int slot) =>
        sp.IsCantrip ? slot : sp.Level + Math.Max(0, slot - AssignedLevel(data, c, sp));

    /// <summary>
    /// そのクラスで覚えられる(準備できる)呪文。クラスの spell_access の束のうち、そのクラスのレベルが lv に届いたもので、
    /// 割り当てたレベル(spelllv)がそのクラスで覚えられるところまでの、使える呪文。
    /// </summary>
    public static IEnumerable<SpellDef> ClassSpells(GameData data, ClassDef cls, Character c, bool cantrips)
    {
        if (cls.Spellcasting is not { } sc) return [];
        int max = ClassMaxSpellLevel(data, cls, c), lv = c.LevelIn(cls.Id);
        return sc.SpellAccess
            .Where(a => a.Lv <= lv && (cantrips ? a.Spelllv == 0 : a.Spelllv > 0 && a.Spelllv <= max))
            .SelectMany(a => data.SpellSets.TryGetValue(a.Set, out var set) ? set.Spells : [])
            .Distinct()
            .Select(id => data.Spells.GetValueOrDefault(id)).OfType<SpellDef>()
            .Where(s => s.Usable && s.IsCantrip == cantrips)
            .OrderBy(s => AssignedLevel(data, cls, c, s)).ThenBy(s => s.Id, StringComparer.Ordinal);
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
            .OrderBy(s => AssignedLevel(data, c, s)).ThenBy(s => s.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// 呪文を唱える画面で、その呪文を slotLevel のスロット(0 は初級呪文)の欄に並べるか。そのレベルの呪文と、上のスロットで強くなる呪文。
    /// 強くならない呪文も、自分のレベルのスロットが無い(使い切った、ウォーロックのように持っていない)ときは、払える一番低い上の欄に並べる
    /// (並べないと唱える手段が無くなる)。
    /// </summary>
    public static bool ListedAt(GameData data, Character c, SpellDef sp, int slotLevel)
    {
        if (slotLevel == 0 || sp.IsCantrip) return slotLevel == 0 && sp.IsCantrip;
        int level = AssignedLevel(data, c, sp);
        if (level == slotLevel) return true;
        if (level > slotLevel) return false;
        if (sp.Scale == "slot") return true;
        if (CasterClass(data, c) == null || SlotsLeft(data, c, level) > 0) return false;
        return LowestSlot(data, c, sp) == slotLevel;
    }

    /// <summary>その呪文を唱えられる一番低いスロット(初級呪文は 0)。無ければ null。</summary>
    public static int? LowestSlot(GameData data, Character c, SpellDef s)
    {
        if (s.IsCantrip) return 0;
        for (int lv = AssignedLevel(data, c, s); lv <= 9; lv++)
        {
            if (SlotsLeft(data, c, lv) > 0) return lv;
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
