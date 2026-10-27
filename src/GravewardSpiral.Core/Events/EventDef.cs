using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Monsters;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// イベント1つ(パックの events.json)。条件 (when) が合えば、手順 (steps) を上から順に行う。
/// 起こる場所 (place):
/// - "maze": 迷宮のマスの印 event:&lt;id&gt; を踏んだとき。手作りのマップ・はめ込む部屋に書くか、scatter で迷宮に散らす。
///   trigger を "turn" にすると、そのマスで向きを変えたとき(回転・振り向き)に起こる。"both" なら着いたときと向きを変えたときの両方。
/// - "town": 町の「人を訪ねる」に、題 (title) を出す。
/// 一度きり (once): "game" ならゲームで1回、"place" なら置いた場所ごとに1回(散らした遺品など)。省くと何度でも起こる。
/// エディタで組めるよう、条件と手順はすべて決まった種類の部品で書く(種類は EventCondition.KnownTypes / EventStep.KnownTypes)。
/// </summary>
public sealed class EventDef
{
    public const string Collection = "events";

    public string Id { get; set; } = "";
    public LocalizedText? Title { get; set; }
    public string Place { get; set; } = "maze";
    public string? Once { get; set; }

    /// <summary>
    /// 迷宮で起こるとき: enter(マスに着いたとき。既定)/ turn(そのマスで向きを変えたとき)/ both(着いたときと向きを変えたとき)/
    /// cast(そのマスで、キャンプから呪文を唱えたとき)/ rest(そのマスでキャンプの小休憩をしたとき)。カンマで並べられる(enter,cast など)。
    /// </summary>
    public string Trigger { get; set; } = "enter";

