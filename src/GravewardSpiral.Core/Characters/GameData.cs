using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using Traps_ = GravewardSpiral.Core.Items.Traps;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

public sealed class SkillDef
{
    public string Id { get; set; } = "";
    public string Ability { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");

    /// <summary>説明(何をする技能か、このゲームでの使いどころ)。キャラ作成で技能を選ぶときに出す。</summary>
    public LocalizedText? Description { get; set; }

    public Ability AbilityValue => Abilities.Parse(Ability);
}

public sealed class RaceDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public LocalizedText? Subrace { get; set; }
    public Dictionary<string, int> AbilityBonus { get; set; } = [];

    /// <summary>好きな能力値に 1 ずつ足せる数(ハーフエルフ)。</summary>
    public int FreeAbilityPoints { get; set; }

    /// <summary>好きな技能を覚えられる数(ハーフエルフ)。</summary>
    public int FreeSkills { get; set; }

    public List<string> Skills { get; set; } = [];
    public bool Darkvision { get; set; }
    public int HpPerLevel { get; set; }
    public List<string> Traits { get; set; } = [];

    /// <summary>種族の技と能力(features.json の id。level は合計のレベル)。数は合計のレベルで数え、class_level は使えない。</summary>
    public List<FeatureRef> Features { get; set; } = [];

    /// <summary>種族の技の回数(上限と戻り方。上限は合計のレベルで数える)。</summary>
    public List<PoolDef> Pools { get; set; } = [];

    public LocalizedText? Description { get; set; }
}

public sealed class ClassDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public int HitDie { get; set; } = 8;

    /// <summary>伸び方(攻撃の回数と能力値上昇のレベル)。docs/class-data-design.md の progression。</summary>
    public ProgressionDef? Progression { get; set; }

    /// <summary>覚える技と能力(features.json の id と、覚えるクラスのレベル)。</summary>
    public List<FeatureRef> Features { get; set; } = [];

    /// <summary>技の回数(上限と戻り方)。状態の画面には、この並びで残りを出す。</summary>
    public List<PoolDef> Pools { get; set; } = [];

    /// <summary>大事な能力値の順(能力値を「おまかせ」で割り当てるときに使う)。</summary>
    public List<string> Primary { get; set; } = [];

    public List<string> Saves { get; set; } = [];
    public List<string> Armor { get; set; } = [];
    public List<string> Weapons { get; set; } = [];
    public List<string> Tools { get; set; } = [];

    /// <summary>クラスのタグ(arcane・divine など。罠やイベントの who の tag:&lt;名前&gt; で当たる相手を決める)。</summary>
    public List<string> Tags { get; set; } = [];
    public int SkillChoices { get; set; }

    /// <summary>選べる技能。["*"] ならどれでも。</summary>
    public List<string> Skills { get; set; } = [];

    /// <summary>呪文の扱い。術者のクラスだけが持つ(docs/class-data-design.md の spellcasting)。</summary>
    public SpellcastingDef? Spellcasting { get; set; }

    /// <summary>
    /// マルチクラスの条件(5版)。どれか1つの組を満たせばよく、組の中は全部の能力値が要る(例: ファイターは 筋力 13 か 敏捷力 13)。
    /// 新しいクラスを取るときは、新しいクラスと今のクラス全部の条件を満たす必要がある。
    /// </summary>
    public List<Dictionary<string, int>> Multiclass { get; set; } = [];

    /// <summary>キャラクターの能力値がマルチクラスの条件を満たすか(条件が無ければ満たす)。</summary>
    public bool MeetsMulticlass(AbilityScores scores) =>
        Multiclass.Count == 0 || Multiclass.Any(group => group.All(kv => scores[Abilities.Parse(kv.Key)] >= kv.Value));

    /// <summary>最初の金のダイス式。10 倍して金貨にする(5版の初期の財産と同じ)。</summary>
    public string StartGold { get; set; } = "4d4";

    public List<string> StartItems { get; set; } = [];
    public LocalizedText? Description { get; set; }

    public IEnumerable<Ability> PrimaryAbilities => Primary.Select(Abilities.Parse);
    public bool SavesWith(Ability a) => Saves.Any(s => Abilities.Parse(s) == a);
}

/// <summary>
/// クラスの呪文の扱い。ability は呪文に使う能力値、style は 準備する(prepared)・覚える(known)・呪文書から準備する(book)・協約魔術(pact)、
/// progression はスロットの伸び方(full / half / third / pact。マルチクラスの術者のレベルの数え方もこれで決まる)、
/// from_level は呪文を使い始めるクラスのレベル(それより下では、覚える数も準備の数も 0)。
/// cantrips・known・prepared は覚える初級呪文の数・覚える呪文の数・準備できる数で、そのクラスのレベルで数える。
/// </summary>
public sealed class SpellcastingDef
{
    public string Ability { get; set; } = "";
    public CasterKind Style { get; set; }
    /// <summary>スロットの伸び方(spell_progressions.json の id)。</summary>
    public string Progression { get; set; } = "";
    public int FromLevel { get; set; } = 1;
    public LevelNumber? Cantrips { get; set; }
    public LevelNumber? Known { get; set; }
    public LevelNumber? Prepared { get; set; }
    public List<SpellAccess> SpellAccess { get; set; } = [];
}

/// <summary>クラスのレベル lv になったら、set の束の呪文を spelllv レベルの呪文として使える(0 は初級呪文)。</summary>
public sealed class SpellAccess
{
    public int Lv { get; set; } = 1;
    public int Spelllv { get; set; }
    public string Set { get; set; } = "";
}

/// <summary>
/// クラスの伸び方。attacks は1回の攻撃で振る回数(そのクラスのレベルで数える。書かなければ 1。マルチクラスは一番多いもの)、
/// ability_increases は能力値上昇を得るクラスのレベル(書かなければ 5版のふつうの 4・8・12・16・19。マルチクラスはクラスごとに数えて足す)。
/// </summary>
public sealed class ProgressionDef
{
    public static readonly IReadOnlyList<int> StandardAbilityIncreases = [4, 8, 12, 16, 19];

