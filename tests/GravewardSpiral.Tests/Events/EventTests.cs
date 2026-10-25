using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Events;

public class EventTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    /// <summary>第1部のシナリオを重ねたもの(物語のイベントと部屋)。</summary>
    private static readonly ContentLibrary StoryContent = ContentLibrary.Load(TestPaths.Content, TestPaths.Gravekeeper);
    private static readonly GameData StoryData = GameData.Load(StoryContent);

    /// <summary>はめ込む部屋を1階の迷宮にして、6人のパーティで立たせる。</summary>
    private static (GameSession Session, ExplorationState State) Room(string prefab, int x, int y, string? pack = null)
    {
        var floor = MazeTextFormat.Parse(File.ReadAllText(Path.Combine(pack ?? Path.Combine(TestPaths.Content, "srd"), "maps", "prefabs", prefab)), prefab);
        var dungeon = new Dungeon("t", [floor]);
        var session = GameSession.New(0, new GenerationRequest(1, 1, FloorSizeMode.Fixed(floor.Width, floor.Height)), dungeon);
        foreach (var cls in new[] { "fighter", "cleric", "rogue", "wizard", "paladin", "ranger" })
        {
            var c = CharacterFactory.QuickCreate(Data, cls, "human", cls, 5);
            session.Roster.Characters.Add(c);
            session.Roster.AddToParty(c);
        }
        var state = new ExplorationState(dungeon, new PartyPosition(0, new GridPos(x, y), Direction.North));
        return (session, state);
    }

    private static EventContext Ctx(GameSession s, ExplorationState state, ulong seed = 1) => new()
    {
        Data = Data,
        Session = s,
        Maze = state,
        At = (state.Position.Floor, state.Position.Cell),
        Rng = new Pcg32(seed, 3),
        Depth = 0.5,
        TargetLevel = 5,
        Treasure = TreasureSettings.Load(Content),
    };

    /// <summary>イベントを最後まで進める。選択は picks の順に選び、戦闘は wins の通りに終える。</summary>
    private static List<EventOutput> Drive(EventRunner r, int[]? picks = null, bool win = true)
    {
        var outputs = new List<EventOutput>();
        var queue = new Queue<int>(picks ?? []);
        for (int guard = 0; guard < 200; guard++)
        {
            var o = r.Next();
            outputs.Add(o);
            switch (o)
            {
                case EventDone: return outputs;
                case EventChoice: r.Choose(queue.Count > 0 ? queue.Dequeue() : 0); break;
                case EventCombat: r.CombatEnded(win); break;
            }
        }
        throw new InvalidOperationException("イベントが終わりません");
    }

    private static EventRunner Run(string id, GameSession s, ExplorationState state, ulong seed = 1) => new(Data.Events[id], Ctx(s, state, seed));

    [Fact]
    public void OnlyRepeatingEventsCanBeNoted()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var writing = Run("wall_writing", s, state);
        Drive(writing);
        Assert.True(writing.CanNote);
        var lever = Run("lever", s, state);
        Drive(lever, [1]);   // 引かずに立ち去る
        Assert.True(EventRunner.Available(Data.Events["lever"], Ctx(s, state)));   // また来れば起こる
        Assert.False(lever.CanNote);   // あとで済ませるものは書き留めさせない
        var whisper = Run("whisper", s, state);
        Drive(whisper);
        Assert.False(whisper.CanNote);
    }

    [Fact]
    public void WallWritingReadsTheSameAtTheSamePlace()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        string Read(ulong seed, EventContext? ctx = null) => string.Join("|", Drive(new EventRunner(Data.Events["wall_writing"], ctx ?? Ctx(s, state, seed)))
            .OfType<EventMessage>().Select(m => m.Text?.Get("ja")));
        var first = Read(1);
        for (ulong seed = 2; seed < 8; seed++) Assert.Equal(first, Read(seed));   // 来るたびに同じ
        var texts = new HashSet<string>();
        for (int x = 0; x < 12; x++)
            texts.Add(Read(1, new EventContext { Data = Data, Session = s, Maze = state, At = (0, new GridPos(x, 3)), Rng = new Pcg32(1, 3), Depth = 0.5, TargetLevel = 5, Treasure = TreasureSettings.Load(Content) }));
        Assert.True(texts.Count > 2);   // 場所が違えば、違う文
    }

    [Fact]
    public void NotingStopsOnlyThatPlaceInThatWorld()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var (_, other) = Room("lever_room.txt", 1, 1);   // 別の迷宮(別の世界)
        var writing = Data.Events["wall_writing"];
        Assert.Null(writing.Once);
        Drive(Run("wall_writing", s, state));
        Assert.True(EventRunner.Available(writing, Ctx(s, state)));   // 書き留めなければ、また起こる
        state.Notes.Add(new MazeNote { Floor = 0, Cell = new GridPos(1, 1), EventId = "wall_writing", Lines = [LocalizedText.Of("x")] });
        Assert.False(EventRunner.Available(writing, Ctx(s, state)));
        // 同じ迷宮のほかのマス、同じマスのほかのイベント、別の迷宮の同じ場所は、そのまま
        var elsewhere = new EventContext { Data = Data, Session = s, Maze = state, At = (0, new GridPos(2, 1)), Rng = new Pcg32(1, 3), Depth = 0.5, TargetLevel = 5, Treasure = TreasureSettings.Load(Content) };
        Assert.True(EventRunner.Available(writing, elsewhere));
        Assert.True(EventRunner.Available(Data.Events["whisper"], Ctx(s, state)));
        Assert.True(EventRunner.Available(writing, Ctx(s, other)));
    }

    [Fact]
    public void PackEventsPassTheCheck()
    {
        Assert.True(Content.IsValid, string.Join("\n", Content.Problems));
        foreach (var id in new[] { "wall_writing", "remains", "gravestone", "fountain", "lever", "riddle_door", "sealed_vault" })
            Assert.Contains(id, Data.Events.Keys);
    }

    [Fact]
    public void CheckFindsBrokenParts()
    {
        var bad = new EventDef
        {
            Id = "bad",
            Steps = [new EventStep { Type = "give_item", Id = "no_such_item" }, new EventStep { Type = "dance" },
                     new EventStep { Type = "choice", Options = [new EventOption { Steps = [new EventStep { Type = "damage", Dice = "2x6" }] }] }],
            When = [new EventCondition { Type = "flag" }],
        };
        var problems = EventCheck.Check(Data, bad);
        Assert.Contains(problems, p => p.Contains("no_such_item"));
        Assert.Contains(problems, p => p.Contains("dance"));
        Assert.Contains(problems, p => p.Contains("label"));
        Assert.Contains(problems, p => p.Contains("2x6"));
        Assert.Contains(problems, p => p.Contains("id がありません"));
    }

    [Fact]
    public void LeverOpensTheGateInTheSameRoom()
    {
        var (s, state) = Room("lever_room.txt", 0, 1);
        var nav = new MazeNavigator(state);
        state.Position = new PartyPosition(0, new GridPos(2, 1), Direction.East);
        Assert.Equal(MoveResult.Locked, nav.Forward());

        state.Position = new PartyPosition(0, new GridPos(0, 1), Direction.North);
        var outputs = Drive(Run("lever", s, state), [0]);
        Assert.Contains(outputs, o => o is EventMessage { Key: "event.gate_opened" });

        state.Position = new PartyPosition(0, new GridPos(2, 1), Direction.East);
        Assert.Equal(MoveResult.Moved, nav.Forward());
    }

    [Fact]
    public void RiddleDoorStopsAskingOnceAnswered()
    {
        var (s, state) = Room("riddle_door.txt", 1, 2);
        var riddle = Data.Events["riddle_door"];
        Drive(new EventRunner(riddle, Ctx(s, state)), [1]);   // 間違える
        Assert.True(EventRunner.Available(riddle, Ctx(s, state)));
        Drive(new EventRunner(riddle, Ctx(s, state)), [0]);   // 正しく答える
        Assert.True(state.IsUnlocked(0, new GridPos(1, 2), Direction.North));
        Assert.False(EventRunner.Available(riddle, Ctx(s, state)));
    }

    [Fact]
    public void TwinSwitchesNeedBoth()
    {
        var (s, state) = Room("twin_room.txt", 0, 0);
        Drive(Run("twin_switch_a", s, state), [0]);
        Assert.False(state.IsUnlocked(0, new GridPos(2, 0), Direction.South));
        Assert.False(EventRunner.Available(Data.Events["twin_switch_a"], Ctx(s, state)));   // 踏んだスイッチはもう動かない

        state.Position = new PartyPosition(0, new GridPos(4, 0), Direction.North);
        Drive(Run("twin_switch_b", s, state), [0]);
        Assert.True(state.IsUnlocked(0, new GridPos(2, 0), Direction.South));
    }

    [Fact]
    public void LeavingKeepsTheEventForLater()
    {
        var (s, state) = Room("lever_room.txt", 1, 2);
        var remains = Data.Events["remains"];
        Drive(new EventRunner(remains, Ctx(s, state)), [2]);   // 立ち去る
        Assert.True(EventRunner.Available(remains, Ctx(s, state)));
        Drive(new EventRunner(remains, Ctx(s, state)), [0]);   // 調べる
        Assert.False(EventRunner.Available(remains, Ctx(s, state)));
    }

    [Fact]
    public void SealedVaultNeedsAllThreeSigils()
    {
        var (s, state) = Room("sealed_vault.txt", 1, 2);
        var shrine = Run("sigil_shrine_sun", s, state);
        var outputs = Drive(shrine);
        Assert.Contains(outputs, o => o is EventCombat c && c.Groups.Count == 1);
        Assert.Contains(s.Flags, f => f == "done:sigil_shrine_sun");
        Assert.False(EventRunner.Available(Data.Events["sigil_shrine_sun"], Ctx(s, state)));

        Drive(Run("sealed_vault", s, state), [0]);
        Assert.False(state.IsUnlocked(0, new GridPos(1, 1), Direction.South));

        var party = s.Roster.PartyMembers.ToList();
        party[1].Inventory.TryAdd(ItemInstance.Of("sigil_moon"), Data.Items);
        party[2].Inventory.TryAdd(ItemInstance.Of("sigil_star"), Data.Items);
        Drive(Run("sealed_vault", s, state), [0]);
        Assert.True(state.IsUnlocked(0, new GridPos(1, 1), Direction.South));
        Assert.False(EventRunner.Available(Data.Events["sealed_vault"], Ctx(s, state)));   // 開けたら、もう聞いてこない
        Assert.DoesNotContain(party, c => c.Inventory.Items.Any(i => i.ItemId.StartsWith("sigil_")));
    }

    [Fact]
    public void FleeingDoesNotFinishAOnceEvent()
    {
        var (s, state) = Room("sigil_sun.txt", 1, 0);
        Drive(Run("sigil_shrine_sun", s, state), win: false);
        Assert.True(EventRunner.Available(Data.Events["sigil_shrine_sun"], Ctx(s, state)));
        Assert.DoesNotContain(s.Roster.PartyMembers, c => c.Inventory.Items.Any(i => i.ItemId == "sigil_sun"));
    }

    [Fact]
    public void ScatterPlacesEventsTheSameWayForTheSameSeed()
    {
        var settings = MazeGenSettings.Load(Content);
        string[] Marks(ulong seed)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 8, FloorSizeMode.Fixed(16, 16))).Dungeon;
            int placed = EventPlacer.Scatter(Data, dungeon, settings.EventsPerFloor, seed);
            Assert.True(placed >= dungeon.Floors.Count);
            return dungeon.Floors.SelectMany((f, i) => f.AllCells().Where(p => f[p].Mark?.StartsWith("event:") == true).Select(p => $"{i}:{p}:{f[p].Mark}")).ToArray();
        }
        Assert.Equal(Marks(5), Marks(5));
        Assert.NotEqual(Marks(5), Marks(6));
    }

    [Fact]
    public void EventScaleLiftsOnlyAboveTheBand()
    {
        Assert.Equal("4d6+6", EventScale.Dice("2d6+3", 2));
        Assert.Equal("1d6", EventScale.Dice("1d6", 1));
        Assert.Equal(400, EventScale.Reward(100, 2));
        Assert.Equal(-400, EventScale.Reward(-100, 2));
        var low = new EventDef { Id = "x", Scatter = new EventScatter { MinLevel = 1, MaxLevel = 6 } };
        Assert.Equal(1, EventScale.For(low, 5));
        Assert.Equal(2, EventScale.For(low, 12));
        Assert.Equal(3, EventScale.DcBonus(low, 12));
        Assert.Equal(20, EventScale.Dc(19, 3));
        Assert.Equal(1, EventScale.For(new EventDef { Id = "y", Scatter = new EventScatter { MinLevel = 10 } }, 30));   // 上限の無い帯はそのまま
    }

    [Fact]
    public void LowBandEventPaysMoreInAHighWorld()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var ev = new EventDef
        {
            Id = "lift_test", Place = "maze", Scatter = new EventScatter { MinLevel = 1, MaxLevel = 6 },
            Steps =
            [
                new EventStep { Type = "text", Text = Core.Content.LocalizedText.Of("A purse of 100 G.", "100 G の財布。") },
                new EventStep
                {
                    Type = "choice", Text = Core.Content.LocalizedText.Of("?"),
                    Options =
                    [
                        // 払う額も条件も引き上がる(100 G の条件 → 400 G)
                        new EventOption { Label = Core.Content.LocalizedText.Of("pay"), When = [new EventCondition { Type = "gold", Value = 100 }],
                            Steps = [new EventStep { Type = "gold", Value = -100 }] },
                        new EventOption { Label = Core.Content.LocalizedText.Of("take"), Steps = [new EventStep { Type = "gold", Value = 100 }] },
                    ],
                },
            ],
        };
        foreach (var c in s.Roster.PartyMembers) c.Gold = 0;
        s.Roster.PartyMembers.First().Gold = 300;   // 300 G では、引き上げた 400 G の条件に届かない
        var ctx = new EventContext { Data = Data, Session = s, Maze = state, Rng = new Pcg32(1, 1), TargetLevel = 12, Treasure = TreasureSettings.Load(Content) };
        var r = new EventRunner(ev, ctx);
        var said = Assert.IsType<EventMessage>(r.Next());
        Assert.Equal("400 G の財布。", said.Text!.Get("ja"));   // 文の中の額も引き上げる
        var choice = Assert.IsType<EventChoice>(r.Next());
        Assert.Single(choice.Labels);   // 払う選択肢は出ない
        r.Choose(0);
        Drive(r);
        Assert.Equal(300 + 400, s.Roster.PartyMembers.Sum(c => c.Gold));
    }

    [Fact]
    public void OnceEventsAppearOncePerWorld()
    {
        var settings = MazeGenSettings.Load(Content);
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 10, FloorSizeMode.Fixed(16, 16))).Dungeon;
            EventPlacer.Scatter(Data, dungeon, new IntRange(3, 3), seed);
            var ids = dungeon.Floors.SelectMany(f => f.AllCells().Select(p => f[p].Mark)).OfType<string>()
                .Where(m => m.StartsWith(EventPlacer.MarkPrefix)).Select(m => Data.Events[m[EventPlacer.MarkPrefix.Length..]]).ToList();
            foreach (var g in ids.Where(e => e.Once == "place" && e.Scatter is { Max: 0, Chain: null }).GroupBy(e => e.Id)) Assert.Single(g);   // はめ込んだ部屋のイベントは除く
        }
    }

    [Fact]
    public void SeenAndRareEventsAppearLessOften()
    {
        Assert.Equal(0.2, new EventScatter { Rarity = "rare" }.EffectiveWeight, 3);
        var settings = MazeGenSettings.Load(Content);
        int Count(IReadOnlySet<string>? seen, string id)
        {
            int n = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 6, FloorSizeMode.Fixed(14, 14))).Dungeon;
                EventPlacer.Scatter(Data, dungeon, new IntRange(3, 3), seed, null, seen);
                n += dungeon.Floors.Sum(f => f.AllCells().Count(p => f[p].Mark == EventPlacer.MarkPrefix + id));
            }
            return n;
        }
        // 前の世界で見たイベントは、出にくくなる(出なくはならない)
        Assert.True(Count(new HashSet<string> { "fountain" }, "fountain") < Count(null, "fountain"));
    }

    [Fact]
    public void FinishingAnEventRemembersItAndTheClanCarriesIt()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        Drive(new EventRunner(Data.Events["fountain"], Ctx(s, state)), [1]);   // 立ち去る(again)でも、見たことにはなる
        Assert.Contains("fountain", s.EventsSeen);
        var clan = ClanFile.From(s, "c", null);
        var next = GameSession.New(1, s.Request, s.Dungeon);
        clan.ApplyTo(next);
        Assert.Contains("fountain", next.EventsSeen);
    }

    [Fact]
    public void ChainsArePlacedWholeAndInOrder()
    {
        // 試しの組: 鍵を拾う(浅い階) → 扉(同じか深い階) → 宝(さらに同じか深い階)
        var data = GameData.Load(Content);
        EventDef Part(string id, int step, double minDepth = 0) => new()
        {
            Id = id, Place = "maze", Once = "place",
            Scatter = new EventScatter { Chain = "test_chain", Step = step, MinDepth = minDepth },
            Steps = [new EventStep { Type = "text", Text = Core.Content.LocalizedText.Of("x") }],
        };
        data.Events["tc_key"] = Part("tc_key", 1);
        data.Events["tc_door"] = Part("tc_door", 2, 0.3);
        data.Events["tc_hoard"] = Part("tc_hoard", 3);
        var settings = MazeGenSettings.Load(Content);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 6, FloorSizeMode.Fixed(14, 14))).Dungeon;
            var chains = EventPlacer.ScatterChains(data, dungeon, new IntRange(99, 99), seed);
            int FloorOf(string id) => dungeon.Floors.ToList().FindIndex(f => f.AllCells().Any(p => f[p].Mark == EventPlacer.MarkPrefix + id));
            int key = FloorOf("tc_key"), door = FloorOf("tc_door"), hoard = FloorOf("tc_hoard");
            if (!chains.Contains("test_chain"))
            {
                Assert.Equal((-1, -1, -1), (key, door, hoard));   // 途中までは置かない
                continue;
            }
            Assert.True(key >= 0 && key <= door && door <= hoard, $"seed {seed}: {key} {door} {hoard}");
            Assert.True(door >= 0.3 * (dungeon.Floors.Count - 1) - 1e-9);
        }
    }

    [Fact]
    public void AtLeastOneQuestChainIsPlacedWhenAsked()
    {
        var data = GameData.Load(Content);
        EventDef Part(string id, string chain, int step) => new()
        {
            Id = id, Place = "maze", Once = "place",
            Scatter = new EventScatter { Chain = chain, Step = step, Weight = chain == "tq" ? 0.001 : 100 },
            Steps = [new EventStep { Type = "text", Text = Core.Content.LocalizedText.Of("x") }],
        };
        data.Events["tq_a"] = Part("tq_a", "tq", 1);
        data.Events["tq_b"] = Part("tq_b", "tq", 2);
        data.Events["tm_a"] = Part("tm_a", "tm", 1);
        data.Events["tm_b"] = Part("tm_b", "tm", 2);
        data.Events["tq_town"] = new EventDef
        {
            Id = "tq_town", Place = "town", Title = Core.Content.LocalizedText.Of("q"),
            When = [new EventCondition { Type = "flag", Id = EventPlacer.ChainFlag("tq") }],
        };
        var settings = MazeGenSettings.Load(Content);
        var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(3, 6, FloorSizeMode.Fixed(14, 14))).Dungeon;
        // 依頼の組の重みが小さくても、依頼の組を1つは必ず置く(パックの依頼の組か、試しの組のどれか)
        var quests = EventPlacer.QuestChains(data);
        Assert.Contains("tq", quests);
        Assert.DoesNotContain("tm", quests);
        foreach (var e in data.Events.Values.Where(e => e.Scatter?.Chain is { } c && c != "tm")) e.Scatter!.Weight = 0.001;
        var placed = EventPlacer.ScatterChains(data, dungeon, new IntRange(1, 1), 3, minQuests: 1);
        Assert.Contains(placed, c => quests.Contains(c));
    }

    [Fact]
    public void PlacingEventsSetsTheChainFlag()
    {
        var settings = MazeGenSettings.Load(Content);
        var request = GenerationRequest.Default(settings, 9);
        var session = GameSession.New(0, request, new MazeGenerator(settings).Generate(request).Dungeon);
        session.PlaceEvents(Data, settings, Core.Combat.EncounterSettings.Load(Content));
        var placed = session.Dungeon.Floors.SelectMany(f => f.AllCells().Select(p => f[p].Mark)).OfType<string>()
            .Where(m => m.StartsWith(EventPlacer.MarkPrefix)).Select(m => Data.Events[m[EventPlacer.MarkPrefix.Length..]])
            .Where(e => e.Scatter?.Chain != null).Select(e => e.Scatter!.Chain!).Distinct();
        foreach (var c in placed) Assert.Contains(EventPlacer.ChainFlag(c), session.Flags);
    }

    [Fact]
    public void OnceRoomsAppearAtMostOncePerDungeon()
    {
        var settings = MazeGenSettings.Load(Content);
        int complete = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 10, FloorSizeMode.Fixed(20, 20))).Dungeon;
            int Count(string mark) => dungeon.Floors.Sum(f => f.AllCells().Count(p => f[p].Mark == mark));
            foreach (var m in new[] { "event:sigil_shrine_sun", "event:sigil_shrine_moon", "event:sigil_shrine_star", "event:sealed_vault", "event:twin_switch_a" })
                Assert.True(Count(m) <= 1, $"seed {seed}: {m} が {Count(m)} 個");
            if (Count("event:sigil_shrine_sun") + Count("event:sigil_shrine_moon") + Count("event:sigil_shrine_star") + Count("event:sealed_vault") == 4) complete++;
            Assert.True(MazeBot.Explore(dungeon).Ok);
        }
        Assert.True(complete > 0);
    }

    [Fact]
    public void CountersAndFlagsSurviveSaving()
    {
        using var dir = new TestPaths.TempDir();
        var (s, _) = Room("lever_room.txt", 0, 1);
        s.Counters["levers"] = 3;
        s.Flags.Add("twin_a");
        var store = new SaveStore(dir.Path);
        store.Save(s, Content);
        var back = store.Load(s.ClanId, s.WorldKey);
        Assert.Equal(3, back.Counters["levers"]);
        Assert.Contains("twin_a", back.Flags);
    }

    [Fact]
    public void StoryALanternAndNameOpenTheWay()
    {
        EventRunner Run(string id, GameSession s, ExplorationState state) => new(StoryData.Events[id], new EventContext
        {
            Data = StoryData, Session = s, Maze = state, At = (state.Position.Floor, state.Position.Cell), Rng = new Pcg32(1, 3),
            Depth = 0.5, TargetLevel = 5, Treasure = TreasureSettings.Load(StoryContent),
        });
        var (s, state) = Room("a_b5_seal.txt", 1, 2, TestPaths.Gravekeeper);
        Drive(Run("a_b5_seal", s, state), [0]);
        Assert.False(state.IsUnlocked(0, new GridPos(1, 1), Direction.South));   // 角灯が無いと開かない
        Drive(Run("a_b2_remains", s, state));
        Assert.Contains("a_found_remains", s.Flags);
        Drive(Run("a_b5_seal", s, state), [0]);
        Assert.True(state.IsUnlocked(0, new GridPos(1, 1), Direction.South));

        var (s2, stele) = Room("a_b4_stele.txt", 1, 2, TestPaths.Gravekeeper);
        Drive(Run("a_b4_stele", s2, stele), [0]);
        Assert.False(stele.IsUnlocked(0, new GridPos(1, 1), Direction.South));   // 名前を知らない
        s2.Flags.Add("a_found_remains");
        var town = new EventContext { Data = StoryData, Session = s2, Rng = new Pcg32(1, 1) };
        Drive(new EventRunner(StoryData.Events["a_priest"], town));
        Assert.Contains("a_priest_name", s2.Flags);
        Drive(Run("a_b4_stele", s2, stele), [0]);   // オルド
        Assert.True(stele.IsUnlocked(0, new GridPos(1, 1), Direction.South));
    }

    [Fact]
    public void TheFreeDungeonHasNoStoryRoomsButAMasterAtTheBottom()
    {
        var settings = MazeGenSettings.Load(Content);
        var request = GenerationRequest.Default(settings, 3);
        var dungeon = new MazeGenerator(settings).Generate(request).Dungeon;
        Assert.DoesNotContain(dungeon.Floors, f => f.AllCells().Any(p => f[p].Mark?.StartsWith("event:a_") == true));
        Assert.Contains(dungeon.Floors[^1].AllCells(), p => dungeon.Floors[^1][p].Mark == "boss:final");
        var session = GameSession.New(0, request, dungeon);
        session.BuildRosters(Data, EncounterSettings.Load(Content));
        var boss = session.Boss(Data, "final")!;
        Assert.True(Data.Bosses["final"].Auto);
        Assert.NotEmpty(boss.Groups);   // 世界のレベル帯から選んだ主
        Assert.All(boss.Groups, g => Assert.True(Data.Monsters.ContainsKey(g.Monster)));
        Assert.Equal(boss.Groups.Select(g => g.Monster), session.Boss(Data, "final")!.Groups.Select(g => g.Monster));
        // 第1部では、決まったボス(オルド)のまま
        Assert.False(StoryData.Bosses["final"].Auto);
        Assert.Equal("wraith", StoryData.Bosses["final"].Groups[0].Monster);
    }

    [Fact]
    public void ThePartOneDungeonIsFixedInTheScenario()
    {
        // シナリオの迷宮はシナリオの中身(生成したものを保存してある)。新しいゲームでは生成せずに、これを使う
        var dungeon = DungeonLoader.ScenarioDungeon(StoryContent)!;
        Assert.NotNull(dungeon);
        Assert.Null(DungeonLoader.ScenarioDungeon(Content));   // 自由な迷宮は毎回生成する
        Assert.Equal(6, dungeon.Floors.Count);
        Assert.Empty(dungeon.Validate());
        int FloorOf(string mark) => dungeon.Floors.ToList().FindIndex(f => f.AllCells().Any(p => f[p].Mark == mark));
        Assert.Equal(0, FloorOf("event:a_b1_footprints"));
        Assert.Equal(4, FloorOf("event:a_b5_seal"));
        Assert.Equal(5, FloorOf("boss:final"));
        Assert.True(MazeBot.Explore(dungeon).Ok);
        var request = DungeonLoader.RequestFor(dungeon, 99);
        Assert.Equal((99UL, 6), (request.Seed, request.Floors));
    }

    [Fact]
    public void StoryARoomsLandOnTheirFloors()
    {
        Assert.True(StoryContent.IsValid, string.Join("\n", StoryContent.Problems));
        var settings = MazeGenSettings.Load(StoryContent);
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(GenerationRequest.Default(settings, seed)).Dungeon;
            Assert.Equal(6, dungeon.Floors.Count);
            int FloorOf(string mark) => dungeon.Floors.ToList().FindIndex(f => f.AllCells().Any(p => f[p].Mark == mark));
            Assert.Equal(0, FloorOf("event:a_b1_footprints"));
            Assert.Equal(1, FloorOf("event:a_b2_remains"));
            Assert.Equal(2, FloorOf("event:a_b3_bell"));
            Assert.Equal(3, FloorOf("event:a_b4_stele"));
            Assert.Equal(4, FloorOf("event:a_b5_seal"));
            Assert.Equal(5, FloorOf("boss:final"));
            Assert.True(MazeBot.Explore(dungeon).Ok);
        }
    }
}
