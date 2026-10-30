using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

/// <summary>状態(5版の死亡セーヴと、Wizardry の灰・消失)。</summary>
public enum LifeState { Alive, Unconscious, Dead, Ashes, Lost }

public enum Alignment { Good, Neutral, Evil }

/// <summary>クラス1つとそのレベル(マルチクラスでは複数持つ)。</summary>
public sealed class ClassLevel
{
    public string ClassId { get; set; } = "";
    public int Level { get; set; }
}

/// <summary>1つのクラスの呪文(初級呪文、覚えた呪文か呪文書、準備した呪文)。</summary>
public sealed class SpellBook
{
    public List<string> Cantrips { get; set; } = [];

    /// <summary>覚えている呪文(バード・ソーサラー・レンジャー・ウォーロック)か、呪文書(ウィザード)。</summary>
    public List<string> Known { get; set; } = [];

    /// <summary>準備した呪文(クレリック・ドルイド・パラディン・ウィザード)。</summary>
    public List<string> Prepared { get; set; } = [];

    public bool Has(string spellId) => Cantrips.Contains(spellId) || Known.Contains(spellId) || Prepared.Contains(spellId);
}

/// <summary>キャラクター1人。セーブにそのまま入る。</summary>
public sealed class Character
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string RaceId { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string BackgroundId { get; set; } = "";
    public Alignment Alignment { get; set; } = Alignment.Neutral;

    /// <summary>全部のクラスのレベルの合計(習熟ボーナスと経験値に使う)。</summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// クラスごとのレベル(5版のマルチクラス)。先頭が最初のクラスで、ClassId と同じ。
    /// 古いセーブでは空なので、読み込んだら NormalizeClasses で ClassId と Level から作る。
    /// </summary>
    public List<ClassLevel> Classes { get; set; } = [];

    public IEnumerable<string> ClassIds => Classes.Count == 0 ? [ClassId] : Classes.Select(x => x.ClassId);

    /// <summary>そのクラスのレベル(持っていなければ 0)。</summary>
    public int LevelIn(string classId) =>
        Classes.Count == 0 ? (classId == ClassId ? Level : 0) : Classes.Where(x => x.ClassId == classId).Sum(x => x.Level);

    /// <summary>古いセーブ(クラスが1つの形)を、クラスごとのレベルの形にする。</summary>
    public void NormalizeClasses()
    {
        if (Classes.Count == 0 && ClassId.Length > 0) Classes.Add(new ClassLevel { ClassId = ClassId, Level = Level });
    }
    public long Xp { get; set; }

    /// <summary>種族の増加を足したあとの能力値。</summary>
    public AbilityScores Scores { get; set; } = new();

    public int MaxHp { get; set; }
    public int Hp { get; set; }

    /// <summary>残っているヒットダイスの数(小休憩で使う。大休憩でレベルの半分戻る)。</summary>
    public int HitDice { get; set; }

    public List<string> Skills { get; set; } = [];
    public LifeState State { get; set; } = LifeState.Alive;
    public long Gold { get; set; }
    public Inventory Inventory { get; set; } = new();

    /// <summary>呪文(術者のクラスごと。マルチクラスでは、クラスごとに覚えて準備する)。</summary>
    public Dictionary<string, SpellBook> Books { get; set; } = [];

    /// <summary>そのクラスの呪文(無ければ作る)。</summary>
    public SpellBook Book(string classId) => Books.TryGetValue(classId, out var b) ? b : Books[classId] = new SpellBook();

    /// <summary>使った呪文スロット(呪文レベルごと。協約魔術のスロットは別に PactSlotsUsed)。大休憩で戻る。</summary>
    public Dictionary<int, int> SlotsUsed { get; set; } = [];

    /// <summary>使ったウォーロックの協約魔術のスロット。小休憩でも戻る。</summary>
    public int PactSlotsUsed { get; set; }

    /// <summary>クラスの能力の使った回数(ClassFeatures の回数の名前ごと)。大休憩で全部、小休憩で小休憩のものが戻る。</summary>
    public Dictionary<string, int> FeatureUses { get; set; } = [];

    /// <summary>直前の小休憩で、秘術の回復が戻したスロットのレベルの合計(画面で知らせる。セーブしない)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int LastSlotsRecovered { get; set; }

    /// <summary>一時的な HP(ダメージはここから減る。大休憩で消える)。</summary>
    public int TempHp { get; set; }

    /// <summary>
    /// エピックレベルの恩恵(L21 から1レベルごとに1つ選ぶ)。str〜cha は能力値 +1(30 まで)、hp は最大 HP +10、
    /// damage は武器のダメージ +1、save はセーヴ +1。
    /// </summary>
    public List<string> EpicBoons { get; set; } = [];

    public const int MaxEpicScore = 30;
    public static readonly string[] BoonKinds = ["str", "dex", "con", "int", "wis", "cha", "hp", "damage", "save"];

    /// <summary>まだ選んでいない恩恵の数。</summary>
    public int EpicPoints => Math.Max(0, Level - 20) - EpicBoons.Count;

    /// <summary>恩恵を1つ選ぶ。選べなければ false。</summary>
    public bool TakeBoon(string kind)
    {
        if (EpicPoints <= 0 || !BoonKinds.Contains(kind)) return false;
        if (kind.Length == 3 && kind != "hp" && Abilities.All.Any(a => a.Code() == kind) && Scores[Abilities.Parse(kind)] >= MaxEpicScore) return false;
        EpicBoons.Add(kind);
        if (kind == "hp")
        {
            MaxHp += 10;
            if (IsAlive) Hp += 10;
        }
        else if (Abilities.All.Any(a => a.Code() == kind)) Scores[Abilities.Parse(kind)]++;
        return true;
    }

    /// <summary>
    /// 能力値上昇で上げた能力値(1点ごとに1つ。str〜cha)。5版どおり、クラスのレベル 4・8・12・16・19(ファイターは 6・14、
    /// ローグは 10 も)で2点ずつ得て、訓練場で振る。能力値は 20 まで。
    /// </summary>
    public List<string> AbilityIncreases { get; set; } = [];

    public const int MaxIncreasedScore = 20;

    /// <summary>種族の特性を持っているか(races.json の traits)。</summary>
    public bool HasTrait(GameData data, string trait) => data.Races.TryGetValue(RaceId, out var r) && r.Traits.Contains(trait);

    /// <summary>まだ振っていない能力値上昇の点(クラスごとのレベルで数えて足す)。</summary>
    public int AbilityPoints(GameData data) => 2 * Classes.Sum(x => ClassFeatures.AbilityIncreaseCount(data, x.ClassId, x.Level)) - AbilityIncreases.Count;

    /// <summary>能力値上昇の点を1つ振る(その能力値 +1)。振れなければ false。耐久力の修正値が上がれば、最大 HP もレベルの分上がる。</summary>
    public bool IncreaseAbility(GameData data, Ability a)
    {
        if (AbilityPoints(data) <= 0 || Scores[a] >= MaxIncreasedScore) return false;
        int conBefore = Scores.Mod(Ability.Con);
        AbilityIncreases.Add(a.Code());
        Scores[a]++;
        if (a == Ability.Con && Scores.Mod(Ability.Con) > conBefore)
        {
            MaxHp += Level;
            if (IsAlive) Hp += Level;
        }
        return true;
    }

    /// <summary>能力値上昇の点を、おまかせで全部振る(ボット・確認用の起動)。クラスの主な能力値、耐久力、敏捷力の順に 20 まで。</summary>
    public void AutoIncreaseAbilities(GameData data)
    {
        var order = data.Classes[ClassId].PrimaryAbilities.Concat([Ability.Con, Ability.Dex]).Concat(Abilities.All).Distinct().ToList();
        while (AbilityPoints(data) > 0)
        {
            var a = order.FirstOrDefault(x => Scores[x] < MaxIncreasedScore, (Ability)(-1));
            if ((int)a < 0 || !IncreaseAbility(data, a)) break;
        }
    }

    /// <summary>武器のダメージに足す値(装備の damage の修正値と、エピックの恩恵)。</summary>
    public int DamageBonus(GameData data) => PassiveBonus(data, "damage") + EpicBoons.Count(b => b == "damage");

    /// <summary>戦闘のあとも残っている状態(毒など)。大休憩で消える。</summary>
    public List<Combat.LastingCondition> Lasting { get; set; } = [];

    public int ProficiencyBonus => Progression.ProficiencyBonus(Level);

    public bool IsAlive => State is LifeState.Alive;
    public bool CanAct => State == LifeState.Alive && Hp > 0;

    /// <summary>次のレベルに要る経験値に届いているか(上げるのは訓練場)。</summary>
    public bool CanLevelUp => Xp >= Progression.XpForLevel(Level + 1);

    public bool IsProficient(string skillId) => Skills.Contains(skillId);

    /// <summary>
    /// 装備の効果を入れた能力値(set_ability: 元の値より高ければ、その値にする。オーガの籠手の筋力 19 など)。
    /// 元の能力値は Scores に入っていて、レベルアップや HP の計算はそちらを使う。
    /// </summary>
    public AbilityScores Stats(GameData data)
    {
        var s = Scores.Clone();
        foreach (var p in Passive(data).Where(p => p.Type == Effects.EffectPart.SetAbility && p.Ability != null))
        {
            var a = Abilities.Parse(p.Ability!);
            s[a] = Math.Max(s[a], p.Value ?? 0);
        }
        return s;
    }

    /// <summary>
    /// 効いている装備の、持っているあいだの効果(同調の要るものは同調しているときだけ)。
    /// +7 からの属性つきの鎧・盾は、その属性への抵抗も足す。
    /// </summary>
    public IEnumerable<Effects.EffectPart> Passive(GameData data)
    {
        foreach (var (_, item, def) in Inventory.Active(data.Items))
        {
            foreach (var p in def.Passive) yield return p;
            foreach (var p in item.AffixPassive(data)) yield return p;
            if (def.Kind is ItemKind.Armor or ItemKind.Shield && item.Element != null && item.TotalBonus(def) >= ItemInstance.ElementFrom)
                yield return new Effects.EffectPart { Type = Effects.EffectPart.Resist, DamageType = item.Element };
        }
    }

    /// <summary>装備による修正値の合計(stat: ac / save / attack / damage / disarm / flee / 技能の id)。</summary>
    public int PassiveBonus(GameData data, string stat) =>
        Passive(data).Where(p => p.Type == Effects.EffectPart.Modifier && p.Stat == stat).Sum(p => p.Value ?? 0);

    public int SkillBonus(GameData data, string skillId)
    {
        var skill = data.Skills[skillId];
        int bonus = Stats(data).Mod(skill.AbilityValue) + (IsProficient(skillId) ? ProficiencyBonus : 0);
        return bonus + PassiveBonus(data, skillId);   // 装備の技能の上乗せ(隠密、知覚など)
    }

    public int SaveBonus(GameData data, Ability a) =>
        Stats(data).Mod(a) + (SaveProficient(data, a) ? ProficiencyBonus : 0) + PassiveBonus(data, "save") + EpicBoons.Count(b => b == "save");

    /// <summary>セーヴの習熟(最初のクラスと、能力の save_proficiency。つかみどころのない心で判断力、金剛の魂で全部)。</summary>
    public bool SaveProficient(GameData data, Ability a) =>
        data.Classes[ClassId].SavesWith(a)
        || PassiveRules.Effects(data, this, "save_proficiency").Any(e => e.Effect.Ability == null || Abilities.Parse(e.Effect.Ability) == a);

    /// <summary>着ている鎧の習熟があるか(無いと攻撃が不利になり、呪文を唱えられない。5版)。鎧と盾の両方を見る。</summary>
    public bool ArmorProficient(GameData data)
    {
        if (Inventory[EquipSlot.Armor] is { } armor && data.Items.TryGetValue(armor.ItemId, out var a) && !CanWear(data, a)) return false;
        if (Inventory[EquipSlot.OffHand] is { } off && data.Items.TryGetValue(off.ItemId, out var s) && !CanWear(data, s)) return false;
        return true;
    }

    /// <summary>
    /// その武器の習熟があるか(武器でない品は true)。分類(単純・軍用)か、武器そのもの(魔法の武器は元の武器 base でも)。
    /// 習熟が無くても持てるが、攻撃に習熟ボーナスが付かない(5版)。
    /// </summary>
    public bool WeaponProficient(GameData data, ItemDef def) =>
        def.Kind != ItemKind.Weapon || ClassIds.Select(id => data.Classes[id])
            .Any(c => c.Weapons.Contains(def.Category ?? "") || c.Weapons.Contains(def.Id) || def.Base != null && c.Weapons.Contains(def.Base));

    /// <summary>その品を習熟ありで使えるか(鎧・盾は着られるか、武器は習熟があるか)。</summary>
    public bool Proficient(GameData data, ItemDef def) => CanWear(data, def) && WeaponProficient(data, def);

    /// <summary>その鎧か盾の習熟があるか(鎧と盾でない品は true)。装備の画面は、習熟の無い鎧と盾を付けさせない。</summary>
    public bool CanWear(GameData data, ItemDef def)
    {
        string? need = def.Kind == ItemKind.Shield ? "shield" : def.ArmorType?.ToString().ToLowerInvariant();
        return need == null || ClassIds.Select(id => data.Classes[id]).Any(c => c.Armor.Contains(need));
    }

    /// <summary>
    /// AC。鎧を着ていれば鎧の AC + 敏捷力の修正値(上限あり)。着ていなければ 10 + 敏捷力
    /// (バーバリアンとモンクは、それに耐久力か判断力の修正値も足す)。盾を持っていれば +2。
    /// </summary>
    /// <param name="unarmoredBase">鎧を着ていないときの基本の AC の別の計算(メイジ・アーマーの 13。敏捷力の修正を足す)。鎧なしの防御と比べて高い方を使う(5版)。</param>
    /// <param name="now">能力の効く場面を決める今の様子(戦闘の激怒など。省けば戦闘の外)。</param>
    public int ArmorClass(GameData data, int? unarmoredBase = null, PassiveRules.Situation? now = null)
    {
        var stats = Stats(data);
        int dex = stats.Mod(Ability.Dex);
        int ac;
        if (Inventory[EquipSlot.Armor] is { } armor && data.Items.TryGetValue(armor.ItemId, out var a))
        {
            // 重装鎧(上限 0)は敏捷力の修正を足さない(マイナスでも引かない)。中装鎧は上限まで
            ac = a.ArmorClass + armor.HitAcBonus(a) + (a.DexCap is 0 ? 0 : a.DexCap is int cap ? Math.Min(dex, cap) : dex);
        }
        else
        {
            ac = 10 + dex;
            // 鎧なしの防御(能力の ac_ability): まとまり unarmored_defense の、先に取ったクラスのもの(効く場面に当たらなければ付かない。5版)
            foreach (var e in PassiveRules.Effects(data, this, "ac_ability", now ?? new PassiveRules.Situation()))
                ac += stats.Mod(Abilities.Parse(e.Effect.Ability!));
            if (unarmoredBase is int ub) ac = Math.Max(ac, ub + dex);
        }
        if (Inventory[EquipSlot.OffHand] is { } off && data.Items.TryGetValue(off.ItemId, out var s) && s.Kind == ItemKind.Shield)
            ac += s.ArmorClass + off.HitAcBonus(s);
        ac += PassiveRules.Sum(data, this, "ac_bonus", now: now);   // 能力の ac_bonus(ファイティングスタイルの防御は、武器を持たず鎧を着ているとき)
        return ac + PassiveBonus(data, "ac");
    }

    /// <summary>1 レベル上がるときの HP の増え方(平均値で固定: 上げるクラスのヒットダイスの半分 + 1 + 耐久力。最低 1)。</summary>
    public int HpGainPerLevel(GameData data, string? classId = null)
    {
        var cls = data.Classes[classId ?? ClassId];
        var race = data.Races[RaceId];
        return Math.Max(1, cls.HitDie / 2 + 1 + Scores.Mod(Ability.Con) + race.HpPerLevel);
    }

    /// <summary>
    /// 訓練場でのレベルアップ。経験値が足りていなければ何もしない。上がった数を返す。
    /// classId は上げるクラス(省略すると最初のクラス。マルチクラスで別のクラスを選ぶ画面は M8)。
    /// </summary>
    public int LevelUp(GameData data, string? classId = null, int maxLevels = int.MaxValue)
    {
        NormalizeClasses();
        classId ??= ClassId;
        int gained = 0;
        while (CanLevelUp && gained < maxLevels)
        {
            Level++;
            var entry = Classes.FirstOrDefault(x => x.ClassId == classId);
            if (entry == null) Classes.Add(entry = new ClassLevel { ClassId = classId, Level = 0 });
            entry.Level++;
            int hp = HpGainPerLevel(data, classId);
            MaxHp += hp;
            if (IsAlive) Hp += hp;
            HitDice++;
            gained++;
        }
        if (gained > 0) Spellcasting.AutoPick(data, this);   // 増えた分の呪文を、おまかせで足す(訓練場で選び直せる)
        return gained;
    }

    /// <summary>
    /// 次のレベルで上げられるクラスか。今のクラスなら上げられる。新しいクラス(マルチクラス)は、5版どおり、
    /// 新しいクラスと今のクラス全部のマルチクラスの条件(能力値)を満たすときだけ。
    /// </summary>
    public bool CanTakeClass(GameData data, string classId)
    {
        if (ClassIds.Contains(classId)) return true;
        if (!data.Classes.TryGetValue(classId, out var cls)) return false;
        return cls.MeetsMulticlass(Scores) && ClassIds.All(id => data.Classes[id].MeetsMulticlass(Scores));
    }

    /// <summary>大休憩(宿屋)。HP が全部戻り、ヒットダイスがレベルの半分(最低 1)戻る。</summary>
    public void LongRest(double hpFraction = 1.0)
    {
        if (!IsAlive) return;
        Hp = Math.Max(Hp, Math.Min(MaxHp, (int)Math.Ceiling(MaxHp * hpFraction)));
        HitDice = Math.Min(Level, HitDice + Math.Max(1, Level / 2));
        SlotsUsed.Clear();
        PactSlotsUsed = 0;
        TempHp = 0;
        FeatureUses.Clear();
        Lasting.Clear();
        // ワンドや杖の回数が戻る
        foreach (var item in Inventory.Items.Concat(Inventory.Equipped.Values).Where(i => i.Charges != null)) item.Charges = null;
    }

    /// <summary>
    /// 小休憩(キャンプで1時間)。ヒットダイスを1つずつ使って HP を戻す(最大 HP まで、ヒットダイスがある限り)。
    /// ウォーロックは協約魔術のスロットが戻る。使ったヒットダイスの数を返す。
    /// </summary>
    public int ShortRest(GameData data, IRandom rng)
    {
        if (!IsAlive) return 0;
        var cls = data.Classes[ClassId];   // ヒットダイスの大きさは最初のクラス(マルチクラスの混ざったヒットダイスは M8)
        int used = 0;
        while (HitDice > 0 && Hp < MaxHp)
        {
            HitDice--;
            used++;
            Hp = Math.Min(MaxHp, Hp + Math.Max(1, rng.Roll(cls.HitDie) + Scores.Mod(Ability.Con)));
        }
        PactSlotsUsed = 0;   // 協約魔術のスロットが戻る(ふつうのスロットは戻らない)
        LastSlotsRecovered = ClassFeatures.ShortRest(data, this);   // 小休憩で戻る技の回数と、ウィザードの秘術の回復
        return used;
    }
}

