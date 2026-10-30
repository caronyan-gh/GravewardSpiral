using System.Text.Json;
using System.Text.Json.Serialization;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// セーブの読み書き。セーブはクランが一番上で、その下にシナリオごとの世界がある。
///   saves/&lt;クランの id&gt;/clan.json                          名前、名簿(キャラ)、クランハウス、クリアの数、見たイベント
///   saves/&lt;クランの id&gt;/worlds/&lt;世界の key&gt;/world.json   その世界のこと(迷宮・地図・フラグ・パーティの並び・どこにいるか・置いてきた死体)
/// 名簿はクランで1つなので、どの世界でも同じキャラ。キャラに場所は持たせず、ほかの世界の迷宮にいる人(中断したパーティと置いてきた死体)は、
/// 読むときに、その世界の world.json から数えて Roster.Away に入れる(その世界では使えない)。
/// 書くときは一時ファイルに書いてから置き換え、前の版を *.bak.json に残す(書いている途中で落ちても壊れない)。
/// 前の形(saves/slot&lt;N&gt;/save.json)は MigrateOldSlots でクランと世界に移し、元は saves/old/ に残す。
/// </summary>
public sealed class SaveStore
{
    /// <summary>今の世界のファイルの形の版。形を変えたら上げて、Migrate に移し方を足す。</summary>
    public const int CurrentVersion = 3;

    private const string ClanFileName = "clan.json";
    private const string WorldFileName = "world.json";
    private const string WorldsDir = "worlds";
    private const string OldDir = "old";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public SaveStore(string root) => Root = root;

    public string Root { get; }

    private string ClanDir(string clanId) => Path.Combine(Root, clanId);
    private string WorldDir(string clanId, string worldKey) => Path.Combine(ClanDir(clanId), WorldsDir, worldKey);

    /// <summary>世界のファイル(テストと確かめ用)。</summary>
    public string WorldFile(string clanId, string worldKey) => Path.Combine(WorldDir(clanId, worldKey), WorldFileName);

    /// <summary>世界の見出し(続きからの一覧用)。DeepestFloor は地図を描いた一番深い階、CurrentFloor は迷宮の中で中断した今の階(1 が B1。町なら 0)。</summary>
    public sealed record WorldSummary(string ClanId, string Key, DateTime SavedAt, string Location, IReadOnlyList<string> Party, int DeepestFloor, int Floors, ulong Seed,
        string? Scenario, IReadOnlyList<string> PartyIds, int CurrentFloor = 0);

    /// <summary>クランの見出し。Worlds は最近遊んだ順。</summary>
    public sealed record ClanSummary(string Id, string? Name, DateTime SavedAt, int Characters, IReadOnlyList<WorldSummary> Worlds)
    {
        public WorldSummary? Latest => Worlds.FirstOrDefault();
    }

