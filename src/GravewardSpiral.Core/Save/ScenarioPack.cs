using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.Monsters;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// シナリオエディタで開いているシナリオ(パックのフォルダ)。エディタが直すのは、manifest・迷宮(dungeons.json と maps)・
/// イベント(events.json)・最下層の主(bosses.json の final)・世界の文(worlds.json)・レベル帯(encounters.json)。
/// それ以外のファイル(文言・はめ込む部屋・画像など)は、読まずにそのまま残す。
/// </summary>
public sealed class ScenarioPack
{
    public string Directory { get; private set; } = "";
    public PackManifest Manifest { get; set; } = new();
    public List<MazeFloor> Floors { get; set; } = [];
    public List<EventDef> Events { get; set; } = [];

    /// <summary>酒場の噂(rumors.json。条件付き)。</summary>
    public List<RumorDef> Rumors { get; set; } = [];

    /// <summary>
    /// シナリオのデータ(品・モンスター・呪文の名前)。JSON のまま持つ(extends や、エディタが知らない項目もそのまま残すため)。
    /// 呪文は id と name だけ(シナリオの呪文は名前だけ変えられる)。
    /// </summary>
    public List<JsonObject> Items { get; set; } = [];
    public List<JsonObject> Monsters { get; set; } = [];
    public List<JsonObject> Spells { get; set; } = [];

    /// <summary>シナリオの文言(言語 → キー → 文。text/&lt;言語&gt;.json)。迷宮の「文言の印」(message:キー)などが使う。</summary>
    public Dictionary<string, Dictionary<string, string>> Texts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>データの種類(ファイルの名前)と、その中身。</summary>
    public IEnumerable<(string Collection, List<JsonObject> List)> DataLists()
    {
        yield return ("items", Items);
        yield return ("monsters", Monsters);
        yield return (ContentLibrary.SpellCollection, Spells);
    }

    /// <summary>階ごとの決まった顔ぶれ(モンスターの id。Floors と同じ並び。空の階は自動で選ぶ)。</summary>
    public List<List<string>> Rosters { get; set; } = [];

    /// <summary>名のある敵(null なら決めていない。新しいゲームのたびに抽選する)。DungeonDef.Named。</summary>
    public List<Monsters.NamedSpawn>? Named { get; set; }

    /// <summary>散らすイベントを迷宮のマスに置いてある(新しいゲームでは散らさない)。DungeonDef.EventsPlaced。</summary>
    public bool EventsPlaced { get; set; }

    /// <summary>読んだときの dungeons.json のメモ(そのまま残す)。</summary>
    private string? _dungeonNote;

    /// <summary>その階の顔ぶれ(並びが足りなければ足す)。</summary>
    public List<string> RosterOf(int floor)
    {
        while (Rosters.Count <= floor) Rosters.Add([]);
        return Rosters[floor];
    }

    /// <summary>世界の文(序文、階ごとの名前・入ったときの文・書き付け、主の名前・出会い・倒したとき)。</summary>
    public Ai.WorldText Text { get; set; } = new();

    /// <summary>最下層の主の決まった組(null なら、標準の final のまま。世界のレベル帯から選ぶ)。</summary>
    public List<BossDef.BossGroup>? BossGroups { get; set; }

    /// <summary>
    /// 最下層の主のほかのボス(bosses.json の final 以外。JSON のまま持つ)。迷宮の印 boss:&lt;id&gt; のマスで戦う。
    /// 出会ったときと倒したときの文は、文言(Texts)のキー boss.&lt;id&gt;.intro / .victory に書く。
    /// </summary>
    public List<JsonObject> Bosses { get; set; } = [];

    /// <summary>読んだときの bosses.json の final(主の名前・文言のキーなどを、そのまま残すため)。</summary>
    private JsonObject? _final;

    public double FirstLevel { get; set; } = 1;
    public double LastLevel { get; set; } = 5;

    public string Id => Manifest.Id;

    /// <summary>迷宮の階のファイル(maps/&lt;id&gt;_b&lt;n&gt;.txt)。</summary>
    public string MapFile(int floor) => $"maps/{Id}_b{floor + 1}.txt";

