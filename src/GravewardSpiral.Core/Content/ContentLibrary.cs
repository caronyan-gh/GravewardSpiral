using System.Text.Json;
using System.Text.Json.Serialization;

namespace GravewardSpiral.Core.Content;

/// <summary>
/// コンテンツパックを読み合わせたもの。
/// パックは Content/&lt;パック&gt;/ のフォルダで、manifest.json と、データの JSON(1ファイル = 1つの集まり、中身は id を持つ物の配列)、
/// text/&lt;言語&gt;.json(画面の文言)を持つ。依存する順に読み、同じ集まりの同じ id は後から読んだ方で丸ごと置き換える。
/// "disabled": true の物は、それまでに読んだ同じ id の物ごと外す。
/// </summary>
public sealed class ContentLibrary
{
    public const string ManifestFile = "manifest.json";

    /// <summary>呪文の種類の名前(シナリオでは名前だけ変えられる)。</summary>
    public const string SpellCollection = "spells";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly Dictionary<string, Dictionary<string, ContentItem>> _collections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase);

    public List<PackManifest> Packs { get; } = [];

    /// <summary>読み込みで見つかった問題。1つでもあれば、ゲームは一覧を出して止まる。</summary>
    public List<string> Problems { get; } = [];

    public bool IsValid => Problems.Count == 0;

    /// <summary>
    /// contentRoot の直下のパック(標準のパック)と、extraPackDirs のパック(選んだシナリオ)を読む。
    /// 直下にあっても kind が scenario のパックは、選ばれたときだけ読む。
    /// </summary>
    public static ContentLibrary Load(string contentRoot, params string[] extraPackDirs)
    {
        var lib = new ContentLibrary();
        if (!Directory.Exists(contentRoot))
        {
            lib.Problems.Add($"コンテンツのフォルダがありません: {contentRoot}");
            return lib;
        }

        var manifests = new List<PackManifest>();
        foreach (var dir in Directory.EnumerateDirectories(contentRoot).Order(StringComparer.Ordinal))
        {
            var m = ReadManifest(dir, out var error);
            if (error != null) lib.Problems.Add($"{Rel(contentRoot, Path.Combine(dir, ManifestFile))}: {error}");
            if (m != null && m.Kind != ScenarioCatalog.Kind) manifests.Add(m);   // fonts など、パックでないフォルダは m が null
        }
        foreach (var dir in extraPackDirs)
        {
            var m = ReadManifest(dir, out var error);
            if (m != null) manifests.Add(m);
            else lib.Problems.Add($"{dir}: {error ?? "manifest.json がありません"}");
        }

        foreach (var m in lib.OrderByDependencies(manifests))
        {
            lib.Packs.Add(m);
            lib.LoadPack(contentRoot, m);
        }
        lib.ResolveExtends();
        return lib;
    }

    /// <summary>
    /// "extends": "元の id" と書いた物を、同じ種類の元の物の中身に、書いた項目を上書きしたものにする(名前だけ変えた品やモンスターなど)。
    /// 元をたどって全部のパックを読んだあとに直す。元が無い・自分を元にしている・輪になっているものは問題にする。
    /// </summary>
    private void ResolveExtends()
    {
        foreach (var (collection, items) in _collections)
        {
            var done = new Dictionary<string, ContentItem>(StringComparer.Ordinal);
            ContentItem Resolve(ContentItem item, HashSet<string> path)
            {
                if (done.TryGetValue(item.Id, out var r)) return r;
                if (!item.Data.TryGetProperty("extends", out var ext) || ext.ValueKind != JsonValueKind.String) return done[item.Id] = item;
                string baseId = ext.GetString()!;
                if (!path.Add(item.Id) || baseId == item.Id || !items.TryGetValue(baseId, out var baseItem))
                {
                    Problems.Add($"{item.Pack.Id}/{collection}.json の '{item.Id}': extends の元 '{baseId}' が無いか、輪になっています");
                    return done[item.Id] = item;
                }
                var merged = System.Text.Json.Nodes.JsonNode.Parse(Resolve(baseItem, path).Data.GetRawText())!.AsObject();
                foreach (var (key, value) in System.Text.Json.Nodes.JsonNode.Parse(item.Data.GetRawText())!.AsObject())
                    if (key != "extends") merged[key] = value?.DeepClone();
                // 写したモンスターは、自分の絵が無ければ元の絵を使う
                if (collection == "monsters" && merged["art_from"] == null) merged["art_from"] = baseId;
                return done[item.Id] = item with { Data = JsonDocument.Parse(merged.ToJsonString()).RootElement.Clone() };
            }
            foreach (var id in items.Keys.ToList()) items[id] = Resolve(items[id], new HashSet<string>(StringComparer.Ordinal));
        }
    }

    /// <summary>フォルダの manifest.json を読む。無ければ null(error も null)、読めなければ null と理由。</summary>
    public static PackManifest? ReadManifest(string dir, out string? error)
    {
        error = null;
        string path = Path.Combine(dir, ManifestFile);
        if (!File.Exists(path)) return null;
        try
        {
            var m = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("空です");
            if (string.IsNullOrWhiteSpace(m.Id)) throw new JsonException("id がありません");
            m.Directory = dir;
            return m;
        }
        catch (JsonException ex)
        {
            error = $"読めません: {ex.Message}";
            return null;
        }
    }

    /// <summary>選んでいるシナリオ(標準のパックだけなら null)。</summary>
    public PackManifest? Scenario => Packs.LastOrDefault(p => p.Kind == ScenarioCatalog.Kind);

    private List<PackManifest> OrderByDependencies(List<PackManifest> manifests)
    {
        var byId = new Dictionary<string, PackManifest>(StringComparer.Ordinal);
        foreach (var m in manifests)
        {
            if (!byId.TryAdd(m.Id, m)) Problems.Add($"パックの id '{m.Id}' が2つあります");
        }
        var ordered = new List<PackManifest>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 = 調べている途中, 2 = 済み
        void Visit(PackManifest m)
        {
            if (state.TryGetValue(m.Id, out var s))
            {
                if (s == 1) Problems.Add($"パック '{m.Id}' の依存が輪になっています");
                return;
            }
            state[m.Id] = 1;
            foreach (var dep in m.Depends)
            {
                if (byId.TryGetValue(dep, out var d)) Visit(d);
                else Problems.Add($"パック '{m.Id}' が依存するパック '{dep}' がありません");
            }
            state[m.Id] = 2;
            ordered.Add(m);
        }
        foreach (var m in byId.Values.OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.Ordinal)) Visit(m);
        return ordered;
    }

    private void LoadPack(string contentRoot, PackManifest pack)
    {
        if (pack.Format > ContentFormat.Current)
        {
            Problems.Add($"パック '{pack.Id}' はデータの形の版 {pack.Format} で、今のゲーム(版 {ContentFormat.Current})より新しいので読めません。ゲームを新しくしてください");
            return;
        }
        foreach (var file in Directory.EnumerateFiles(pack.Directory, "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (Path.GetFileName(file) == ManifestFile) continue;
            LoadCollection(contentRoot, pack, name, file);
        }

        string textDir = Path.Combine(pack.Directory, "text");
        if (Directory.Exists(textDir))
        {
            foreach (var file in Directory.EnumerateFiles(textDir, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file), JsonOptions) ?? [];
                    string lang = Path.GetFileNameWithoutExtension(file);
                    if (!_texts.TryGetValue(lang, out var merged)) _texts[lang] = merged = new(StringComparer.Ordinal);
                    foreach (var (k, v) in texts) merged[k] = v;
                }
                catch (JsonException ex)
                {
                    Problems.Add($"{Rel(contentRoot, file)}: 読めません: {ex.Message}");
                }
            }
        }
    }

    private void LoadCollection(string contentRoot, PackManifest pack, string collection, string file)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            Problems.Add($"{Rel(contentRoot, file)}: 読めません: {ex.Message}");
            return;
        }
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            Problems.Add($"{Rel(contentRoot, file)}: 中身は配列にしてください([{{\"id\": ...}}, ...])");
            return;
        }

        if (!_collections.TryGetValue(collection, out var items)) _collections[collection] = items = new(StringComparer.Ordinal);
        var seenInPack = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            index++;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                Problems.Add($"{Rel(contentRoot, file)}: {index} 番目に id がありません");
                continue;
            }
            string id = idProp.GetString()!;
            if (!seenInPack.Add(id))
            {
                Problems.Add($"{Rel(contentRoot, file)}: id '{id}' が2回あります");
                continue;
            }
            // シナリオの呪文は、標準の呪文の名前だけ変えられる(新しい呪文や、効果の違う呪文は作れない。キャラが覚えてシナリオの外へ出るため)
            if (pack.Kind == ScenarioCatalog.Kind && collection == SpellCollection)
            {
                if (!items.TryGetValue(id, out var baseSpell))
                    Problems.Add($"{Rel(contentRoot, file)}: 呪文 '{id}' は標準のパックにありません(シナリオの呪文は、名前を変えることだけできます)");
                else if (element.EnumerateObject().Any(p => p.Name is not ("id" or "name")))
                    Problems.Add($"{Rel(contentRoot, file)}: 呪文 '{id}' に名前のほかの項目があります(シナリオの呪文は、名前を変えることだけできます)");
                else if (System.Text.Json.Nodes.JsonNode.Parse(baseSpell.Data.GetRawText()) is System.Text.Json.Nodes.JsonObject renamed)
                {
                    renamed["name"] = System.Text.Json.Nodes.JsonNode.Parse(element.GetProperty("name").GetRawText());
                    items[id] = baseSpell with { Data = JsonDocument.Parse(renamed.ToJsonString()).RootElement.Clone() };   // 出どころは元のパックのまま
                }
                continue;
            }
            bool disabled = element.TryGetProperty("disabled", out var dis) && dis.ValueKind == JsonValueKind.True;
            if (disabled) items.Remove(id);
            else if (pack.Format < ContentFormat.Current && System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText()) is System.Text.Json.Nodes.JsonObject old)
            {
                // 古い形のパックは、今の形に直してから持つ
                var upgraded = ContentFormat.Upgrade(collection, old, pack.Format);
                items[id] = new ContentItem(collection, id, JsonDocument.Parse(upgraded.ToJsonString()).RootElement.Clone(), pack);
            }
            else items[id] = new ContentItem(collection, id, element.Clone(), pack);
        }
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    public IReadOnlyCollection<ContentItem> All(string collection) =>
        _collections.TryGetValue(collection, out var items) ? items.Values : [];

    public ContentItem? Find(string collection, string id) =>
        _collections.TryGetValue(collection, out var items) && items.TryGetValue(id, out var item) ? item : null;

    public bool Has(string collection, string id) => Find(collection, id) != null;

    /// <summary>参照切れを問題として記録する(読み込みのあとの検査で使う)。</summary>
    public void RequireReference(ContentItem from, string field, string targetCollection, string? targetId)
    {
        if (targetId == null) return;
        if (!Has(targetCollection, targetId))
            Problems.Add($"{from.Pack.Id}/{from.Collection}.json の '{from.Id}' の {field}: {targetCollection} に '{targetId}' がありません");
    }

    /// <summary>文言のある言語(パックの text/&lt;言語&gt;.json)。日本語・英語を先に、あとは名前の順。</summary>
    public IReadOnlyList<string> Languages => _texts.Keys
        .OrderBy(l => l == "ja" ? 0 : l == "en" ? 1 : 2).ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyDictionary<string, string> Texts(string language) =>
        _texts.TryGetValue(language, out var t) ? t : new Dictionary<string, string>();
}

