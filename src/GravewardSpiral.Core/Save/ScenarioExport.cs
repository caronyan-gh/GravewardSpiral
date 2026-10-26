using System.Text.Json;
using System.Text.Json.Nodes;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// シナリオのパックに残した自由な迷宮の世界(パックの worlds.json。id はシナリオの id)。
/// 新しいゲームでこのシナリオを選ぶと、由来・世界の文・主の組をここから入れる(迷宮は dungeons.json、レベル帯は encounters.json)。
/// </summary>
public sealed class WorldPackDef
{
    public const string Collection = "worlds";

    public string Id { get; set; } = "";

    /// <summary>元の世界のシード(記録用。新しいゲームのシードは毎回変わり、顔ぶれと散らすイベントの抽選に使う)。</summary>
    public ulong Seed { get; set; }

    public Ai.WorldOrigin? Origin { get; set; }
    public Ai.WorldText? Text { get; set; }
    public string? Note { get; set; }
    public Dictionary<string, List<Monsters.BossDef.BossGroup>> BossPicks { get; set; } = [];

    /// <summary>選んでいるシナリオの世界(無ければ null。手で作ったシナリオには無い)。</summary>
    public static WorldPackDef? Of(ContentLibrary content) =>
        content.Scenario is { } sc && content.Find(Collection, sc.Id) is { } item ? Read.GetValue(item, i => i.As<WorldPackDef>()) : null;

    /// <summary>読んだもの(画面が毎回聞くので、パックの物ごとに一度だけ読む。使う側は写してから書き換える)。</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentItem, WorldPackDef> Read = new();

    /// <summary>新しい世界に、パックに残した由来・文・主の組を入れる(文と組は写して入れ、パックの物を書き換えない)。</summary>
    public void ApplyTo(GameSession session)
    {
        session.Origin = Origin;
        if (Text != null) session.World = JsonSerializer.Deserialize<Ai.WorldText>(JsonSerializer.Serialize(Text, ContentLibrary.JsonOptions), ContentLibrary.JsonOptions);
    }

    /// <summary>主の組を、パックに残したものにする(BuildRosters で選び直したものを置き換える)。</summary>
    public void ApplyBossPicks(GameSession session)
    {
        foreach (var (id, groups) in BossPicks)
            session.BossPicks[id] = groups.Select(g => new Monsters.BossDef.BossGroup { Monster = g.Monster, Count = g.Count, Name = g.Name }).ToList();
    }
}

/// <summary>
/// フルオートで作った自由な迷宮を、シナリオのパック(manifest・dungeons.json と maps・worlds.json・encounters.json)に書き出す。
/// 迷宮は、イベントや名のある敵を置く前の形で残す(新しいゲームでは、その上に毎回抽選して置く)。
/// 書き出したあとは、その世界のセーブはこのシナリオを遊んでいる形になり、ほかのクランも同じ迷宮と物語で始められる。
/// </summary>
public static class ScenarioExport
{
    public const string Version = "1.0";

    /// <summary>自由な迷宮のシナリオの id(free-シード。その id のパックがあれば -2, -3 … を付ける)。</summary>
    public static string NewId(ulong seed, Func<string, bool> exists)
    {
        string id = $"free-{seed}";
        for (int n = 2; exists(id); n++) id = $"free-{seed}-{n}";
        return id;
    }

    /// <summary>迷宮の各階をマップのテキストにする(置いたイベントの印も入る)。</summary>
    public static List<string> MapTexts(Dungeon dungeon) => dungeon.Floors.Select(MazeTextFormat.Write).ToList();

    /// <summary>
    /// パックを dir に書く(フォルダは作る。あれば中身を書き換える)。
    /// maps は MapTexts で取っておいたもの、enc はこの世界のレベル帯を当てた遭遇の設定。
    /// </summary>
    public static void Write(string dir, string id, GameSession session, IReadOnlyList<string> maps, Combat.EncounterSettings enc,
        ContentLibrary content, GameData data)
    {
        Directory.CreateDirectory(Path.Combine(dir, "maps"));
        var floors = new List<string>();
        for (int i = 0; i < maps.Count; i++)
        {
            string file = $"maps/{id}_b{i + 1}.txt";
            File.WriteAllText(Path.Combine(dir, file), maps[i]);
            floors.Add(file);
        }
        var name = Name(session, data);
        var manifest = new PackManifest
        {
            Id = id,
            Kind = ScenarioCatalog.Kind,
            Name = name,
            Version = Version,
            Depends = ["srd"],
            Order = 100,
            Format = ContentFormat.Current,
            Guid = System.Guid.NewGuid().ToString("D"),
            Author = session.ClanName,
            Description = Description(session),
            Levels = [(int)Math.Floor(enc.FirstLevel), (int)Math.Ceiling(enc.LastLevel)],
            Dungeon = id,
        };
        WriteJson(dir, ContentLibrary.ManifestFile, manifest);
        // 出来上がった世界のまま残す: 顔ぶれ・名のある敵(倒したかどうかは戻す)・置いたイベント(マップの印)
        WriteJson(dir, "dungeons.json", new[]
        {
            new DungeonLoader.DungeonDef
            {
                Id = id, Name = name, Floors = floors, Rosters = session.FloorRosters.Select(r => r.ToList()).ToList(), EventsPlaced = true,
                Named = session.Named.Select(n => new Monsters.NamedSpawn { Id = n.Id, Floor = n.Floor, Lair = n.Lair, X = n.X, Y = n.Y, Rumored = n.Rumored }).ToList(),
            },
        });
        WriteJson(dir, "worlds.json", new[]
        {
            new WorldPackDef { Id = id, Seed = session.Request.Seed, Origin = session.Origin, Text = session.World, BossPicks = session.BossPicks },
        });
        // レベル帯は、標準の遭遇の設定を写して、この世界の帯にしたもの(同じ id で上書きする)
        if (content.Find(Combat.EncounterSettings.Collection, Combat.EncounterSettings.DefaultId) is { } item
            && JsonNode.Parse(item.Data.GetRawText()) is JsonObject node)
        {
            node["first_level"] = enc.FirstLevel;
            node["last_level"] = enc.LastLevel;
            File.WriteAllText(Path.Combine(dir, $"{Combat.EncounterSettings.Collection}.json"),
                new JsonArray(node).ToJsonString(Indented));
        }
    }

    /// <summary>シナリオの名前(町の名前があれば「〇〇の迷宮」。無ければシードで)。</summary>
    public static LocalizedText Name(GameSession session, GameData data) =>
        session.Origin is { } o && data.WorldParts.GetValueOrDefault(o.Town)?.Name is { } town
            ? LocalizedText.Of($"The Dungeon of {town.Get("en")}", $"{town.Get("ja")}の迷宮")
            : LocalizedText.Of($"Free Dungeon {session.Request.Seed}", $"自由な迷宮 {session.Request.Seed}");

    /// <summary>説明は、序文の最初の段落(長ければ切る)。</summary>
    private static LocalizedText? Description(GameSession session)
    {
        if (session.World?.Prologue is not { } p) return null;
        static string First(string s, int max)
        {
            string para = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            return para.Length <= max ? para : para[..max] + "…";
        }
        return LocalizedText.Of(First(p.Get("en"), 200), First(p.Get("ja"), 90));
    }

    private static void WriteJson<T>(string dir, string file, T value) =>
        File.WriteAllText(Path.Combine(dir, file), JsonSerializer.Serialize(value, Indented));

    private static readonly JsonSerializerOptions Indented = new(ContentLibrary.JsonOptions)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