    /// <summary>クランの一覧(最近遊んだ順)。読めないものは飛ばす。</summary>
    public List<ClanSummary> ListClans()
    {
        var list = new List<ClanSummary>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            string file = Path.Combine(dir, ClanFileName);
            if (!File.Exists(file)) continue;
            try
            {
                var clan = JsonSerializer.Deserialize<ClanData>(File.ReadAllText(file), Json)!;
                var worlds = ReadWorlds(clan.Id)
                    .Select(w => Summary(clan, w.Key, w.Data))
                    .OrderByDescending(w => w.SavedAt).ToList();
                list.Add(new ClanSummary(clan.Id, clan.Name, worlds.FirstOrDefault()?.SavedAt ?? clan.SavedAt, clan.Characters.Count, worlds));
            }
            catch (Exception ex) when (ex is JsonException or IOException or NullReferenceException)
            {
                // 壊れたクランは一覧に出さない
            }
        }
        return list.OrderByDescending(c => c.SavedAt).ToList();
    }

    public bool HasAny() => ListClans().Count > 0;

    public bool WorldExists(string clanId, string worldKey) => File.Exists(WorldFile(clanId, worldKey));

    private static WorldSummary Summary(ClanData clan, string key, SaveData data)
    {
        var party = data.Roster.Party.Select(id => clan.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? "?").ToList();
        int deepest = data.Exploration?.AutoMap.Select(m => m.Floor).DefaultIfEmpty(0).Max() ?? 0;
        int current = data.Location == "maze" && data.Exploration is { } e ? e.Floor + 1 : 0;
        return new WorldSummary(clan.Id, key, data.SavedAt, data.Location, party, deepest + 1, data.Dungeon.Floors.Count, data.Generation.Seed, data.Scenario, data.Roster.Party, current);
    }

    /// <summary>クランの世界のファイルを読む(見出しと、よそにいる人を数えるのに使う)。組の合わない世界は、読む前に戻す(RecoverWorld)。</summary>
    private List<(string Key, SaveData Data)> ReadWorlds(string clanId)
    {
        var list = new List<(string, SaveData)>();
        string dir = Path.Combine(ClanDir(clanId), WorldsDir);
        if (!Directory.Exists(dir)) return list;
        var saves = ReadWorldSaves(Path.Combine(ClanDir(clanId), ClanFileName));
        foreach (var w in Directory.EnumerateDirectories(dir))
        {
            string file = Path.Combine(w, WorldFileName);
            if (!File.Exists(file)) continue;
            try
            {
                RecoverWorld(saves, Path.GetFileName(w), file);
                list.Add((Path.GetFileName(w), JsonSerializer.Deserialize<SaveData>(File.ReadAllText(file), Json)!));
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
        return list;
    }

    /// <summary>クランを読む(新しい世界を、そのクランの名簿で始めるとき)。無ければ null。</summary>
    public ClanData? LoadClan(string clanId)
    {
        string file = Path.Combine(ClanDir(clanId), ClanFileName);
        if (!File.Exists(file)) return null;
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidDataException("クランが読めません");
        var clan = node.Deserialize<ClanData>(Json) ?? throw new InvalidDataException("クランが読めません");
        foreach (var c in clan.Characters) c.NormalizeClasses();
        // クリアの印を持つ前のクランは、クリアした世界から拾う
        clan.ClearedScenarios ??= ScenarioClears.FromWorlds(ReadWorlds(clanId).Select(w => w.Data));
        return clan;
    }

    /// <summary>
    /// ほかの世界の迷宮にいる人(キャラの id → その世界のシナリオの id か世界の key)。迷宮の中で中断したパーティと、置いてきた死体。
    /// exceptWorld の世界は数えない(いま遊ぶ世界)。
    /// </summary>
    public Dictionary<string, string> Away(string clanId, string? exceptWorld)
    {
        var away = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, data) in ReadWorlds(clanId))
        {
            if (key == exceptWorld) continue;
            foreach (var id in InMaze(data)) away[id] = data.Scenario ?? key;
        }
        return away;
    }

    /// <summary>その世界の迷宮にいる人の id(迷宮の中で中断したパーティと、置いてきた死体)。世界を始め直すと、この人たちはロストする。</summary>
    public List<string> InMaze(string clanId, string worldKey) =>
        ReadWorlds(clanId).Where(w => w.Key == worldKey).SelectMany(w => InMaze(w.Data)).Distinct().ToList();

    private static IEnumerable<string> InMaze(SaveData data) =>
        (data.Location == "maze" ? data.Roster.Party : []).Concat(data.Roster.LeftInMaze.Keys);

    /// <summary>その世界を消す。クランに世界が無くなっても、クラン(名簿)は残す(消すなら DeleteClan)。</summary>
    public void DeleteWorld(string clanId, string worldKey)
    {
        var dir = WorldDir(clanId, worldKey);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public void DeleteClan(string clanId)
    {
        var dir = ClanDir(clanId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>セーブする(クランと世界の両方を書く)。savedAt は、古いセーブを移すときに元の日時を残すのに使う。</summary>
    public void Save(GameSession session, ContentLibrary content, DateTime? savedAt = null)
    {
        var world = SaveData.From(session, content);
        if (savedAt is { } at) world.SavedAt = at;
        world.Roster = new Roster { Party = [.. session.Roster.Party], LeftInMaze = new(session.Roster.LeftInMaze) };   // キャラはクランに書く
        var clan = ClanData.From(session);
        if (savedAt is { } t) clan.SavedAt = t;
        // 世界とクランは同じ時の組で残す: 両方を一時ファイルに書き終えてから置き換え、クランの置き換えに失敗したら世界を前のものに戻す。
        // 置き換えの間で落ちたときのために、世界とクランに同じ保存の印を付ける(読むときに組が違えば、世界を前の版に戻す。RecoverWorld)
        string worldPath = WorldFile(session.ClanId, session.WorldKey), clanPath = Path.Combine(ClanDir(session.ClanId), ClanFileName);
        world.SaveId = Guid.NewGuid().ToString("N");
        clan.WorldSaves = ReadWorldSaves(clanPath);
        clan.WorldSaves[session.WorldKey] = world.SaveId;
        string worldTmp = WriteTemp(worldPath, world), clanTmp = WriteTemp(clanPath, clan);
        bool hadWorld = File.Exists(worldPath);
        Replace(worldTmp, worldPath);
        try
        {
            Replace(clanTmp, clanPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (hadWorld) File.Copy(Path.ChangeExtension(worldPath, ".bak.json"), worldPath, overwrite: true);
                else File.Delete(worldPath);
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { }   // 戻せなくても、次に読むときに RecoverWorld が戻す
            throw;
        }
    }

    /// <summary>クランのファイルにある、世界ごとの最後の保存の印(読めなければ空)。ほかの世界の分を残すのに使う。</summary>
    private static Dictionary<string, string> ReadWorldSaves(string clanPath)
    {
        try
        {
            if (File.Exists(clanPath) && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(clanPath))?["world_saves"] is { } node)
                return node.Deserialize<Dictionary<string, string>>(Json) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException) { }
        return [];
    }

    /// <summary>
    /// 世界とクランが別の保存の組なら(世界を置き換えたあと、クランを置き換える前に落ちた)、世界を前の版(.bak.json)に戻す。
    /// 書く順は世界が先なので、組が違うのは世界だけが新しいとき。前の版も組が合わなければ、追いかけずにそのまま読む。印の無い古いセーブは見ない。
    /// </summary>
    private static void RecoverWorld(Dictionary<string, string> worldSaves, string worldKey, string worldPath)
    {
        if (!worldSaves.TryGetValue(worldKey, out var expected)) return;
        if (SaveIdOf(worldPath) is not { } id || id == expected) return;
        string backup = Path.ChangeExtension(worldPath, ".bak.json");
        if (File.Exists(backup) && SaveIdOf(backup) == expected) File.Copy(backup, worldPath, overwrite: true);
    }

    private static string? SaveIdOf(string path)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?["save_id"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException) { return null; }
    }

    private static void WriteJson<T>(string path, T data) => Replace(WriteTemp(path, data), path);

    /// <summary>path の横の一時ファイルに書く(置き換えは Replace)。一時ファイルの場所を返す。</summary>
    private static string WriteTemp<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        return tmp;
    }

    /// <summary>一時ファイルで path を置き換える(前のものは .bak.json に残す)。</summary>
    private static void Replace(string tmp, string path)
    {
        string backup = Path.ChangeExtension(path, ".bak.json");
        if (File.Exists(path)) File.Replace(tmp, path, backup);
        else File.Move(tmp, path);
    }

    /// <summary>クランの世界を読んで、1つの GameSession にする。ほかの世界の迷宮にいる人は Roster.Away に入る。</summary>
    public GameSession Load(string clanId, string worldKey)
    {
        var clan = LoadClan(clanId) ?? throw new InvalidDataException("クランがありません");
        RecoverWorld(clan.WorldSaves, worldKey, WorldFile(clanId, worldKey));
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(WorldFile(clanId, worldKey))) ?? throw new InvalidDataException("セーブが空です");
        int version = node["version"]?.GetValue<int>() ?? 0;
        if (version > CurrentVersion) throw new InvalidDataException($"このセーブ(版 {version})は、今のゲーム(版 {CurrentVersion})より新しいので読めません");
        Migrate(node, version);
        var data = node.Deserialize<SaveData>(Json) ?? throw new InvalidDataException("セーブが読めません");
        var session = data.ToSession(clan);
        MarkAway(session);
        return session;
    }

    /// <summary>ほかの世界の迷宮にいる人を Roster.Away に入れ、パーティから外す(新しい世界を始めるときにも使う)。</summary>
    public void MarkAway(GameSession session)
    {
        session.Roster.Away.Clear();
        foreach (var (id, where) in Away(session.ClanId, session.WorldKey))
            if (session.Roster.Find(id) != null && !session.Roster.LeftInMaze.ContainsKey(id)) session.Roster.Away[id] = where;
        session.Roster.Party.RemoveAll(session.Roster.Away.ContainsKey);
    }

    /// <summary>古い版の世界のファイルを、今の形へ移す。版を上げたら、ここに移し方を足す。</summary>
    private static void Migrate(System.Text.Json.Nodes.JsonNode node, int version)
    {
        // 版 2: 第1部が標準のパックからシナリオ(gravekeeper)に分かれた。シナリオの無い版 1 のセーブは第1部の世界
        if (version < 2 && node["scenario"] is null) node["scenario"] = ScenarioCatalog.Gravekeeper;
        // 版 3: クランと世界に分かれた(移すのは MigrateOldSlots。ファイルの中身の形は同じ)
        node["version"] = CurrentVersion;
    }

    /// <summary>クランの id を持つ前のセーブのクラン(枠ごとに別のクラン)。</summary>
    public static string LegacyClanId(int slot) => $"slot{slot}";

    /// <summary>
    /// 前の形のセーブ(saves/slot&lt;N&gt;/save.json)を、クランと世界に移す。元のフォルダは saves/old/ に移して残す。
    /// 同じクランのセーブが2つ以上あれば、新しいほうの名簿をクランの名簿にする。移した数を返す。読めないセーブは移さずに残す。
    /// </summary>
    public int MigrateOldSlots(ContentLibrary content)
    {
        if (!Directory.Exists(Root)) return 0;
        var old = new List<(string Dir, GameSession Session, DateTime SavedAt)>();
        foreach (var dir in Directory.EnumerateDirectories(Root, "slot*"))
        {
            string file = Path.Combine(dir, "save.json");
            if (!File.Exists(file) || !int.TryParse(Path.GetFileName(dir)["slot".Length..], out int slot)) continue;
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
                if (node == null) continue;
                Migrate(node, node["version"]?.GetValue<int>() ?? 0);
                var data = node.Deserialize<SaveData>(Json);
                if (data == null) continue;
                var clan = new ClanData
                {
                    Id = data.ClanId ?? LegacyClanId(slot),
                    Name = data.ClanName,
                    Characters = data.Roster.Characters,
                    House = data.ClanHouse,
                    Clears = data.Clears,
                    EventsSeen = data.EventsSeen,
                };
                old.Add((dir, data.ToSession(clan), data.SavedAt));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or FormatException) { }
        }
        // 元のフォルダを先に old/ へ移す(クランの id が slot<N> のとき、同じ名前のフォルダに書くため)
        foreach (var (dir, _, _) in old)
        {
            Directory.CreateDirectory(Path.Combine(Root, OldDir));
            string target = Path.Combine(Root, OldDir, Path.GetFileName(dir));
            if (Directory.Exists(target)) target += "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            Directory.Move(dir, target);
        }
        // 古い順に書く(同じクランなら、新しいセーブの名簿が最後に残る)
        foreach (var (_, session, savedAt) in old.OrderBy(x => x.SavedAt)) Save(session, content, savedAt);
        return old.Count;
    }
}

