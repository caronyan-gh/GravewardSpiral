using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Core.Events;

/// <summary>イベントを行う場の情報。迷宮の外(町)では Maze と At は空。</summary>
public sealed class EventContext
{
    public required GameData Data { get; init; }
    public required GameSession Session { get; init; }
    public required IRandom Rng { get; init; }
    public ExplorationState? Maze { get; init; }
    public (int Floor, GridPos Cell)? At { get; init; }

    /// <summary>今の階の深さ(割合)と目安のレベル(宝と、この階の敵に使う)。</summary>
    public double Depth { get; init; }
    public double TargetLevel { get; init; } = 1;

    /// <summary>そのマスで向きを変えて起きた(条件 turned)。</summary>
    public bool Turned { get; init; }

    /// <summary>このマスへどう動いて入ったか(条件 entered。forward / back / left / right。階段やテレポーターで来たときは null)。</summary>
    public string? Entered { get; init; }

    /// <summary>そのマスで唱えた呪文(trigger が cast で起こしたとき。条件 cast)。</summary>
    public string? Cast { get; init; }

    /// <summary>そのマスで休んで起きた(trigger が rest。条件 rested)。</summary>
    public bool Rested { get; init; }

    /// <summary>手順 pick で選んだ人。選んだあとの条件と手順は、この人だけを見る。</summary>
    public Character? Chosen { get; set; }

    /// <summary>
    /// 低い帯のイベントを帯より上の階で起こしたときの引き上げの倍率(EventScale.For)。動かしているイベントが決める。
    /// 金の条件(gold)も、この倍率の2乗で見る(払う額と合わせる)。
    /// </summary>
    public double RewardScale { get; set; } = 1;
    public TreasureSettings? Treasure { get; init; }

    public List<Character> Party => Session.Roster.PartyMembers.ToList();

    /// <summary>一人ずつを見る条件と手順の相手。選んだ人がいればその人だけ、いなければパーティ。</summary>
    public List<Character> Who => Chosen != null ? [Chosen] : Party;
}

/// <summary>イベントが画面に頼むこと。画面はこれを見せて、Next / Choose / CombatEnded で進める。</summary>
public abstract record EventOutput;

/// <summary>文を見せる。Text か、文言のキー Key と引数(LocalizedText は画面の言語で出す)。</summary>
public sealed record EventMessage(LocalizedText? Text, string? Key, object[] Args, LocalizedText? Speaker = null, string? Art = null) : EventOutput;

/// <summary>選んでもらう。選ばれたら Choose(何番目か)。</summary>
public sealed record EventChoice(LocalizedText? Prompt, List<LocalizedText> Labels) : EventOutput;

/// <summary>戦う。終わったら CombatEnded(勝ったか)。groups も boss も無ければ、この階の敵と戦う。</summary>
public sealed record EventCombat(List<(string Monster, int Count)> Groups, string? Boss) : EventOutput;

public sealed record EventSound(string Id) : EventOutput;

/// <summary>パーティを別のマスへ移した(手順 warp)。イベントはここで終わる。画面は着いたマスの様子を知らせる。</summary>
public sealed record EventWarp(bool ChangedFloor) : EventOutput;
public sealed record EventToTown : EventOutput;
public sealed record EventDone : EventOutput;

/// <summary>
/// イベントを1つ行う。画面に関わらない(テストできる)。
/// Next() を呼ぶたびに、次に画面がすること(文・選択・戦闘・音)を返し、終わると EventDone を返す。
/// 選択と戦闘のあとは、Choose / CombatEnded を呼んでから Next() を続ける。
/// </summary>
public sealed class EventRunner
{
    private readonly EventDef _def;
    private readonly EventContext _ctx;
    private readonly Stack<(List<EventStep> Steps, int Index)> _frames = new();
    private readonly Queue<EventOutput> _out = new();
    private List<EventOption>? _choices;
    private EventStep? _pick;              // 一人を選んでいる手順
    private List<Character> _pickable = [];
    private EventStep? _combat;
    private bool _finished;
    private bool _aborted;   // 逃げて終わった(一度きりでも済んだことにしない)

