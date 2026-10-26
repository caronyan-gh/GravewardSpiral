using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Editor;

/// <summary>
/// エディタの状態。標準のパック(srd。データと検査の土台)と、開いているシナリオ。
/// 開いているシナリオの中身は ScenarioPack が持ち、タブはそれを直接書き換える(保存すると、フォルダに書く)。
/// </summary>
public sealed class EditorContext
{
    /// <summary>標準のパック(Content の直下。srd など)。</summary>
    public required string ContentRoot { get; init; }
    public required ContentLibrary BaseContent { get; init; }
    public required GameData BaseData { get; init; }

    /// <summary>新しく作るシナリオの置き場所(ゲームが探す ドキュメント\GravewardSpiral\Scenarios)。</summary>
    public required string ScenariosDir { get; init; }

    public ScenarioPack? Pack { get; private set; }

    /// <summary>エディタの設定(AI のモデルなど)。</summary>
    public EditorSettings Settings { get; } = EditorSettings.Load();

    /// <summary>直して、まだ保存していない。</summary>
    public bool Dirty { get; set; }

    /// <summary>開いているシナリオが替わった(タブが作り直す)。</summary>
    public event Action? PackChanged;

    /// <summary>中身を直した(ほかのタブが見直す)。</summary>
    public event Action? Edited;

    /// <summary>窓を作り直すとき、前の窓のタブへの知らせを外す。</summary>
    public void DetachViews()
    {
        PackChanged = null;
        Edited = null;
    }

    public static EditorContext Create(string? scenariosDir)
    {
        string root = FindContent();
        var content = ContentLibrary.Load(root);
        return new EditorContext
        {
            ContentRoot = root,
            BaseContent = content,
            BaseData = GameData.Load(content),
            ScenariosDir = scenariosDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GravewardSpiral", "Scenarios"),
        };
    }

    public void Open(ScenarioPack pack)
    {
        Pack = pack;
        Dirty = false;
        PackChanged?.Invoke();
    }

    public void MarkEdited()
    {
        Dirty = true;
        Edited?.Invoke();
    }

    /// <summary>自動生成の迷宮で、新しいシナリオを作る(まだ書かない)。</summary>
    public ScenarioPack NewPack(string id, ulong seed, int floors, int width, int height)
    {
        var settings = MazeGenSettings.Load(BaseContent);
        var request = new GenerationRequest(seed, Math.Clamp(floors, 1, settings.MaxFloors), FloorSizeMode.Fixed(width, height));
        var dungeon = new MazeGenerator(settings).Generate(request).Dungeon;
        return ScenarioPack.New(Path.Combine(ScenariosDir, id), id, dungeon.Floors, Environment.UserName);
    }

    /// <summary>その階だけ、自動生成で作り直したもの(同じ大きさ。ほかの階とつなぐ仕掛けは、検査で確かめる)。</summary>
    public MazeFloor RegenerateFloor(ScenarioPack pack, int floor, ulong seed)
    {
        var settings = MazeGenSettings.Load(BaseContent);
        var f = pack.Floors[floor];
        var request = new GenerationRequest(seed, pack.Floors.Count, FloorSizeMode.Fixed(f.Width, f.Height));
        return new MazeGenerator(settings).Generate(request).Dungeon.Floors[floor];
    }

    /// <summary>
    /// シナリオの中身を自動で選んで書き込む(ScenarioFill)。シナリオを重ねたデータで選ぶので、先に保存する。
    /// 何を埋めたか(と、読めなかったときの問題)を返し、タブを作り直す。
    /// </summary>
    public (ScenarioFill.Parts Done, string? Problem) Fill(ScenarioFill.Parts parts, bool redo)
    {
        if (Pack is not { } pack) return (ScenarioFill.Parts.None, null);
        if (pack.LoadErrors.Count > 0) return (ScenarioFill.Parts.None, Loc.T("読めなかったファイルがあるので保存できません: ", "Some files could not be read, so it cannot be saved: ") + string.Join(", ", pack.LoadErrors));
        pack.Save(BaseContent);
        var (content, data) = LoadScenario();
        if (data == null) return (ScenarioFill.Parts.None, string.Join(" / ", content.Problems.Take(3)));
        var done = ScenarioFill.Fill(pack, data, content, (ulong)Random.Shared.NextInt64(1, long.MaxValue), parts, redo);
        pack.Save(BaseContent);
        Dirty = false;
        PackChanged?.Invoke();
        return (done, null);
    }