/// <summary>クランのファイル(clan.json)の形。名簿はクランで1つ。</summary>
public sealed class ClanData
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public List<Character> Characters { get; set; } = [];
    public ClanHouse House { get; set; } = new();
    public int Clears { get; set; }

    /// <summary>クリアの印(ScenarioClears)。null は印を持つ前のファイル(読むときに、クリアした世界から拾う)。</summary>
    public Dictionary<string, int>? ClearedScenarios { get; set; }
    public List<string> EventsSeen { get; set; } = [];
    public Dictionary<string, int> LegendsHeard { get; set; } = [];
    /// <summary>世界ごとの、最後にこのクランと一緒に保存した印(世界の key → SaveData.SaveId)。食い違いを見つけるのに使う。</summary>
    public Dictionary<string, string> WorldSaves { get; set; } = [];

    public static ClanData From(GameSession s) => new()
    {
        Id = s.ClanId,
        Name = s.ClanName,
        Characters = s.Roster.Characters,
        House = s.ClanHouse,
        Clears = s.Clears,
        ClearedScenarios = new(s.ClearedScenarios, StringComparer.Ordinal),
        EventsSeen = s.EventsSeen.Order(StringComparer.Ordinal).ToList(),
        LegendsHeard = new(s.LegendsHeard, StringComparer.Ordinal),
    };
}

