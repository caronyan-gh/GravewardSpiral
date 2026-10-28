using System.Text.Json;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Core.Save;

/// <summary>エディタと検査で共用するフラグ・セットの編集。実行時のフラグ保存方式には触れない。</summary>
public static class EventAuthoring
{
    private static string Allocate(HashSet<string> used, string prefix)
    {
        for (int n = 1; ; n++)
        {
            string id = $"{prefix}_{n:000}";
            if (used.Add(id)) return id;
        }
    }
    public static string NewId(ScenarioPack pack, string prefix, IEnumerable<EventDef>? standard = null) => Allocate(UsedIds(pack, standard ?? []), prefix);
    private static HashSet<string> UsedIds(ScenarioPack pack, IEnumerable<EventDef> standard)
    {
        var events = pack.Events.Concat(standard).ToList();
        return events.Select(e => e.Id).Concat(events.SelectMany(FlagNames))
            .Concat(events.SelectMany(e => Steps(e).Select(s => s.Id).Concat(Conditions(e).Select(c => c.Id))).OfType<string>())
            .Concat(pack.Flags.Select(f => f.Id))
            .Concat(pack.EventSets.SelectMany(s => s.Parts.Select(p => p.Target).Concat(s.OwnedFlags).Concat(s.OwnedCounters).Append(s.Id)))
            .Concat(pack.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>().Select(m => m.Contains(':') ? m[(m.IndexOf(':') + 1)..] : m))
            .Concat(pack.Elevators.Select(e => e.Power).OfType<string>())
            .Concat(pack.Rumors.SelectMany(r => r.When).Select(c => c.Id).OfType<string>())
            .Concat(pack.Bosses.Select(b => (string?)b["reward_flag"]).OfType<string>()).ToHashSet(StringComparer.Ordinal);
    }
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, ContentLibrary.JsonOptions), ContentLibrary.JsonOptions)!;
    public static IEnumerable<EventStep> Steps(EventDef e) => Walk(e.Steps);
    private static IEnumerable<EventStep> Walk(IEnumerable<EventStep> steps)
    {
        foreach (var s in steps)
        {
            yield return s;
            foreach (var child in s.Children()) foreach (var inner in Walk(child)) yield return inner;
        }
    }
    public static IEnumerable<EventCondition> Conditions(EventDef e) => e.When.Concat(Steps(e).SelectMany(s => s.Conditions()));
    public static IEnumerable<string> FlagNames(EventDef e) => Steps(e).Where(s => s.Type is "flag" or "unflag").Select(s => s.Id)
        .Concat(Conditions(e).Where(c => c.Type == "flag").Select(c => c.Id)).Append(e.OwnFlag).OfType<string>().Distinct();
    public static List<(int Floor, GridPos Cell)> Locations(ScenarioPack pack, string mark) => pack.Floors
        .SelectMany((f, i) => f.AllCells().Where(c => f[c].Mark == mark).Select(c => (i, c))).ToList();
    public static IEnumerable<EventDef> Events(ScenarioPack pack, IEnumerable<EventDef> standard) =>
        pack.Events.Concat(standard).DistinctBy(e => e.Id);

    public static EventDef DuplicateEvent(ScenarioPack pack, EventDef source, IEnumerable<EventDef>? standard = null)
    {
        var e = Copy(source);
        var used = UsedIds(pack, (standard ?? []).Append(source));
        e.Id = Allocate(used, "event");
        if (source.OwnFlag is { } flag)
        {
            e.OwnFlag = Allocate(used, "flag");
            Rewrite(e, new Dictionary<string, string> { [source.Id] = e.Id }, new Dictionary<string, string> { [flag] = e.OwnFlag }, [], [], []);
        }
        else Rewrite(e, new Dictionary<string, string> { [source.Id] = e.Id }, [], [], [], []);
        pack.Events.Add(e);
        return e;
    }

    /// <summary>選んだイベントと run/timer で呼ぶ処理をセットにまとめる。専用フラグ以外は所有を推測しない。</summary>
    public static EventSetDef FromEvent(EventDef root, IReadOnlyDictionary<string, EventDef> events)
    {
        var set = new EventSetDef { Id = "set_" + root.Id, Name = root.Title, Kind = root.Title };
        void Add(EventDef e, bool place)
        {
            if (set.Parts.Any(p => p.Kind == "event" && p.Target == e.Id)) return;
            set.Parts.Add(new() { Target = e.Id, Name = e.Title ?? LocalizedText.Of(e.Id, e.Note?.Split('。')[0] ?? e.Id), Place = place && e.Place == "maze" });
            foreach (var s in Steps(e))
            {
                if (s.Type is "run" or "timer" && s.Id != null && events.TryGetValue(s.Id, out var child)) Add(child, false);
                string? kind = s.Type is "open" or "close" ? "gate" : s.Type == "warp" ? "dest" : null;
                if (kind != null && s.Id != null && !set.Parts.Any(p => p.Kind == kind && p.Target == s.Id))
                    set.Parts.Add(new() { Kind = kind, Target = s.Id, Name = LocalizedText.Of(s.Id, s.Id) });
            }
        }
        Add(root, true);
        return set;
    }

    /// <summary>標準の明示セットと既存の scatter.chain を一覧にする。chain から作る場合もグローバルの所有権は推測しない。</summary>
    public static List<EventSetDef> Templates(ContentLibrary content, IReadOnlyDictionary<string, EventDef> events)
    {
        var sets = content.All("event_sets").Select(x => x.As<EventSetDef>()).ToList();
        foreach (var group in events.Values.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!))
        {
            if (sets.Any(s => s.Id == group.Key)) continue;
            var first = group.OrderBy(e => e.Scatter!.Step).First();
            var set = new EventSetDef { Id = group.Key, Name = first.Title ?? LocalizedText.Of(group.Key, first.Note?.Split('。')[0] ?? group.Key) };
            set.Kind = set.Name;
            foreach (var e in group.OrderBy(e => e.Scatter!.Step))
                foreach (var part in FromEvent(e, events).Parts)
                    if (!set.Parts.Any(p => p.Mark == part.Mark)) set.Parts.Add(part);
            // 町の依頼は地図には置かないが、セットを取り込むときは一緒に写す。
            foreach (var e in events.Values.Where(e => e.Place == "town" && e.When.Any(c => c.Type == "flag" && c.Id == EventPlacer.ChainFlag(group.Key))))
                if (!set.Parts.Any(p => p.Target == e.Id && p.Kind == "event")) set.Parts.Add(new() { Target = e.Id, Name = e.Title, Place = false });
            sets.Add(set);
        }
        return sets;
    }

    /// <summary>セットを一度で追加。新しいイベント・門・着地点・専用状態を割り当て、内部参照をまとめて直す。配置位置は作者が選ぶ。</summary>
    public static EventSetDef ImportSet(ScenarioPack pack, EventSetDef source, IReadOnlyDictionary<string, EventDef> events, LocalizedText name)
    {
        if (source.Parts.Any(p => p.Kind is not ("event" or "gate" or "dest") || string.IsNullOrWhiteSpace(p.Target)))
            throw new InvalidOperationException("セットの部品には event / gate / dest と参照先が必要です");
        if (source.Parts.GroupBy(p => p.Mark).Any(g => g.Count() > 1)) throw new InvalidOperationException("セットに同じ部品が重複しています");
        var set = Copy(source);
        var used = UsedIds(pack, events.Values);
        foreach (var id in source.OwnedFlags.Concat(source.OwnedCounters).Concat(source.Parts.Select(p => p.Target)).Append(source.Id)) used.Add(id);
        set.Id = Allocate(used, "set");
        set.Name = name;
        set.Kind ??= source.Name;
        var eventIds = source.Parts.Where(p => p.Kind == "event").Select(p => p.Target).Distinct()
            .ToDictionary(id => id, _ => Allocate(used, "event"));
        foreach (var id in eventIds.Keys) if (!events.ContainsKey(id)) throw new InvalidOperationException($"セットのイベント {id} がありません");
        var owned = source.OwnedFlags.Concat(source.Parts.Select(p => p.OwnFlag).OfType<string>()).Concat(eventIds.Keys.Select(id => events[id].OwnFlag).OfType<string>()).Distinct().ToList();
        used.UnionWith(owned);
        var flags = owned.ToDictionary(id => id, _ => Allocate(used, "flag"));
        var counters = source.OwnedCounters.Distinct().ToDictionary(id => id, _ => Allocate(used, "counter"));
        var gates = source.Parts.Where(p => p.Kind == "gate").Select(p => p.Target).Distinct().ToDictionary(id => id, _ => Allocate(used, "gate"));
        var dests = source.Parts.Where(p => p.Kind == "dest").Select(p => p.Target).Distinct().ToDictionary(id => id, _ => Allocate(used, "dest"));
        foreach (var chain in eventIds.Keys.Select(id => events[id].Scatter?.Chain).OfType<string>().Distinct())
            flags.TryAdd(EventPlacer.ChainFlag(chain), EventPlacer.ChainFlag(set.Id));
        var copies = new List<EventDef>();
        foreach (var (old, id) in eventIds)
        {
            var e = Copy(events[old]);
            e.Id = id;
            var part = source.Parts.First(p => p.Kind == "event" && p.Target == old);
            e.OwnFlag = part.OwnFlag ?? e.OwnFlag;
            if (e.OwnFlag != null) e.OwnFlag = flags[e.OwnFlag];
            if (e.Scatter != null) e.Scatter.Chain = set.Id;
            Rewrite(e, eventIds, flags, counters, gates, dests);
            e.Title = LocalizedText.Of($"{name.Get("en")} - {part.Name?.Get("en") ?? old}", $"{name.Get("ja")} - {part.Name?.Get("ja") ?? old}");
            copies.Add(e);
        }
        foreach (var p in set.Parts)
        {
            p.Target = (p.Kind == "event" ? eventIds : p.Kind == "gate" ? gates : dests)[p.Target];
            p.OwnFlag = null; // 取り込み後の所有者は EventDef.OwnFlag。テンプレートの指定を二重管理しない
        }
        set.OwnedFlags = source.OwnedFlags.Select(id => flags[id]).ToList();
        set.OwnedCounters = source.OwnedCounters.Select(id => counters[id]).ToList();
        foreach (var old in source.OwnedFlags.Distinct())
        {
            string id = flags[old];
            if (copies.Any(e => e.OwnFlag == id)) continue;
            var definition = pack.Flags.FirstOrDefault(f => f.Id == old);
            var copied = definition != null ? Copy(definition) : new EventFlagDef { Name = LocalizedText.Of(old, old) };
            copied.Id = id;
            pack.Flags.Add(copied);
        }
        pack.Events.AddRange(copies);
        pack.EventSets.Add(set);
        return set;
    }

    private static void Rewrite(EventDef e, Dictionary<string, string> events, Dictionary<string, string> flags,
        Dictionary<string, string> counters, Dictionary<string, string> gates, Dictionary<string, string> dests)
    {
        string? Map(Dictionary<string, string> ids, string? id) => id != null ? ids.GetValueOrDefault(id, id) : null;
        foreach (var c in Conditions(e))
            c.Id = c.Type switch { "flag" => Map(flags, c.Id), "done" => Map(events, c.Id), "counter" => Map(counters, c.Id), _ => c.Id };
        foreach (var s in Steps(e))
            s.Id = s.Type switch
            {
                "flag" or "unflag" => Map(flags, s.Id), "counter" or "counter_set" => Map(counters, s.Id),
                "run" or "timer" or "timer_stop" => Map(events, s.Id), "open" or "close" => Map(gates, s.Id), "warp" => Map(dests, s.Id), _ => s.Id,
            };
    }

    /// <summary>選んだマスへ置く。門は錠のあるマスへ。置き換えや複数配置を黙って行わない。</summary>
    public static void Place(ScenarioPack pack, string mark, int floor, GridPos cell, bool move, IEnumerable<EventDef> standard)
    {
        if (floor < 0 || floor >= pack.Floors.Count || !pack.Floors[floor].InBounds(cell.X, cell.Y) || pack.Floors[floor][cell].IsSolid)
            throw new InvalidOperationException("床のマスを選んでください");
        var f = pack.Floors[floor];
        if (f[cell].Mark is { Length: > 0 } old && old != mark) throw new InvalidOperationException("別の印があります。先に外してください");
        if (f[cell].Feature != CellFeature.None) throw new InvalidOperationException("階段などの仕掛けのないマスを選んでください");
        if (mark.StartsWith("gate:") && !DirectionExtensions.All.Any(d => f[cell][d] == EdgeType.Locked))
            throw new InvalidOperationException("門は錠のある辺に接するマスに置いてください");
        var places = Locations(pack, mark).Where(p => p != (floor, cell)).ToList();
        bool unique = pack.EventSets.SelectMany(s => s.Parts).Any(p => p.Place && p.Mark == mark)
            || (mark.StartsWith("event:") && Events(pack, standard).Any(e => e.Id == mark[6..] && e.OwnFlag != null));
        if (unique && places.Count > 0 && !move) throw new InvalidOperationException("配置済みです。移動するか複製してください");
        if (move) foreach (var (i, c) in places) pack.Floors[i][c].Mark = null;
        f[cell].Mark = mark;
    }

    /// <summary>残りを選んだ階へ一括配置。門を先に錠へ割り当て、全部置けるときだけ変更する。</summary>
    public static void PlaceRemaining(ScenarioPack pack, EventSetDef set, int floor)
    {
        if (floor < 0 || floor >= pack.Floors.Count) throw new InvalidOperationException("階を選んでください");
        var f = pack.Floors[floor];
        var free = f.AllCells().Where(c => !f[c].IsSolid && f[c].Feature == CellFeature.None && string.IsNullOrEmpty(f[c].Mark)).ToList();
        var planned = new List<(GridPos Cell, string Mark)>();
        foreach (var part in set.Parts.Where(p => p.Place).OrderBy(p => p.Kind == "gate" ? 0 : 1))
        {
            var existing = Locations(pack, part.Mark);
            if (existing.Count > 1) throw new InvalidOperationException("重複した配置を先に整理してください");
            if (existing.Count == 1) continue;
            var choices = free.Where(c => part.Kind != "gate" || DirectionExtensions.All.Any(d => f[c][d] == EdgeType.Locked)).ToList();
            if (choices.Count == 0) throw new InvalidOperationException(part.Kind == "gate" ? "開く扉を置くための、錠に接した空きマスがありません" : "全部の部品を置く空きマスがありません");
            var cell = choices[0]; free.Remove(cell); planned.Add((cell, part.Mark));
        }
        foreach (var (cell, mark) in planned) f[cell].Mark = mark;
    }

    public static List<string> Check(ScenarioPack pack, IEnumerable<EventDef> standard, IEnumerable<EventSetDef>? templates = null)
    {
        var errors = new List<string>();
        var events = Events(pack, standard).ToDictionary(e => e.Id);
        foreach (var group in pack.EventSets.SelectMany(s => s.Parts.Where(p => p.Place).Select(p => (Set: s, Part: p))).GroupBy(x => x.Part.Mark))
            if (group.Count() > 1) errors.Add($"部品 {group.Key} が複数のセットに属しています");
        foreach (var group in events.Values.Where(e => e.OwnFlag != null).GroupBy(e => e.OwnFlag))
            if (group.Count() > 1) errors.Add($"専用フラグ {group.Key} が複数のイベントで共有されています: {string.Join(", ", group.Select(e => e.Id))}");
        foreach (var e in events.Values.Where(e => e.OwnFlag != null))
            if (Locations(pack, "event:" + e.Id).Count > 1) errors.Add($"専用フラグを持つイベント {e.Id} が複数箇所にあります");
        var sets = pack.EventSets.Concat((templates ?? []).Where(s => !pack.EventSets.Any(x => x.Id == s.Id) && s.Parts.Any(p => p.Place && Locations(pack, p.Mark).Count > 0)));
        foreach (var set in sets)
        {
            string name = set.Name?.Get("ja") ?? set.Id;
            foreach (var p in set.Parts)
            {
                if (p.Kind is not ("event" or "gate" or "dest")) { errors.Add($"{name}: 部品の種類 {p.Kind} が不明です"); continue; }
                if (p.Kind == "event" && !events.ContainsKey(p.Target)) errors.Add($"{name}: イベント {p.Target} がありません");
                if (!p.Place) continue;
                int n = Locations(pack, p.Mark).Count;
                if (n != 1) errors.Add($"{name} - {p.Name?.Get("ja") ?? p.Target}: " + (n == 0 ? "未配置" : $"{n} 箇所に重複配置"));
            }
        }
        return errors;
    }
}