    /// <summary>散らしたイベントの印を消す(保存してから。手で置いた散らさないイベントは残す)。消した数。</summary>
    public int ClearScattered()
    {
        if (Pack is not { } pack || pack.LoadErrors.Count > 0) return 0;   // 読めなかったファイルがあれば保存しない(中身を失わないように)
        pack.Save(BaseContent);
        if (LoadScenario().Data is not { } data) return 0;
        int n = ScenarioFill.ClearScattered(pack, data);
        pack.EventsPlaced = false;
        pack.Save(BaseContent);
        Dirty = false;
        PackChanged?.Invoke();
        return n;
    }

    /// <summary>
    /// データのタブの JSON を確かめる(保存しない)。シナリオの JSON を使い捨てのフォルダに写し、その1つを書き換えて、
    /// ゲームと同じ読み込みの検査を流し、その id の問題だけを返す。知らない項目名(書き間違い)も問題にする。
    /// </summary>
    public List<string> CheckData(string collection, System.Text.Json.Nodes.JsonObject edited, int index)
    {
        if (Pack is not { } pack || pack.DataLists().FirstOrDefault(x => x.Collection == collection).List is not { } list) return [];
        string id = (string?)edited["id"] ?? "";
        string temp = Path.Combine(Path.GetTempPath(), "GravewardSpiralEditor", "check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            foreach (var file in Directory.EnumerateFiles(pack.Directory, "*.json")) File.Copy(file, Path.Combine(temp, Path.GetFileName(file)));
            var copy = list.Select((o, i) => i == index ? edited.DeepClone() : o.DeepClone()).ToArray();
            if (index >= list.Count) copy = [.. copy, edited.DeepClone()];
            File.WriteAllText(Path.Combine(temp, collection + ".json"), new System.Text.Json.Nodes.JsonArray(copy).ToJsonString());
            var content = ContentLibrary.Load(ContentRoot, temp);
            try { GameData.Load(content); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException) { content.Problems.Add(ex.Message); }
            var problems = content.Problems.Where(p => p.Contains($"'{id}'")).ToList();
            // 知らない項目名: extends を解いた中身を、厳しく読む
            if (content.Find(collection, id) is { } item)
            {
                try
                {
                    string raw = item.Data.GetRawText();
                    _ = collection == "monsters"
                        ? System.Text.Json.JsonSerializer.Deserialize<Core.Monsters.MonsterDef>(raw, Core.Events.EventDraftCheck.Strict)
                        : (object?)System.Text.Json.JsonSerializer.Deserialize<Core.Items.ItemDef>(raw, Core.Events.EventDraftCheck.Strict);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    problems.Add(System.Text.RegularExpressions.Regex.Match(ex.Message, "property '(.+?)' could not be mapped") is { Success: true } m
                        ? Loc.T($"知らない項目 '{m.Groups[1].Value}'(書き間違いかもしれない。「項目の一覧」で確かめる)", $"Unknown field '{m.Groups[1].Value}' (maybe a typo; see \"Field list\")")
                        : ex.Message);
                }
            }
            return problems.Distinct().ToList();
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// イベントの下書きを、ゲームと同じ検査にかける(保存しない。AI の下書きを当てる前に使う)。
    /// まだ保存していない品・モンスター・ほかのイベントも見えるように、使い捨てのフォルダに今の中身を書いて読む。同じ id のイベントは下書きで置き換える。
    /// items はイベントと一緒に書いた新しい品(品の中身に足して読み、その品の読み込みの問題と、知らない項目名も返す)。
    /// </summary>
    public List<string> CheckEvent(System.Text.Json.Nodes.JsonObject draft, IReadOnlyList<System.Text.Json.Nodes.JsonObject> items)
    {
        if (Pack is not { } pack) return [];
        string id = (string?)draft["id"] ?? "";
        string temp = Path.Combine(Path.GetTempPath(), "GravewardSpiralEditor", "check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            foreach (var file in Directory.EnumerateFiles(pack.Directory, "*.json")) File.Copy(file, Path.Combine(temp, Path.GetFileName(file)));
            var newIds = items.Select(i => (string?)i["id"] ?? "").ToHashSet();
            foreach (var (collection, list) in pack.DataLists())
            {
                string file = Path.Combine(temp, collection + ".json");
                // 新しい品は品の中身に足す(同じ id があれば、新しい方で置き換える)
                var all = collection == "items" ? list.Where(o => !newIds.Contains((string?)o["id"] ?? "")).Concat(items).ToList() : list;
                if (all.Count > 0) File.WriteAllText(file, new System.Text.Json.Nodes.JsonArray(all.Select(o => (System.Text.Json.Nodes.JsonNode)o.DeepClone()).ToArray()).ToJsonString());
                else File.Delete(file);
            }
            File.WriteAllText(Path.Combine(temp, "events.json"), System.Text.Json.JsonSerializer.Serialize(pack.Events.Where(e => e.Id != id), Core.Content.ContentLibrary.JsonOptions));
            var content = ContentLibrary.Load(ContentRoot, temp);
            GameData data;
            try { data = GameData.Load(content); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException) { return [ex.Message]; }
            var json = System.Text.Json.JsonSerializer.SerializeToElement(draft);
            var problems = Core.Events.EventDraftCheck.Check(data, content, [("AI", json)], allowExisting: true, handPlaced: true).Problems;
            // 新しい品: 読み込みの問題と、知らない項目名(書き間違い)
            foreach (var itemId in newIds)
            {
                problems.AddRange(content.Problems.Where(p => p.Contains($"'{itemId}'")).Select(p => Loc.T("新しい品: ", "New item: ") + p));
                if (content.Find("items", itemId) is not { } found) continue;
                try { _ = System.Text.Json.JsonSerializer.Deserialize<Core.Items.ItemDef>(found.Data.GetRawText(), Core.Events.EventDraftCheck.Strict); }
                catch (System.Text.Json.JsonException ex) { problems.Add(Loc.T($"新しい品 '{itemId}': ", $"New item '{itemId}': ") + ex.Message); }
            }
            return problems.Distinct().ToList();
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 品かモンスターの下書きを入れたゲームのデータ(保存しない。AI に頼む窓で、強さを試すのに使う)。
    /// 使い捨てのフォルダに今の中身を書き、その1つを下書きにして読む。読めなければ null と理由。
    /// </summary>
    public (GameData? Data, string? Problem) DataWith(string collection, System.Text.Json.Nodes.JsonObject draft)
    {
        if (Pack is not { } pack) return (null, null);
        string id = (string?)draft["id"] ?? "";
        string temp = Path.Combine(Path.GetTempPath(), "GravewardSpiralEditor", "data-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            foreach (var file in Directory.EnumerateFiles(pack.Directory, "*.json")) File.Copy(file, Path.Combine(temp, Path.GetFileName(file)));
            foreach (var (name, list) in pack.DataLists())
            {
                var all = name == collection ? list.Where(o => (string?)o["id"] != id).Append(draft).ToList() : list;
                string file = Path.Combine(temp, name + ".json");
                if (all.Count > 0) File.WriteAllText(file, new System.Text.Json.Nodes.JsonArray(all.Select(o => (System.Text.Json.Nodes.JsonNode)o.DeepClone()).ToArray()).ToJsonString());
                else File.Delete(file);
            }
            var content = ContentLibrary.Load(ContentRoot, temp);
            try { return (GameData.Load(content), null); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException) { return (null, ex.Message); }
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    /// <summary>確かめ用の偽の AI(--fake-ai)。本物の API を呼ばない。</summary>
    public static Core.Ai.IAiClient? FakeAi { get; set; }

    /// <summary>AI の窓口(偽の AI か、API キーと設定のモデル)。キーが無ければ null と、画面に出す理由。</summary>
    public Core.Ai.IAiClient? Ai(out string? error)
    {
        error = null;
        if (FakeAi != null) return FakeAi;
        if (Game.Ai.ApiKeyStore.Load(Game.Ai.ApiKeyStore.Claude) is not { } key)
        {
            error = Loc.T("API キーがありません(上の帯の「設定」で入れる)", "No API key (set it in Settings on the top bar)");
            return null;
        }
        return new Core.Ai.ClaudeClient(key, Settings.AiModel);
    }

    /// <summary>開いているシナリオを標準のパックに重ねて読んだもの(検査とおすすめに使う。保存してから呼ぶ)。</summary>
    public (ContentLibrary Content, GameData? Data) LoadScenario()
    {
        var content = ContentLibrary.Load(ContentRoot, Pack!.Directory);
        DungeonLoader.ValidateAll(content);
        GameData? data = null;
        try { data = GameData.Load(content); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException)
        {
            content.Problems.Add(ex.Message);
        }
        return (content, data);
    }

    /// <summary>リポジトリの Content(GravewardSpiral.slnx の横)か、エディタの横の Content。</summary>
    private static string FindContent()
    {
        string here = Path.Combine(AppContext.BaseDirectory, "Content");
        if (Directory.Exists(here)) return here;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string c = Path.Combine(dir.FullName, "Content");
            if (Directory.Exists(c) && File.Exists(Path.Combine(dir.FullName, "GravewardSpiral.slnx"))) return c;
        }
        throw new DirectoryNotFoundException("Content が見つかりません");
    }
}
