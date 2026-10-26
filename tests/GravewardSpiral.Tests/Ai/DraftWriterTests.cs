using System.Text.Json;
using System.Text.Json.Nodes;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;

namespace GravewardSpiral.Tests.Ai;

/// <summary>シナリオエディタの AI の補助(イベント・品・モンスターの下書き)。</summary>
public class DraftWriterTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    /// <summary>決まった返事を順に返す偽の AI。</summary>
    private sealed class ScriptedAi(params string[] drafts) : IAiClient
    {
        public List<AiJsonRequest> Requests { get; } = [];
        public string Model => "fake";

        public Task<AiJsonReply> GenerateJsonAsync(AiJsonRequest request, CancellationToken cancel)
        {
            string draft = drafts[Math.Min(Requests.Count, drafts.Length - 1)];
            Requests.Add(request);
            return Task.FromResult(new AiJsonReply(JsonSerializer.SerializeToElement(new { json = draft, note = "メモ" }), null));
        }
    }

    /// <summary>エディタと同じく、知らない項目名と知らない部品を問題にする検査。</summary>
    private static List<string> Check(JsonObject draft) => Check(draft, []);

    private static List<string> Check(JsonObject draft, IReadOnlyList<JsonObject> items)
    {
        try
        {
            var e = draft.Deserialize<EventDef>(EventDraftCheck.Strict)!;
            Data.Events[e.Id] = e;
            return EventCheck.Check(Data, e).ToList();
        }
        catch (JsonException ex) { return [ex.Message]; }
        finally { Data.Events.Remove((string?)draft["id"] ?? ""); }
    }

    private static DraftWriter.Brief Brief(string? baseJson = null) =>
        new(DraftWriter.Kind.Event, FieldDocs.Schema(typeof(EventDef), FieldDocs.Load()), baseJson, "盗賊なら罠を外せる宝箱", "Skills: stealth", "Use this id: ai_chest");

    [Fact]
    public void TheSchemaCarriesThePartsAndTheirDescriptions()
    {
        string schema = FieldDocs.Schema(typeof(EventDef), FieldDocs.Load());
        Assert.Contains("## EventStep", schema);
        Assert.Contains("## EventCondition", schema);
        Assert.Contains("- steps (list of EventStep)", schema);
        Assert.Contains("take_item", schema);   // 手順の種類ごとの説明(型の説明書き)も入る
    }

    [Fact]
    public async Task AGoodDraftIsReturnedAfterOneCall()
    {
        var ai = new ScriptedAi("""{ "id": "ai_chest", "place": "maze", "steps": [{ "type": "text", "text": { "en": "A chest.", "ja": "宝箱がある。" } }] }""");
        var result = await DraftWriter.WriteAsync(ai, Brief(), Check, CancellationToken.None);
        Assert.Empty(result.Problems);
        Assert.Equal(1, result.Tries);
        Assert.Equal("メモ", result.Note);
        Assert.Equal("ai_chest", (string?)result.Draft!["id"]);
        // 頼む文に、スキーマ・手がかり・元・希望が入る
        string user = ai.Requests[0].User;
        Assert.Contains("# Schema", user);
        Assert.Contains("Use this id: ai_chest", user);
        Assert.Contains("(none: write a new one)", user);
        Assert.Contains("盗賊なら罠を外せる宝箱", user);
    }

    [Fact]
    public void WithTheCacheTheSamePreambleGoesFirstAndApart()
    {
        // キャッシュは前から同じ部分にしか効かない: 指示・スキーマ・共通の手がかりを Cached に、頼み事ごとのものは User に
        var cached = DraftWriter.BuildRequest(Brief() with { Cache = true, Specific = "Use this id: ai_chest" });
        Assert.Contains("# Schema", cached.Cached);
        Assert.DoesNotContain("# Schema", cached.User);
        Assert.DoesNotContain("ai_chest", cached.Cached);
        Assert.Contains("Use this id: ai_chest", cached.User);
        var other = DraftWriter.BuildRequest(Brief() with { Cache = true, Specific = "Use this id: ai_door", Wish = "扉" });
        Assert.Equal(cached.Cached, other.Cached);
        Assert.Equal(cached.System, other.System);
        // 切ると、全部を User に入れる(Cached は無し)
        var plain = DraftWriter.BuildRequest(Brief());
        Assert.Null(plain.Cached);
        Assert.Contains("# Schema", plain.User);
    }

    [Fact]
    public void ScatterDepthWrittenAsFloorsIsCaught()
    {
        // ChatGPT が「1〜5 階」のつもりで書いた深さ。割合なので、そのままだと最下層にしか置かれない
        var floors = JsonNode.Parse("""{ "id": "ai_chest", "place": "maze", "scatter": { "min_depth": 1, "max_depth": 5 }, "steps": [{ "type": "text", "text": { "en": "A chest.", "ja": "宝箱。" } }] }""")!.AsObject();
        Assert.Contains(Check(floors), p => p.Contains("0〜1 の割合"));
        var share = JsonNode.Parse("""{ "id": "ai_chest", "place": "maze", "scatter": { "min_depth": 0, "max_depth": 0.6 }, "steps": [{ "type": "text", "text": { "en": "A chest.", "ja": "宝箱。" } }] }""")!.AsObject();
        Assert.Empty(Check(share));
    }

    [Fact]
    public void TheChatTextCarriesTheSameRulesAndAReplyShape()
    {
        // API キーが無い人の分: API と同じ指示・スキーマ・手がかり・希望に、返事の形を足す
        var brief = Brief() with { Cache = true };
        string chat = DraftWriter.ChatText(brief);
        var api = DraftWriter.BuildRequest(brief);
        Assert.Contains(api.System.Trim(), chat);
        Assert.EndsWith("Do not reply with the draft alone.\n", chat);   // 返事の形は最後に、例つきで
        Assert.Contains("# Schema", chat);
        Assert.Contains("盗賊なら罠を外せる宝箱", chat);
        Assert.Contains("\"draft\"", chat);
        string fix = DraftWriter.FixText("""{ "id": "x" }""", ["知らない手順 dance"]);
        Assert.Contains("# Your previous draft", fix);
        Assert.Contains("- 知らない手順 dance", fix);
    }

    [Fact]
    public void APastedChatAnswerIsRead()
    {
        var (draft, items, note, differences, error, wrapped) = DraftWriter.ParseChat("""
            はい、作りました。
            ```json
            { "draft": { "id": "ai_chest", "steps": [] }, "items": [{ "id": "ai_chest_key", "kind": "key" }], "note": "宝箱", "differences": ["毒は軽くした"] }
            ```
            """);
        Assert.Null(error);
        Assert.Equal("ai_chest", (string?)draft!["id"]);
        Assert.Equal("宝箱", note);
        Assert.Equal(["毒は軽くした"], differences);
        Assert.True(wrapped);
        Assert.Equal("ai_chest_key", (string?)Assert.Single(items)["id"]);
        // 形を守らず、オブジェクトだけを返したときも読む(形を守っていないことは知らせる)
        var bare = DraftWriter.ParseChat("""{ "id": "ai_door" }""");
        Assert.Equal("ai_door", (string?)bare.Draft!["id"]);
        Assert.False(bare.Wrapped);
        Assert.NotNull(DraftWriter.ParseChat("ごめんなさい、作れません").Error);
    }

    [Fact]
    public async Task NewItemsComeBackWithTheEventAndAreChecked()
    {
        // イベントと一緒に書いた新しい品は、検査にも渡り、結果にも入る。品のスキーマは前置き(キャッシュする方)に入る
        IReadOnlyList<JsonObject>? seen = null;
        var brief = Brief() with { ItemSchema = FieldDocs.Schema(typeof(Core.Items.ItemDef), FieldDocs.Load()), Cache = true };
        Assert.Contains("# Schema for new items", DraftWriter.BuildRequest(brief).Cached);
        var withItems = new ItemAi("""{ "id": "ai_chest_key", "kind": "key", "no_random": true }""");
        var result = await DraftWriter.WriteAsync(withItems, brief, (d, items) => { seen = items; return []; }, CancellationToken.None);
        Assert.Equal("ai_chest_key", (string?)Assert.Single(result.Items)["id"]);
        Assert.Single(seen!);
    }

    /// <summary>新しい品を1つ付けて返す偽の AI。</summary>
    private sealed class ItemAi(string item) : IAiClient
    {
        public string Model => "fake";

        public Task<AiJsonReply> GenerateJsonAsync(AiJsonRequest request, CancellationToken cancel) =>
            Task.FromResult(new AiJsonReply(JsonSerializer.SerializeToElement(new
            {
                json = """{ "id": "ai_chest", "place": "maze", "steps": [{ "type": "give_item", "id": "ai_chest_key", "count": 1 }] }""",
                items = new[] { item }, note = "鍵", differences = Array.Empty<string>(),
            }), null));
    }

    [Fact]
    public async Task ProblemsAreSentBackOnceToBeFixed()
    {
        var ai = new ScriptedAi(
            """{ "id": "ai_chest", "place": "maze", "steps": [{ "type": "dance" }] }""",
            """{ "id": "ai_chest", "place": "maze", "steps": [{ "type": "text", "text": { "en": "A chest.", "ja": "宝箱がある。" } }] }""");
        var result = await DraftWriter.WriteAsync(ai, Brief("""{ "id": "ai_chest" }"""), Check, CancellationToken.None);
        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Tries);
        Assert.Contains("# Your previous draft", ai.Requests[1].User);
        Assert.Contains("dance", ai.Requests[1].User);
    }

    [Fact]
    public async Task ItStopsAfterOneFixAndKeepsTheLastDraftAndProblems()
    {
        var ai = new ScriptedAi("""{ "id": "ai_chest", "steps": [{ "type": "dance" }] }""", "これは JSON ではない");
        var result = await DraftWriter.WriteAsync(ai, Brief(), Check, CancellationToken.None);
        Assert.Equal(1 + DraftWriter.Retries, ai.Requests.Count);
        Assert.Null(result.Draft);
        Assert.Contains(result.Problems, p => p.StartsWith("JSON を読めません"));
    }
}