/// <summary>キャラ作成の選択。</summary>
public sealed record CharacterChoices(
    string Name,
    string RaceId,
    string ClassId,
    string BackgroundId,
    Alignment Alignment,
    AbilityScores BaseScores,
    IReadOnlyList<Ability> FreeAbilityPicks,
    IReadOnlyList<string> ClassSkills,
    IReadOnlyList<string> FreeSkills);

/// <summary>キャラ作成。選択を確かめてから、能力値・HP・技能・最初の装備と金を決める。</summary>
public static class CharacterFactory
{
    public const int MaxNameLength = 12;

    /// <summary>ポイント購入: 使える点数と、値ごとの点数(5版)。</summary>
    public const int PointBuyBudget = 27;

    public static int PointCost(int score) => score switch
    {
        8 => 0, 9 => 1, 10 => 2, 11 => 3, 12 => 4, 13 => 5, 14 => 7, 15 => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(score), "ポイント購入では 8〜15 です"),
    };

    /// <summary>
    /// 能力値の標準の値(5版)。ランダムには振らず、この6つを割り振る
    /// (振り直しで良い値を狙うと、1人作るのに何時間もかかるため。ユーザーの指定)。
    /// </summary>
    public static readonly int[] StandardArray = [15, 14, 13, 12, 10, 8];

    /// <summary>振った値を、クラスの大事な能力値の順に、高い値から割り当てる(おまかせ)。</summary>
    public static AbilityScores AutoAssign(IEnumerable<int> values, ClassDef cls)
    {
        var order = cls.PrimaryAbilities.Concat(Abilities.All).Distinct().ToList();
        var sorted = values.OrderByDescending(v => v).ToList();
        var scores = new AbilityScores();
        for (int i = 0; i < order.Count && i < sorted.Count; i++) scores[order[i]] = sorted[i];
        return scores;
    }

    /// <summary>選択の問題を返す(無ければ空)。</summary>
    public static List<string> Validate(GameData data, CharacterChoices c)
    {
        var p = new List<string>();
        string name = c.Name.Trim();
        if (name.Length == 0) p.Add("名前がありません");
        if (name.Length > MaxNameLength) p.Add($"名前は {MaxNameLength} 文字までです");
        if (!data.Races.TryGetValue(c.RaceId, out var race)) p.Add($"種族 '{c.RaceId}' がありません");
        if (!data.Classes.TryGetValue(c.ClassId, out var cls)) p.Add($"クラス '{c.ClassId}' がありません");
        if (!data.Backgrounds.TryGetValue(c.BackgroundId, out var bg)) p.Add($"背景 '{c.BackgroundId}' がありません");
        foreach (var a in Abilities.All)
        {
            if (c.BaseScores[a] is < 3 or > 18) p.Add($"{a} の値 {c.BaseScores[a]} は 3〜18 の外です");
        }
        if (race == null || cls == null || bg == null) return p;

        if (c.FreeAbilityPicks.Count != race.FreeAbilityPoints || c.FreeAbilityPicks.Distinct().Count() != c.FreeAbilityPicks.Count)
            p.Add($"好きな能力値を {race.FreeAbilityPoints} つ(別々に)選んでください");
        if (c.FreeAbilityPicks.Any(a => race.AbilityBonus.ContainsKey(a.Code())))
            p.Add("種族で上がる能力値は、好きな能力値に選べません");

        var allowed = data.SkillChoicesFor(cls).ToHashSet();
        if (c.ClassSkills.Count != cls.SkillChoices || c.ClassSkills.Distinct().Count() != c.ClassSkills.Count)
            p.Add($"クラスの技能を {cls.SkillChoices} つ(別々に)選んでください");
        if (c.ClassSkills.Any(s => !allowed.Contains(s))) p.Add("そのクラスでは選べない技能があります");
        if (c.FreeSkills.Count != race.FreeSkills || c.FreeSkills.Any(s => !data.Skills.ContainsKey(s)))
            p.Add($"好きな技能を {race.FreeSkills} つ選んでください");
        var fixedSkills = race.Skills.Concat(bg.Skills).ToHashSet();
        if (c.ClassSkills.Concat(c.FreeSkills).Any(fixedSkills.Contains) || c.ClassSkills.Intersect(c.FreeSkills).Any())
            p.Add("種族や背景で覚える技能と同じ技能は、選べません");
        return p;
    }

    public static Character Create(GameData data, CharacterChoices c)
    {
        var problems = Validate(data, c);
        if (problems.Count > 0) throw new ArgumentException(string.Join(" / ", problems));
        var race = data.Races[c.RaceId];
        var cls = data.Classes[c.ClassId];
        var bg = data.Backgrounds[c.BackgroundId];

        var scores = c.BaseScores.Clone();
        foreach (var (code, bonus) in race.AbilityBonus) scores[Abilities.Parse(code)] += bonus;
        foreach (var a in c.FreeAbilityPicks) scores[a] += 1;

        int hp = Math.Max(1, cls.HitDie + Abilities.Modifier(scores.Con) + race.HpPerLevel);
        var ch = new Character
        {
            Name = c.Name.Trim(),
            RaceId = race.Id,
            ClassId = cls.Id,
            BackgroundId = bg.Id,
            Alignment = c.Alignment,
            Classes = [new ClassLevel { ClassId = cls.Id, Level = 1 }],
            Scores = scores,
            MaxHp = hp,
            Hp = hp,
            HitDice = 1,
            Skills = race.Skills.Concat(bg.Skills).Concat(c.ClassSkills).Concat(c.FreeSkills).Distinct().ToList(),
            // 最初の金もランダムにしない(ダイスの平均を 10 倍した決まった額)
            Gold = (long)Math.Round(DiceExpression.Parse(cls.StartGold).Average * 10),
        };
        GiveStartingItems(data, ch, cls);
        Spellcasting.AutoPick(data, ch);   // 呪文はおまかせで選んでおく(訓練場で選び直せる)
        return ch;
    }

    /// <summary>
    /// 選択をおまかせにして1人作る(シミュレータ、デバッグ、テスト用)。能力値は標準の値をクラスに合わせて並べ、
    /// 技能と好きな能力値は選べるものの先頭から取る。level まで経験値を入れて上げる。
    /// </summary>
    public static Character QuickCreate(GameData data, string name, string raceId, string classId, int level = 1, string backgroundId = "soldier")
    {
        var cls = data.Classes[classId];
        var race = data.Races[raceId];
        var bg = data.Backgrounds.ContainsKey(backgroundId) ? data.Backgrounds[backgroundId] : data.Backgrounds.Values.First();
        var fixedSkills = race.Skills.Concat(bg.Skills).ToHashSet();
        var picks = data.SkillChoicesFor(cls).Where(s => !fixedSkills.Contains(s)).Take(cls.SkillChoices).ToList();
        var free = data.Skills.Keys.Order(StringComparer.Ordinal).Where(s => !fixedSkills.Contains(s) && !picks.Contains(s)).Take(race.FreeSkills).ToList();
        var freeAb = cls.PrimaryAbilities.Concat(Abilities.All).Distinct()
            .Where(a => !race.AbilityBonus.ContainsKey(a.Code())).Take(race.FreeAbilityPoints).ToList();
        var c = Create(data, new CharacterChoices(name, raceId, classId, bg.Id, Alignment.Neutral,
            AutoAssign(StandardArray, cls), freeAb, picks, free));
        if (level > 1)
        {
            c.Xp = Progression.XpForLevel(level);
            c.LevelUp(data);
            c.AutoIncreaseAbilities(data);
            Spellcasting.AutoPick(data, c);   // 能力値が上がって増えた準備の枠を埋める
        }
        return c;
    }

    /// <summary>最初の装備: 武器を利き手に、盾をもう片方の手に、鎧を体に、袋を背に。残りは袋に入れる。</summary>
    private static void GiveStartingItems(GameData data, Character ch, ClassDef cls)
    {
        var inv = ch.Inventory;
        var rest = new List<ItemInstance>();
        foreach (var id in cls.StartItems)
        {
            var def = data.Items[id];
            var item = ItemInstance.Of(id);
            EquipSlot? slot = def.Kind switch
            {
                ItemKind.Backpack when inv[EquipSlot.Backpack] == null => EquipSlot.Backpack,
                ItemKind.Armor when inv[EquipSlot.Armor] == null => EquipSlot.Armor,
                ItemKind.Shield when inv[EquipSlot.OffHand] == null => EquipSlot.OffHand,
                ItemKind.Weapon when def.Ranged && inv[EquipSlot.Ranged] == null => EquipSlot.Ranged,
                ItemKind.Weapon when !def.Ranged && inv[EquipSlot.MainHand] == null => EquipSlot.MainHand,
                _ => null,
            };
            if (slot is { } s) inv.Equipped[s] = item;
            else rest.Add(item);
        }
        foreach (var item in rest)
        {
            if (!inv.TryAdd(item, data.Items)) break;
        }
    }
}