    private IEnumerable<string> TriggerParts => Trigger.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public List<EventCondition> When { get; set; } = [];
    public List<EventStep> Steps { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool OnEnter => TriggerParts.Any(t => t is "enter" or "both");

    [System.Text.Json.Serialization.JsonIgnore]
    public bool OnTurn => TriggerParts.Any(t => t is "turn" or "both");

    [System.Text.Json.Serialization.JsonIgnore]
    public bool OnCast => TriggerParts.Contains("cast");

    [System.Text.Json.Serialization.JsonIgnore]
    public bool OnRest => TriggerParts.Contains("rest");

    /// <summary>イベントのあいだ流す BGM(パックの中のパス。例: audio/bgm/story.mp3)。終わったら、前の曲に戻す。</summary>
    public string? Bgm { get; set; }

    /// <summary>迷宮に散らす(新しいゲームを作るとき、合う深さの階の空いたマスに置く)。</summary>
    public EventScatter? Scatter { get; set; }

    /// <summary>
    /// 伝説の噂(酒場で、ほかの世界の話として聞ける)。聞いたクランが次に作る世界では、このイベントが出やすくなる(聞いた世界の数だけ倍。EventPlacer.HeardMultiplier)。
    /// </summary>
    public LocalizedText? Rumor { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// 終わったときに、またそこで起こる(何度でも起こるイベントか、立ち去った)なら、メモに書き留められる。
    /// 仕掛けの確かめなど、書き留めてはいけないものは false にする。
    /// </summary>
    public bool Memo { get; set; } = true;

    /// <summary>ゲームで1回のイベントが済んだ印(Flags に入れる)。</summary>
    public string DoneFlag => "done:" + Id;
}

/// <summary>迷宮に散らす決まり。where は any / dead_end(行き止まり) / room(3方向以上が開いたマス) / corridor。</summary>
public sealed class EventScatter
{
    /// <summary>置いてよい一番浅い深さ。0〜1 の割合(0 が B1、1 が最下層。階の数ではない)。</summary>
    public double MinDepth { get; set; }

    /// <summary>置いてよい一番深い深さ。0〜1 の割合(0 が B1、1 が最下層。階の数ではない)。</summary>
    public double MaxDepth { get; set; } = 1;

    /// <summary>
    /// 置いてよい階の目安のレベル(世界のレベル帯で決まる)。min_level より下の階には置かない。
    /// max_level より上の階にも置くが、出やすさを下げ、金・経験値・ダメージ・難易度を引き上げる(EventScale)。
    /// </summary>
    public double MinLevel { get; set; }
    public double MaxLevel { get; set; } = 999;

    public double Weight { get; set; } = 1;
    public string Where { get; set; } = "any";

    /// <summary>1つの迷宮に置く数の上限(0 なら上限なし)。</summary>
    public int Max { get; set; }

    /// <summary>
    /// 連動するイベントの組の名前。同じ chain の部品は、組ごとまとめて1つの世界に置く(1つだけ置くことはしない)。
    /// step の小さい順に、同じ階か、より深い階に置く(鍵を拾う → 扉を開ける、の順になる)。
    /// 置いた組はフラグ chain:&lt;名前&gt; を立てるので、町の人の依頼などは when でそれを見て出せる。
    /// 組の部品は、ふつうの散らすイベントとしては抽選しない。組の出やすさとレベルは、step が一番小さい部品の weight と min/max_level で決める。
    /// </summary>
    public string? Chain { get; set; }
    public int Step { get; set; }

    /// <summary>レア度: common / uncommon / rare / legendary。出やすさ(weight)に RarityWeight を掛ける。</summary>
    public string Rarity { get; set; } = "common";

    public static readonly Dictionary<string, double> RarityWeights = new()
    {
        ["common"] = 1, ["uncommon"] = 0.5, ["rare"] = 0.2, ["legendary"] = 0.12,
    };

    /// <summary>レア度を掛けた出やすさ。</summary>
    public double EffectiveWeight => Math.Max(0, Weight) * RarityWeights.GetValueOrDefault(Rarity, 1);
}

/// <summary>
/// 条件の部品。not で逆にする。
/// flag(id が立っている) / done(イベント id が済んだ) / item(id を count 以上持つ) / gold(value 以上) / level(誰かが value 以上) /
/// class(id のクラスの人がいる) / race(id の種族の人がいる) / counter(id を op で value と比べる) / chance(value % で) /
/// key(この階の鍵を持つ) / alive(動ける人が value 以上) / depth(今の階の深さが value % 以上) /
/// facing(迷宮でパーティが id の向き north / east / south / west を向いている。地図の上が north) /
/// turned(向きを変えて起きた。trigger が both のイベントで、着いたときと分ける) /
/// equipped(誰かが id の品を装備している) / slots(誰かに value レベル以上の呪文スロットが残っている) /
/// entered(このマスへ id の動きで入った forward / back / left / right。階段・落とし穴・テレポーターなどで来たときは、どれでもない) /
/// status(誰かが id の状態。poisoned など) / hp(誰かの HP の割合 % を op で value と比べる) / armed(誰かが武器を手に持っている。not で「誰も持っていない」) /
/// lit(明かりがついている) / zone(今のマスの区域が id の dark / fog / antimagic) /
/// timer(歩数のタイマー id が動いている。残りの歩数を op で value と比べる。既定は 1 以上 = 動いている) /
/// cast(trigger が cast で起きたとき、唱えた呪文が id。id を省くとどの呪文でも) / rested(trigger が rest で、休んで起きた)。
/// 手順 pick で一人を選んだあとは、item / equipped / slots / level / class / race / status / hp / armed はその人だけを見る
/// </summary>
public sealed class EventCondition
{
    public static readonly HashSet<string> KnownTypes = ["flag", "done", "item", "gold", "level", "class", "race", "counter", "chance", "key", "alive", "depth", "facing", "turned", "equipped", "slots", "entered", "status", "hp", "armed", "lit", "zone", "cast", "rested", "timer"];

    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public int Value { get; set; } = 1;
    public int Count { get; set; } = 1;

    /// <summary>counter の比べ方(&gt;= / &lt;= / == / != / &gt; / &lt;)。</summary>
    public string Op { get; set; } = ">=";

    public bool Not { get; set; }
}

/// <summary>選択肢、または random の候補。</summary>
public sealed class EventOption
{
    public LocalizedText? Label { get; set; }
    public List<EventCondition> When { get; set; } = [];
    public List<EventStep> Steps { get; set; } = [];
    public double Weight { get; set; } = 1;
}

/// <summary>
/// 手順の部品。種類ごとに使う項目を以下に書く。
/// - text: text(か文言のキー key)、speaker、art(モンスターの id か、パックの画像のパス)
/// - choice: text(問い)、options[label, when, steps]。when の合わない選択肢は出さない
/// - if: when、then、else
/// - check: skill か ability、dc。パーティで一番得意な人が振る(pick で選んだあとは、その人)。success、fail
/// - random: options[weight, steps] から1つ。per_place を true にすると、同じ迷宮の同じマスでは毎回同じものを選ぶ(壁の文字など)
/// - flag / unflag: id
/// - counter: id に value を足す。counter_set: id を value にする
/// - give_item: id、count。take_item: id、count(足りなければ取れるだけ)
/// - treasure: count 個を、この階の宝の表から
/// - gold: value(負なら払う。足りなければ持っているだけ)
/// - xp: 動ける人それぞれに value
/// - heal: dice(省くと全回復)。damage: dice、type、ability と dc(セーヴで半分)。どちらも動ける人全員。damage では HP は 1 より下がらない
///   (heal・damage・condition・cure・xp・give_item・take_item は、pick で選んだあとは、その人だけ)
/// - condition: id(状態の名前)、value(分。0 なら治療するまで)、ability と dc(セーヴで防ぐ)。cure: id(省くと全部)
/// - light: value 分の明かり(0 なら明かりを消す)
/// - combat: groups[monster, count] か boss か、どちらも無ければこの階の敵。win / lose(逃げた)
/// - open: id。印 gate:&lt;id&gt; のマスの錠を開ける(この階)。close: id。開けた錠をまた閉める
/// - pick: 一人を選ぶ。text(問い)、when(選べる人の条件。一人ずつ見る)。value が 1 ならくじで選ぶ(罠が一人を襲う、など)。
///   選んだら then、選べる人がいないか、やめたら else(else に手順があるときだけ「やめる」を出す)。選んだ人は、イベントの終わりか次の pick まで
/// - spend_slot: value レベル以上の呪文スロットを1つ使う(低いものから。pick で選んだあとはその人、でなければ残っている人)。使えたら then、無ければ else
/// - timer: id、value。value 歩あとに、id のイベント(迷宮のイベント)を、そのとき迷宮のどこにいても起こす。同じ id をもう一度置くと、歩数を数え直す。
///   休むと(キャンプの小休憩)進む。町にいるあいだは止まる。timer_stop: id。そのタイマーを止める
/// - warp: id。印 dest:&lt;id&gt; のマス(この階を先に、無ければほかの階)へパーティを移す。向きはそのまま。イベントはここで終わる
/// - sound: id(効果音)
/// - run: id のイベントを続けて行う
/// - town: 町へ戻る
/// - ending: この世界をクリアにする。町へ戻り、エンディングを流す(最下層の主を倒さずに終わるシナリオに。クランにクリアの印が付く)
/// - end: ここで終わる
/// - again: ここで終わり、一度きりでも済んだことにしない(立ち去る、など。また来れば起こる)
/// - world_doc: この世界の、この階の書き付け(AI が書いたものか、シナリオの worlds.json の documents)を1つ見せる。
///   value が 1 以上ならその番目、0 なら同じ迷宮の同じマスで毎回同じものを抽選する(同じ階の2か所は、同じものになることがある)。
///   AI を使っていない世界では else の手順を行う(決まった文を書いておく)
/// </summary>
public sealed class EventStep
{
    public static readonly HashSet<string> KnownTypes =
    [
        "text", "choice", "if", "check", "random", "flag", "unflag", "counter", "counter_set", "give_item", "take_item", "treasure", "gold",
        "xp", "heal", "damage", "condition", "cure", "light", "combat", "open", "sound", "run", "town", "end", "again",
        "world_doc", "ending", "close", "pick", "spend_slot", "warp", "timer", "timer_stop",
    ];

    public string Type { get; set; } = "";

    public LocalizedText? Text { get; set; }
    public string? Key { get; set; }
    public LocalizedText? Speaker { get; set; }
    public string? Art { get; set; }

    public List<EventOption> Options { get; set; } = [];
    public bool PerPlace { get; set; }
    public List<EventCondition> When { get; set; } = [];
    public List<EventStep> Then { get; set; } = [];
    public List<EventStep> Else { get; set; } = [];

    public string? Skill { get; set; }
    public string? Ability { get; set; }
    public int Dc { get; set; } = 10;
    public List<EventStep> Success { get; set; } = [];
    public List<EventStep> Fail { get; set; } = [];

    public string? Id { get; set; }
    public int Value { get; set; }
    public int Count { get; set; } = 1;
    public string? Dice { get; set; }

    /// <summary>damage のダメージの種類(抵抗を見る)。</summary>
    public string? DamageType { get; set; }

    public List<BossDef.BossGroup> Groups { get; set; } = [];
    public string? Boss { get; set; }
    public List<EventStep> Win { get; set; } = [];
    public List<EventStep> Lose { get; set; } = [];

    /// <summary>入れ子の手順を全部(検査に使う)。</summary>
    public IEnumerable<List<EventStep>> Children()
    {
        yield return Then;
        yield return Else;
        yield return Success;
        yield return Fail;
        yield return Win;
        yield return Lose;
        foreach (var o in Options) yield return o.Steps;
    }
}
