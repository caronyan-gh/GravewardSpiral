using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Effects;
using GravewardSpiral.Core.Items;
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

    /// <summary>
    /// 低い帯のイベントを帯より上の階で起こしたときの引き上げの倍率(EventScale.For)。動かしているイベントが決める。
    /// 金の条件(gold)も、この倍率の2乗で見る(払う額と合わせる)。
    /// </summary>
    public double RewardScale { get; set; } = 1;
    public TreasureSettings? Treasure { get; init; }

    public List<Character> Party => Session.Roster.PartyMembers.ToList();
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
    }

    private readonly double _lift;
    private readonly int _dcBonus;

    /// <summary>引き上げているとき、文の中の「30 G」も引き上げた額に書き換える(払う額・もらう額と合わせる)。</summary>
    private LocalizedText? LiftText(LocalizedText? text)
    {
        if (text == null || _lift <= 1) return text;
        return new LocalizedText(text.Texts.ToDictionary(kv => kv.Key,
            kv => System.Text.RegularExpressions.Regex.Replace(kv.Value, @"(\d+)(\s?G)(?![A-Za-z])", m => EventScale.Reward(int.Parse(m.Groups[1].Value), _lift) + m.Groups[2].Value)));
    }

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

    public EventOutput Next()
    {
        while (true)
        {
            if (_out.Count > 0) return _out.Dequeue();
            if (_choices != null || _combat != null) throw new InvalidOperationException("選択か戦闘の結果を先に渡してください");
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
        var data = _ctx.Data;
        switch (s.Type)
        {
            case "text":
                _out.Enqueue(new EventMessage(LiftText(s.Text), s.Key, [], s.Speaker, s.Art));
                break;
            case "world_doc":
            {
                // AI が書いたこの階の書き付けを1つ(この迷宮のこのマスでは毎回同じもの)。無ければ else の手順(決まった文)
                var docs = _ctx.At is { } here ? _ctx.Session.World?.Floor(here.Floor)?.Documents : null;
                if (docs is { Count: > 0 })
                {
                    var rng = PlaceRng(new EventStep { Type = s.Type, PerPlace = true });
                    _out.Enqueue(new EventMessage(docs[rng.Next(0, docs.Count)], null, [], s.Speaker, s.Art));
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
                if (_ctx.Maze != null) _ctx.Maze.LightMinutes = Math.Max(_ctx.Maze.LightMinutes, s.Value);
                Say("event.light");
                break;
            case "combat":
                _combat = s;
                return new EventCombat(s.Groups.Select(g => (g.Monster, Math.Max(1, g.Count))).ToList(), s.Boss);
            case "open":
                if (Open(s.Id!) > 0) Say("event.gate_opened");
                break;
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

    /// <summary>条件を全部満たすか。</summary>
    public static bool Test(IEnumerable<EventCondition> conditions, EventContext ctx) => conditions.All(c => Test(c, ctx) != c.Not);

    private static bool Test(EventCondition c, EventContext ctx)
    {
        var party = ctx.Party;
        var session = ctx.Session;
        return c.Type switch
        {
            "flag" => session.Flags.Contains(c.Id ?? ""),
            "done" => session.Flags.Contains("done:" + c.Id),
            "item" => party.Sum(p => p.Inventory.Items.Where(i => i.ItemId == c.Id).Sum(i => i.Count)
                                    + p.Inventory.Equipped.Values.Count(i => i.ItemId == c.Id)) >= c.Count,
            "gold" => PartyGold.Total(party) >= EventScale.Reward(c.Value, ctx.RewardScale),
            "level" => party.Any(p => p.Level >= c.Value),
            "class" => party.Any(p => p.ClassIds.Contains(c.Id)),
            "race" => party.Any(p => p.RaceId == c.Id),
            "counter" => Compare(session.Counters.GetValueOrDefault(c.Id ?? ""), c.Op, c.Value),
            "chance" => ctx.Rng.Next(0, 100) < c.Value,
            "key" => ctx.Maze != null && ctx.Maze.Keys.Contains(ctx.At?.Floor ?? ctx.Maze.Position.Floor),
            "alive" => party.Count(p => p.CanAct) >= c.Value,
            "depth" => ctx.Depth * 100 >= c.Value - 1e-9,
            _ => false,
        };
    }

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