/// <summary>世界のファイル(world.json)の形。前の形(save.json)と同じ形で、名簿のキャラとクランのことは clan.json に書く(古いセーブを読むときだけここにある)。</summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveStore.CurrentVersion;
    public DateTime SavedAt { get; set; } = DateTime.Now;
    /// <summary>この保存の印(クランの WorldSaves と同じ値なら、同じ時の組)。</summary>
    public string? SaveId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Packs { get; set; } = [];
    public string Location { get; set; } = "town";
    public GenerationData Generation { get; set; } = new();
    public DungeonData Dungeon { get; set; } = new();
    public Roster Roster { get; set; } = new();
    public List<Items.ItemInstance> ShopSold { get; set; } = [];
    public List<string> Flags { get; set; } = [];
    public Dictionary<string, int> Counters { get; set; } = [];

    /// <summary>歩数のタイマー(イベントの id と残りの歩数)。</summary>
    public Dictionary<string, int> Timers { get; set; } = [];
    public Difficulty Difficulty { get; set; } = new();
    public PendingCombat? PendingCombat { get; set; }
    public string? Scenario { get; set; }
    public string? ScenarioVersion { get; set; }
    public string? ClanName { get; set; }
    public string? ClanId { get; set; }
    public ClanHouse ClanHouse { get; set; } = new();
    public List<List<string>> FloorRosters { get; set; } = [];
    public List<Items.DropBoost> FloorDrops { get; set; } = [];
    public List<Monsters.NamedSpawn> Named { get; set; } = [];
    public Ai.WorldText? World { get; set; }
    public Ai.WorldOrigin? Origin { get; set; }
    public Dictionary<string, List<Monsters.BossDef.BossGroup>> BossPicks { get; set; } = [];
    public double? FirstLevel { get; set; }
    public double? LastLevel { get; set; }
    public double LevelShift { get; set; }
    public int Clears { get; set; }
    public List<string> EventsSeen { get; set; } = [];
    public ExplorationData? Exploration { get; set; }
    public RngData Rng { get; set; } = new();

    public sealed class GenerationData
    {
        public ulong Seed { get; set; }
        public int Floors { get; set; }
        public int MinWidth { get; set; }
        public int MaxWidth { get; set; }
        public int MinHeight { get; set; }
        public int MaxHeight { get; set; }
    }

    /// <summary>迷宮は、階ごとにマップのテキスト形式で持つ(作り直さずに読める)。</summary>
    public sealed class DungeonData
    {
        public string Id { get; set; } = "";
        public List<string> Floors { get; set; } = [];
    }

    public sealed class ExplorationData
    {
        public int Floor { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public Direction Facing { get; set; }
        public long Steps { get; set; }
        public long StepsSinceCombat { get; set; }
        public int LightMinutes { get; set; }
        public int FeatherMinutes { get; set; }
        public List<EdgeData> Unlocked { get; set; } = [];
        public List<EdgeData> UsedMarks { get; set; } = [];
        public List<NoteData> Notes { get; set; } = [];
        public List<int> OccupiedRooms { get; set; } = [];
        public int RoomsFloor { get; set; } = -1;
        public List<int> Keys { get; set; } = [];
        public List<EdgeData> Discovered { get; set; } = [];
        public List<FloorMapData> AutoMap { get; set; } = [];
    }

    public sealed record EdgeData(int Floor, int X, int Y, Direction Dir);

    public sealed class NoteData
    {
        public int Floor { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string EventId { get; set; } = "";
        public List<Content.LocalizedText> Lines { get; set; } = [];
    }

    public sealed class FloorMapData
    {
        public int Floor { get; set; }
        public List<int[]> Visited { get; set; } = [];
        public List<SeenEdge> Edges { get; set; } = [];
        public List<MarkData> Marks { get; set; } = [];
    }

    public sealed record SeenEdge(int X, int Y, Direction Dir, EdgeType Type);
    public sealed record MarkData(int X, int Y, CellFeature Feature);

    public sealed class RngData
    {
        public ulong Master { get; set; }
        public Dictionary<string, PcgState> Streams { get; set; } = [];
    }

    public static SaveData From(GameSession s, ContentLibrary content)
    {
        var data = new SaveData
        {
            CreatedAt = s.CreatedAt,
            Packs = content.Packs.ToDictionary(p => p.Id, p => p.Version),
            Location = s.Location == Save.Location.Maze ? "maze" : "town",
            Generation = new GenerationData
            {
                Seed = s.Request.Seed,
                Floors = s.Request.Floors,
                MinWidth = s.Request.SizeMode.MinWidth,
                MaxWidth = s.Request.SizeMode.MaxWidth,
                MinHeight = s.Request.SizeMode.MinHeight,
                MaxHeight = s.Request.SizeMode.MaxHeight,
            },
            Dungeon = new DungeonData { Id = s.Dungeon.Id, Floors = s.Dungeon.Floors.Select(MazeTextFormat.Write).ToList() },
            Roster = s.Roster,
            ShopSold = s.ShopSold,
            Flags = [.. s.Flags],
            Counters = new(s.Counters),
            Timers = new(s.Timers),
            Difficulty = s.Difficulty,
            PendingCombat = s.PendingCombat,
            Scenario = s.Scenario,
            ScenarioVersion = s.ScenarioVersion,
            ClanName = s.ClanName,
            ClanId = s.ClanId,
            ClanHouse = s.ClanHouse,
            FloorRosters = s.FloorRosters,
            FloorDrops = s.FloorDrops,
            Named = s.Named,
            World = s.World,
            Origin = s.Origin,
            BossPicks = s.BossPicks,
            FirstLevel = s.FirstLevel,
            LastLevel = s.LastLevel,
            LevelShift = s.LevelShift,
            Clears = s.Clears,
            EventsSeen = s.EventsSeen.Order(StringComparer.Ordinal).ToList(),
            Rng = new RngData { Master = s.Rng.MasterSeed, Streams = s.Rng.Save() },
        };
        if (s.Exploration is { } e)
        {
            data.Exploration = new ExplorationData
            {
                Floor = e.Position.Floor,
                X = e.Position.Cell.X,
                Y = e.Position.Cell.Y,
                Facing = e.Position.Facing,
                Steps = e.Steps,
                StepsSinceCombat = e.StepsSinceCombat,
                LightMinutes = e.LightMinutes,
                FeatherMinutes = e.FeatherMinutes,
                Unlocked = e.UnlockedEdges().Select(d => new EdgeData(d.Floor, d.Cell.X, d.Cell.Y, d.Dir)).ToList(),
                UsedMarks = e.UsedMarks.Select(m => new EdgeData(m.Floor, m.Cell.X, m.Cell.Y, Direction.North)).ToList(),
                OccupiedRooms = e.OccupiedRooms.ToList(),
                RoomsFloor = e.RoomsFloor,
                Notes = e.Notes.Select(n => new NoteData { Floor = n.Floor, X = n.Cell.X, Y = n.Cell.Y, EventId = n.EventId, Lines = n.Lines }).ToList(),
                Keys = [.. e.Keys],
                Discovered = e.DiscoveredEdges().Select(d => new EdgeData(d.Floor, d.Cell.X, d.Cell.Y, d.Dir)).ToList(),
                AutoMap = e.AutoMap.Floors.Select(kv => new FloorMapData
                {
                    Floor = kv.Key,
                    Visited = kv.Value.Visited.Select(p => new[] { p.X, p.Y }).ToList(),
                    Edges = kv.Value.Edges.Select(x => new SeenEdge(x.Key.Cell.X, x.Key.Cell.Y, x.Key.Dir, x.Value)).ToList(),
                    Marks = kv.Value.Marks.Select(m => new MarkData(m.Key.X, m.Key.Y, m.Value)).ToList(),
                }).ToList(),
            };
        }
        return data;
    }

    /// <summary>世界のファイルとクランから、遊ぶ形にする(名簿・クランハウス・クリアの数・見たイベントはクランのもの)。</summary>
    public GameSession ToSession(ClanData clan)
    {
        var floors = Dungeon.Floors.Select((text, i) => MazeTextFormat.Parse(text, $"セーブの B{i + 1}")).ToList();
        var dungeon = new Dungeon(Dungeon.Id, floors);
        var session = new GameSession
        {
            Slot = 1,
            Request = new GenerationRequest(Generation.Seed, Generation.Floors,
                new FloorSizeMode(Generation.MinWidth, Generation.MaxWidth, Generation.MinHeight, Generation.MaxHeight)),
            Dungeon = dungeon,
            Rng = RngStreams.Restore(Rng.Master, Rng.Streams),
            Roster = new Roster { Characters = clan.Characters, Party = Roster.Party, LeftInMaze = Roster.LeftInMaze },
            ShopSold = ShopSold,
            Flags = [.. Flags],
            Counters = new(Counters),
            Timers = new(Timers ?? [], StringComparer.Ordinal),
            Difficulty = Difficulty,
            PendingCombat = PendingCombat,
            Scenario = Scenario,
            ScenarioVersion = ScenarioVersion,
            ClanName = clan.Name,
            ClanId = clan.Id,
            ClanHouse = clan.House,
            FloorRosters = FloorRosters,
            FloorDrops = FloorDrops,
            Named = Named,
            World = World,
            Origin = Origin,
            BossPicks = new(BossPicks, StringComparer.Ordinal),
            FirstLevel = FirstLevel,
            LastLevel = LastLevel,
            LevelShift = LevelShift,
            Clears = clan.Clears,
            ClearedScenarios = new(clan.ClearedScenarios ?? [], StringComparer.Ordinal),
            EventsSeen = clan.EventsSeen.ToHashSet(StringComparer.Ordinal),
            LegendsHeard = new(clan.LegendsHeard, StringComparer.Ordinal),
            Location = Location == "maze" ? Save.Location.Maze : Save.Location.Town,
            CreatedAt = CreatedAt,
        };
        foreach (var c in clan.Characters) c.NormalizeClasses();   // 古いセーブ(クラスが1つの形)を移す
        if (Exploration is { } e)
        {
            var state = new ExplorationState(dungeon, new PartyPosition(e.Floor, new GridPos(e.X, e.Y), e.Facing)) { Steps = e.Steps, StepsSinceCombat = e.StepsSinceCombat, LightMinutes = e.LightMinutes, FeatherMinutes = e.FeatherMinutes };
            foreach (var u in e.Unlocked) state.Unlock(u.Floor, new GridPos(u.X, u.Y), u.Dir);
            foreach (var m in e.UsedMarks) state.UsedMarks.Add((m.Floor, new GridPos(m.X, m.Y)));
            state.RoomsFloor = e.RoomsFloor;
            foreach (var r in e.OccupiedRooms) state.OccupiedRooms.Add(r);
            foreach (var n in e.Notes) state.Notes.Add(new MazeNote { Floor = n.Floor, Cell = new GridPos(n.X, n.Y), EventId = n.EventId, Lines = n.Lines });
            foreach (var k in e.Keys) state.Keys.Add(k);
            foreach (var d in e.Discovered) state.Discover(d.Floor, new GridPos(d.X, d.Y), d.Dir);
            foreach (var m in e.AutoMap)
            {
                var map = state.AutoMap.For(m.Floor);
                foreach (var v in m.Visited) map.Visited.Add(new GridPos(v[0], v[1]));
                foreach (var x in m.Edges) map.See(new GridPos(x.X, x.Y), x.Dir, x.Type);
                foreach (var k in m.Marks) map.Marks[new GridPos(k.X, k.Y)] = k.Feature;
            }
            session.Exploration = state;
        }
        return session;
    }
}
