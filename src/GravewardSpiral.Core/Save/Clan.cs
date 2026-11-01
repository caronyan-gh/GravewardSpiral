using System.Text.Json;
using GravewardSpiral.Core.Characters;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// セーブにあるクランの見出し(新しく始めるの画面で、連れていくクランを選ぶのに使う)。名簿・クランハウス・クリアの印など。
/// クランをファイルにして持ち出すのは SaveStore.ExportClan(.gsclan)。
/// </summary>
public sealed class ClanFile
{
    public string Name { get; set; } = "";

    /// <summary>クランの id(GameSession.ClanId)。持ち込んだ世界のセーブも、このクランのものとして並ぶ。</summary>
    public string? Id { get; set; }

    public DateTime SavedAt { get; set; } = DateTime.Now;

    /// <summary>最近遊んだ世界のシナリオ(自由な迷宮なら null)。</summary>
    public string? Scenario { get; set; }

    /// <summary>このクランがクリアした世界の数(周回の数)。</summary>
    public int Clears { get; set; }

    /// <summary>クリアの印(ScenarioClears)。</summary>
    public Dictionary<string, int>? ClearedScenarios { get; set; }

    /// <summary>このクランが見たイベント(新しい世界では出にくくする)。</summary>
    public List<string> EventsSeen { get; set; } = [];

    /// <summary>酒場で聞いた伝説の噂(イベントの id → 聞いた世界の数。次に作る世界で出やすくなる)。</summary>
    public Dictionary<string, int> LegendsHeard { get; set; } = [];

    public List<Character> Characters { get; set; } = [];

    /// <summary>このクランで遊んだ世界のシナリオ(null は自由な迷宮)。連れて入れるかを決める(ClanImport)。</summary>
    public List<string?> Played { get; set; } = [];

    /// <summary>パーティの並び(キャラの Id)。</summary>
    public List<string> Party { get; set; } = [];

    /// <summary>クランハウス(倉庫の中身と段階)。</summary>
    public ClanHouse House { get; set; } = new();

    /// <summary>
    /// セーブにあるクラン(新しい世界をそのクランで始める。名簿は写さずに、そのクランのものを使う)。Path は "clan:&lt;id&gt;"。
    /// 名前が無ければ unnamed(クランの id)。Scenario は最近遊んだ世界のシナリオ。
    /// </summary>
    public static List<(string Path, ClanFile Clan)> FromSaves(SaveStore saves, Func<string, string> unnamed)
    {
        var list = new List<(string, ClanFile)>();
        foreach (var summary in saves.ListClans())
        {
            try
            {
                var data = saves.LoadClan(summary.Id);
                if (data == null) continue;
                list.Add(($"clan:{summary.Id}", new ClanFile
                {
                    Name = data.Name ?? unnamed(data.Id),
                    Id = data.Id,
                    SavedAt = summary.SavedAt,
                    Scenario = summary.Latest?.Scenario,
                    Played = summary.Worlds.Select(w => w.Scenario).Concat(data.PlayedBefore).Distinct().ToList(),
                    Clears = data.Clears,
                    ClearedScenarios = data.ClearedScenarios,
                    EventsSeen = data.EventsSeen,
                    LegendsHeard = data.LegendsHeard,
                    Characters = data.Characters,
                    House = data.House,
                }));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { }
        }
        return list;
    }
}