public sealed class PackManifest
{
    public string Id { get; set; } = "";
    public LocalizedText? Name { get; set; }
    public string Version { get; set; } = "0";

    /// <summary>データの形の版(ContentFormat.Current)。省くと 1。古ければ読むときに今の形へ直し、新しすぎれば読まない。</summary>
    public int Format { get; set; } = 1;

    /// <summary>
    /// シナリオを見分ける GUID(エディタが作るときに付ける)。id は人が決めるので重なり得るが、これは重ならない。
    /// オリジナルの品の出どころは、この GUID と品の id の組で見分ける。
    /// </summary>
    public string? Guid { get; set; }
    public List<string> Depends { get; set; } = [];

    /// <summary>依存の無いパックどうしの読む順番(小さい方が先)。</summary>
    public int Order { get; set; }

    /// <summary>パックの種類。"scenario" なら、新しいゲームで選ぶシナリオ(選んだときだけ読む)。省くと標準のパック。</summary>
    public string? Kind { get; set; }

    /// <summary>シナリオの作者と説明(選ぶ画面に出す)。</summary>
    public string? Author { get; set; }
    public LocalizedText? Description { get; set; }

    /// <summary>シナリオの推奨レベル [最小, 最大](選ぶ画面に出す)。</summary>
    public List<int> Levels { get; set; } = [];

