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

    /// <summary>元の最大 HP(レベルで上がる分。セーブには max_hp としてこれだけを持つ)。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_hp")]
    public int BaseMaxHp { get; set; }

    /// <summary>最大 HP(元の分 + 装備の追加効果の分)。足し引きは元の分に入る。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int MaxHp
    {
        get => BaseMaxHp + GearHp;
        set => BaseMaxHp = value - GearHp;
    }

    /// <summary>装備の追加効果で増えている最大 HP(修正値 max_hp)。RefreshGearHp で数え直す。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int GearHp { get; private set; }

    /// <summary>装備の最大 HP の分を数え直す(装備を替えたあと。AC を数えるときと戦闘の始めにも呼ぶ)。減ったら今の HP も最大までにする。</summary>
    public void RefreshGearHp(GameData data)
    {
        int gear = Math.Max(0, PassiveBonus(data, "max_hp"));
        if (gear == GearHp) return;
        GearHp = gear;
        if (Hp > MaxHp) Hp = MaxHp;
    }

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

    /// <summary>
    /// 装備の追加効果(slots:&lt;レベル&gt;)で増えたスロットの数の、最後に見た値(呪文レベルごと)。装備を付け外しして増減したら、
    /// 残りはそのままで最大だけ変える(Spellcasting.Sync)。休んで戻したときに今の値にする。無ければ、今の値で数え始める。
    /// </summary>
    public Dictionary<int, int> SlotBonusSeen { get; set; } = [];

    /// <summary>協約魔術のスロットの、装備で増えた数の最後に見た値。</summary>
    public int? PactBonusSeen { get; set; }

    /// <summary>クラスの能力の使った回数(ClassFeatures の回数の名前ごと)。大休憩で全部、小休憩で小休憩のものが戻る。</summary>
    public Dictionary<string, int> FeatureUses { get; set; } = [];

    /// <summary>直前の小休憩で、秘術の回復が戻したスロットのレベルの合計(画面で知らせる。セーブしない)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int LastSlotsRecovered { get; set; }

    /// <summary>一時的な HP(ダメージはここから減る。大休憩で消える)。</summary>
    public int TempHp { get; set; }

    /// <summary>
    /// エピックの恩恵。4 つのクラスを全部上限まで上げたあと(合計 51 から)、1レベルごとに1つ選ぶ。
    /// hp は最大 HP +10、damage は武器のダメージ +1、spell は呪文のダメージと回復 +1。どれも上限なし。
    /// 前の形の恩恵(能力値・save)が残っていても、数えない(能力値と HP は取ったときに足してある)。
    /// </summary>
    public List<string> EpicBoons { get; set; } = [];

    public static readonly string[] BoonKinds = ["hp", "damage", "spell"];

    /// <summary>呪文の熟達(ウィザード L18)で選んだ呪文の id。枠を使わずに何度でも唱えられる(Spellcasting.Mastery)。</summary>
    public string? MasterySpell { get; set; }

    /// <summary>森の主(ドルイド L20)で選んだ召喚の呪文の id。戦闘の始めから、その召喚がいる(Spellcasting.Companion)。</summary>
    public string? CompanionSpell { get; set; }

    /// <summary>
    /// クラスのレベルの上限。取った順に 20・15・10・5(1人 4 クラスまで)。1つしか 20 にできないので、
    /// どれを深くしてどれをつまむかが作りになる。
    /// </summary>
    public static readonly int[] ClassCaps = [20, 15, 10, 5];
    public static int MaxClasses => ClassCaps.Length;

    /// <summary>この合計レベルを超えたら、クラスなしでレベルが上がり、恩恵を得る(上限の合計 50)。</summary>
    public static int EpicFrom => ClassCaps.Sum();

    /// <summary>エピックのレベルで増える HP(一律。d8 の平均 + 耐久力。最低 1)。</summary>
    public const int EpicHpBase = 5;

    /// <summary>クラスなしで上げたレベルの数(合計レベルからクラスのレベルを引いた分)。</summary>
    public int EpicLevels => Math.Max(0, Level - ClassIds.Sum(LevelIn));

    /// <summary>まだ選んでいない恩恵の数。</summary>
    public int EpicPoints => EpicLevels - EpicBoons.Count(BoonKinds.Contains);

    /// <summary>取った恩恵の数(種類ごと)。</summary>
    public int BoonCount(string kind) => EpicBoons.Count(b => b == kind);

    /// <summary>恩恵を1つ選ぶ。選べなければ false。</summary>
    public bool TakeBoon(string kind)
    {
        if (EpicPoints <= 0 || !BoonKinds.Contains(kind)) return false;
        EpicBoons.Add(kind);
        if (kind == "hp")
        {
            MaxHp += 10;
            if (IsAlive) Hp += 10;
        }
        return true;
    }

    /// <summary>
    /// 能力値上昇で上げた能力値(1点ごとに1つ。str〜cha)。5版どおり、クラスのレベル 4・8・12・16・19(ファイターは 6・14、
    /// ローグは 10 も)で2点ずつ得て、訓練場で振る。能力値は 20 まで。6つとも 20 になったら 30 まで。
    /// </summary>
    public List<string> AbilityIncreases { get; set; } = [];

    public const int MaxIncreasedScore = 20;
    public const int MaxScore = 30;

    /// <summary>能力値上昇で上げられる上限(6つとも 20 以上なら 30、そうでなければ 20)。score は仮の分を足した値を渡せる。</summary>
    public static int IncreaseCap(Func<Ability, int> score) => Abilities.All.All(a => score(a) >= MaxIncreasedScore) ? MaxScore : MaxIncreasedScore;

    public int IncreaseCap() => IncreaseCap(a => Scores[a]);

    /// <summary>種族の特性を持っているか(races.json の traits)。</summary>
    public bool HasTrait(GameData data, string trait) => data.Races.TryGetValue(RaceId, out var r) && r.Traits.Contains(trait);

    /// <summary>まだ振っていない能力値上昇の点(クラスごとのレベルで数えて足す)。</summary>
    public int AbilityPoints(GameData data) => 2 * Classes.Sum(x => ClassFeatures.AbilityIncreaseCount(data, x.ClassId, x.Level)) - AbilityIncreases.Count;

    /// <summary>能力値上昇の点を1つ振る(その能力値 +1)。振れなければ false。耐久力の修正値が上がれば、最大 HP もレベルの分上がる。</summary>
    public bool IncreaseAbility(GameData data, Ability a)
    {
        if (AbilityPoints(data) <= 0 || Scores[a] >= IncreaseCap()) return false;
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

    /// <summary>能力値上昇の点を、おまかせで全部振る(ボット・確認用の起動)。クラスの主な能力値、耐久力、敏捷力の順に上限まで。</summary>
    public void AutoIncreaseAbilities(GameData data)
    {
        var order = data.Classes[ClassId].PrimaryAbilities.Concat([Ability.Con, Ability.Dex]).Concat(Abilities.All).Distinct().ToList();
        while (AbilityPoints(data) > 0)
        {
            int cap = IncreaseCap();
            var a = order.FirstOrDefault(x => Scores[x] < cap, (Ability)(-1));
            if ((int)a < 0 || !IncreaseAbility(data, a)) break;
        }
    }

    /// <summary>武器のダメージに足す値(装備の damage の修正値と、エピックの恩恵)。</summary>
    public int DamageBonus(GameData data) => PassiveBonus(data, "damage") + BoonCount("damage");

    /// <summary>戦闘のあとも残っている状態(毒など)。大休憩で消える。</summary>
    public List<Combat.LastingCondition> Lasting { get; set; } = [];

    /// <summary>戦闘のあとも続いている呪文の効き目(メイジ・アーマーなど)。1時間までのものは歩数で減り、長いものは大休憩か町に戻るまで。</summary>
    public List<Combat.LastingBuff> Buffs { get; set; } = [];

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
        foreach (var (slot, item, def) in Inventory.Active(data.Items))
        {
            foreach (var p in def.Passive) yield return p;
            // 武器の追加効果: 命中・ダメージ・薙ぎは、その武器で殴ったときだけ(CombatEngine の攻撃で足す。二刀流で重ならないように)。
            // それ以外(呪文・抵抗・能力値など)は、利き手・逆手・遠隔のどれでも持っているだけで効く
            foreach (var p in item.AffixPassive(data, Level))
                if (def.Kind != ItemKind.Weapon || p.Type != Effects.EffectPart.Modifier || !AffixDef.WeaponLocalStats.Contains(p.Stat)) yield return p;
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
        Stats(data).Mod(a) + (SaveProficient(data, a) ? ProficiencyBonus : 0) + PassiveBonus(data, "save");

    /// <summary>セーヴの習熟(最初のクラスと、能力の save_proficiency。つかみどころのない心で判断力、金剛の魂で全部)。</summary>
    public bool SaveProficient(GameData data, Ability a) =>
        data.Classes[ClassId].SavesWith(a)
        || PassiveRules.Effects(data, this, "save_proficiency").Any(e => e.Effect.Ability == null || Abilities.Parse(e.Effect.Ability) == a);

    /// <summary>鎧を着ているか(ローブは鎧ではない。鎧なしの防御・武術・メイジ・アーマーはローブの上からも効く)。</summary>
    public bool WearingArmor(GameData data) =>
        Inventory[EquipSlot.Armor] is { } armor && !(data.Items.TryGetValue(armor.ItemId, out var a) && a.ArmorType == ArmorType.Robe);

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
        if (def.ArmorType == ArmorType.Robe) return true;   // ローブは誰でも着られる
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
        RefreshGearHp(data);   // AC は装備の画面やパーティの欄でいつも数えるので、ここで最大 HP の装備の分もそろえる
        var stats = Stats(data);
        int dex = stats.Mod(Ability.Dex);
        int ac;
        if (Inventory[EquipSlot.Armor] is { } armor && data.Items.TryGetValue(armor.ItemId, out var a) && a.ArmorType != ArmorType.Robe)
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
            // ローブ: 鎧なしの AC に、強化値の AC の分だけ足す
            if (Inventory[EquipSlot.Armor] is { } robe && data.Items.TryGetValue(robe.ItemId, out var rd)) ac += robe.HitAcBonus(rd);
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
    /// classId は上げるクラス。省略すると、20 に届いていない今のクラス(先に取ったものから)、それも無ければ取れる新しいクラス。
    /// クラスは 4 つまで、上限は取った順に 20・15・10・5。4 つとも上限になったあと(合計 51 から)はクラスなしで上がり、
    /// HP は一律(<see cref="EpicHpBase"/> + 耐久力)、恩恵の点が1つ増える。上げられるクラスが無ければ止まる。
    /// </summary>
    public int LevelUp(GameData data, string? classId = null, int maxLevels = int.MaxValue)
    {
        NormalizeClasses();
        int gained = 0;
        while (CanLevelUp && gained < maxLevels)
        {
            int hp;
            if (IsEpic)
            {
                hp = EpicHpGain(data);
            }
            else
            {
                // 選んだクラスは数と上限だけを見る(能力値の条件は、選ぶ画面で見ている)
                string? id = classId ?? AutoClass(data);
                if (id == null || !WithinClassLimits(id)) break;
                var entry = Classes.FirstOrDefault(x => x.ClassId == id);
                if (entry == null) Classes.Add(entry = new ClassLevel { ClassId = id, Level = 0 });
                entry.Level++;
                hp = HpGainPerLevel(data, id);
            }
            Level++;
            MaxHp += hp;
            if (IsAlive) Hp += hp;
            HitDice++;
            gained++;
        }
        if (gained > 0) Spellcasting.AutoPick(data, this);   // 増えた分の呪文を、おまかせで足す(訓練場で選び直せる)
        return gained;
    }

    /// <summary>4 つのクラスが全部上限で、次はクラスなしのエピックのレベルか。</summary>
    public bool IsEpic => ClassIds.Count() >= MaxClasses && ClassIds.All(id => LevelIn(id) >= ClassCap(id));

    /// <summary>そのクラスのレベルの上限(取った順で決まる。まだ取っていなければ、次に取ったときの上限)。</summary>
    public int ClassCap(string classId)
    {
        var ids = ClassIds.ToList();
        int i = ids.IndexOf(classId);
        if (i < 0) i = ids.Count;
        return i < ClassCaps.Length ? ClassCaps[i] : 0;
    }

    /// <summary>エピックのレベル1つで増える HP(一律。クラスのヒットダイスによらない)。</summary>
    public int EpicHpGain(GameData data) =>
        Math.Max(1, EpicHpBase + Scores.Mod(Ability.Con) + (data.Races.TryGetValue(RaceId, out var race) ? race.HpPerLevel : 0));

    /// <summary>おまかせで上げるクラス: 20 に届いていない今のクラス、無ければ取れる新しいクラス(クラスの並び順)。</summary>
    public string? AutoClass(GameData data) =>
        ClassIds.FirstOrDefault(id => LevelIn(id) < ClassCap(id))
        ?? data.ClassOrder.FirstOrDefault(id => !ClassIds.Contains(id) && CanTakeClass(data, id));

    /// <summary>今、上げられるクラスがあるか(エピックのレベルなら、クラスは要らない)。</summary>
    public bool HasLevelToTake(GameData data) => IsEpic || AutoClass(data) != null;

    /// <summary>
    /// 次のレベルで上げられるクラスか。今のクラスは上限(取った順に 20・15・10・5)まで上げられる。新しいクラス(マルチクラス)は、クラスが 4 つ未満で、5版どおり
    /// 新しいクラスと今のクラス全部のマルチクラスの条件(能力値)を満たすときだけ。
    /// </summary>
    public bool CanTakeClass(GameData data, string classId)
    {
        if (!WithinClassLimits(classId)) return false;
        if (ClassIds.Contains(classId)) return true;
        if (!data.Classes.TryGetValue(classId, out var cls)) return false;
        return cls.MeetsMulticlass(Scores) && ClassIds.All(id => data.Classes[id].MeetsMulticlass(Scores));
    }

    /// <summary>クラスの数(4 つまで)と、そのクラスのレベルの上限(取った順に 20・15・10・5)に収まるか。</summary>
    public bool WithinClassLimits(string classId) =>
        ClassIds.Contains(classId) ? LevelIn(classId) < ClassCap(classId) : ClassIds.Count() < MaxClasses;

    /// <summary>大休憩(宿屋)。HP が hpFraction まで戻り、ヒットダイスが全部戻る(5版の 2024 年の決まりと同じ。何泊でもできるので、半分ずつ戻しても手間が増えるだけ)。</summary>
    /// <param name="data">渡すと、装備で増えたスロットも満タンとして数え始める(渡さなければ、次に数えたときの値から)。</param>
    public void LongRest(double hpFraction = 1.0, GameData? data = null)
    {
        if (!IsAlive) return;
        Hp = Math.Max(Hp, Math.Min(MaxHp, (int)Math.Ceiling(MaxHp * hpFraction)));
        HitDice = Level;
        SlotsUsed.Clear();
        PactSlotsUsed = 0;
        if (data != null) { Magic.Spellcasting.MarkFull(data, this); Magic.Spellcasting.DropCampOnlyPrepared(data, this); }
        else { SlotBonusSeen.Clear(); PactBonusSeen = null; }
        TempHp = 0;
        FeatureUses.Clear();
        Lasting.Clear();
        Buffs.Clear();
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
        int used = 0, mending = Math.Max(0, PassiveBonus(data, "hit_die_heal"));   // 手当ての(装備): ヒットダイス1つごとに +N
        while (HitDice > 0 && Hp < MaxHp)
        {
            HitDice--;
            used++;
            Hp = Math.Min(MaxHp, Hp + Math.Max(1, rng.Roll(cls.HitDie) + Scores.Mod(Ability.Con)) + mending);
        }
        Combat.Lasting.Pass(this, 60);   // 小休憩は1時間。1時間の呪文の効き目は切れる
        PactSlotsUsed = 0;   // 協約魔術のスロットが戻る(ふつうのスロットは戻らない)
        PactBonusSeen = Magic.Spellcasting.PactBonus(data, this);
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
