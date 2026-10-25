using System.Text.Json;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Ai;

public class WorldWriterTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private static string T(string en, string ja) => $$"""{"en": {{JsonSerializer.Serialize(en)}}, "ja": {{JsonSerializer.Serialize(ja)}}}""";

    /// <summary>決まった返事をする AI(呼ばれた回数も数える)。</summary>
    private sealed class FakeAi(AiJsonReply reply) : IAiClient
    {
        public int Calls { get; private set; }
        public AiJsonRequest? Last { get; private set; }
        public string Model => "fake";

        public Task<AiJsonReply> GenerateJsonAsync(AiJsonRequest request, CancellationToken cancel)
        {
            Calls++;
            Last = request;
            return Task.FromResult(reply);
        }
    }

    [Fact]
    public void TheWorldPromptIsInThePackAndTheRequestCarriesTheStoryAndFloors()
    {
        // 自由な迷宮は物語の芯を持たず、第1部のシナリオは芯(オルド)を持つ
        Assert.Contains("No fixed story", WorldWriter.Prompt(Content)!.Story);
        var prompt = WorldWriter.Prompt(ContentLibrary.Load(TestPaths.Content, TestPaths.Gravekeeper));
        Assert.NotNull(prompt);
        Assert.Contains("Ord", prompt!.Story);
        var request = WorldWriter.BuildRequest(Data, prompt, 6, "灰の手");
        Assert.Contains("exactly 6 entries", request.User);
        Assert.Contains("B6", request.User);
        Assert.Contains("灰の手", request.User);
        Assert.Equal(JsonValueKind.Object, request.Schema.ValueKind);
    }

    [Fact]
    public void TheFreeDungeonGetsInspirationWordsFromTheWorldSeed()
    {
        var free = WorldWriter.Prompt(Content)!;
        string Words(ulong seed) => WorldWriter.BuildRequest(Data, free, 6, null, seed).User.Split('\n').Single(l => l.StartsWith("# Inspiration"));
        Assert.Equal(Words(7), Words(7));   // 同じ世界なら同じ言葉
        Assert.Contains(Enumerable.Range(1, 20), s => Words((ulong)s) != Words(7));
        Assert.Contains("do NOT reuse", free.Story);
        // 第1部は物語の芯があるので、着想の言葉は渡さない
        var story = WorldWriter.Prompt(ContentLibrary.Load(TestPaths.Content, TestPaths.Gravekeeper))!;
        Assert.DoesNotContain("# Inspiration", WorldWriter.BuildRequest(Data, story, 6, null, 7).User);
    }

    [Fact]
    public void ParseKeepsGoodTextAndDropsTheRest()
    {
        string longJa = new('あ', WorldWriter.NameLimit.Ja + 1);
        var json = Json($$"""
            {
              "prologue": {{T("Long ago...", "昔…")}},
              "floors": [
                { "name": {{T("Wet Graves", "濡れた墓")}}, "enter": {{T("Water drips.", "水が滴る。")}},
                  "documents": [ {{T("a", "あ")}}, {{T("b", "い")}}, {{T("c", "う")}} ] },
                { "name": {{T("Too Long", longJa)}}, "enter": { "en": "Only English.", "ja": "" }, "documents": [] }
              ]
            }
            """);
        var world = WorldWriter.Parse(json, 3, "test");
        Assert.Equal("昔…", world.Prologue!.Get("ja"));
        Assert.Equal(3, world.Floors.Count);   // 足りない階は空で埋める
        Assert.Equal("濡れた墓", world.Floors[0].Name!.Get("ja"));
        Assert.Equal(WorldWriter.DocumentsPerFloor, world.Floors[0].Documents.Count);   // 多すぎる書き付けは捨てる
        Assert.Null(world.Floors[1].Name);   // 長すぎる
        Assert.Null(world.Floors[1].Enter);   // 日本語が空
        Assert.Null(world.Floors[2].Name);
        Assert.Equal("test", world.Model);
    }

    [Fact]
    public async Task AFailedReplyGivesNoWorld()
    {
        var prompt = WorldWriter.Prompt(Content)!;
        var ai = new FakeAi(AiJsonReply.Fail("unauthorized"));
        var (world, error) = await WorldWriter.WriteAsync(ai, Data, prompt, 4, null, CancellationToken.None);
        Assert.Null(world);
        Assert.Equal("unauthorized", error);
        Assert.Equal(1, ai.Calls);
    }

    private static (GameSession Session, ExplorationState State) World()
    {
        var request = new GenerationRequest(77, 2, FloorSizeMode.Fixed(10, 10));
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(request).Dungeon;
        var session = GameSession.New(0, request, dungeon);
        var c = CharacterFactory.QuickCreate(Data, "Rowan", "human", "fighter", 3);
        session.Roster.Characters.Add(c);
        session.Roster.AddToParty(c);
        var state = new ExplorationState(dungeon, new PartyPosition(0, dungeon.Floors[0].Start, Direction.North));
        return (session, state);
    }

    private static string Read(GameSession s, ExplorationState state, GridPos at, ulong seed)
    {
        var ctx = new EventContext
        {
            Data = Data, Session = s, Maze = state, At = (0, at), Rng = new Pcg32(seed, 3), Depth = 0, TargetLevel = 2,
            Treasure = TreasureSettings.Load(Content),
        };
        var r = new EventRunner(Data.Events["world_writing"], ctx);
        var texts = new List<string>();
        for (int guard = 0; guard < 50; guard++)
        {
            var o = r.Next();
            if (o is EventDone) break;
            if (o is EventMessage m && m.Text != null) texts.Add(m.Text.Get("ja"));
        }
        return texts.Last();
    }

    [Fact]
    public void WorldDocShowsTheAiWritingOrTheBuiltInText()
    {
        var (s, state) = World();
        var at = new GridPos(2, 2);
        // AI を使っていない世界では、決まった文のどれか(同じ場所では毎回同じ)
        string builtIn = Read(s, state, at, 1);
        Assert.Equal(builtIn, Read(s, state, at, 9));
        s.World = new WorldText
        {
            Floors = [new WorldFloorText { Documents = [LocalizedText.Of("One", "一つ目の書き付け"), LocalizedText.Of("Two", "二つ目の書き付け")] }],
        };
        string written = Read(s, state, at, 1);
        Assert.Contains("書き付け", written);
        for (ulong seed = 2; seed < 6; seed++) Assert.Equal(written, Read(s, state, at, seed));
    }

    [Fact]
    public void TheWorldTextIsSavedWithTheGame()
    {
        using var dir = new TestPaths.TempDir();
        var store = new SaveStore(dir.Path);
        var (s, _) = World();
        s.World = new WorldText
        {
            Model = "test",
            Prologue = LocalizedText.Of("Long ago.", "昔のこと。"),
            Floors = [new WorldFloorText { Name = LocalizedText.Of("Wet Graves", "濡れた墓"), Documents = [LocalizedText.Of("a", "あ")] }],
        };
        store.Save(s, Content);
        var back = store.Load(s.ClanId, s.WorldKey).World!;
        Assert.Equal("昔のこと。", back.Prologue!.Get("ja"));
        Assert.Equal("濡れた墓", back.Floor(0)!.Name!.Get("ja"));
        Assert.Single(back.Floor(0)!.Documents);
        Assert.Null(back.Floor(5));
    }
}