    /// <summary>新しいシナリオ(まだ書いていない)。迷宮は渡した階(自動生成したものか、手で作るための空の階)。</summary>
    public static ScenarioPack New(string dir, string id, IEnumerable<MazeFloor> floors, string? author = null) => new()
    {
        Directory = dir,
        Manifest = new PackManifest
        {
            Id = id,
            Kind = ScenarioCatalog.Kind,
            Name = LocalizedText.Of(id, id),
            Version = "1.0",
            Depends = ["srd"],
            Order = 50,
            Format = ContentFormat.Current,
            Guid = System.Guid.NewGuid().ToString("D"),
            Author = author,
            Levels = [1, 5],
            Dungeon = id,
        },
        Floors = floors.Select((f, i) => MazeEdit.Copy(f, $"{id}_b{i + 1}")).ToList(),
    };

    /// <summary>フォルダのシナリオを読む。読めないところは problems に足し、読めたところだけで開く。</summary>
    public static ScenarioPack Load(string dir, List<string> problems)
    {
        var pack = new ScenarioPack { Directory = dir };
        pack.Manifest = ContentLibrary.ReadManifest(dir, out var error) ?? throw new InvalidDataException(error ?? "manifest.json がありません");
        string? Read(string file) => File.Exists(Path.Combine(dir, file)) ? File.ReadAllText(Path.Combine(dir, file)) : null;
        T? Json<T>(string file) where T : class
        {
            try { return Read(file) is { } text ? JsonSerializer.Deserialize<T>(text, ContentLibrary.JsonOptions) : null; }
            catch (JsonException ex)
            {
                problems.Add($"{file}: {ex.Message}");
                return null;
            }
        }
        // 迷宮: manifest の dungeon の階を読む
        var dungeons = Json<List<DungeonLoader.DungeonDef>>("dungeons.json") ?? [];
        if (dungeons.FirstOrDefault(d => d.Id == pack.Manifest.Dungeon) is { } def)
        {
            pack.Rosters = def.Rosters;
            pack.Named = def.Named;
            pack.EventsPlaced = def.EventsPlaced;
            pack._dungeonNote = def.Note;
            foreach (var path in def.Floors)
            {
                try { pack.Floors.Add(MazeTextFormat.Parse(File.ReadAllText(Path.Combine(dir, path)), path)); }
                catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException)
                {
                    problems.Add($"{path}: {ex.Message}");
                }
            }
        }
        pack.Events = Json<List<EventDef>>("events.json") ?? [];
        foreach (var (collection, list) in pack.DataLists())
        {
            try
            {
                if (Read(collection + ".json") is { } raw && JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonArray dataArray)
                    list.AddRange(dataArray.OfType<JsonObject>().Select(o => o.DeepClone().AsObject()));
            }
            catch (JsonException ex)
            {
                problems.Add($"{collection}.json: {ex.Message}");
            }
        }
        string textDir = Path.Combine(dir, "text");
        if (System.IO.Directory.Exists(textDir))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(textDir, "*.json"))
            {
                try { pack.Texts[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? []; }
                catch (JsonException ex) { problems.Add($"text/{Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
        pack.Rumors = Json<List<RumorDef>>(RumorDef.Collection + ".json") ?? [];
        if (Json<List<WorldPackDef>>(WorldPackDef.Collection + ".json")?.FirstOrDefault(w => w.Id == pack.Id)?.Text is { } text) pack.Text = text;
        pack.BossGroups = Json<List<BossDef>>("bosses.json")?.FirstOrDefault(b => b.Id == GameSession.FinalBossId)?.Groups;
        if (Read("bosses.json") is { } bossText && JsonNode.Parse(bossText) is JsonArray bossArray)
            foreach (var o in bossArray.OfType<JsonObject>())
            {
                if ((string?)o["id"] == GameSession.FinalBossId) pack._final = o.DeepClone().AsObject();
                else pack.Bosses.Add(o.DeepClone().AsObject());
            }
        if (Read($"{Combat.EncounterSettings.Collection}.json") is { } enc && JsonNode.Parse(enc) is JsonArray arr
            && arr.OfType<JsonObject>().FirstOrDefault(o => (string?)o["id"] == Combat.EncounterSettings.DefaultId) is { } e)
        {
            pack.FirstLevel = (double?)e["first_level"] ?? pack.FirstLevel;
            pack.LastLevel = (double?)e["last_level"] ?? pack.LastLevel;
        }
        return pack;
    }

    /// <summary>
    /// フォルダに書く。baseContent は標準のパック(レベル帯は、その遭遇の設定を写して帯だけ変える)。
    /// 階の数が減ったら、余った maps のファイルは消す(このシナリオの名前のものだけ)。
    /// </summary>
    public void Save(ContentLibrary baseContent)
    {
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "maps"));
        Manifest.Kind = ScenarioCatalog.Kind;
        Manifest.Format = ContentFormat.Current;
        Manifest.Guid ??= System.Guid.NewGuid().ToString("D");   // 古いシナリオを開いて保存したときも、GUID を付ける
        Manifest.Dungeon = Id;
        Manifest.Levels = [(int)Math.Floor(FirstLevel), (int)Math.Ceiling(LastLevel)];
        var files = new List<string>();
        for (int i = 0; i < Floors.Count; i++)
        {
            Floors[i] = MazeEdit.Copy(Floors[i], $"{Id}_b{i + 1}");
            File.WriteAllText(Path.Combine(Directory, MapFile(i)), MazeTextFormat.Write(Floors[i]));
            files.Add(MapFile(i));
        }
        foreach (var old in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "maps"), $"{Id}_b*.txt"))
            if (!files.Contains("maps/" + Path.GetFileName(old))) File.Delete(old);
        WriteJson(ContentLibrary.ManifestFile, Manifest);
        // 顔ぶれは階の数にそろえ、どの階も空なら書かない
        var rosters = Enumerable.Range(0, Floors.Count).Select(i => i < Rosters.Count ? Rosters[i] : []).ToList();
        if (rosters.All(r => r.Count == 0)) rosters = [];
        WriteJson("dungeons.json", new[]
        {
            new DungeonLoader.DungeonDef { Id = Id, Name = Manifest.Name, Floors = files, Rosters = rosters, Named = Named, EventsPlaced = EventsPlaced, Note = _dungeonNote },
        }, prune: true);
        WriteJson("events.json", Events, prune: true);
        string rumors = Path.Combine(Directory, RumorDef.Collection + ".json");
        if (Rumors.Count > 0) WriteJson(RumorDef.Collection + ".json", Rumors, prune: true);
        else if (File.Exists(rumors)) File.Delete(rumors);
        // データは、中身が無ければファイルを消す(標準のパックのまま)
        foreach (var (collection, list) in DataLists())
        {
            string file = Path.Combine(Directory, collection + ".json");
            if (list.Count > 0) File.WriteAllText(file, new JsonArray(list.Select(o => (JsonNode)o.DeepClone()).ToArray()).ToJsonString(Indented));
            else if (File.Exists(file)) File.Delete(file);
        }
        foreach (var (lang, texts) in Texts)
        {
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "text"));
            File.WriteAllText(Path.Combine(Directory, "text", lang + ".json"),
                JsonSerializer.Serialize(new SortedDictionary<string, string>(texts, StringComparer.Ordinal), Indented));
        }
        // 世界の文は、書いてあるときだけ(空の worlds.json は、パックの決まった序文や AI の手伝いを止めてしまう)
        string worlds = Path.Combine(Directory, WorldPackDef.Collection + ".json");
        if (HasText(Text)) WriteJson(WorldPackDef.Collection + ".json", new[] { new WorldPackDef { Id = Id, Text = Text } });
        else if (File.Exists(worlds)) File.Delete(worlds);
        // 最下層の主: 読んだ final を残して、組だけを書き換える(名前・文言のキーはそのまま)。ほかのボスは Bosses のまま
        string bosses = Path.Combine(Directory, "bosses.json");
        if (BossGroups is { Count: > 0 } groups)
        {
            if (_final == null && baseContent.Find("bosses", GameSession.FinalBossId) is { } baseFinal)
                _final = JsonNode.Parse(baseFinal.Data.GetRawText())!.AsObject();   // 標準の final を写す
            if (_final != null)
            {
                _final["auto"] = false;
                _final["groups"] = JsonSerializer.SerializeToNode(groups, Indented);
            }
        }
        else if (_final != null && (bool?)_final["auto"] != true) _final = null;   // 組を空にしたら、標準の(世界のレベル帯から選ぶ)主に戻す
        var all = new JsonArray();
        if (_final != null) all.Add(_final.DeepClone());
        foreach (var b in Bosses) all.Add(b.DeepClone());
        if (all.Count > 0) File.WriteAllText(bosses, all.ToJsonString(Indented));
        else if (File.Exists(bosses)) File.Delete(bosses);
        if (baseContent.Find(Combat.EncounterSettings.Collection, Combat.EncounterSettings.DefaultId) is { } item
            && JsonNode.Parse(item.Data.GetRawText()) is JsonObject enc)
        {
            enc["first_level"] = FirstLevel;
            enc["last_level"] = LastLevel;
            File.WriteAllText(Path.Combine(Directory, $"{Combat.EncounterSettings.Collection}.json"), new JsonArray(enc).ToJsonString(Indented));
        }
    }

    /// <summary>世界の文に、何か書いてあるか。</summary>
    private static bool HasText(Ai.WorldText t) =>
        t.Prologue != null || t.Boss is { } b && (b.Name != null || b.Intro != null || b.Victory != null)
        || t.Floors.Any(f => f.Name != null || f.Enter != null || f.Documents.Count > 0);

    /// <summary>zip に書き出す(フォルダの中身を、一番上に manifest.json がある形で)。</summary>
    public void ExportZip(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(Directory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    /// <summary>
    /// 名前を付けて保存: フォルダを丸ごと(エディタが触らない文言・はめ込む部屋・絵・曲も)dir に写し、新しい id と GUID の
    /// 別のシナリオにして書く。迷宮のファイルは新しい id の名前に付け替える(元の名前のものは写した先から消す)。
    /// </summary>
    public void SaveAs(string dir, string id, ContentLibrary baseContent)
    {
        if (System.IO.Directory.Exists(dir)) throw new IOException($"{dir} はもうあります");
        Copy(Directory, dir);
        // 元の迷宮のファイル(dungeons.json の、このシナリオの迷宮の階)は、新しい名前で書き直すので消す
        try
        {
            var defs = JsonSerializer.Deserialize<List<DungeonLoader.DungeonDef>>(File.ReadAllText(Path.Combine(dir, "dungeons.json")), ContentLibrary.JsonOptions) ?? [];
            foreach (var path in defs.FirstOrDefault(d => d.Id == Manifest.Dungeon)?.Floors ?? [])
                if (File.Exists(Path.Combine(dir, path))) File.Delete(Path.Combine(dir, path));
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        Directory = dir;
        Manifest.Id = id;
        Manifest.Guid = System.Guid.NewGuid().ToString("D");
        Save(baseContent);
    }

    private static void Copy(string from, string to)
    {
        System.IO.Directory.CreateDirectory(to);
        foreach (var file in System.IO.Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var sub in System.IO.Directory.EnumerateDirectories(from)) Copy(sub, Path.Combine(to, Path.GetFileName(sub)));
    }

    /// <summary>迷宮(読み込みの検査やボットに渡す形)。</summary>
    public Dungeon Dungeon() => new(Id, Floors);

    private void WriteJson<T>(string file, T value, bool prune = false)
    {
        var node = JsonSerializer.SerializeToNode(value, Indented);
        if (prune && node != null) Prune(node);
        File.WriteAllText(Path.Combine(Directory, file), node?.ToJsonString(Indented) ?? "null");
    }

    /// <summary>イベントなどを、空の項目を省いた読みやすい JSON にする(エディタの JSON の表示)。</summary>
    public static string CleanJson<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Indented);
        if (node != null) Prune(node);
        return node?.ToJsonString(Indented) ?? "null";
    }

    /// <summary>空の項目(null と空の配列)と、読まない項目を消す(イベントの JSON を手で書いたものに近い形にする)。</summary>
    private static void Prune(JsonNode node)
    {
        if (node is JsonObject o)
        {
            foreach (var key in o.Select(x => x.Key).ToList())
            {
                var v = o[key];
                if (v == null || v is JsonArray { Count: 0 } || key == "done_flag") o.Remove(key);   // done_flag は id から決まる(読まない)
                else Prune(v);
            }
        }
        else if (node is JsonArray a)
        {
            foreach (var x in a) if (x != null) Prune(x);
        }
    }

    private static readonly JsonSerializerOptions Indented = new(ContentLibrary.JsonOptions)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