    public EventRunner(EventDef def, EventContext ctx)
    {
        _def = def;
        _ctx = ctx;
        _frames.Push((def.Steps, 0));
        // 低い帯のイベントを上の世界で起こすときは、金・経験値・ダメージ・難易度を引き上げる
        _lift = EventScale.For(def, ctx.TargetLevel, ctx.Data);
        _dcBonus = EventScale.DcBonus(def, ctx.TargetLevel, ctx.Data);
        ctx.RewardScale = _lift;
        ctx.Chosen = null;   // 町のイベントのように、同じ場の情報を続けて使うときに持ち越さない
    }

    private readonly double _lift;
    private readonly int _dcBonus;

    /// <summary>引き上げているとき、文の中の金額も引き上げた額に書き換える(払う額・もらう額と合わせる)。</summary>
    private LocalizedText? LiftText(LocalizedText? text)
    {
        if (text == null || _lift <= 1) return text;
        return new LocalizedText(text.Texts.ToDictionary(kv => kv.Key, kv => LiftMoney(kv.Value, _lift)));
    }

    /// <summary>
    /// 文の中の金額(「30 G」「金貨 30 枚」「金貨30枚」「30 枚の金貨」「30 gold」)を、lift 倍に引き上げた額にする。
    /// 数と単位のあいだの空白は、元のまま残す。
    /// </summary>
    public static string LiftMoney(string text, double lift)
    {
        string Up(string n) => EventScale.Reward(int.Parse(n), lift).ToString();
        text = MoneyAfter.Replace(text, m => Up(m.Groups[1].Value) + m.Groups[2].Value);
        return MoneyBefore.Replace(text, m => m.Groups[1].Value + Up(m.Groups[2].Value) + m.Groups[3].Value);
    }

    /// <summary>数のあとに単位: 30 G / 30 枚の金貨 / 30 gold(gold pieces も)。</summary>
    private static readonly System.Text.RegularExpressions.Regex MoneyAfter =
        new(@"(\d+)(\s?(?:G(?![A-Za-z])|枚の金貨|[Gg]old\b))");

    /// <summary>単位のあとに数: 金貨 30 枚 / 金貨30枚。</summary>
    private static readonly System.Text.RegularExpressions.Regex MoneyBefore = new(@"(金貨\s?)(\d+)(\s?枚)");

    public EventDef Def => _def;

    /// <summary>起こった場所(迷宮のマス)。町のイベントなら null。</summary>
    public (int Floor, GridPos Cell)? At => _ctx.At;

    /// <summary>
    /// 終わったあと、メモに書き留められるか。何度でも起こる迷宮のイベントだけ(一度きりのものを立ち去っただけなら、
    /// レバーや謎かけのように、あとで済ませるものなので書き留めさせない)。
    /// </summary>
    public bool CanNote => _finished && _def.Once == null && _def.Memo && _def.Place == "maze" && _ctx.At != null;

    /// <summary>今ここで起こるか(一度きりで済んでいない、条件が合う)。</summary>
    public static bool Available(EventDef def, EventContext ctx)
    {
        if (def.Once == "game" && ctx.Session.Flags.Contains(def.DoneFlag)) return false;
        if (def.Once == "place" && ctx.At is { } at && ctx.Maze != null && ctx.Maze.UsedMarks.Contains(at)) return false;
        if (ctx.At is { } here && ctx.Maze != null && ctx.Maze.IsNoted(here.Floor, here.Cell, def.Id)) return false;   // 書き留めた場所
        ctx.RewardScale = EventScale.For(def, ctx.TargetLevel, ctx.Data);   // 金の条件は、そのイベントの引き上げで見る
        return Test(def.When, ctx);
    }

    /// <summary>
    /// random の乱数。per_place なら、この迷宮(セーブのシード)・マス・イベント・手順で決まる乱数にする(来るたびに同じものを選ぶ)。
    /// </summary>
    private IRandom PlaceRng(EventStep s)
    {
        if (!s.PerPlace || _ctx.At is not { } at) return _ctx.Rng;
        ulong h = 1469598103934665603UL;
        foreach (char ch in $"{_def.Id}/{at.Floor}/{at.Cell.X}/{at.Cell.Y}/{_def.Steps.IndexOf(s)}") h = (h ^ ch) * 1099511628211UL;
        return new Pcg32(_ctx.Session.Rng.MasterSeed ^ h, 7);
    }

    /// <summary>1回の Next で、何も出さずに進める手順の上限(run の輪などで止まらなくならないように)。</summary>
    public const int MaxStepsPerNext = 10_000;

