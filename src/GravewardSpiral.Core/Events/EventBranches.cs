using System.Text.Json.Nodes;

namespace GravewardSpiral.Core.Events;

/// <summary>エディタとデータ移行で共有する分岐の変換。文や未知の項目を再生成せず、JSON の枝を移す。</summary>
public static class EventBranches
{
    public static bool IsIfChain(JsonObject step) => (string?)step["type"] == "if"
        && step["else"] is JsonArray { Count: 1 } tail && tail[0] is JsonObject next && (string?)next["type"] == "if";

    /// <summary>単独の if も変換できる。else にほかの手順があれば、それ以上はまとめない。</summary>
    public static JsonObject ToCases(JsonObject step)
    {
        if ((string?)step["type"] != "if") throw new ArgumentException("if が必要です", nameof(step));
        var result = (JsonObject)step.DeepClone();
        var cases = new JsonArray();
        var current = step;
        while (true)
        {
            cases.Add(new JsonObject { ["when"] = current["when"]?.DeepClone() ?? new JsonArray(), ["then"] = current["then"]?.DeepClone() ?? new JsonArray() });
            if (!IsIfChain(current)) break;
            current = (JsonObject)current["else"]![0]!;
        }
        result["type"] = "cases";
        result.Remove("when");
        result.Remove("then");
        result["cases"] = cases;
        result["else"] = current["else"]?.DeepClone() ?? new JsonArray();
        return result;
    }

    public static JsonObject ToIf(JsonObject step)
    {
        if ((string?)step["type"] != "cases") throw new ArgumentException("cases が必要です", nameof(step));
        var branches = (step["cases"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        JsonArray tail = step["else"]?.DeepClone() as JsonArray ?? [];
        for (int i = branches.Count - 1; i >= 0; i--)
        {
            var b = branches[i];
            tail = [new JsonObject { ["type"] = "if", ["when"] = b["when"]?.DeepClone() ?? new JsonArray(), ["then"] = b["then"]?.DeepClone() ?? new JsonArray(), ["else"] = tail }];
        }
        var result = (JsonObject)step.DeepClone();
        result.Remove("cases");
        result["type"] = "if";
        var first = tail.FirstOrDefault() as JsonObject;
        result["when"] = branches.Count > 0 ? first!["when"]!.DeepClone() : new JsonArray();
        result["then"] = branches.Count > 0 ? first!["then"]!.DeepClone() : tail.DeepClone();
        result["else"] = branches.Count > 0 ? first!["else"]!.DeepClone() : new JsonArray();
        return result;
    }

    /// <summary>イベントの配列を一度だけ移行する。既に cases ならそのまま。戻り値はまとめた連なりの数。</summary>
    public static int ConvertChains(JsonArray events)
    {
        int count = 0;
        void Steps(JsonArray list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not JsonObject s) continue;
                if (IsIfChain(s)) { s = ToCases(s); list[i] = s; count++; }
                foreach (string key in new[] { "then", "else", "success", "fail", "win", "lose" })
                    if (s[key] is JsonArray child) Steps(child);
                if (s["options"] is JsonArray options)
                    foreach (var o in options.OfType<JsonObject>()) if (o["steps"] is JsonArray child) Steps(child);
                if (s["cases"] is JsonArray cases)
                    foreach (var c in cases.OfType<JsonObject>()) if (c["then"] is JsonArray child) Steps(child);
            }
        }
        foreach (var e in events.OfType<JsonObject>()) if (e["steps"] is JsonArray steps) Steps(steps);
        return count;
    }

    /// <summary>場合がすべて単一の同じ対象を調べているときのひな型。種族・クラスの id は比べる値。</summary>
    public static EventCondition? CommonSubject(EventStep step)
    {
        if (step.Cases.Count == 0 || step.Cases.Any(c => c.When.Count != 1 || c.When[0].Type == "chance")) return null;
        var first = step.Cases[0].When[0];
        return step.Cases.All(c => c.When[0].Type == first.Type && (!HasTargetId(first.Type) || c.When[0].Id == first.Id)) ? first : null;
    }

    public static bool HasTargetId(string type) => type is "counter" or "timer" or "item" or "equipped" or "flag" or "done";

    /// <summary>単調増加の上限で分けた整数の範囲だけを表示用に求める。逆順・複合条件・乱数は条件文のまま。</summary>
    public static IReadOnlyList<(long? Min, long Max)>? Ranges(EventStep step)
    {
        var subject = CommonSubject(step);
        if (subject == null || subject.Type is not ("counter" or "gold" or "hp" or "timer")) return null;
        var result = new List<(long?, long)>();
        long? min = null;
        foreach (var branch in step.Cases)
        {
            var c = branch.When[0];
            long max;
            if (c.Type == "gold" && c.Not) max = (long)c.Value - 1; // gold は >= の否定で「未満」
            else if (c.Type != "gold" && !c.Not && c.Op is "<=" or "<") max = (long)c.Value - (c.Op == "<" ? 1 : 0);
            else return null;
            if (min.HasValue && max < min.Value) return null;
            result.Add((min, max));
            min = max + 1;
        }
        return result;
    }
}
