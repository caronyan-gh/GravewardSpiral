using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// 迷宮にイベントを散らす(壁の文字、遺品、墓石など。events.json の scatter を持つもの)。
/// 新しいゲームを作るときに1回だけ行い、マスに印 event:&lt;id&gt; を書く(迷宮と一緒にセーブに入る)。
/// 階段・仕掛け・ほかの印のあるマスには置かない。道をふさがないので、到達の検査には関わらない。
/// </summary>
public static class EventPlacer
{
    public const string MarkPrefix = "event:";

    /// <summary>組のフラグ(置いた組の chain:&lt;名前&gt;)。</summary>
    public static string ChainFlag(string chain) => "chain:" + chain;

    /// <summary>
    /// 町の依頼の組(町のイベントが when でフラグ chain:&lt;名前&gt; を見る組)の名前。
    /// </summary>
    public static HashSet<string> QuestChains(GameData data) => data.Events.Values.Where(e => e.Place == "town")
        .SelectMany(e => e.When).Where(c => c.Type == "flag" && !c.Not && c.Id?.StartsWith("chain:") == true)
        .Select(c => c.Id!["chain:".Length..]).ToHashSet();

    /// <summary>
    /// 連動するイベントの組を、chains の数だけ置く(組の部品を step の順に、同じ階か深い階へ)。置いた組の名前を返す。
    /// 町の依頼の組は、置ける限り minQuests 個は必ず置く(いつも町にいる人に、毎回何かしら頼まれごとがある)。
    /// 先に組を置いてから、ふつうのイベントを散らす。
    /// </summary>
    /// <summary>このクランが前の世界で見たイベントの出やすさの倍率(またこれか、を減らす。ゼロにはしない)。</summary>
    public const double SeenWeight = 0.3;

    /// <summary>クランが噂を聞いたイベント(伝説の噂)の出やすさの倍率。聞いた世界の数だけ倍になる(上限 16 倍)。</summary>
    public static double HeardMultiplier(IReadOnlyDictionary<string, int>? heard, string id) =>
        heard != null && heard.TryGetValue(id, out int n) && n > 0 ? Math.Min(16, Math.Pow(2, n)) : 1;

    /// <param name="allow">散らしてよいイベント(省くと全部。シナリオが標準のイベントを混ぜないときは、シナリオのものだけ。ScenarioOnly)。</param>
    /// <summary>
    /// 選んでいるシナリオが標準のイベントを混ぜないなら、そのシナリオのパックのイベントだけを通すもの。混ぜるなら(か、シナリオが無ければ)null。
    /// </summary>
    public static Func<EventDef, bool>? ScenarioOnly(Content.ContentLibrary content)
    {
        if (content.Scenario is not { StandardEvents: false } scenario) return null;
        var mine = content.All(EventDef.Collection).Where(i => i.Pack.Id == scenario.Id).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        return e => mine.Contains(e.Id);
    }