    public EventOutput Next()
    {
        int ran = 0;
        while (true)
        {
            if (_out.Count > 0) return _out.Dequeue();
            if (++ran > MaxStepsPerNext)
            {
                // 止まらない流れ(検査をすり抜けた run の輪など)は打ち切って終わらせる
                _frames.Clear();
                _finished = true;
                return new EventDone();
            }
            if (_choices != null || _combat != null || _pick != null) throw new InvalidOperationException("選択か戦闘の結果を先に渡してください");
            if (_finished) return new EventDone();
            if (_frames.Count == 0)
            {
                Finish();
                continue;
            }
            var (steps, index) = _frames.Pop();
            if (index >= steps.Count) continue;
            _frames.Push((steps, index + 1));
            // 待つもの(選択・戦闘)は、その前に出した文のあとに見せる
            if (Run(steps[index]) is { } waiting) _out.Enqueue(waiting);
        }
    }

    public void Choose(int index)
    {
        if (_pick is { } pick)
        {
            _pick = null;
            if (index >= 0 && index < _pickable.Count)
            {
                _ctx.Chosen = _pickable[index];
                _frames.Push((pick.Then, 0));
            }
            else _frames.Push((pick.Else, 0));   // やめた
            return;
        }
        var options = _choices ?? throw new InvalidOperationException("選ぶものがありません");
        _choices = null;
        if (index >= 0 && index < options.Count) _frames.Push((options[index].Steps, 0));
    }

    public void CombatEnded(bool won)
    {
        var step = _combat ?? throw new InvalidOperationException("戦闘をしていません");
        _combat = null;
        if (won) _frames.Push((step.Win, 0));
        else if (step.Lose.Count > 0) _frames.Push((step.Lose, 0));
        else
        {
            _aborted = true;
            _frames.Clear();
        }
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _ctx.Session.EventsSeen.Add(_def.Id);   // 次の世界では出にくくする(立ち去っても、見たことにはなる)
        if (_aborted) return;
        if (_def.Once == "game") _ctx.Session.Flags.Add(_def.DoneFlag);
        if (_def.Once == "place" && _ctx.At is { } at) _ctx.Maze?.UsedMarks.Add(at);
    }

    private void Say(string key, params object[] args) => _out.Enqueue(new EventMessage(null, key, args));

