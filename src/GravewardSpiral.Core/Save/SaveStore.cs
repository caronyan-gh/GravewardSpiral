using System.Text.Json;
using System.Text.Json.Serialization;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// セーブの読み書き。枠ごとに saves/&lt;枠&gt;/save.json に書く。
/// 書くときは一時ファイルに書いてから置き換え、前の版を save.bak.json に残す(書いている途中で落ちても壊れない)。
/// セーブには版の番号を入れ、古い版は読むときに新しい形へ移す。
/// </summary>
public sealed class SaveStore
{
    /// <summary>今のセーブの形の版。形を変えたら上げて、Migrate に移し方を足す。</summary>
    public const int CurrentVersion = 1;

    public const int SlotCount = 3;
    private const string FileName = "save.json";
    private const string BackupName = "save.bak.json";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public SaveStore(string root) => Root = root;

    public string Root { get; }

    private string SlotDir(int slot) => Path.Combine(Root, $"slot{slot}");

    /// <summary>その枠のセーブのファイル。</summary>
    public string SlotFile(int slot) => Path.Combine(SlotDir(slot), FileName);

    public bool Exists(int slot) => File.Exists(Path.Combine(SlotDir(slot), FileName));

    /// <summary>空いている一番小さい枠。全部埋まっていれば null。</summary>
    public int? FirstEmptySlot() => Enumerable.Range(1, SlotCount).Cast<int?>().FirstOrDefault(s => !Exists(s!.Value));

    public sealed record SlotSummary(int Slot, DateTime SavedAt, string Location, IReadOnlyList<string> Party, int DeepestFloor, int Floors, ulong Seed,
        string? Scenario = null);

    /// <summary>枠の一覧(続きからの画面用)。読めない枠は飛ばす。</summary>
    public List<SlotSummary> List()
    {
        var list = new List<SlotSummary>();
        for (int s = 1; s <= SlotCount; s++)
        {
            if (!Exists(s)) continue;
            try
            {
                var data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(Path.Combine(SlotDir(s), FileName)), Json)!;
                var party = data.Roster.Party.Select(id => data.Roster.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? "?").ToList();
                int deepest = data.Exploration?.AutoMap.Select(m => m.Floor).DefaultIfEmpty(0).Max() ?? 0;
                list.Add(new SlotSummary(s, data.SavedAt, data.Location, party, deepest + 1, data.Dungeon.Floors.Count, data.Generation.Seed, data.Scenario));
            }
            catch (Exception ex) when (ex is JsonException or IOException or NullReferenceException)
            {
                // 壊れた枠は一覧に出さない(読み込みのときにエラーを出す)
            }
        }
        return list;
    }

    public void Delete(int slot)
    {
        var dir = SlotDir(slot);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public void Save(GameSession session, ContentLibrary content)
    {
        var data = SaveData.From(session, content);
        string dir = SlotDir(session.Slot);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, FileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        if (File.Exists(path)) File.Replace(tmp, path, Path.Combine(dir, BackupName));
        else File.Move(tmp, path);
    }

    public GameSession Load(int slot)
    {
        string path = Path.Combine(SlotDir(slot), FileName);
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("セーブが空です");
        int version = node["version"]?.GetValue<int>() ?? 0;
        if (version > CurrentVersion) throw new InvalidDataException($"このセーブ(版 {version})は、今のゲーム(版 {CurrentVersion})より新しいので読めません");
        Migrate(node, version);
        var data = node.Deserialize<SaveData>(Json) ?? throw new InvalidDataException("セーブが読めません");
        return data.ToSession(slot);
    }

    /// <summary>古い版のセーブを、今の形へ移す。版を上げたら、ここに移し方を足す。</summary>
    private static void Migrate(System.Text.Json.Nodes.JsonNode node, int version)
    {
        // 版 1 が最初の形なので、今は移すものが無い
        node["version"] = CurrentVersion;
    }
}

/// <summary>セーブのファイルの形。</summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveStore.CurrentVersion;
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Packs { get; set; } = [];
    public string Location { get; set; } = "town";
    public GenerationData Generation { get; set; } = new();
    public DungeonData Dungeon { get; set; } = new();
    public Roster Roster { get; set; } = new();
    public List<Items.ItemInstance> ShopSold { get; set; } = [];
    public List<string> Flags { get; set; } = [];
    public Dictionary<string, int> Counters { get; set; } = [];
    public Difficulty Difficulty { get; set; } = new();
    public PendingCombat? PendingCombat { get; set; }
    public string? Scenario { get; set; }
    public string? ScenarioVersion { get; set; }
    public string? ClanName { get; set; }
    public ClanHouse ClanHouse { get; set; } = new();
    public List<List<string>> FloorRosters { get; set; } = [];
    public List<Items.DropBoost> FloorDrops { get; set; } = [];
    public List<Monsters.NamedSpawn> Named { get; set; } = [];
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
            Difficulty = s.Difficulty,
            PendingCombat = s.PendingCombat,
            Scenario = s.Scenario,
            ScenarioVersion = s.ScenarioVersion,
            ClanName = s.ClanName,
            ClanHouse = s.ClanHouse,
            FloorRosters = s.FloorRosters,
            FloorDrops = s.FloorDrops,
            Named = s.Named,
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

    public GameSession ToSession(int slot)
    {
        var floors = Dungeon.Floors.Select((text, i) => MazeTextFormat.Parse(text, $"セーブの B{i + 1}")).ToList();
        var dungeon = new Dungeon(Dungeon.Id, floors);
        var session = new GameSession
        {
            Slot = slot,
            Request = new GenerationRequest(Generation.Seed, Generation.Floors,
                new FloorSizeMode(Generation.MinWidth, Generation.MaxWidth, Generation.MinHeight, Generation.MaxHeight)),
            Dungeon = dungeon,
            Rng = RngStreams.Restore(Rng.Master, Rng.Streams),
            Roster = Roster,
            ShopSold = ShopSold,
            Flags = [.. Flags],
            Counters = new(Counters),
            Difficulty = Difficulty,
            PendingCombat = PendingCombat,
            Scenario = Scenario,
            ScenarioVersion = ScenarioVersion,
            ClanName = ClanName,
            ClanHouse = ClanHouse,
            FloorRosters = FloorRosters,
            FloorDrops = FloorDrops,
            Named = Named,
            FirstLevel = FirstLevel,
            LastLevel = LastLevel,
            LevelShift = LevelShift,
            Clears = Clears,
            EventsSeen = EventsSeen.ToHashSet(StringComparer.Ordinal),
            Location = Location == "maze" ? Save.Location.Maze : Save.Location.Town,
            CreatedAt = CreatedAt,
        };
        foreach (var c in Roster.Characters) c.NormalizeClasses();   // 古いセーブ(クラスが1つの形)を移す
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