    public static List<string> ScatterChains(GameData data, Dungeon dungeon, IntRange chains, ulong seed, Func<double, double>? levelAt = null,
        int minQuests = 0, IReadOnlySet<string>? seen = null, IReadOnlyDictionary<string, int>? heard = null, Func<EventDef, bool>? allow = null)
    {
        IRandom rng = RngStreams.Create(seed, "events/chains");
        int floors = dungeon.Floors.Count;
        double DepthOf(int i) => floors <= 1 ? 0 : i / (double)(floors - 1);
        bool Allowed(EventDef e, int i)
        {
            var sc = e.Scatter!;
            double d = DepthOf(i), lv = levelAt?.Invoke(d) ?? 0;
            // 帯より上の階にも置く(数字は EventScale で引き上げる)。帯より下には置かない
            return d >= sc.MinDepth - 1e-9 && d <= sc.MaxDepth + 1e-9 && (levelAt == null || lv >= sc.MinLevel - 1e-9);
        }
        var existingMarks = dungeon.Floors.SelectMany(f => f.AllCells().Select(p => f[p].Mark)).Where(m => m != null).ToHashSet();
        var groups = data.Events.Values.Where(e => e.Place == "maze" && e.Scatter?.Chain != null && (allow == null || allow(e)))
            .GroupBy(e => e.Scatter!.Chain!).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Parts: g.OrderBy(e => e.Scatter!.Step).ThenBy(e => e.Id, StringComparer.Ordinal).ToList()))
            // 専用フラグを持つ組は、手で一部を置いた後に自動で増やさない。不足はエディタの検査で知らせる。
            .Where(g => !g.Parts.Any(e => e.OwnFlag != null) || !g.Parts.Any(e => existingMarks.Contains(MarkPrefix + e.Id)))
            .Where(g => Enumerable.Range(0, floors).Any(i => Allowed(g.Parts[0], i))).ToList();
        var placed = new List<string>();
        var quests = QuestChains(data);
        int want = chains.Pick(rng), questsPlaced = 0;
        while (placed.Count < Math.Max(want, minQuests) && groups.Count > 0)
        {
            // 依頼の組が足りないうちは、依頼の組から選ぶ
            bool needQuest = questsPlaced < minQuests && groups.Any(g => quests.Contains(g.Name));
            if (!needQuest && placed.Count >= want) break;
            var pool = groups.Where(g => !needQuest || quests.Contains(g.Name)).ToList();
            // 迷宮の一番浅い階でも帯より上なら、出やすさを下げる
            double W(EventDef first) => first.Scatter!.EffectiveWeight
                * (levelAt != null && levelAt(0) > first.Scatter.MaxLevel + 1e-9 ? EventScale.AboveBandWeight : 1)
                * (seen != null && seen.Contains(first.Id) ? SeenWeight : 1)
                * HeardMultiplier(heard, first.Id);
            double total = pool.Sum(g => W(g.Parts[0])), r = rng.NextDouble() * total;
            var chosen = pool[^1];
            foreach (var g in pool)
            {
                if ((r -= W(g.Parts[0])) < 0)
                {
                    chosen = g;
                    break;
                }
            }
            var (name, parts) = chosen;
            groups.Remove(chosen);
            // 何度か試す(部品ごとに、前の部品と同じか深い階で、置けるマスのある階を選ぶ)
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var marks = new List<(MazeFloor F, GridPos P)>();
                int floor = 0;
                bool ok = true;
                foreach (var e in parts)
                {
                    var choices = Enumerable.Range(floor, floors - floor).Where(i => Allowed(e, i)
                        && dungeon.Floors[i].AllCells().Any(p => Fits(dungeon.Floors[i], p, "any"))).ToList();
                    if (choices.Count == 0)
                    {
                        ok = false;
                        break;
                    }
                    floor = choices[rng.Next(0, choices.Count)];
                    var f = dungeon.Floors[floor];
                    var cells = f.AllCells().Where(p => Fits(f, p, e.Scatter!.Where)).ToList();
                    if (cells.Count == 0) cells = f.AllCells().Where(p => Fits(f, p, "any")).ToList();
                    var cell = cells[rng.Next(0, cells.Count)];
                    f[cell].Mark = MarkPrefix + e.Id;
                    marks.Add((f, cell));
                }
                if (ok)
                {
                    placed.Add(name);
                    if (quests.Contains(name)) questsPlaced++;
                    break;
                }
                foreach (var (f, p) in marks) f[p].Mark = null;   // 組の途中までは置かない
            }
        }
        return placed;
    }

    /// <param name="levelAt">深さ(0〜1)からその階の目安のレベル。null ならレベルで絞らない。</param>
    /// <param name="seen">このクランが前の世界で見たイベント(出やすさを SeenWeight 倍にする)。</param>
    /// <param name="heard">このクランが噂を聞いたイベント(聞いた回数だけ出やすくする。HeardMultiplier)。</param>
    /// <remarks>一度きり(once が place)のイベントは、max を書いていなければ1つの世界に1つまで(同じものを別の階に置かない)。</remarks>
    public static int Scatter(GameData data, Dungeon dungeon, IntRange perFloor, ulong seed, Func<double, double>? levelAt = null,
        IReadOnlySet<string>? seen = null, IReadOnlyDictionary<string, int>? heard = null, Func<EventDef, bool>? allow = null)
    {
        var pool = data.Events.Values.Where(e => e.Scatter != null && e.Scatter.Chain == null && e.Place == "maze" && (allow == null || allow(e)))
            .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (pool.Count == 0) return 0;
        var rng = RngStreams.Create(seed, "events/scatter");
        var used = new Dictionary<string, int>();
        int floors = dungeon.Floors.Count, placed = 0;
        for (int i = 0; i < floors; i++)
        {
            double depth = floors <= 1 ? 0 : i / (double)(floors - 1);
            double level = levelAt?.Invoke(depth) ?? 0;
            var f = dungeon.Floors[i];
            int count = perFloor.Pick(rng);
            for (int k = 0; k < count; k++)
            {
                var choices = pool.Where(e => depth >= e.Scatter!.MinDepth && depth <= e.Scatter.MaxDepth
                    && (levelAt == null || level >= e.Scatter.MinLevel - 1e-9)
                    && used.GetValueOrDefault(e.Id) < MaxPerWorld(e)).ToList();
                if (choices.Count == 0) break;
                var ev = PickWeighted(choices, rng, e => (levelAt != null && level > e.Scatter!.MaxLevel + 1e-9 ? EventScale.AboveBandWeight : 1)
                    * (seen != null && seen.Contains(e.Id) ? SeenWeight : 1)
                    * HeardMultiplier(heard, e.Id));
                var cells = f.AllCells().Where(p => Fits(f, p, ev.Scatter!.Where)).ToList();
                if (cells.Count == 0) cells = f.AllCells().Where(p => Fits(f, p, "any")).ToList();
                if (cells.Count == 0) break;
                f[cells[rng.Next(0, cells.Count)]].Mark = MarkPrefix + ev.Id;
                used[ev.Id] = used.GetValueOrDefault(ev.Id) + 1;
                placed++;
            }
        }
        return placed;
    }

    /// <summary>1つの世界に置く数の上限(max。書いていなければ、一度きりのものは 1、何度でも起こるものは上限なし)。</summary>
    public static int MaxPerWorld(EventDef e) => e.OwnFlag != null ? 1 : e.Scatter!.Max > 0 ? e.Scatter.Max : e.Once == "place" ? 1 : int.MaxValue;

    private static EventDef PickWeighted(List<EventDef> list, IRandom rng, Func<EventDef, double>? factor = null)
    {
        double W(EventDef e) => e.Scatter!.EffectiveWeight * (factor?.Invoke(e) ?? 1);
        double total = list.Sum(W);
        double r = rng.NextDouble() * total;
        foreach (var e in list)
        {
            r -= W(e);
            if (r < 0) return e;
        }
        return list[^1];
    }

    private static bool Fits(MazeFloor f, GridPos p, string where)
    {
        var c = f[p];
        if (c.IsSolid || c.Feature != CellFeature.None || c.Mark != null) return false;
        int open = DirectionExtensions.All.Count(d => c[d] != EdgeType.Wall && f.Neighbor(p, d) != null);
        if (open == 0) return false;
        return where switch
        {
            "dead_end" => open == 1,
            "corridor" => open == 2,
            "room" => open >= 3,
            _ => true,
        };
    }
}