    /// <summary>手順を1つ行う。画面の答えを待つもの(選択・戦闘)なら、それを返す。</summary>
    private EventOutput? Run(EventStep s)
    {
        var party = _ctx.Party;
        var able = party.Where(c => c.CanAct).ToList();
        // 一人を選んだあとは、その人だけ
        if (_ctx.Chosen is { } chosen)
        {
            party = [chosen];
            able = [chosen];
        }
        var data = _ctx.Data;
        switch (s.Type)
        {
            case "text":
                _out.Enqueue(new EventMessage(LiftText(s.Text), s.Key, [], s.Speaker, s.Art));
                break;
            case "world_doc":
            {
                // この階の書き付けを1つ。value が 1 以上ならその番目(同じ階の2か所に、別々の書き付けを置くとき)、
                // 0 ならこの迷宮のこのマスで決まる1つ(毎回同じもの)。無ければ else の手順(決まった文)
                var docs = _ctx.At is { } here ? _ctx.Session.World?.Floor(here.Floor)?.Documents : null;
                if (docs is { Count: > 0 })
                {
                    int index = s.Value > 0 ? (s.Value - 1) % docs.Count : PlaceRng(new EventStep { Type = s.Type, PerPlace = true }).Next(0, docs.Count);
                    _out.Enqueue(new EventMessage(docs[index], null, [], s.Speaker, s.Art));
                }
                else _frames.Push((s.Else, 0));
                break;
            }
            case "choice":
            {
                var shown = s.Options.Where(o => Test(o.When, _ctx)).ToList();
                if (shown.Count == 0) break;
                _choices = shown;
                return new EventChoice(LiftText(s.Text), shown.Select(o => LiftText(o.Label) ?? LocalizedText.Of("...")).ToList());
            }
            case "if":
                _frames.Push((Test(s.When, _ctx) ? s.Then : s.Else, 0));
                break;
            case "check":
                _frames.Push((Check(s, able) ? s.Success : s.Fail, 0));
                break;
            case "random":
            {
                double total = s.Options.Sum(o => Math.Max(0, o.Weight));
                double r = PlaceRng(s).NextDouble() * total;
                foreach (var o in s.Options)
                {
                    r -= Math.Max(0, o.Weight);
                    if (r < 0)
                    {
                        _frames.Push((o.Steps, 0));
                        break;
                    }
                }
                break;
            }
            case "flag":
                _ctx.Session.Flags.Add(s.Id!);
                break;
            case "unflag":
                _ctx.Session.Flags.Remove(s.Id!);
                break;
            case "counter":
                _ctx.Session.Counters[s.Id!] = _ctx.Session.Counters.GetValueOrDefault(s.Id!) + s.Value;
                break;
            case "counter_set":
                _ctx.Session.Counters[s.Id!] = s.Value;
                break;
            case "give_item":
                Give(ItemInstance.Of(s.Id!, Math.Max(1, s.Count)), able);
                break;
            case "take_item":
            {
                int taken = TakeItem(party, s.Id!, Math.Max(1, s.Count));
                if (taken > 0) Say("event.lost_item", ItemInstance.Of(s.Id!, taken));
                break;
            }
            case "treasure":
                if (_ctx.Treasure != null)
                    foreach (var item in Treasure.RollMany(data, _ctx.Treasure, _ctx.Depth, Math.Max(1, s.Count), _ctx.Rng, _ctx.TargetLevel)) Give(item, able);
                break;
            case "gold":
            {
                int gold = EventScale.Reward(s.Value, _lift);
                if (gold >= 0)
                {
                    PartyGold.Share(able.Count > 0 ? able : party, gold);
                    Say("event.got_gold", gold);
                }
                else
                {
                    long pay = Math.Min(-gold, PartyGold.Total(party));
                    PartyGold.Pay(party, pay);
                    Say("event.lost_gold", pay);
                }
                break;
            }
            case "xp":
            {
                int xp = EventScale.Reward(s.Value, _lift);
                foreach (var c in able) c.Xp += xp;
                Say("event.got_xp", xp);
                break;
            }
            case "heal":
                foreach (var c in able)
                {
                    int amount = s.Dice == null ? c.MaxHp : DiceExpression.Parse(EventScale.Dice(s.Dice, _lift)!).Roll(_ctx.Rng).Total;
                    c.Hp = Math.Min(c.MaxHp, c.Hp + amount);
                }
                Say(s.Dice == null ? "event.healed_full" : "event.healed");
                break;
            case "damage":
                foreach (var c in able)
                {
                    int dmg = DiceExpression.Parse(EventScale.Dice(s.Dice ?? "1d6", _lift)!).Roll(_ctx.Rng).Total;
                    if (s.Ability != null && Saved(c, s.Ability, EventScale.Dc(s.Dc, _dcBonus))) dmg /= 2;
                    if (s.DamageType != null && c.Passive(data).Any(p => p.Type == EffectPart.Resist && p.DamageType == s.DamageType)) dmg /= 2;
                    if (dmg <= 0) continue;
                    c.Hp = Math.Max(1, c.Hp - dmg);
                    Say("event.hurt", c.Name, dmg);
                }
                break;
            case "condition":
            {
                if (!TryCondition(s.Id, out var cond)) break;
                foreach (var c in able)
                {
                    if (s.Ability != null && Saved(c, s.Ability, EventScale.Dc(s.Dc, _dcBonus))) continue;
                    if (c.Passive(data).Any(p => p.Type == EffectPart.ImmuneCondition && p.Condition == cond)) continue;
                    c.Lasting.RemoveAll(l => l.Condition == cond);
                    c.Lasting.Add(new LastingCondition { Condition = cond, Minutes = s.Value > 0 ? s.Value : null });
                    Say("event.condition", c.Name, cond);
                }
                break;
            }
            case "cure":
            {
                bool any = TryCondition(s.Id, out var cond);
                foreach (var c in party) c.Lasting.RemoveAll(l => !any || l.Condition == cond);
                Say("event.cured");
                break;
            }
            case "light":
                if (_ctx.Maze == null) break;
                if (s.Value <= 0)
                {
                    _ctx.Maze.LightMinutes = 0;   // 明かりを消す
                    Say("event.light_out");
                    break;
                }
                _ctx.Maze.LightMinutes = Math.Max(_ctx.Maze.LightMinutes, s.Value);
                Say("event.light");
                break;
            case "combat":
                _combat = s;
                return new EventCombat(s.Groups.Select(g => (g.Monster, Math.Max(1, g.Count))).ToList(), s.Boss);
            case "open":
                if (Open(s.Id!) > 0) Say("event.gate_opened");
                break;
            case "close":
                if (Close(s.Id!) > 0) Say("event.gate_closed");
                break;
            case "timer":
                _ctx.Session.Timers[s.Id!] = Math.Max(1, s.Value);
                break;
            case "timer_stop":
                _ctx.Session.Timers.Remove(s.Id!);
                break;
            case "pick":
            {
                // 選ぶ相手は、動ける人全員から(when は一人ずつ見る)
                _ctx.Chosen = null;
                var ok = new List<Character>();
                foreach (var c in _ctx.Party.Where(c => c.CanAct))
                {
                    _ctx.Chosen = c;
                    if (Test(s.When, _ctx)) ok.Add(c);
                }
                _ctx.Chosen = null;
                if (ok.Count == 0)
                {
                    _frames.Push((s.Else, 0));
                    break;
                }
                if (s.Value == 1)
                {
                    // くじ: 選ぶ画面を出さずに一人
                    _ctx.Chosen = ok[_ctx.Rng.Next(0, ok.Count)];
                    _frames.Push((s.Then, 0));
                    break;
                }
                _pick = s;
                _pickable = ok;
                var labels = ok.Select(c => LocalizedText.Of(c.Name)).ToList();
                if (s.Else.Count > 0) labels.Add(LocalizedText.Of("Never mind", "やめておく"));
                return new EventChoice(LiftText(s.Text), labels);
            }
            case "spend_slot":
            {
                int min = Math.Max(1, s.Value);
                var who = able.FirstOrDefault(c => LowestSlot(c, min) > 0);
                if (who == null)
                {
                    _frames.Push((s.Else, 0));
                    break;
                }
                int level = LowestSlot(who, min);
                Spellcasting.UseSlot(who, level);
                Say("event.slot_used", who.Name, level);
                _frames.Push((s.Then, 0));
                break;
            }
            case "warp":
            {
                if (_ctx.Maze is not { } maze || Destination(maze, s.Id!) is not { } to) break;
                bool changed = to.Floor != maze.Position.Floor;
                maze.Position = new PartyPosition(to.Floor, to.Cell, maze.Position.Facing);
                maze.AutoMap.Record(maze);   // 着いたマスを地図に
                _frames.Clear();
                _out.Enqueue(new EventWarp(changed));
                break;
            }
            case "sound":
                _out.Enqueue(new EventSound(s.Id!));
                break;
            case "run":
                if (data.Events.TryGetValue(s.Id!, out var other)) _frames.Push((other.Steps, 0));
                break;
            case "town":
                _frames.Clear();
                _out.Enqueue(new EventToTown());
                break;
            case "ending":
                _ctx.Session.Flags.Add(GameSession.EndingFlag);
                _frames.Clear();
                _out.Enqueue(new EventToTown());
                break;
            case "end":
                _frames.Clear();
                break;
            case "again":
                _aborted = true;
                _frames.Clear();
                break;
        }
        return null;
    }

