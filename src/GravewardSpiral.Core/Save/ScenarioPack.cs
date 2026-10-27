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

    /// <summary>
    /// 読めなかったファイル(階のマップ、データ、文言など)。一つでもあれば保存しない(読めたところだけで書くと、
    /// 読めなかった階を詰めたり、読めなかったデータのファイルを消したりして、中身を失うため)。
    /// </summary>
    public List<string> LoadErrors { get; } = [];

    /// <summary>読んだ worlds.json のこのシナリオの項目(由来・シード・固定のボスなど。保存では文だけ差し替えて残す)。</summary>
    private JsonObject? _worldRaw;

    /// <summary>読んだ encounters.json(シナリオ独自の遭遇の設定。保存ではレベル帯だけ差し替えて残す)。</summary>
    private JsonArray? _encountersRaw;

    /// <summary>
    /// エレベーターの縦穴(DungeonDef.Elevators)。開いているあいだは、止まる階のマスも Floors に入れておき(地図と検査がそのまま使える)、
    /// 書くときはマップから外して dungeons.json にだけ書く。縦穴を変えたら <see cref="SetElevators"/> で入れ直す。
    /// </summary>
    public List<ElevatorShaft> Elevators { get; private set; } = [];

    /// <summary>縦穴を入れ替える(前の縦穴のマスを外してから、新しい縦穴を空いている階に入れる)。</summary>
    public void SetElevators(IEnumerable<ElevatorShaft> shafts)
    {
        foreach (var old in Elevators) Maze.Elevators.Clear(Floors, old);
        Elevators = shafts.ToList();
        Maze.Elevators.ApplyAll(Floors, Elevators);
    }

    /// <summary>前の形のエレベーター(階ごとの elevator:組 の印)を縦穴に直す(自動生成の迷宮や、古いシナリオを開いたとき)。直した数。</summary>
    private int ConvertLegacyElevators()
    {
        var made = Maze.Elevators.FromLegacy(Floors, new Rng.Pcg32(1, 7), Elevators);
        Elevators.AddRange(made);
        return made.Count;
    }

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
    public static ScenarioPack New(string dir, string id, IEnumerable<MazeFloor> floors, string? author = null)
    {
        var pack = NewPack(dir, id, floors, author);
        pack.ConvertLegacyElevators();
        return pack;
    }

    private static ScenarioPack NewPack(string dir, string id, IEnumerable<MazeFloor> floors, string? author) => new()
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
                pack.LoadErrors.Add(file);
                return null;
            }
        }
        JsonNode? Node(string file)
        {
            try { return Read(file) is { } text ? JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) : null; }
            catch (JsonException ex)
            {
                problems.Add($"{file}: {ex.Message}");
                pack.LoadErrors.Add(file);
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
                    pack.LoadErrors.Add(path);
                }
            }
            pack.SetElevators(def.Elevators);
            pack.ConvertLegacyElevators();
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
                pack.LoadErrors.Add(collection + ".json");
            }
        }
        string textDir = Path.Combine(dir, "text");
        if (System.IO.Directory.Exists(textDir))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(textDir, "*.json"))
            {
                try { pack.Texts[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? []; }
                catch (JsonException ex)
                {
                    problems.Add($"text/{Path.GetFileName(file)}: {ex.Message}");
                    pack.LoadErrors.Add($"text/{Path.GetFileName(file)}");
                }
            }
        }
        pack.Rumors = Json<List<RumorDef>>(RumorDef.Collection + ".json") ?? [];
        if (Node(WorldPackDef.Collection + ".json") is JsonArray worldArray
            && worldArray.OfType<JsonObject>().FirstOrDefault(w => (string?)w["id"] == pack.Id) is { } worldNode)
        {
            pack._worldRaw = worldNode.DeepClone().AsObject();
            try
            {
                if (worldNode.Deserialize<WorldPackDef>(ContentLibrary.JsonOptions)?.Text is { } text) pack.Text = text;
            }
            catch (JsonException ex)
            {
                problems.Add($"{WorldPackDef.Collection}.json: {ex.Message}");
                pack.LoadErrors.Add(WorldPackDef.Collection + ".json");
            }
        }
        pack.BossGroups = Json<List<BossDef>>("bosses.json")?.FirstOrDefault(b => b.Id == GameSession.FinalBossId)?.Groups;
        // 書き出した世界の主の組(worlds.json の boss_picks.final)は、編集する組に取り込む(保存すると決まった主として bosses.json に書く)。
        // worlds.json に残すと、新しいゲームで編集した組の上に当たってしまうので、そこからは消す
        if (pack._worldRaw?["boss_picks"] is JsonObject picks && picks[GameSession.FinalBossId] is JsonArray finalPicks)
        {
            try
            {
                pack.BossGroups ??= finalPicks.Deserialize<List<BossDef.BossGroup>>(ContentLibrary.JsonOptions);
            }
            catch (JsonException) { }
            if (pack.BossGroups != null)
            {
                picks.Remove(GameSession.FinalBossId);
                if (picks.Count == 0) pack._worldRaw.Remove("boss_picks");
            }
        }
        if (Node("bosses.json") is JsonArray bossArray)
            foreach (var o in bossArray.OfType<JsonObject>())
            {
                if ((string?)o["id"] == GameSession.FinalBossId) pack._final = o.DeepClone().AsObject();
                else pack.Bosses.Add(o.DeepClone().AsObject());
            }
        if (Node($"{Combat.EncounterSettings.Collection}.json") is JsonArray arr)
        {
            pack._encountersRaw = arr.DeepClone().AsArray();
            if (arr.OfType<JsonObject>().FirstOrDefault(o => (string?)o["id"] == Combat.EncounterSettings.DefaultId) is { } e)
            {
                pack.FirstLevel = (double?)e["first_level"] ?? pack.FirstLevel;
                pack.LastLevel = (double?)e["last_level"] ?? pack.LastLevel;
            }
        }
        return pack;
    }

    /// <summary>書き出した世界の、最下層の主のほかのボスの組(worlds.json の boss_picks)のモンスターの id を変える(ScenarioEdit.RenameMonster)。</summary>
    internal void RenameInWorldBossPicks(string old, string id)
    {
        if (_worldRaw?["boss_picks"] is not JsonObject picks) return;
        foreach (var (_, groups) in picks)
            foreach (var g in (groups as JsonArray)?.OfType<JsonObject>() ?? [])
                if ((string?)g["monster"] == old) g["monster"] = id;
    }

    /// <summary>
    /// フォルダに書く。baseContent は標準のパック(レベル帯は、その遭遇の設定を写して帯だけ変える)。
    /// 階の数が減ったら、余った maps のファイルは消す(このシナリオの名前のものだけ)。
    /// </summary>
    public void Save(ContentLibrary baseContent)
    {
        if (LoadErrors.Count > 0)
            throw new IOException($"読めなかったファイルがあるので、保存しません(読めたところだけで書くと、中身を失うため): {string.Join("、", LoadErrors.Distinct())}。ファイルを直してから開き直してください");
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
            // 縦穴の止まるマスはマップに書かない(dungeons.json の elevators から、読むときに入れる)
            var written = MazeEdit.Copy(Floors[i], Floors[i].Id);
            foreach (var shaft in Elevators) Maze.Elevators.Clear([written], shaft);
            File.WriteAllText(Path.Combine(Directory, MapFile(i)), MazeTextFormat.Write(written));
            files.Add(MapFile(i));
        }
        foreach (var old in System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "maps"), $"{Id}_b*.txt"))
            if (!files.Contains("maps/" + Path.GetFileName(old))) File.Delete(old);
        WriteJson(ContentLibrary.ManifestFile, Manifest);
        // 顔ぶれは階の数にそろえ、どの階も空なら書かない
        var rosters = Enumerable.Range(0, Floors.Count).Select(i => i < Rosters.Count ? Rosters[i] : []).ToList();
        if (rosters.All(r => r.Count == 0)) rosters = [];
        var dungeonNode = JsonSerializer.SerializeToNode(new[]
        {
            new DungeonLoader.DungeonDef { Id = Id, Name = Manifest.Name, Floors = files, Rosters = rosters, Named = Named, EventsPlaced = EventsPlaced, Note = _dungeonNote, Elevators = Elevators },
        }, Indented)!;
        Prune(dungeonNode);
        // 名のある敵「なし」(空の並び)は空のまま書く(省くと「未指定」になり、新しいゲームのたびに抽選してしまう)
        if (Named is { Count: 0 }) dungeonNode[0]!.AsObject()["named"] = new JsonArray();
        File.WriteAllText(Path.Combine(Directory, "dungeons.json"), dungeonNode.ToJsonString(Indented));
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
        var world = _worldRaw?.DeepClone().AsObject() ?? new JsonObject { ["id"] = Id };
        world["id"] = Id;
        if (HasText(Text)) world["text"] = JsonSerializer.SerializeToNode(Text, Indented);
        else world.Remove("text");
        if (world.Count > 1) File.WriteAllText(worlds, new JsonArray(world).ToJsonString(Indented));   // 文も由来も無ければ書かない
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
        var encounters = _encountersRaw?.DeepClone().AsArray() ?? new JsonArray();
        var enc = encounters.OfType<JsonObject>().FirstOrDefault(o => (string?)o["id"] == Combat.EncounterSettings.DefaultId);
        if (enc == null && baseContent.Find(Combat.EncounterSettings.Collection, Combat.EncounterSettings.DefaultId) is { } item
            && JsonNode.Parse(item.Data.GetRawText()) is JsonObject fromBase)
        {
            enc = fromBase;
            encounters.Add(enc);
        }
        if (enc != null)
        {
            enc["first_level"] = FirstLevel;
            enc["last_level"] = LastLevel;
            File.WriteAllText(Path.Combine(Directory, $"{Combat.EncounterSettings.Collection}.json"), encounters.ToJsonString(Indented));
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
        if (LoadErrors.Count > 0)
            throw new IOException($"読めなかったファイルがあるので、保存しません: {string.Join("、", LoadErrors.Distinct())}。ファイルを直してから開き直してください");
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
                else if (key == "trigger" && v is JsonValue t && t.TryGetValue<string>(out var trigger) && trigger == "enter") o.Remove(key);   // 既定(着いたとき)は書かない
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