    public LevelNumber? Attacks { get; set; }
    public List<int>? AbilityIncreases { get; set; }
}

/// <summary>
/// 技の回数1つ。max は上限(数の書き方。そのクラスのレベルで数える)、recharge は戻る休憩(short / long)、from_level はこのクラスのレベルから持つ
/// (それより下では 0)、short_with はその勝手に効く能力を持てば小休憩でも戻る(鼓舞と鼓舞の泉)。
/// 2つのクラスが同じ id の回数を持てば、上限は大きいほう。小休憩では、小休憩で戻るクラスの上限まで満たす。
/// </summary>
public sealed class PoolDef
{
    public string Id { get; set; } = "";
    public LevelNumber Max { get; set; } = LevelNumber.Of(0);
    public string Recharge { get; set; } = "long";
    public int FromLevel { get; set; } = 1;
    public string? ShortWith { get; set; }
}

/// <summary>クラスが覚える技か能力(features.json の id と、覚えるクラスのレベル)。name はこのクラスでの呼び名の文言のキー、subclass は見込み(今は使わない)。</summary>
public sealed class FeatureRef
{
    public string Id { get; set; } = "";
    public int Level { get; set; } = 1;
    public string? Name { get; set; }
    public string? Subclass { get; set; }
}

/// <summary>
/// 技(action)か、勝手に効く能力(passive)の中身(features.json)。values はその能力の数(名前と数の書き方。武術のダイスなど)で、
/// 能力をくれたクラスのレベルで数える。効き方そのものは、まだコアが id で知っている(名前で呼ぶ特別な部品。Step 4c・5 で効果の部品に)。
/// </summary>
public sealed class ClassFeatureDef
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public Dictionary<string, LevelNumber> Values { get; set; } = [];

    /// <summary>効く場面(PassiveRules.Whens)。書かなければ always。</summary>
    public string When { get; set; } = "always";

    /// <summary>重なりを数えるまとまり。書かなければ id。</summary>
    public string? Group { get; set; }

    /// <summary>まとまりの中での重なり方(own / max / first)。</summary>
    public string Stacks { get; set; } = "own";

    /// <summary>効果の部品(PassiveRules.EffectTypes)。書いていない能力の効き方は、コアが id で知っている。</summary>
    public List<PassiveEffectDef> Effects { get; set; } = [];

    /// <summary>状態の画面の能力の一覧に出すか(ファイティングスタイルは、専用の行に出すので false)。</summary>
    public bool Listed { get; set; } = true;

    // ---- 技(kind が action)の項目。効き方は、まだコアが id で知っている(Step 5c で数をデータに) ----

    /// <summary>技の動きの部品(ClassFeatures.ActionParts の名前)。省くと id。別の id で同じ部品を使えば、数や回数の違う派生の技になる。</summary>
    public string? Part { get; set; }

    /// <summary>技の動きの部品の名前(part、省けば id)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string PartId => Part ?? Id;

    /// <summary>技の相手(self / ally / enemy_group / none)。</summary>
    public FeatureTarget Target { get; set; } = FeatureTarget.Self;

    /// <summary>技が使う回数(クラスの pools の id)。無ければ回数を使わない(ディヴァイン・スマイトは呪文スロット)。</summary>
    public string? Pool { get; set; }

    /// <summary>技1回で使う数。</summary>
    public int Cost { get; set; } = 1;

    /// <summary>獣の姿でも使えるか(武器や素手の打撃に乗せる技は false)。</summary>
    public bool BeastForm { get; set; } = true;

    /// <summary>技のダイス(セカンド・ウィンドの回復の 1d10 など。呪文と同じ書き方)。</summary>
    public string? Dice { get; set; }

    /// <summary>変身の技で選べる変身先(shapes.json の id)。キーはその技をくれたクラスのレベルで、そのレベル以下の一番大きなキーの一覧を使う。</summary>
    public Dictionary<string, List<string>> Shapes { get; set; } = [];
}

/// <summary>
/// 勝手に効く能力の効果1つ。to は self(自分)か allies(パーティ全員。オーラ)。ability・condition・damage_type・value は種類ごとに使う
/// (ac_ability は鎧なしの AC に足す能力値、save_proficiency はそのセーヴの習熟(書かなければ全部)、save_bonus は value をセーヴに足す(ability があればその能力値のセーヴだけ)、
/// immune_condition はその状態にならない、immune_damage はその種類のダメージを受けない)。
/// </summary>
public sealed class PassiveEffectDef
{
    public string Type { get; set; } = "";
    public string To { get; set; } = "self";
    public string? Ability { get; set; }
    public Effects.Condition? Condition { get; set; }
    public string? DamageType { get; set; }
    public LevelNumber? Value { get; set; }

    /// <summary>advantage の振る判定(PassiveRules.Rolls。initiative / melee_attack / attacked / save。save は condition でその状態のセーヴに、on: magic で魔法へのセーヴに絞れる)。</summary>
    public string? Roll { get; set; }

    /// <summary>効く攻撃(種類ごとに書ける値が決まる。PassiveRules.OnsFor)。</summary>
    public string? On { get; set; }

    /// <summary>extra_damage で足すダメージのダイス(呪文と同じ書き方)。</summary>
    public string? Dice { get; set; }

    /// <summary>spell_damage_bonus の呪文の id。</summary>
    public string? Spell { get; set; }

    /// <summary>stay_up で、成功のたびに難易度に足す数。</summary>
    public int Step { get; set; }

    /// <summary>stay_up で、セーヴの代わりに使う回数(pools の id。不屈の耐久)。</summary>
    public string? Pool { get; set; }

    /// <summary>no_cast と no_concentration を外す能力の id(その能力を持っていれば効かない。獣の姿の no_cast を外す獣の呪文)。</summary>
    public string? Unless { get; set; }

    /// <summary>効果が働いたときに出す文言のキー(反応や踏みとどまったとき。無ければ出さない。instant_kill は省くと combat.instant_kill)。</summary>
    public string? Message { get; set; }