    /// <summary>min レベル以上で、残っている一番低い呪文スロットのレベル(無ければ 0)。</summary>
    private static int LowestSlot(Character c, int min)
    {
        for (int lv = min; lv <= 9; lv++)
        {
            if (Math.Max(0, Spellcasting.RegularSlots(c, lv) - c.SlotsUsed.GetValueOrDefault(lv)) + Spellcasting.PactLeft(c, lv) > 0) return lv;
        }
        return 0;
    }

    /// <summary>印 dest:id のマス。今の階を先に探し、無ければ浅い階から。</summary>
    private static (int Floor, GridPos Cell)? Destination(ExplorationState maze, string id)
    {
        var floors = maze.Dungeon.Floors;
        foreach (int i in new[] { maze.Position.Floor }.Concat(Enumerable.Range(0, floors.Count).Where(i => i != maze.Position.Floor)))
        {
            var f = floors[i];
            foreach (var p in f.AllCells())
            {
                if (f[p].Mark == "dest:" + id && !f[p].IsSolid) return (i, p);
            }
        }
        return null;
    }

    /// <summary>判定。動ける人のうち、一番得意な人が d20 を振る。</summary>
    private bool Check(EventStep s, List<Character> able)
    {
        if (able.Count == 0) return false;
        var data = _ctx.Data;
        SkillDef? skill = s.Skill != null && data.Skills.TryGetValue(s.Skill, out var sk) ? sk : null;
        var ability = skill?.AbilityValue ?? Abilities.Parse(s.Ability ?? "wis");
        int Bonus(Character c) => c.Stats(data).Mod(ability) + (skill != null && c.IsProficient(skill.Id) ? c.ProficiencyBonus : 0);
        var who = able.OrderByDescending(Bonus).First();
        int total = _ctx.Rng.Roll(20) + Bonus(who);
        bool ok = total >= EventScale.Dc(s.Dc, _dcBonus);
        Say(ok ? "event.check_ok" : "event.check_ng", who.Name, (object?)skill?.Name ?? ability, total);
        return ok;
    }