    /// <summary>
    /// シナリオの迷宮(dungeons.json の id)。迷宮はシナリオの中身で、新しいゲームでは生成せずにこれを使う(シード・階層・大きさは選ばない)。
    /// 省くと、新しいゲームのたびに生成する。
    /// </summary>
    public string? Dungeon { get; set; }

    /// <summary>クランを連れて入れるか(シナリオ)。</summary>
    public bool AllowImport { get; set; } = true;

    /// <summary>
    /// シナリオの迷宮に、標準のパック(srd など)の散らすイベントと連動する組も混ぜるか(シナリオ)。false なら、このシナリオのイベントだけを散らす。
    /// </summary>
    public bool StandardEvents { get; set; } = true;

    [JsonIgnore]
    public string Directory { get; set; } = "";
}

/// <summary>パックの中の物1つ。中身は JSON のまま持ち、使う側が型に読み直す。</summary>
public sealed record ContentItem(string Collection, string Id, JsonElement Data, PackManifest Pack)
{
    public T As<T>() => Data.Deserialize<T>(ContentLibrary.JsonOptions)
                        ?? throw new JsonException($"{Collection} の '{Id}' が読めません");

    /// <summary>パックのフォルダからの相対パスを、実際のパスにする。</summary>
    public string ResolvePath(string relative) => Path.GetFullPath(Path.Combine(Pack.Directory, relative));
}