    /// <summary>instant_kill の敵の抵抗: 0〜resist_die−1 の乱数が resist_base + 敵のレベル(CR の切り捨て)を超えたときだけ倒せる。resist_die が 0 なら抵抗しない。</summary>
    public int ResistBase { get; set; }

    /// <summary>instant_kill の敵の抵抗の乱数の幅(Wizardry の #1〜3 は resist_base 10・resist_die 35。CR 24 以上には効かない)。</summary>
    public int ResistDie { get; set; }

    /// <summary>instant_kill がボス(bosses.json のボスと、名のある敵)に効かない。</summary>
    public bool NoBoss { get; set; }
}

/// <summary>
/// スロットの伸び方(spell_progressions.json)。caster_level はそのクラスだけのときの術者のレベル、multiclass はマルチクラスで足す術者のレベル
/// (どちらもそのクラスのレベルで数え、ふつうのスロットの表 spell_slots.json を引く)。pact があれば協約魔術で、ふつうのスロットに数えず、
/// 協約魔術のクラスのレベルの合計で、スロットの数(slots)とレベル(level)を決める。
/// </summary>
public sealed class SpellProgressionDef
{
    public string Id { get; set; } = "";
    public LevelNumber? CasterLevel { get; set; }
    public LevelNumber? Multiclass { get; set; }
    public PactSlotsDef? Pact { get; set; }
}

/// <summary>協約魔術のスロットの数とレベル(協約魔術のクラスのレベルの合計で数える)。</summary>
public sealed class PactSlotsDef
{
    public LevelNumber Slots { get; set; } = LevelNumber.Of(0);
    public LevelNumber Level { get; set; } = LevelNumber.Of(0);
}

/// <summary>ふつうのスロットの表の1行(spell_slots.json)。id は術者のレベル(1〜20)、slots は 1 レベルからの呪文レベルごとの数。</summary>
public sealed class SpellSlotRowDef
{
    public string Id { get; set; } = "";
    public List<int> Slots { get; set; } = [];
}

/// <summary>名前を付けた呪文の束(spellsets.json)。どのクラスが使うかは、クラスの spell_access で決める。</summary>
public sealed class SpellSetDef
{
    public string Id { get; set; } = "";
    public List<string> Spells { get; set; } = [];
}

public sealed class BackgroundDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = LocalizedText.Of("");
    public List<string> Skills { get; set; } = [];
    public LocalizedText? Description { get; set; }
}