    private bool Saved(Character c, string ability, int dc) => _ctx.Rng.Roll(20) + c.SaveBonus(_ctx.Data, Abilities.Parse(ability)) >= dc;

    private static bool TryCondition(string? id, out Condition cond)
    {
        cond = default;
        return id != null && Enum.TryParse(id.Replace("_", ""), true, out cond);
    }

    private void Give(ItemInstance item, List<Character> able)
    {
        var data = _ctx.Data;
        if (!data.Items.TryGetValue(item.ItemId, out var def)) return;
        var taker = able.FirstOrDefault(c => c.Inventory.TryAdd(item, data.Items));
        if (taker != null) Say("event.got_item", taker.Name, item);
        else if (def.Kind == Items.ItemKind.Key || def.NoRandom)
        {
            // 大事な品は、持ちきれなくても失わない(二度と手に入らないことがある)。倉庫の枠を超えても置く
            _ctx.Session.ClanHouse.Storage.Add(item);
            Say("event.item_sent_home", item);
        }
        else Say("event.item_left", item);
    }

    private static int TakeItem(List<Character> party, string id, int count)
    {
        int taken = 0;
        foreach (var c in party)
        {
            foreach (var item in c.Inventory.Items.Where(i => i.ItemId == id).ToList())
            {
                int n = Math.Min(item.Count, count - taken);
                item.Count -= n;
                taken += n;
                if (item.Count <= 0) c.Inventory.Items.Remove(item);
                if (taken >= count) return taken;
            }
        }
        // 足りなければ、装備している分も取る(「持っているか」の条件は装備も数えるので、それと合わせる)
        foreach (var c in party)
        {
            foreach (var (slot, item) in c.Inventory.Equipped.Where(kv => kv.Value.ItemId == id).ToList())
            {
                int have = Math.Max(1, item.Count);
                int n = Math.Min(have, count - taken);
                taken += n;
                if (have > n) item.Count = have - n;   // 束(矢など)は取った数だけ減らす
                else c.Inventory.Equipped.Remove(slot);
                if (taken >= count) return taken;
            }
        }
        return taken;
    }

    /// <summary>印 gate:id のマスの錠を開ける(今の階)。開けた数を返す。</summary>
    private int Open(string id)
    {
        if (_ctx.Maze is not { } maze) return 0;
        int floor = _ctx.At?.Floor ?? maze.Position.Floor;
        var f = maze.Dungeon.Floors[floor];
        int opened = 0;
        foreach (var p in f.AllCells().Where(p => f[p].Mark == "gate:" + id))
        {
            foreach (var d in DirectionExtensions.All.Where(d => f[p][d] == EdgeType.Locked && !maze.IsUnlocked(floor, p, d)))
            {
                maze.Unlock(floor, p, d);
                opened++;
            }
        }
        return opened;
    }

    /// <summary>印 gate:id のマスの、開けた錠をまた閉める(今の階)。閉めた数を返す。</summary>
    private int Close(string id)
    {
        if (_ctx.Maze is not { } maze) return 0;
        int floor = _ctx.At?.Floor ?? maze.Position.Floor;
        var f = maze.Dungeon.Floors[floor];
        int closed = 0;
        foreach (var p in f.AllCells().Where(p => f[p].Mark == "gate:" + id))
        {
            foreach (var d in DirectionExtensions.All.Where(d => f[p][d] == EdgeType.Locked && maze.IsUnlocked(floor, p, d)))
            {
                maze.Relock(floor, p, d);
                closed++;
            }
        }
        return closed;
    }

