using System.Text.Json;
using System.Text.Json.Serialization;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Events;

/// <summary>
/// イベントの下書き(JSON の配列)の検査。Sim の --check-events とシナリオエディタが使う。
/// 知らない項目名は誤りにする(書き間違いで部品が黙って無視されないように)。ほかに、id の重なり、迷宮の scatter と町の title、
/// EventCheck の検査、払う選択肢の所持金の条件、連動する組(部品が2つ以上、step が重ならない、chain: のフラグの組があること)を見る。
/// </summary>
public static class EventDraftCheck
{
    /// <summary>知らない項目名を誤りにする読み方。</summary>
    public static readonly JsonSerializerOptions Strict = new(ContentLibrary.JsonOptions)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>検査の結果(問題の一覧と、読めたイベントの数と、読めたイベント)。</summary>
    public sealed record Result(List<string> Problems, int Count, List<EventDef> Events);

    /// <param name="drafts">出どころの名前(ファイル名など)と、イベントの JSON の組。</param>
    /// <param name="allowExisting">パックに既にある id を重なりとしない(置き換える下書き、エディタで開いたシナリオのイベント)。</param>
    /// <param name="handPlaced">迷宮のイベントを、散らさずにマップの印で置いてもよい(シナリオエディタ。scatter が無くてもよい)。</param>
    public static Result Check(GameData data, ContentLibrary content, IEnumerable<(string Source, JsonElement Json)> drafts, bool allowExisting = false,
        bool handPlaced = false)
    {
        var problems = new List<string>();
        var seen = allowExisting ? new HashSet<string>() : new HashSet<string>(data.Events.Keys);
        var read = new List<EventDef>();
        foreach (var (source, el) in drafts)
        {
            string id = el.ValueKind == JsonValueKind.Object && el.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "?" : "?";
            EventDef e;
            try { e = el.Deserialize<EventDef>(Strict)!; }
            catch (JsonException ex)
            {
                problems.Add($"{source} '{id}': {ex.Message}");
                continue;
            }
            read.Add(e);
            if (!seen.Add(e.Id)) problems.Add($"{source} '{e.Id}': id が既にあります");
            if (e.Place == "maze" && e.Scatter == null && !handPlaced) problems.Add($"{source} '{e.Id}': 迷宮のイベントに scatter がありません");
            if (e.Place == "town" && e.Title == null) problems.Add($"{source} '{e.Id}': 町のイベントに title がありません");
            problems.AddRange(EventCheck.Check(data, e, content).Select(x => $"{source} '{e.Id}': {x}"));
            // 散らすイベントがほうびをくれるなら、一度きりにする(無いと、戻って踏み直すたびにもらえる)。読み込みは止めないので、ここ(書くときの検査)だけで見る
            if (e.Scatter != null && e.Once == null && Save.EventAuthoring.Steps(e).Any(st => st.Type is "xp" or "treasure" or "give_item" || st.Type == "gold" && st.Value > 0))
                problems.Add($"{source} '{e.Id}': 散らすイベントが経験値・お金・品をくれるのに once がありません(ふつうは once: place)");
            // 払う選択肢(gold が負)には、所持金の条件を付ける(足りなくても払えたことになって、ただで賭けられてしまう)
            foreach (var (label, need) in PayingOptions(e.Steps).Where(x => x.Need > 0))
                problems.Add($"{source} '{e.Id}': 選択肢「{label}」は {need} G を払うのに、when に gold {need} 以上の条件がありません");
        }
        // 連動する組: 下書きとパックを合わせて、部品が2つ以上、step が重ならない。町などが見る chain: のフラグの組があること
        var all = data.Events.Values.Where(e => !read.Any(d => d.Id == e.Id)).Concat(read).ToList();
        var chainGroups = all.Where(e => e.Scatter?.Chain != null).GroupBy(e => e.Scatter!.Chain!).ToList();
        foreach (var g in chainGroups.Where(g => g.Any(read.Contains)))
        {
            if (g.Count() < 2) problems.Add($"組 '{g.Key}': 部品が1つしかありません");
            foreach (var dup in g.GroupBy(e => e.Scatter!.Step).Where(x => x.Count() > 1))
                problems.Add($"組 '{g.Key}': step {dup.Key} が重なっています({string.Join(", ", dup.Select(e => e.Id))})");
        }
        var chainNames = chainGroups.Select(g => g.Key).ToHashSet();
        foreach (var e in read)
        {
            foreach (var c in Conditions(e).Where(c => c.Type == "flag" && c.Id?.StartsWith("chain:") == true && !chainNames.Contains(c.Id["chain:".Length..])))
                problems.Add($"'{e.Id}': フラグ {c.Id} の組がありません");
        }
        return new Result(problems, read.Count, read);
    }

    /// <summary>ファイルの下書き(JSON の配列)を読む。読めなければ問題に足す。</summary>
    public static List<(string Source, JsonElement Json)> ReadFile(string file, List<string> problems)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(file)) ?? [];
            return list.Select(x => (file, x)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            problems.Add($"{file}: 読めません: {ex.Message}");
            return [];
        }
    }

    /// <summary>イベントの中の条件を全部(選択肢と、入れ子の手順の中も)。</summary>
    public static IEnumerable<EventCondition> Conditions(EventDef e)
    {
        IEnumerable<EventCondition> Walk(List<EventStep> steps) =>
            steps.SelectMany(st => st.Conditions().Concat(st.Children().SelectMany(Walk)));
        return e.When.Concat(Walk(e.Steps));
    }

    /// <summary>払う選択肢(gold が負)で、所持金の条件が足りないもの(選択肢の名前と、要る金)。</summary>
    public static IEnumerable<(string Label, int Need)> PayingOptions(List<EventStep> steps)
    {
        foreach (var st in steps)
        {
            foreach (var o in st.Options)
            {
                var pay = o.Steps.FirstOrDefault(x => x.Type == "gold" && x.Value < 0);
                int have = o.When.Where(w => w.Type == "gold" && !w.Not).Select(w => w.Value).DefaultIfEmpty(0).Max();
                if (pay != null && have < -pay.Value) yield return (o.Label?.Get("ja") ?? "?", -pay.Value);
                foreach (var x in PayingOptions(o.Steps)) yield return x;
            }
            foreach (var child in st.Children().Where(c => c != null))
                foreach (var x in PayingOptions(child)) yield return x;
        }
    }
}