/// <summary>キャラクター・アイテム・モンスターのデータ(パックの races / classes / skills / backgrounds / items / monsters)。</summary>
public sealed class GameData
{
    public Dictionary<string, SkillDef> Skills { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RaceDef> Races { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ClassDef> Classes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, BackgroundDef> Backgrounds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ItemDef> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, MonsterDef> Monsters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SpellDef> Spells { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SpellSetDef> SpellSets { get; } = new(StringComparer.Ordinal);

    /// <summary>スロットの伸び方(spell_progressions.json)。クラスの spellcasting の progression で id を指す。</summary>
    public Dictionary<string, SpellProgressionDef> SpellProgressions { get; } = new(StringComparer.Ordinal);

    /// <summary>ふつうのスロットの表(spell_slots.json。キーは術者のレベル)。どのクラスも、マルチクラスでもこの1つを引く。</summary>
    public Dictionary<string, SpellSlotRowDef> SpellSlots { get; } = new(StringComparer.Ordinal);

    /// <summary>罠(traps.json)。</summary>
    public Dictionary<string, TrapDef> Traps { get; } = new(StringComparer.Ordinal);

    /// <summary>罠の束(trapsets.json)。宝箱は chest。</summary>
    public Dictionary<string, TrapSetDef> TrapSets { get; } = new(StringComparer.Ordinal);

    /// <summary>変身先(shapes.json。モンスターと同じ書き方で、変身して戦うための数値)。モンスターの数値を直しても、変身の釣り合いは変わらない。</summary>
    public Dictionary<string, MonsterDef> Shapes { get; } = new(StringComparer.Ordinal);

    /// <summary>技と勝手に効く能力の中身(features.json)。クラスは features で id を指す。</summary>
    public Dictionary<string, ClassFeatureDef> Features { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, WishDef> Wishes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, BossDef> Bosses { get; } = new(StringComparer.Ordinal);

    /// <summary>名のある敵(1つの世界に 0〜2 体。決まったユニーク品を必ず落とす)。</summary>
    public Dictionary<string, NamedMonsterDef> NamedMonsters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Events.EventDef> Events { get; } = new(StringComparer.Ordinal);

    /// <summary>シナリオの噂(酒場で聞く。条件付き)。</summary>
    public Dictionary<string, Events.RumorDef> Rumors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Combat.FloorTheme> FloorThemes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AffixDef> Affixes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DropTable> Drops { get; } = new(StringComparer.Ordinal);
    public List<string> WishOrder { get; } = [];

    /// <summary>自由な迷宮の由来の部品(場所・災い・底・町の頼み・町の名前)。</summary>
    public Dictionary<string, Ai.WorldPart> WorldParts { get; } = new(StringComparer.Ordinal);

    /// <summary>種族ごとの名前の一覧(おまかせの名前)。</summary>
    public Dictionary<string, NameListDef> NameLists { get; } = new(StringComparer.Ordinal);

    /// <summary>パックの順番どおりの並び(画面に出す順)。</summary>
    public List<string> RaceOrder { get; } = [];
    public List<string> ClassOrder { get; } = [];
    public List<string> BackgroundOrder { get; } = [];

    public const string SpellSetCollection = "spellsets";
    public const string SpellProgressionCollection = "spell_progressions";
    public const string SpellSlotCollection = "spell_slots";
    public const string FeatureCollection = "features";
    public const string ShapeCollection = "shapes";

    /// <summary>
    /// 前の形の呪文(classes を書いたもの。シナリオのパックの呪文など)は、前の形と同じく使えるクラスを置き換える:
    /// 全部の束から外してから、そのクラスの束(&lt;クラス&gt;_cantrips か &lt;クラス&gt;_&lt;レベル&gt;)に足す。
    /// 束が無ければ作る(標準のクラスの spell_access は、この名前の束を指している)。
    /// </summary>
    private void AddLegacySpellClasses()
    {
        foreach (var s in Spells.Values.Where(s => s.Classes != null))
        {
            foreach (var set in SpellSets.Values) set.Spells.Remove(s.Id);
            foreach (var cls in s.Classes!)
            {
                string id = s.IsCantrip ? $"{cls}_cantrips" : $"{cls}_{s.Level}";
                if (!SpellSets.TryGetValue(id, out var set)) SpellSets[id] = set = new SpellSetDef { Id = id };
                if (!set.Spells.Contains(s.Id)) set.Spells.Add(s.Id);
            }
        }
    }

    /// <summary>その呪文を、束を通して使えるクラス(クラスの並びの順)。</summary>
    public IEnumerable<ClassDef> ClassesWithSpell(string spellId) =>
        ClassOrder.Distinct().Select(id => Classes.GetValueOrDefault(id)).OfType<ClassDef>()
            .Where(c => c.Spellcasting?.SpellAccess.Any(a => SpellSets.TryGetValue(a.Set, out var set) && set.Spells.Contains(spellId)) == true);

    public static GameData Load(ContentLibrary content)
    {
        var d = new GameData();
        void Read<T>(string collection, Dictionary<string, T> into, List<string>? order, Func<T, string> id) where T : class
        {
            foreach (var item in content.All(collection))
            {
                try
                {
                    var v = item.As<T>();
                    // シナリオのパックで定義した品は、出どころを覚える(セーブに中身を写すため)
                    if (v is Items.ItemDef def && item.Pack.Kind == Content.ScenarioCatalog.Kind)
                        (def.OriginScenario, def.OriginGuid) = (item.Pack.Id, item.Pack.Guid ?? "id:" + item.Pack.Id);
                    into[id(v)] = v;
                    order?.Add(id(v));
                }
                catch (System.Text.Json.JsonException ex)
                {
                    content.Problems.Add($"{item.Pack.Id}/{collection}.json の '{item.Id}' が読めません: {ex.Message}");
                }
            }
        }
        Read("skills", d.Skills, null, s => s.Id);
        Read("races", d.Races, d.RaceOrder, r => r.Id);
        Read("classes", d.Classes, d.ClassOrder, c => c.Id);
        Read("backgrounds", d.Backgrounds, d.BackgroundOrder, b => b.Id);
        Read("items", d.Items, null, i => i.Id);
        Read("monsters", d.Monsters, null, m => m.Id);
        Read("spells", d.Spells, null, s => s.Id);
        Read(SpellSetCollection, d.SpellSets, null, s => s.Id);
        Read(SpellProgressionCollection, d.SpellProgressions, null, p => p.Id);
        Read(SpellSlotCollection, d.SpellSlots, null, r => r.Id);
        Read(TrapDef.Collection, d.Traps, null, t => t.Id);
        Read(TrapSetDef.Collection, d.TrapSets, null, t => t.Id);
        Read(FeatureCollection, d.Features, null, f => f.Id);
        Read(ShapeCollection, d.Shapes, null, m => m.Id);
        d.AddLegacySpellClasses();
        Read("wishes", d.Wishes, d.WishOrder, w => w.Id);
        Read("bosses", d.Bosses, null, b => b.Id);
        Read(NamedMonsterDef.Collection, d.NamedMonsters, null, n => n.Id);
        Read(Core.Events.EventDef.Collection, d.Events, null, e => e.Id);
        Read(Core.Events.RumorDef.Collection, d.Rumors, null, r => r.Id);
        Read(Combat.FloorTheme.Collection, d.FloorThemes, null, t => t.Id);
        Read(AffixDef.Collection, d.Affixes, null, a => a.Id);
        Read(DropTable.Collection, d.Drops, null, t => t.Id);
        Read(NameListDef.Collection, d.NameLists, null, n => n.Id);
        Read(Ai.WorldPart.Collection, d.WorldParts, null, p => p.Id);
        int before = content.Problems.Count;
        d.Check(content);
        AddSources(content, before);
        return d;
    }

    /// <summary>検査の文("events 'id': …" の形)に、その物を書いたファイルを添える(どのパックの、どのファイルを直せばよいか分かるように)。</summary>
    private static void AddSources(ContentLibrary content, int from)
    {
        for (int i = from; i < content.Problems.Count; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(content.Problems[i], @"^([a-z_]+) '([^']+)'");
            if (m.Success && content.SourceOf(m.Groups[1].Value, m.Groups[2].Value) is { } file)
                content.Problems[i] += $"(ファイル: {file})";
        }
    }


    /// <summary>クラスか種族の features と pools の検査(指す能力があるか、レベル、重なり、回数の戻り方)。</summary>
    private void CheckFeatureRefs(ContentLibrary content, string where, List<FeatureRef> refs, List<PoolDef> pools)
    {
        foreach (var r in refs)
        {
            if (!Features.ContainsKey(r.Id)) content.Problems.Add($"{where} の features: features に '{r.Id}' がありません");
            if (r.Level is < 1 or > 20) content.Problems.Add($"{where} の features の '{r.Id}': level は 1〜20 です");
        }
        foreach (var dup in refs.GroupBy(r => r.Id).Where(g => g.Count() > 1))
            content.Problems.Add($"{where} の features: '{dup.Key}' が2つあります");
        foreach (var pool in pools)
        {
            if (pool.Recharge is not ("short" or "long")) content.Problems.Add($"{where} の pools '{pool.Id}': recharge は short か long です");
            if (pool.FromLevel is < 1 or > 20) content.Problems.Add($"{where} の pools '{pool.Id}': from_level は 1〜20 です");
            if (pool.ShortWith != null && !Features.ContainsKey(pool.ShortWith))
                content.Problems.Add($"{where} の pools '{pool.Id}': short_with の '{pool.ShortWith}' が features にありません");
        }
        foreach (var dup in pools.GroupBy(p => p.Id).Where(g => g.Count() > 1))
            content.Problems.Add($"{where} の pools: '{dup.Key}' が2つあります");
    }

    /// <summary>
    /// クラスと種族が指す技・能力・回数の文言が日英にあるか(画面に出す名前)。技は feature.&lt;id&gt; と feature.about.&lt;id&gt;、
    /// 能力の一覧に出す能力(listed)は passive.&lt;id&gt;、ファイティングスタイルは passive.&lt;id&gt; と passive.about.&lt;id&gt;、回数は pool.&lt;id&gt;。
    /// </summary>
    private void CheckFeatureTexts(ContentLibrary content)
    {
        var owners = Classes.Values.Select(c => ($"classes '{c.Id}'", c.Features, c.Pools))
            .Concat(Races.Values.Select(r => ($"races '{r.Id}'", r.Features, r.Pools)));
        var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (where, refs, pools) in owners)
        {
            foreach (var r in refs)
            {
                if (!Features.TryGetValue(r.Id, out var f)) continue;
                if (f.Kind == "action") { keys.TryAdd("feature." + f.Id, where); keys.TryAdd("feature.about." + f.Id, where); }
                else if (f.Group == "fighting_style") { keys.TryAdd("passive." + f.Id, where); keys.TryAdd("passive.about." + f.Id, where); }
                else if (f.Listed) keys.TryAdd("passive." + f.Id, where);
            }
            foreach (var pool in pools) keys.TryAdd("pool." + pool.Id, where);
        }
        foreach (var lang in new[] { "ja", "en" })
        {
            var texts = content.Texts(lang);
            foreach (var (key, where) in keys.Where(k => !texts.ContainsKey(k.Key)))
                content.Problems.Add($"{where}: 文言 '{key}' が {lang} にありません(text/{lang}.json)");
        }
    }

    /// <summary>罠と罠の束の検査(手順の部品・当たる相手・セーヴ・状態、束の罠と重み)。</summary>
    private void CheckTraps(ContentLibrary content)
    {
        foreach (var t in Traps.Values)
        {
            foreach (var s in t.Steps)
            {
                string where = $"traps '{t.Id}' の {s.Type}";
                if (!TrapDef.StepTypes.Contains(s.Type)) content.Problems.Add($"traps '{t.Id}': 手順 '{s.Type}' は使えません({string.Join(" / ", TrapDef.StepTypes)})");
                if (!Traps_.ValidWho(s.Who)) content.Problems.Add($"{where}: who '{s.Who}' は trigger / party / tag:<名前> / race:<id> です");
                if (s.Save is { } sv && !PassiveRules.ValidAbility(sv.Ability)) content.Problems.Add($"{where}: save の ability '{sv.Ability}' は能力値です");
                if (s.Type == "damage" && s.Dice != null && !DiceExpression.TryParse(s.Dice, out _, out _)) content.Problems.Add($"{where}: dice '{s.Dice}' が読めません");
                if (s.Type == "condition" && !GravewardSpiral.Core.Events.EventRunner.TryCondition(s.Id, out _)) content.Problems.Add($"{where}: 状態 '{s.Id}' がありません");
            }
        }
        foreach (var set in TrapSets.Values)
        {
            foreach (var pick in set.Traps)
            {
                if (!Traps.ContainsKey(pick.Id)) content.Problems.Add($"trapsets '{set.Id}': traps に '{pick.Id}' がありません");
                if (pick.Weight < 1) content.Problems.Add($"trapsets '{set.Id}' の '{pick.Id}': weight は 1 以上です");
            }
        }
    }

    /// <summary>スロットの表(術者のレベル 1〜20 がそろい、数は 9 レベルまで)と伸び方(協約魔術か、caster_level と multiclass がある)の検査。</summary>
    private void CheckSpellSlots(ContentLibrary content)
    {
        if (Classes.Values.All(c => c.Spellcasting == null)) return;
        foreach (var r in SpellSlots.Values)
        {
            if (!int.TryParse(r.Id, out int lv) || lv is < 1 or > 20) content.Problems.Add($"spell_slots '{r.Id}': id は術者のレベル 1〜20 です");
            if (r.Slots.Count > 9 || r.Slots.Any(n => n < 0)) content.Problems.Add($"spell_slots '{r.Id}': slots は 9 レベルまでの 0 以上の数です");
        }
        var missing = Enumerable.Range(1, 20).Where(l => !SpellSlots.ContainsKey(l.ToString())).ToList();
        if (missing.Count > 0) content.Problems.Add($"spell_slots: 術者のレベル {string.Join("・", missing)} の行がありません");
        foreach (var pr in SpellProgressions.Values.Where(pr => pr.Pact == null && (pr.CasterLevel == null || pr.Multiclass == null)))
            content.Problems.Add($"spell_progressions '{pr.Id}': 協約魔術でなければ caster_level と multiclass が要ります");
    }

    /// <summary>クラスの呪文の扱いの検査(能力値、扱いと伸び方の組、束、割り当てたレベル)。</summary>
    private void CheckSpellcasting(ContentLibrary content, string classId, SpellcastingDef sc)
    {
        string where = $"classes '{classId}' の spellcasting";
        try { Abilities.Parse(sc.Ability); }
        catch (FormatException ex) { content.Problems.Add($"{where}: {ex.Message}"); }
        if (sc.Style == CasterKind.None) content.Problems.Add($"{where}: style は prepared / known / book / pact のどれかです");
        if (!SpellProgressions.TryGetValue(sc.Progression, out var prog))
            content.Problems.Add($"{where}: progression '{sc.Progression}' が spell_progressions にありません");
        else if ((sc.Style == CasterKind.Pact) != (prog.Pact != null))
            content.Problems.Add($"{where}: style が pact のときだけ、progression を協約魔術(pact のあるもの)にします");
        if (sc.FromLevel is < 1 or > 20) content.Problems.Add($"{where}: from_level は 1〜20 です");
        if (sc.Style is CasterKind.Known or CasterKind.Book or CasterKind.Pact && sc.Known == null)
            content.Problems.Add($"{where}: style が {sc.Style.ToString().ToLowerInvariant()} なら known が要ります");
        if (sc.Style is CasterKind.Prepared or CasterKind.Book && sc.Prepared == null)
            content.Problems.Add($"{where}: style が {sc.Style.ToString().ToLowerInvariant()} なら prepared が要ります");
        foreach (var a in sc.SpellAccess)
        {
            if (a.Lv is < 1 or > 20 || a.Spelllv is < 0 or > 9)
                content.Problems.Add($"{where} の spell_access: lv は 1〜20、spelllv は 0〜9 です(set '{a.Set}')");
            if (!SpellSets.TryGetValue(a.Set, out var set))
            {
                content.Problems.Add($"{where} の spell_access: spellsets に '{a.Set}' がありません");
                continue;
            }
            // もとのレベルと違うレベルにも割り当てられる(要るスロットは割り当てたレベル、効果はもとのレベルを基準に上のスロットの分だけ強く)。
            // ただし初級呪文は spelllv 0 だけ、ほかの呪文は 1〜9 だけ(初級呪文はスロットを使わず、キャラクターのレベルで強くなるため)
            foreach (var id in set.Spells.Where(id => Spells.TryGetValue(id, out var s) && s.IsCantrip != (a.Spelllv == 0)))
                content.Problems.Add($"{where} の spell_access: '{id}' は{(Spells[id].IsCantrip ? "初級呪文なので spelllv 0" : "初級呪文ではないので spelllv 1〜9")}の束に入れます(set '{a.Set}' の spelllv {a.Spelllv})");
        }
    }

    private void Check(ContentLibrary content)
    {
        void Need(string where, string collection, IEnumerable<string> ids, Func<string, bool> exists)
        {
            foreach (var id in ids)
            {
                if (!exists(id)) content.Problems.Add($"{where}: {collection} に '{id}' がありません");
            }
        }
        foreach (var s in Skills.Values)
        {
            try { _ = s.AbilityValue; }
            catch (FormatException ex) { content.Problems.Add($"skills '{s.Id}': {ex.Message}"); }
        }
        foreach (var r in Races.Values)
        {
            Need($"races '{r.Id}' の skills", "skills", r.Skills, Skills.ContainsKey);
            foreach (var k in r.AbilityBonus.Keys)
            {
                try { Abilities.Parse(k); }
                catch (FormatException ex) { content.Problems.Add($"races '{r.Id}': {ex.Message}"); }
            }
        }
        foreach (var c in Classes.Values)
        {
            Need($"classes '{c.Id}' の skills", "skills", c.Skills.Where(s => s != "*"), Skills.ContainsKey);
            Need($"classes '{c.Id}' の start_items", "items", c.StartItems, Items.ContainsKey);
            if (!DiceExpression.TryParse(c.StartGold, out _, out var err))
                content.Problems.Add($"classes '{c.Id}' の start_gold: {err}");
            if (c.Spellcasting is { } sc) CheckSpellcasting(content, c.Id, sc);
            CheckFeatureRefs(content, $"classes '{c.Id}'", c.Features, c.Pools);
            if (c.Progression?.AbilityIncreases is { } asi && (asi.Any(l => l is < 1 or > 20) || asi.Distinct().Count() != asi.Count))
                content.Problems.Add($"classes '{c.Id}' の progression の ability_increases: 1〜20 のレベルを重ねずに書きます");
        }
        foreach (var r in Races.Values)
        {
            CheckFeatureRefs(content, $"races '{r.Id}'", r.Features, r.Pools);
            // 種族の数は合計のレベルで数えるので、クラスのレベル(class_level)は使えない
            foreach (var f in r.Features.Select(x => Features.GetValueOrDefault(x.Id)).OfType<ClassFeatureDef>())
                if (f.Values.Values.Concat(f.Effects.Select(e => e.Value)).Any(n => n is { ClassLevel: not 0 }))
                    content.Problems.Add($"races '{r.Id}' の features '{f.Id}': 種族の数は class_level でなく total_level で書きます");
            if (r.Pools.Any(p => p.Max.ClassLevel != 0))
                content.Problems.Add($"races '{r.Id}' の pools: 種族の数は class_level でなく total_level で書きます");
        }
        foreach (var set in SpellSets.Values)
            Need($"spellsets '{set.Id}' の spells", "spells", set.Spells, Spells.ContainsKey);
        CheckSpellSlots(content);
        CheckFeatureTexts(content);
        CheckTraps(content);
        foreach (var f in Features.Values.Where(f => f.Kind is not ("passive" or "action")))
            content.Problems.Add($"features '{f.Id}': kind は passive か action です");
        foreach (var f in Features.Values.Where(f => f.Kind == "action" && !ClassFeatures.ActionParts.Contains(f.PartId)))
            content.Problems.Add($"features '{f.Id}': 技(action)の動きはコアの部品です。part(省けば id)は {string.Join(" / ", ClassFeatures.ActionParts)} のどれかにします(勝手に効く能力なら kind を passive に)");
        foreach (var f in Features.Values) content.Problems.AddRange(PassiveRules.Check(f));
        content.Problems.AddRange(PassiveRules.CheckMaxGroups(Features.Values));
        foreach (var f in Features.Values)
        {
            foreach (var (lv, ids) in f.Shapes)
            {
                if (!int.TryParse(lv, out int n) || n is < 1 or > 20) content.Problems.Add($"features '{f.Id}' の shapes: キー '{lv}' は 1〜20 のレベルです");
                Need($"features '{f.Id}' の shapes", "shapes", ids, Shapes.ContainsKey);
            }
        }
        foreach (var m in Shapes.Values) CheckMonster(content, m);
        var poolIds = Classes.Values.SelectMany(c => c.Pools).Concat(Races.Values.SelectMany(r => r.Pools)).Select(p => p.Id).ToHashSet();
        // 回数の持ち主を確かめるのは、どれかのクラスか種族が持つ能力だけ(標準のクラスを外すと、その能力は誰も持たず、使われないだけ)
        var owned = Classes.Values.SelectMany(c => c.Features).Concat(Races.Values.SelectMany(r => r.Features)).Select(r => r.Id).ToHashSet();
        foreach (var f in Features.Values.Where(f => owned.Contains(f.Id) && f.Kind == "action" && f.Pool != null && !poolIds.Contains(f.Pool)))
            content.Problems.Add($"features '{f.Id}': pool '{f.Pool}' を持つクラスか種族がありません");
        foreach (var (f, e) in Features.Values.SelectMany(f => f.Effects.Select(e => (f, e))).Where(x => x.e.Unless != null && !Features.ContainsKey(x.e.Unless)))
            content.Problems.Add($"features '{f.Id}' の {e.Type}: unless の '{e.Unless}' が features にありません");
        foreach (var (f, e) in Features.Values.Where(f => owned.Contains(f.Id)).SelectMany(f => f.Effects.Select(e => (f, e))).Where(x => x.e.Pool != null && !poolIds.Contains(x.e.Pool)))
            content.Problems.Add($"features '{f.Id}' の {e.Type}: pool '{e.Pool}' を持つクラスか種族がありません");
        foreach (var b in Backgrounds.Values)
            Need($"backgrounds '{b.Id}' の skills", "skills", b.Skills, Skills.ContainsKey);
        ResolveItemBases(content);
        foreach (var i in Items.Values)
        {
            CheckEffects(content, $"items '{i.Id}' の passive", i.Passive);
            CheckEffects(content, $"items '{i.Id}' の use", i.Use);
            CheckEffects(content, $"items '{i.Id}' の on_hit", i.OnHit);
            if (i.UseSpell != null && !(Spells.TryGetValue(i.UseSpell, out var us) && us.Usable))
                content.Problems.Add($"items '{i.Id}' の use_spell: 使える呪文 '{i.UseSpell}' がありません");
            if (i.Kind == ItemKind.Accessory && i.Slot is not ("ring" or "neck" or "cloak" or "head" or "hands" or "feet" or "waist"))
                content.Problems.Add($"items '{i.Id}': slot '{i.Slot}' は ring / neck / cloak / head / hands / feet / waist のどれかです");
            if (i.Damage != null && !DiceExpression.TryParse(i.Damage, out _, out var err))
                content.Problems.Add($"items '{i.Id}' の damage: {err}");
        }
        foreach (var m in Monsters.Values) CheckMonster(content, m);
        foreach (var s in Spells.Values)
        {
            CheckEffects(content, $"spells '{s.Id}'", s.Effects);
            if (s.Summon != null && !Monsters.ContainsKey(s.Summon.Monster))
                content.Problems.Add($"spells '{s.Id}' の summon: monsters に '{s.Summon.Monster}' がありません");
        }
        foreach (var b in Bosses.Values)
            Need($"bosses '{b.Id}' の groups", "monsters", b.Groups.Select(g => g.Monster), Monsters.ContainsKey);
        foreach (var n in NamedMonsters.Values)
        {
            Need($"named_monsters '{n.Id}' の monster", "monsters", [n.Monster], Monsters.ContainsKey);
            Need($"named_monsters '{n.Id}' の item", "items", [n.Item], Items.ContainsKey);
            if (n.Placement is not ("lair" or "wander" or "either")) content.Problems.Add($"named_monsters '{n.Id}' の placement は lair / wander / either です");
        }
        foreach (var w in Wishes.Values)
            CheckEffects(content, $"wishes '{w.Id}'", w.Effects);
        foreach (var e in Events.Values) content.Problems.AddRange(Core.Events.EventCheck.Check(this, e, content));
        foreach (var r in Rumors.Values) content.Problems.AddRange(Core.Events.RumorDef.Check(r));
        foreach (var e in Events.Values.Where(e => e.Scatter != null && !Core.Events.EventScatter.RarityWeights.ContainsKey(e.Scatter.Rarity)))
            content.Problems.Add($"events '{e.Id}': rarity '{e.Scatter!.Rarity}' は common / uncommon / rare / legendary のどれかです");
        // 連動する組: 部品が2つ以上(1つなら組にする意味が無い)、step が重ならない
        foreach (var g in Events.Values.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!))
        {
            if (g.Count() < 2) content.Problems.Add($"events の組 '{g.Key}': 部品が1つしかありません");
            foreach (var dup in g.GroupBy(e => e.Scatter!.Step).Where(x => x.Count() > 1))
                content.Problems.Add($"events の組 '{g.Key}': step {dup.Key} が重なっています({string.Join(", ", dup.Select(e => e.Id))})");
        }
        foreach (var a in Affixes.Values)
        {
            if (a.Tiers.Count == 0) content.Problems.Add($"affixes '{a.Id}': tiers がありません");
            if (a.Kinds.Count == 0 || a.Kinds.Any(k => k is not ("weapon" or "armor" or "shield")))
                content.Problems.Add($"affixes '{a.Id}': kinds は weapon / armor / shield です");
            if (a.Position is not ("prefix" or "suffix")) content.Problems.Add($"affixes '{a.Id}': position は prefix / suffix です");
            for (int i = 0; i < a.Tiers.Count; i++)
            {
                CheckEffects(content, $"affixes '{a.Id}' の tiers[{i}] の passive", a.Tiers[i].Passive);
                CheckEffects(content, $"affixes '{a.Id}' の tiers[{i}] の on_hit", a.Tiers[i].OnHit);
            }
        }
        foreach (var t in Drops.Values)
        {
            if (t.Monster != null && !Monsters.ContainsKey(t.Monster)) content.Problems.Add($"drops '{t.Id}': monsters に '{t.Monster}' がありません");
            if (t.Boss != null && !Bosses.ContainsKey(t.Boss)) content.Problems.Add($"drops '{t.Id}': bosses に '{t.Boss}' がありません");
            foreach (var e in t.Entries)
            {
                if (e.Item != null && !Items.ContainsKey(e.Item)) content.Problems.Add($"drops '{t.Id}': items に '{e.Item}' がありません");
                if (e.Rarity != null && !Treasure.Rarities.Contains(e.Rarity)) content.Problems.Add($"drops '{t.Id}': 等級 '{e.Rarity}' がありません");
                if ((e.Item == null) == (e.Rarity == null)) content.Problems.Add($"drops '{t.Id}': 候補には item か rarity のどちらか1つを書きます");
            }
        }
    }

    /// <summary>魔法の武器・鎧の base から、書いていない項目(ダメージ、AC、特性など)を写す。</summary>
    private void ResolveItemBases(ContentLibrary content)
    {
        foreach (var i in Items.Values.Where(i => i.Base != null))
        {
            if (!Items.TryGetValue(i.Base!, out var b) || b.Base != null)
            {
                content.Problems.Add($"items '{i.Id}' の base: ふつうの品 '{i.Base}' がありません");
                continue;
            }
            i.Category ??= b.Category;
            if (!i.Ranged) i.Ranged = b.Ranged;
            i.Damage ??= b.Damage;
            i.DamageType ??= b.DamageType;
            i.VersatileDamage ??= b.VersatileDamage;
            if (i.Properties.Count == 0) i.Properties = [.. b.Properties];
            i.ArmorType ??= b.ArmorType;
            if (i.ArmorClass == 0) i.ArmorClass = b.ArmorClass;
            i.DexCap ??= b.DexCap;
            if (i.StrengthRequired == 0) i.StrengthRequired = b.StrengthRequired;
            if (!i.StealthDisadvantage) i.StealthDisadvantage = b.StealthDisadvantage;
        }
    }

    /// <summary>効果の部品の種類・ダイス式・能力値を確かめる。MOD(呪文の能力値)は 0 として読む。</summary>
    /// <summary>modifier の stat に書けるもの(技能は 5版の 18 個)。</summary>
    public static readonly IReadOnlySet<string> ModifierStats = new HashSet<string>
    {
        "ac", "ac_unarmored", "ac_min", "attack", "save", "damage", "attacks", "disarm", "flee",
        "acrobatics", "animal_handling", "arcana", "athletics", "deception", "history", "insight", "intimidation", "investigation",
        "medicine", "nature", "perception", "performance", "persuasion", "religion", "sleight_of_hand", "stealth", "survival",
    };

    private static void CheckEffects(ContentLibrary content, string where, IEnumerable<EffectPart> parts)
    {
        void Dice(string? text)
        {
            if (text == null) return;
            if (!DiceExpression.TryParse(text.Replace("MOD", "0"), out _, out var err)) content.Problems.Add($"{where} の dice '{text}': {err}");
        }
        foreach (var p in parts.SelectMany(e => e.Flatten()))
        {
            if (!EffectPart.KnownTypes.Contains(p.Type)) content.Problems.Add($"{where}: 効果の種類 '{p.Type}' がありません");
            Dice(p.Dice);
            foreach (var v in p.ByLevel.Values) Dice(v);
            if (p.Type == EffectPart.Save || p.Ability != null)
            {
                try { Abilities.Parse(p.Ability ?? ""); }
                catch (FormatException ex) { content.Problems.Add($"{where}: {ex.Message}"); }
            }
            if (p.Type is EffectPart.ConditionType or EffectPart.RemoveCondition && p.Condition == null)
                content.Problems.Add($"{where}: condition がありません");
            bool saveAdv = p.Stat?.StartsWith("save_adv:") == true && Enum.TryParse<Effects.Condition>(p.Stat["save_adv:".Length..], true, out _);
            if (p.Type == EffectPart.Modifier && !ModifierStats.Contains(p.Stat ?? "") && !saveAdv)
                content.Problems.Add($"{where}: modifier の stat '{p.Stat}' は ac / attack / save / damage / attacks / disarm / flee か、技能の id か、save_adv:<状態> です");
            if (p.Type is EffectPart.Resist or EffectPart.Vulnerable && !DamageTypes.All.Contains(p.DamageType ?? ""))
                content.Problems.Add($"{where}: ダメージの種類 '{p.DamageType}' がありません");
            if (p.Type == EffectPart.ImmuneCondition && p.Condition == null) content.Problems.Add($"{where}: condition がありません");
            if (p.Chance is int chance && (p.Type != EffectPart.Kill || chance is < 1 or > 100))
                content.Problems.Add($"{where}: chance は kill だけに書けて、1〜100(百分率)です");
        }
    }

    private static void CheckMonster(ContentLibrary content, MonsterDef m)
    {
        string where = $"monsters '{m.Id}'";
        void Dice(string what, string? text)
        {
            if (text != null && !DiceExpression.TryParse(text, out _, out var err)) content.Problems.Add($"{where} の {what}: {err}");
        }
        Dice("hp", m.Hp);
        Dice("appearing", m.Appearing);
        foreach (var a in m.Actions)
        {
            foreach (var p in a.Effects.SelectMany(e => e.Flatten()))
            {
                if (!EffectPart.KnownTypes.Contains(p.Type)) content.Problems.Add($"{where} の {a.Id}: 効果の種類 '{p.Type}' がありません");
                Dice($"{a.Id} の dice", p.Dice);
                if (p.Type == EffectPart.Save)
                {
                    try { Abilities.Parse(p.Ability ?? ""); }
                    catch (FormatException ex) { content.Problems.Add($"{where} の {a.Id}: {ex.Message}"); }
                }
                if (p.Type == EffectPart.ConditionType && p.Condition == null) content.Problems.Add($"{where} の {a.Id}: condition がありません");
                if (p.Chance is int chance && (p.Type != EffectPart.Kill || chance is < 1 or > 100))
                    content.Problems.Add($"{where} の {a.Id}: chance は kill だけに書けて、1〜100(百分率)です");
            }
        }
        foreach (var opt in m.Multiattack)
        {
            foreach (var piece in opt.Where(x => m.Action(x.Action) == null))
                content.Problems.Add($"{where} の multiattack: 行動 '{piece.Action}' がありません");
        }
    }

    /// <summary>そのクラスで選べる技能。</summary>
    public IEnumerable<string> SkillChoicesFor(ClassDef c) =>
        c.Skills.Contains("*") ? Skills.Keys.Order(StringComparer.Ordinal) : c.Skills;
}