    /// <summary>条件を全部満たすか。</summary>
    public static bool Test(IEnumerable<EventCondition> conditions, EventContext ctx) => conditions.All(c => Test(c, ctx) != c.Not);

    private static bool Test(EventCondition c, EventContext ctx)
    {
        var party = ctx.Party;
        var who = ctx.Who;   // 一人を選んだあとは、その人だけ
        var session = ctx.Session;
        return c.Type switch
        {
            "flag" => session.Flags.Contains(c.Id ?? ""),
            "done" => session.Flags.Contains("done:" + c.Id),
            "item" => who.Sum(p => p.Inventory.Items.Where(i => i.ItemId == c.Id).Sum(i => i.Count)
                                    + p.Inventory.Equipped.Values.Where(i => i.ItemId == c.Id).Sum(i => Math.Max(1, i.Count))) >= c.Count,   // 装備の矢の束も本数で数える
            "gold" => PartyGold.Total(party) >= EventScale.Reward(c.Value, ctx.RewardScale),
            "level" => who.Any(p => p.Level >= c.Value),
            "class" => who.Any(p => p.ClassIds.Contains(c.Id)),
            "race" => who.Any(p => p.RaceId == c.Id),
            "equipped" => who.Any(p => p.Inventory.Equipped.Values.Any(i => i.ItemId == c.Id)),
            "slots" => who.Any(p => p.CanAct && LowestSlot(p, Math.Max(1, c.Value)) > 0),
            "entered" => ctx.Entered != null && string.Equals(ctx.Entered, c.Id, StringComparison.OrdinalIgnoreCase),
            "status" => TryCondition(c.Id, out var cond) && who.Any(p => p.Lasting.Any(l => l.Condition == cond)),
            "hp" => who.Any(p => p.IsAlive && p.MaxHp > 0 && Compare(p.Hp * 100 / p.MaxHp, c.Op, c.Value)),
            "armed" => who.Any(p => p.Inventory.Equipped.Where(kv => kv.Key is EquipSlot.MainHand or EquipSlot.OffHand or EquipSlot.Ranged)
                .Any(kv => ctx.Data.Items.TryGetValue(kv.Value.ItemId, out var def) && def.Kind == ItemKind.Weapon)),
            "lit" => ctx.Maze != null && ctx.Maze.LightMinutes > 0,
            "zone" => ctx.Maze != null && ZoneOf(c.Id) is { } z && z != Zone.None
                && ctx.Maze.Dungeon.Floors[ctx.At?.Floor ?? ctx.Maze.Position.Floor][ctx.At?.Cell ?? ctx.Maze.Position.Cell].Zone.HasFlag(z),
            "cast" => ctx.Cast != null && (string.IsNullOrEmpty(c.Id) || string.Equals(ctx.Cast, c.Id, StringComparison.OrdinalIgnoreCase)),
            "rested" => ctx.Rested,
            "timer" => session.Timers.TryGetValue(c.Id ?? "", out int left) && Compare(left, c.Op, c.Value),
            "counter" => Compare(session.Counters.GetValueOrDefault(c.Id ?? ""), c.Op, c.Value),
            "chance" => ctx.Rng.Next(0, 100) < c.Value,
            "key" => ctx.Maze != null && ctx.Maze.Keys.Contains(ctx.At?.Floor ?? ctx.Maze.Position.Floor),
            "alive" => party.Count(p => p.CanAct) >= c.Value,
            "depth" => ctx.Depth * 100 >= c.Value - 1e-9,
            "turned" => ctx.Turned,
            "facing" => ctx.Maze != null && Enum.TryParse<Direction>(c.Id, ignoreCase: true, out var d) && ctx.Maze.Position.Facing == d,
            _ => false,
        };
    }

    private static Zone? ZoneOf(string? id) => id?.ToLowerInvariant() switch
    {
        "dark" => Zone.Dark,
        "fog" => Zone.Fog,
        "antimagic" => Zone.AntiMagic,
        _ => null,
    };

    private static bool Compare(int a, string op, int b) => op switch
    {
        "<=" => a <= b,
        "==" or "=" => a == b,
        "!=" => a != b,
        ">" => a > b,
        "<" => a < b,
        _ => a >= b,
    };
}
