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

public partial class EventTests
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
    public void FacingAndTurningChooseWhatHappens()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);   // 北を向いている
        EventContext Turning() => new() { Data = Data, Session = s, Maze = state, At = (0, state.Position.Cell), Rng = new Pcg32(1, 3), Depth = 0.5, TargetLevel = 5, Treasure = TreasureSettings.Load(Content), Turned = true };
        Assert.True(EventRunner.Test([new EventCondition { Type = "facing", Id = "north" }], Ctx(s, state)));
        Assert.False(EventRunner.Test([new EventCondition { Type = "facing", Id = "east" }], Ctx(s, state)));
        Assert.False(EventRunner.Test([new EventCondition { Type = "turned" }], Ctx(s, state)));
        Assert.True(EventRunner.Test([new EventCondition { Type = "turned" }], Turning()));

        // 「振り向くな」: 着いただけなら書き付けだけ、そこで向きを変えると霊が出る
        Assert.True(Data.Events["dont_look_back"] is { OnEnter: true, OnTurn: true });
        Assert.DoesNotContain(Drive(Run("dont_look_back", s, state)), o => o is EventCombat);
        Assert.Contains(Drive(new EventRunner(Data.Events["dont_look_back"], Turning())), o => o is EventCombat);

        // 北を指す針: 北を向いていれば隠し物、ほかの向きなら何も無い(また来れば起こる)
        var nav = new MazeNavigator(state);
        nav.TurnRight();
        Assert.DoesNotContain(Drive(Run("lodestone_needle", s, state)), o => o is EventSound);
        nav.TurnLeft();
        Assert.Contains(Drive(Run("lodestone_needle", s, state)), o => o is EventSound { Id: "secret_found" });

        // 像の通路(見本): 像(東)を向いて進むと、試しがやり直しになる
        s.Flags.Add("gaze_ok");
        nav.TurnLeft();   // 西
        Drive(Run("gaze_watch", s, state));
        Assert.Contains("gaze_ok", s.Flags);
        nav.TurnAround();   // 東
        Drive(Run("gaze_watch", s, state));
        Assert.DoesNotContain("gaze_ok", s.Flags);
    }

    [Fact]
    public void AWorldNoteCanBeChosenByNumber()
    {
        // 同じ階の2か所に置く書き付けは、1つ目と2つ目を指定して別々のものを出す(抽選だと同じになることがある)
        var (s, state) = Room("lever_room.txt", 1, 1);
        s.World = new GravewardSpiral.Core.Ai.WorldText { Floors = [new() { Documents = [LocalizedText.Of("first", "一つ目"), LocalizedText.Of("second", "二つ目")] }] };
        string Shown(int value) => Drive(new EventRunner(new EventDef { Id = "w", Steps = [new EventStep { Type = "world_doc", Value = value }] }, Ctx(s, state)))
            .OfType<EventMessage>().Single().Text!.Get("en");
        Assert.Equal("first", Shown(1));
        Assert.Equal("second", Shown(2));
        Assert.Contains(Shown(0), new[] { "first", "second" });
    }

    [Fact]
    public void EndingStepClearsTheWorld()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        Assert.False(s.EndingDue(Data));
        var def = new EventDef { Id = "finale", Steps = [new EventStep { Type = "ending" }, new EventStep { Type = "gold", Value = 100 }] };
        Assert.Empty(EventCheck.Check(Data, def));
        var outs = Drive(new EventRunner(def, Ctx(s, state)));
        Assert.Contains(outs, o => o is EventToTown);
        Assert.True(s.EndingDue(Data));
        Assert.True(s.RecordClear(new PackManifest { Id = "puzzle" }));
        Assert.Equal(1, s.ClearedScenarios[ScenarioClears.Key(new PackManifest { Id = "puzzle" })]);
        Assert.False(s.EndingDue(Data));   // 一度クリアしたら、もう流さない
    }

    [Fact]
    public void FacingAndTriggerAreChecked()
    {
        var bad = new EventDef { Id = "x", Trigger = "look", Steps = [new EventStep { Type = "if", When = [new EventCondition { Type = "facing", Id = "up" }], Then = [new EventStep { Type = "end" }] }] };
        var problems = EventCheck.Check(Data, bad);
        Assert.Contains(problems, p => p.Contains("trigger 'look'"));
        Assert.Contains(problems, p => p.Contains("facing の id 'up'"));
    }

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

    private static EventDef Ev(string json) => System.Text.Json.JsonSerializer.Deserialize<EventDef>(json, ContentLibrary.JsonOptions)!;

    [Fact]
    public void PickTargetsOnlyTheChosenMember()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        foreach (var c in s.Roster.PartyMembers) c.Hp = 1;
        // 泉: 選んだ一人だけが治る。魔法使いは選べない
        var ev = Ev("""
            { "id": "spring", "steps": [{ "type": "pick", "text": { "en": "Who drinks?", "ja": "誰が飲む?" },
              "when": [{ "type": "class", "id": "wizard", "not": true }],
              "then": [{ "type": "heal" }], "else": [{ "type": "again" }] }] }
            """);
        var r = new EventRunner(ev, Ctx(s, state));
        var choice = Assert.IsType<EventChoice>(r.Next());
        Assert.Equal(6, choice.Labels.Count);   // 魔法使いを除く5人と「やめておく」
        r.Choose(1);   // 2人目(聖職者)
        while (r.Next() is not EventDone) { }
        var party = s.Roster.PartyMembers.ToList();
        Assert.Equal(party[1].MaxHp, party[1].Hp);
        Assert.All(party.Where((_, i) => i != 1), c => Assert.Equal(1, c.Hp));

        // くじ: 選ぶ画面を出さずに、一人だけがダメージを受ける
        foreach (var c in party) c.Hp = c.MaxHp;
        var trap = Ev("""{ "id": "dart", "steps": [{ "type": "pick", "value": 1, "then": [{ "type": "damage", "dice": "1d4" }] }] }""");
        Assert.DoesNotContain(Drive(new EventRunner(trap, Ctx(s, state))), o => o is EventChoice);
        Assert.Equal(5, party.Count(c => c.Hp == c.MaxHp));
    }

    [Fact]
    public void EquippedAndSpellSlotsAreChecked()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var fighter = s.Roster.PartyMembers.First(c => c.ClassIds.Contains("fighter"));
        var ctx = Ctx(s, state);
        var equipped = new EventCondition { Type = "equipped", Id = "ring_of_protection" };
        var has = new EventCondition { Type = "item", Id = "ring_of_protection" };
        fighter.Inventory.Items.Add(ItemInstance.Of("ring_of_protection"));
        Assert.True(EventRunner.Test([has], ctx));
        Assert.False(EventRunner.Test([equipped], ctx));   // 持っているだけでは、装備していない
        fighter.Inventory.Items.Clear();
        fighter.Inventory.Equipped[EquipSlot.Ring1] = ItemInstance.Of("ring_of_protection");
        Assert.True(EventRunner.Test([equipped], ctx));

        // 呪文スロット: 3 レベル以上を1つ捧げる。L5 の魔法使いと聖職者に 3 レベルが2つずつ
        Assert.True(EventRunner.Test([new EventCondition { Type = "slots", Value = 3 }], ctx));
        Assert.False(EventRunner.Test([new EventCondition { Type = "slots", Value = 4 }], ctx));
        var altar = Ev("""{ "id": "altar", "steps": [{ "type": "spend_slot", "value": 3, "then": [{ "type": "flag", "id": "altar_fed" }], "else": [{ "type": "flag", "id": "altar_hungry" }] }] }""");
        for (int i = 0; i < 4; i++) Drive(new EventRunner(altar, Ctx(s, state)));
        Assert.Contains("altar_fed", s.Flags);
        Assert.DoesNotContain("altar_hungry", s.Flags);
        Drive(new EventRunner(altar, Ctx(s, state)));   // 5回目は残っていない
        Assert.Contains("altar_hungry", s.Flags);
    }

    [Fact]
    public void GatesCloseAndWarpMovesTheParty()
    {
        var (s, state) = Room("lever_room.txt", 0, 1);
        var nav = new MazeNavigator(state);
        Drive(Run("lever", s, state), [0]);
        var close = Ev("""{ "id": "shut", "steps": [{ "type": "close", "id": "lever" }] }""");
        Assert.Contains(Drive(new EventRunner(close, Ctx(s, state))), o => o is EventMessage { Key: "event.gate_closed" });
        state.Position = new PartyPosition(0, new GridPos(2, 1), Direction.East);
        Assert.Equal(MoveResult.Locked, nav.Forward());

        // 着地点 dest:pool へ移す。イベントはそこで終わる(あとの手順はしない)
        state.Dungeon.Floors[0][new GridPos(3, 1)].Mark = "dest:pool";
        state.Position = new PartyPosition(0, new GridPos(1, 2), Direction.North);
        var warp = Ev("""{ "id": "fall", "steps": [{ "type": "warp", "id": "pool" }, { "type": "flag", "id": "after_warp" }] }""");
        var outputs = Drive(new EventRunner(warp, Ctx(s, state)));
        Assert.Contains(outputs, o => o is EventWarp { ChangedFloor: false });
        Assert.Equal(new GridPos(3, 1), state.Position.Cell);
        Assert.Equal(Direction.North, state.Position.Facing);
        Assert.DoesNotContain("after_warp", s.Flags);
    }

    [Fact]
    public void EnteredTellsHowThePartyStepped()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var back = new EventCondition { Type = "entered", Id = "back" };
        EventContext With(string? how) => new() { Data = Data, Session = s, Maze = state, At = (0, state.Position.Cell), Rng = new Pcg32(1, 3), Entered = how };
        Assert.True(EventRunner.Test([back], With("back")));
        Assert.False(EventRunner.Test([back], With("forward")));
        Assert.False(EventRunner.Test([back], With(null)));   // 階段やテレポーターで来た
        Assert.Empty(EventCheck.Check(Data, Ev("""{ "id": "e", "steps": [{ "type": "if", "when": [{ "type": "entered", "id": "left" }], "then": [] }] }""")));
        Assert.NotEmpty(EventCheck.Check(Data, Ev("""{ "id": "e", "steps": [{ "type": "if", "when": [{ "type": "entered", "id": "up" }], "then": [] }] }""")));
    }

    [Fact]
    public void ThePartysStateCanBeRead()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var party = s.Roster.PartyMembers.ToList();
        var ctx = Ctx(s, state);
        bool Test(string json) => EventRunner.Test([System.Text.Json.JsonSerializer.Deserialize<EventCondition>(json, ContentLibrary.JsonOptions)!], ctx);

        Assert.False(Test("""{ "type": "status", "id": "poisoned" }"""));
        party[2].Lasting.Add(new LastingCondition { Condition = Core.Effects.Condition.Poisoned });
        Assert.True(Test("""{ "type": "status", "id": "poisoned" }"""));

        Assert.False(Test("""{ "type": "hp", "op": "<=", "value": 50 }"""));
        party[0].Hp = party[0].MaxHp / 4;
        Assert.True(Test("""{ "type": "hp", "op": "<=", "value": 50 }"""));

        // 武器: 誰かが持っている。全員が外すと「誰も持っていない」(not)
        Assert.True(Test("""{ "type": "armed" }"""));
        foreach (var c in party) foreach (var slot in new[] { EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Ranged }) c.Inventory.Equipped.Remove(slot);
        Assert.True(Test("""{ "type": "armed", "not": true }"""));

        // 明かり: つける・消す
        Assert.False(Test("""{ "type": "lit" }"""));
        Drive(new EventRunner(Ev("""{ "id": "lamp", "steps": [{ "type": "light", "value": 30 }] }"""), Ctx(s, state)));
        Assert.True(Test("""{ "type": "lit" }"""));
        Assert.Contains(Drive(new EventRunner(Ev("""{ "id": "gust", "steps": [{ "type": "light", "value": 0 }] }"""), Ctx(s, state))), o => o is EventMessage { Key: "event.light_out" });
        Assert.False(Test("""{ "type": "lit" }"""));

        state.Dungeon.Floors[0][new GridPos(1, 1)].Zone = Zone.Dark;
        Assert.True(Test("""{ "type": "zone", "id": "dark" }"""));
        Assert.False(Test("""{ "type": "zone", "id": "fog" }"""));
    }

    [Fact]
    public void EventsCanFireOnCastingOrResting()
    {
        var ev = Ev("""{ "id": "statue", "trigger": "enter,cast", "steps": [] }""");
        Assert.True(ev is { OnEnter: true, OnCast: true, OnTurn: false, OnRest: false });
        Assert.True(Ev("""{ "id": "x", "trigger": "rest", "steps": [] }""") is { OnEnter: false, OnRest: true });
        Assert.Empty(EventCheck.Check(Data, ev).Where(p => p.Contains("trigger")));
        Assert.NotEmpty(EventCheck.Check(Data, Ev("""{ "id": "x", "trigger": "enter,dance", "steps": [] }""")).Where(p => p.Contains("trigger")));

        var (s, state) = Room("lever_room.txt", 1, 1);
        var light = new EventCondition { Type = "cast", Id = "light" };
        var any = new EventCondition { Type = "cast" };
        EventContext With(string? cast, bool rested = false) => new() { Data = Data, Session = s, Maze = state, At = (0, state.Position.Cell), Rng = new Pcg32(1, 3), Cast = cast, Rested = rested };
        Assert.True(EventRunner.Test([light], With("light")));
        Assert.False(EventRunner.Test([light], With("cure_wounds")));
        Assert.True(EventRunner.Test([any], With("cure_wounds")));
        Assert.False(EventRunner.Test([any], With(null)));   // 着いて起きた
        Assert.True(EventRunner.Test([new EventCondition { Type = "rested" }], With(null, rested: true)));
    }

    [Fact]
    public void MoneyInTextIsLiftedInEveryForm()
    {
        // 上の世界で引き上げるとき、文の中の金額も合わせる(AI が書く「金貨 30 枚」の形も)
        Assert.Equal("渡す: 120 G", EventRunner.LiftMoney("渡す: 30 G", 2));
        Assert.Equal("旅の餞別に金貨120枚を渡す", EventRunner.LiftMoney("旅の餞別に金貨30枚を渡す", 2));
        Assert.Equal("金貨 120 枚", EventRunner.LiftMoney("金貨 30 枚", 2));
        Assert.Equal("120 枚の金貨", EventRunner.LiftMoney("30 枚の金貨", 2));
        Assert.Equal("Give him 120 gold. Gold: 40 Gold", EventRunner.LiftMoney("Give him 30 gold. Gold: 10 Gold", 2));
        Assert.Equal("3 goldfish and 30 Goblins", EventRunner.LiftMoney("3 goldfish and 30 Goblins", 2));   // 金額でないものは変えない
        Assert.Equal(EventScale.Reward(30, 2), 120);
    }

    [Fact]
    public void StepTimersRunTheirEventWhenTimeIsUp()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var light = Ev("""{ "id": "lit_candle", "steps": [{ "type": "timer", "id": "candle_out", "value": 3 }] }""");
        Drive(new EventRunner(light, Ctx(s, state)));
        var running = new EventCondition { Type = "timer", Id = "candle_out" };
        Assert.True(EventRunner.Test([running], Ctx(s, state)));
        Assert.True(EventRunner.Test([new EventCondition { Type = "timer", Id = "candle_out", Op = "==", Value = 3 }], Ctx(s, state)));
        Assert.Empty(s.AdvanceTimers(2));
        Assert.Equal(["candle_out"], s.AdvanceTimers(1));   // 3 歩で時が来る(休みで一度に進んでも同じ)
        Assert.False(EventRunner.Test([running], Ctx(s, state)));

        // 止めると、時が来ても何も起こらない。セーブに残る
        Drive(new EventRunner(light, Ctx(s, state)));
        var data = SaveData.From(s, Content);
        Assert.Equal(3, data.Timers["candle_out"]);
        Drive(new EventRunner(Ev("""{ "id": "snuff", "steps": [{ "type": "timer_stop", "id": "candle_out" }] }"""), Ctx(s, state)));
        Assert.Empty(s.AdvanceTimers(10));

        // 時が来たら起こすイベントが無ければ、検査で分かる
        Assert.Contains(EventCheck.Check(Data, Ev("""{ "id": "x", "steps": [{ "type": "timer", "id": "no_such_event", "value": 5 }] }""")), p => p.Contains("no_such_event"));
        Assert.Empty(EventCheck.Check(Data, Ev("""{ "id": "x", "steps": [{ "type": "timer", "id": "lever", "value": 5 }] }""")));
    }

    [Fact]
    public void PartThreeRiteReadsOnlyTheNamesFound()
    {
        var content = ContentLibrary.Load(TestPaths.Content, Path.Combine(TestPaths.Content, "scenarios", "devourer"));
        var data = GameData.Load(content);
        string[] places = ["wall", "milo", "tags", "valdo", "pages", "rudolf", "alden"];
        string Read(IEnumerable<string> found)
        {
            var (s, state) = Room("lever_room.txt", 1, 1);
            foreach (var f in new[] { "dv_riine_met", "dv_bell_given", "dv_lullaby" }) s.Flags.Add(f);
            foreach (var p in found) { s.Flags.Add("dv_name_" + p); s.Counters["dv_names"] = s.Counters.GetValueOrDefault("dv_names") + 1; }
            var ctx = new EventContext { Data = data, Session = s, Maze = state, At = (0, state.Position.Cell), Rng = new Pcg32(1, 3), Depth = 1, TargetLevel = 13, Treasure = TreasureSettings.Load(content) };
            return string.Join("\n", Drive(new EventRunner(data.Events["dv_b10_rite"], ctx)).OfType<EventMessage>().Select(m => m.Text?.Get("en") ?? ""));
        }
        // 名の場所は 7 つ。B2 を飛ばして 6 つ集めたら、B2 の一行は読まない
        string six = Read(places.Where(p => p != "milo"));
        Assert.DoesNotContain("Milo", six);
        Assert.Contains("Rudolf", six);
        Assert.Contains("you who came before", six);
        string all = Read(places);
        Assert.Contains("Milo", all);
        Assert.DoesNotContain("you who came before", all);
        // 名の場所はどれも、数えるのと一緒に自分の印を立てる
        var json = File.ReadAllText(Path.Combine(TestPaths.Content, "scenarios", "devourer", "events.json"));
        foreach (var p in places) Assert.Contains("\"dv_name_" + p + "\"", json);
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

        var (s2, stele) = Room("a_b4_stele.txt", 1, 3, TestPaths.Gravekeeper);
        Drive(Run("a_b4_stele", s2, stele), [0]);
        Assert.False(stele.IsUnlocked(0, new GridPos(1, 2), Direction.South));   // 名前を知らない
        s2.Flags.Add("a_found_remains");
        var town = new EventContext { Data = StoryData, Session = s2, Rng = new Pcg32(1, 1) };
        Drive(new EventRunner(StoryData.Events["a_priest"], town));
        Assert.Contains("a_priest_name", s2.Flags);
        Drive(Run("a_b4_stele", s2, stele), [0]);   // オルド
        Assert.True(stele.IsUnlocked(0, new GridPos(1, 2), Direction.South));

        // 弔いの鐘: 二度では何も起こらず、墓掘りの老人に聞けば「三度」が先頭に出て、奥が開く
        var (s3, bell) = Room("a_b3_bell.txt", 1, 2, TestPaths.Gravekeeper);
        Drive(Run("a_b3_bell", s3, bell), [1]);   // 二度
        Assert.False(bell.IsUnlocked(0, new GridPos(1, 1), Direction.South));
        Drive(new EventRunner(StoryData.Events["a_gravedigger"], new EventContext { Data = StoryData, Session = s3, Rng = new Pcg32(1, 1) }));
        Assert.Contains("a_bell_lore", s3.Flags);
        Drive(Run("a_b3_bell", s3, bell), [0]);   // 教わったとおり三度
        Assert.True(bell.IsUnlocked(0, new GridPos(1, 1), Direction.South));
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

    [Fact]
    public void AnImportantItemNobodyCanCarryGoesToTheClanHouse()
    {
        // 荷物がいっぱいでも、大事な品(名指しでだけ出る品)は失わず、倉庫へ届く(レビューの E01)
        var (s, state) = Room("lever_room.txt", 1, 1);
        foreach (var c in s.Roster.PartyMembers)
            while (c.Inventory.TryAdd(Core.Items.ItemInstance.Of("longsword"), Data.Items)) { }
        var ev = System.Text.Json.JsonSerializer.Deserialize<EventDef>("""{ "id": "gift", "place": "maze", "once": "game", "steps": [{ "type": "give_item", "id": "bloodthirst" }] }""", ContentLibrary.JsonOptions)!;
        Assert.True(Data.Items["bloodthirst"].NoRandom);
        Drive(new EventRunner(ev, Ctx(s, state)));
        Assert.Contains(s.ClanHouse.Storage, i => i.ItemId == "bloodthirst");
    }

    [Fact]
    public void TakingAnItemAlsoTakesOneThatIsEquipped()
    {
        // 「持っているか」は装備も数えるので、取り上げるときも装備から取る(レビューの E03)
        var (s, state) = Room("lever_room.txt", 1, 1);
        foreach (var p in s.Roster.PartyMembers)   // 短剣は、1人が装備している1本だけにする
        {
            p.Inventory.Items.RemoveAll(i => i.ItemId == "dagger");
            foreach (var slot in p.Inventory.Equipped.Where(kv => kv.Value.ItemId == "dagger").Select(kv => kv.Key).ToList()) p.Inventory.Equipped.Remove(slot);
        }
        var c = s.Roster.PartyMembers.First();
        c.Inventory.Equipped[Core.Items.EquipSlot.MainHand] = Core.Items.ItemInstance.Of("dagger");
        var ev = System.Text.Json.JsonSerializer.Deserialize<EventDef>("""{ "id": "trade", "place": "maze", "when": [{ "type": "item", "id": "dagger" }], "steps": [{ "type": "take_item", "id": "dagger" }, { "type": "gold", "value": 10 }] }""", ContentLibrary.JsonOptions)!;
        Assert.True(EventRunner.Available(ev, Ctx(s, state)));
        Drive(new EventRunner(ev, Ctx(s, state)));
        Assert.DoesNotContain(s.Roster.PartyMembers.SelectMany(p => p.Inventory.Equipped.Values), i => i.ItemId == "dagger");
    }

    [Fact]
    public void TakingOneArrowFromAnEquippedBundleLeavesTheRest()
    {
        // 束で装備した矢から1本取り上げても、残りの19本は残る。「持っているか」も本数で数える(再レビューの R03)
        var (s, state) = Room("lever_room.txt", 1, 1);
        foreach (var p in s.Roster.PartyMembers)
        {
            p.Inventory.Items.RemoveAll(i => i.ItemId == "iron_arrows");
            foreach (var slot in p.Inventory.Equipped.Where(kv => kv.Value.ItemId == "iron_arrows").Select(kv => kv.Key).ToList()) p.Inventory.Equipped.Remove(slot);
        }
        var c = s.Roster.PartyMembers.First();
        c.Inventory.Equipped[Core.Items.EquipSlot.Ammo] = Core.Items.ItemInstance.Of("iron_arrows", 20);
        var ev = System.Text.Json.JsonSerializer.Deserialize<EventDef>("""{ "id": "toll", "place": "maze", "when": [{ "type": "item", "id": "iron_arrows", "count": 20 }], "steps": [{ "type": "take_item", "id": "iron_arrows", "count": 1 }] }""", ContentLibrary.JsonOptions)!;
        Assert.True(EventRunner.Available(ev, Ctx(s, state)));
        Drive(new EventRunner(ev, Ctx(s, state)));
        Assert.Equal(19, c.Inventory.Equipped[Core.Items.EquipSlot.Ammo].Count);
    }

    /// <summary>Step 6c: damage と condition の who。tag:arcane は魔法使いだけ、trigger は選んでいなければ隊列の先頭、party は選んだあとでも全員。</summary>
    [Fact]
    public void WhoChoosesWhoIsHurt()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var party = s.Roster.PartyMembers.ToList();
        void Reset() { foreach (var c in party) c.Hp = c.MaxHp; }
        List<string> Hurt(string json)
        {
            Reset();
            Drive(new EventRunner(Ev(json), Ctx(s, state)));
            return party.Where(c => c.Hp < c.MaxHp).Select(c => c.ClassId).ToList();
        }
        Assert.Equal(["wizard"], Hurt("""{ "id": "a", "steps": [{ "type": "damage", "dice": "1d4+1", "who": "tag:arcane" }] }"""));
        Assert.Equal(["cleric", "paladin", "ranger"], Hurt("""{ "id": "d", "steps": [{ "type": "damage", "dice": "1d4+1", "who": "tag:divine" }] }"""));
        Assert.Equal(["fighter"], Hurt("""{ "id": "t", "steps": [{ "type": "damage", "dice": "1d4+1", "who": "trigger" }] }"""));
        Assert.Equal(6, Hurt("""{ "id": "p", "steps": [{ "type": "pick", "value": 1, "then": [{ "type": "damage", "dice": "1d4+1", "who": "party" }] }] }""").Count);
    }

    /// <summary>Step 6c: イベントから罠を使う。死ぬかどうかは手順の lethal、罠の lethal、束の lethal の順で、どれにも無ければ HP 1 で止まる。</summary>
    [Fact]
    public void EventTrapsFollowTheLethalOrder()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var first = s.Roster.PartyMembers.First();
        LifeState After(string step)
        {
            first.Hp = 2;
            first.State = LifeState.Alive;
            Drive(new EventRunner(Ev($$"""{ "id": "x", "steps": [{{step}}] }"""), Ctx(s, state)));
            return first.State;
        }
        Assert.Equal(LifeState.Alive, After("""{ "type": "trap", "id": "crossbow_bolt" }"""));   // 束を通さず、罠にも無い: HP 1 で止まる
        Assert.Equal(1, first.Hp);
        Assert.Equal(LifeState.Dead, After("""{ "type": "trap", "id": "crossbow_bolt", "lethal": true }"""));
        var lines = new List<string>();
        first.Hp = 2;
        first.State = LifeState.Alive;
        foreach (var o in Drive(new EventRunner(Ev("""{ "id": "y", "steps": [{ "type": "trap", "id": "crossbow_bolt" }] }"""), Ctx(s, state))))
            if (o is EventMessage m) lines.Add(m.Key);
        Assert.Equal("event.sprung", lines[0]);
    }

    /// <summary>Step 6c: teleport はこの階のどこかへ飛ばし、alarm は敵を呼ぶ。どちらもイベントはそこで終わる。</summary>
    [Fact]
    public void TeleportAndAlarmEndTheEvent()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        long gold = s.Roster.PartyMembers.Sum(c => c.Gold);
        var outs = Drive(new EventRunner(Ev("""{ "id": "t", "steps": [{ "type": "teleport" }, { "type": "gold", "value": 500 }] }"""), Ctx(s, state, 7)));
        Assert.Contains(outs, o => o is EventWarp { ChangedFloor: false });
        Assert.False(state.Floor[state.Position.Cell.X, state.Position.Cell.Y].IsSolid);
        Assert.Equal(gold, s.Roster.PartyMembers.Sum(c => c.Gold));   // 飛ばしたあとの手順は行わない
        outs = Drive(new EventRunner(Ev("""{ "id": "a", "steps": [{ "type": "alarm" }, { "type": "gold", "value": 500 }] }"""), Ctx(s, state)));
        Assert.Contains(outs, o => o is EventAlarm);
        Assert.Equal(gold, s.Roster.PartyMembers.Sum(c => c.Gold));
    }

    [Fact]
    public void BadTrapStepsAreReported()
    {
        string all = string.Join("\n", Core.Events.EventCheck.Check(Data, Ev("""
            { "id": "b", "steps": [{ "type": "trap" }, { "type": "trap", "id": "no_such" }, { "type": "trap", "set": "no_set" },
              { "type": "damage", "who": "everyone" }, { "type": "condition", "id": "poisoned", "save": { "ability": "luck" } }] }
            """)));
        Assert.Contains("id(罠)か set(罠の束)のどちらか", all);
        Assert.Contains("traps に 'no_such'", all);
        Assert.Contains("trapsets に 'no_set'", all);
        Assert.Contains("who 'everyone'", all);
        Assert.Contains("save:", all);
    }
}
