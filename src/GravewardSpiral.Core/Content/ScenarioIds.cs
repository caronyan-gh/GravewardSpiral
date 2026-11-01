using System.Text.Json;

namespace GravewardSpiral.Core.Content;

/// <summary>
/// シナリオで使えるクラス・種族・呪文の id。シナリオのパックは土台との差だけを書く(新しい id は足す、{"id", "disabled": true} は外す)ので、
/// 土台のファイルの id にシナリオの差を重ねれば分かる(データを全部読まずに済む。続きからの一覧で、そのクランが入れるかを決める)。
/// </summary>
public sealed record ScenarioIds(IReadOnlySet<string> Classes, IReadOnlySet<string> Races, IReadOnlySet<string> Spells)
{
    /// <param name="baseDir">土台のパック(Content/srd)のフォルダ。</param>
    /// <param name="scenarioDir">シナリオのフォルダ(ファイルのシナリオは、展開したフォルダ。ScenarioInfo.Directory)。</param>
    public static ScenarioIds Read(string baseDir, string scenarioDir) => new(
        Ids("classes", baseDir, scenarioDir), Ids("races", baseDir, scenarioDir), Ids("spells", baseDir, scenarioDir));

    private static HashSet<string> Ids(string collection, params string[] dirs)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in dirs)
        {
            string file = Path.Combine(dir, collection + ".json");
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var id) || id.GetString() is not { } s) continue;
                    if (e.TryGetProperty("disabled", out var off) && off.ValueKind == JsonValueKind.True) ids.Remove(s);
                    else ids.Add(s);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }   // 読めないファイルは、検査で知らせる(ここでは数えない)
        }
        return ids;
    }
}
