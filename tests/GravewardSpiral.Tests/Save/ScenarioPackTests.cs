using System.Text.Json;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

public class ScenarioPackTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void EdgesStayMatchedOnBothSides()
    {
        var f = MazeEdit.Blank("t", 4, 3, open: false);
        Assert.Empty(f.Validate());
        var p = new GridPos(1, 1);
        Assert.Equal(EdgeType.Open, MazeEdit.CycleEdge(f, p, Direction.East));
        Assert.Equal(EdgeType.Open, f[2, 1][Direction.West]);
        MazeEdit.SetEdge(f, p, Direction.North, EdgeType.OneWayOut);
        Assert.Equal(EdgeType.OneWayIn, f[1, 0][Direction.South]);
        Assert.False(MazeEdit.SetEdge(f, new GridPos(0, 0), Direction.West, EdgeType.Door));   // 迷宮の端は壁のまま
        MazeEdit.SetEdge(f, new GridPos(2, 2), Direction.East, EdgeType.Door);
        MazeEdit.SetFeature(f, new GridPos(3, 2), CellFeature.Solid);   // 岩盤は四方を壁に
        Assert.Equal(EdgeType.Wall, f[2, 2][Direction.East]);
        Assert.True(f.Validate().Count == 0, string.Join("\n", f.Validate()));

        var open = MazeEdit.Blank("o", 5, 5);
        Assert.Empty(open.Validate());
        Assert.Equal(EdgeType.Open, open[2, 2][Direction.North]);
        Assert.Equal(EdgeType.Wall, open[0, 2][Direction.West]);
    }

    [Fact]
    public void AFloorCanBeResizedAndStaysConsistent()
    {
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(12, 1, FloorSizeMode.Fixed(16, 16))).Dungeon;
        var f = dungeon.Floors[0];
        var big = MazeEdit.Resize(f, 20, 18);
        Assert.Equal((20, 18), (big.Width, big.Height));
        Assert.Empty(big.Validate());
        Assert.Equal(MazeTextFormat.Write(f), MazeTextFormat.Write(MazeEdit.Resize(big, 16, 16)));   // 戻すと元のまま
        var small = MazeEdit.Resize(f, 4, 4);   // 四畳半
        Assert.Empty(small.Validate());
        Assert.True(small.InBounds(small.Start.X, small.Start.Y));
        Assert.All(small.Rooms, r => Assert.True(r.X + r.W <= 4 && r.Y + r.H <= 4));
    }

    [Fact]
    public void AScenarioWrittenByTheEditorLoadsBackAndPlays()
    {
        using var dir = new TestPaths.TempDir();
        var settings = MazeGenSettings.Load(Content);
        var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(77, 3, FloorSizeMode.Fixed(12, 12))).Dungeon;
        string folder = Path.Combine(dir.Path, "my_tale");
        var pack = ScenarioPack.New(folder, "my_tale", dungeon.Floors, "作者");
        pack.Manifest.Name = LocalizedText.Of("My Tale", "わたしの物語");
        pack.FirstLevel = 3;
        pack.LastLevel = 7;
        pack.Text.Prologue = LocalizedText.Of("Long ago.", "昔のこと。");
        pack.Text.Boss = new WorldBossText { Name = LocalizedText.Of("The Keeper", "番人"), Intro = LocalizedText.Of("It wakes.", "それが目を覚ます。") };
        pack.BossGroups = [new() { Monster = "wight" }, new() { Monster = "zombie", Count = 2 }];
        pack.Events.Add(JsonSerializer.Deserialize<EventDef>("""
            {"id": "my_well", "place": "maze", "steps": [{"type": "text", "text": {"en": "A dry well.", "ja": "涸れた井戸。"}}, {"type": "gold", "value": 10}]}
            """, ContentLibrary.JsonOptions)!);
        pack.Floors[0][pack.Floors[0].Start].Mark = EventPlacer.MarkPrefix + "my_well";
        pack.Save(Content);

        // 読み直すと同じ
        var problems = new List<string>();
        var back = ScenarioPack.Load(folder, problems);
        Assert.Empty(problems);
        Assert.Equal("わたしの物語", back.Manifest.Name!.Get("ja"));
        Assert.Equal([3, 7], back.Manifest.Levels);
        Assert.Equal(pack.Floors.Select(MazeTextFormat.Write), back.Floors.Select(MazeTextFormat.Write));
        Assert.Equal("my_well", Assert.Single(back.Events).Id);
        Assert.Equal("番人", back.Text.Boss!.Name!.Get("ja"));
        Assert.Equal(7, back.LastLevel);
        Assert.Equal(["wight", "zombie"], back.BossGroups!.Select(g => g.Monster));
        // イベントは空の項目を書かない
        Assert.DoesNotContain("\"when\"", File.ReadAllText(Path.Combine(folder, "events.json")));

        // ゲームの読み込みの検査を通り、イベント・主・物語・レベル帯がゲームに出る
        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        Assert.Empty(EventDraftCheck.Check(data, content,
            back.Events.Select(e => ("events.json", JsonSerializer.SerializeToElement(e, ContentLibrary.JsonOptions))), allowExisting: true, handPlaced: true).Problems);
        var loaded = DungeonLoader.ScenarioDungeon(content)!;
        Assert.True(ReachabilityValidator.Validate(loaded).Ok);
        var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, 5), loaded);
        WorldPackDef.Of(content)!.ApplyTo(s);
        var enc = EncounterSettings.Load(content);
        Assert.Equal(7, enc.LastLevel);
        s.BuildRosters(data, enc);
        var boss = s.Boss(data, GameSession.FinalBossId)!;
        var fighters = boss.Fighters(data.Monsters);
        Assert.Equal("番人", fighters[0].Def.Name.Get("ja"));   // 決まった組の主にも、物語の名前と文を当てる
        Assert.Equal(2, fighters[1].Count);
        Assert.Equal("それが目を覚ます。", boss.IntroText!.Get("ja"));
        Assert.Equal("昔のこと。", s.World!.Prologue!.Get("ja"));

        // zip にすると、置いたシナリオとして見つかる
        string scenarios = Path.Combine(dir.Path, "Scenarios");
        Directory.CreateDirectory(scenarios);
        back.ExportZip(Path.Combine(scenarios, "my_tale.zip"));
        var found = ScenarioCatalog.Find([scenarios], Path.Combine(dir.Path, "cache"));
        Assert.Equal("my_tale", Assert.Single(found).Id);
    }

    [Fact]
    public void EdgesArePaintedWithABrushAndOneWayFlips()
    {
        var f = MazeEdit.Blank("t", 4, 3, open: false);
        var p = new GridPos(1, 1);
        Assert.Equal(EdgeType.Door, MazeEdit.PaintEdge(f, p, Direction.East, EdgeType.Door));
        Assert.Equal(EdgeType.Door, MazeEdit.PaintEdge(f, p, Direction.East, EdgeType.Door));   // 同じ筆なら変わらない(回さない)
        Assert.Equal(EdgeType.OneWayOut, MazeEdit.PaintEdge(f, p, Direction.East, EdgeType.OneWayOut));
        Assert.Equal(EdgeType.OneWayIn, f[2, 1][Direction.West]);
        Assert.Equal(EdgeType.OneWayIn, MazeEdit.PaintEdge(f, p, Direction.East, EdgeType.OneWayOut));   // もう一度で向きが逆
        Assert.Equal(EdgeType.OneWayOut, f[2, 1][Direction.West]);
        Assert.Equal(EdgeType.OneWayIn, MazeEdit.PaintEdge(f, p, Direction.East, EdgeType.OneWayOut, flip: false));   // 押したまま続けるときは変えない
        Assert.Empty(f.Validate());
    }

    [Fact]
    public void BossesArePlacedFarFromTheArrivalAndKeptWithTheirTexts()
    {
        using var dir = new TestPaths.TempDir();
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(31, 3, FloorSizeMode.Fixed(12, 12))).Dungeon;
        string folder = Path.Combine(dir.Path, "lair");
        var pack = ScenarioPack.New(folder, "lair", dungeon.Floors);
        foreach (var f in pack.Floors)
            foreach (var c in f.AllCells().Where(c => f[c].Mark?.StartsWith("boss:") == true)) f[c].Mark = null;

        // 一番遠い空いたマス: 着く場所から歩いて行けて、ほかのどの空いたマスより遠い
        var last = pack.Floors[^1];
        var at = MazeEdit.PlaceMarkFar(last, "boss:final")!.Value;
        Assert.Equal((2, at), MazeEdit.FindMark(pack.Floors, "boss:final"));
        Assert.Equal(CellFeature.None, last[at].Feature);
        Assert.NotEqual(last.Start, at);

        // 途中の階のボス(文は文言のキーに)
        pack.Bosses.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "gate_troll",
            ["name"] = new System.Text.Json.Nodes.JsonObject { ["en"] = "Gate Troll", ["ja"] = "門のトロル" },
            ["groups"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["monster"] = "troll", ["count"] = 1, ["name"] = new System.Text.Json.Nodes.JsonObject { ["en"] = "Gate Troll", ["ja"] = "門のトロル" } }),
            ["intro"] = "boss.gate_troll.intro",
            ["reward_flag"] = "troll_down",
        });
        pack.Texts["ja"] = new() { ["boss.gate_troll.intro"] = "トロルが門をふさいでいる。" };
        pack.Texts["en"] = new() { ["boss.gate_troll.intro"] = "A troll blocks the gate." };
        Assert.NotNull(MazeEdit.PlaceMarkFar(pack.Floors[1], "boss:gate_troll"));
        pack.Save(Content);

        var back = ScenarioPack.Load(folder, []);
        Assert.Equal("gate_troll", Assert.Single(back.Bosses)["id"]!.GetValue<string>());
        Assert.Equal(1, MazeEdit.FindMark(back.Floors, "boss:gate_troll")!.Value.Floor);
        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        var loaded = DungeonLoader.ScenarioDungeon(content)!;
        var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, 3), loaded);
        var troll = s.Boss(data, "gate_troll")!;
        Assert.Equal("門のトロル", troll.Fighters(data.Monsters)[0].Def.Name.Get("ja"));
        Assert.Equal("トロルが門をふさいでいる。", content.Texts("ja")[troll.Intro!]);
        Assert.Equal("troll_down", troll.RewardFlag);

        // 外すと、どの階からも消える
        Assert.Equal(1, MazeEdit.RemoveMark(back.Floors, "boss:gate_troll"));
        Assert.Null(MazeEdit.FindMark(back.Floors, "boss:gate_troll"));
    }

    [Fact]
    public void ScenarioRumorsAreHeardOnlyWhenTheirConditionsHold()
    {
        using var dir = new TestPaths.TempDir();
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(5, 2, FloorSizeMode.Fixed(10, 10))).Dungeon;
        string folder = Path.Combine(dir.Path, "talk");
        var pack = ScenarioPack.New(folder, "talk", dungeon.Floors);
        pack.Rumors.Add(new RumorDef { Id = "always", Text = LocalizedText.Of("The well is deep.", "井戸は深い。") });
        pack.Rumors.Add(new RumorDef { Id = "after", Text = LocalizedText.Of("The gate is open now.", "門が開いたそうだ。"), When = [new EventCondition { Type = "flag", Id = "gate_open" }] });
        pack.Save(Content);
        Assert.Equal(["always", "after"], ScenarioPack.Load(folder, []).Rumors.Select(r => r.Id));

        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        var loaded = DungeonLoader.ScenarioDungeon(content)!;
        var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, 2), loaded);
        var ctx = new EventContext { Data = data, Session = s, Rng = new Core.Rng.Pcg32(1, 1) };
        Assert.Equal(["always"], RumorDef.Available(data, ctx).Select(r => r.Id));
        s.Flags.Add("gate_open");
        Assert.Equal(["after", "always"], RumorDef.Available(data, ctx).Select(r => r.Id));

        // 文が片方の言語で空なら、読み込みの問題
        pack.Rumors.Add(new RumorDef { Id = "bad", Text = LocalizedText.Of("Only English.") });
        pack.Save(Content);
        var bad = ContentLibrary.Load(TestPaths.Content, folder);
        GameData.Load(bad);
        Assert.Contains(bad.Problems, p => p.Contains("rumors 'bad'"));
    }

    [Fact]
    public void AFilledScenarioIsTheSameWorldInEveryNewGame()
    {
        using var dir = new TestPaths.TempDir();
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(21, 4, FloorSizeMode.Fixed(14, 14))).Dungeon;
        string folder = Path.Combine(dir.Path, "made");
        var pack = ScenarioPack.New(folder, "made", dungeon.Floors);
        pack.Save(Content);
        var data = GameData.Load(ContentLibrary.Load(TestPaths.Content, folder));
        Assert.Equal(ScenarioFill.Parts.All, ScenarioFill.Fill(pack, data, Content, 99, ScenarioFill.Parts.All));
        Assert.All(Enumerable.Range(0, 4), i => Assert.NotEmpty(pack.Rosters[i]));
        Assert.NotNull(pack.Named);
        Assert.True(pack.EventsPlaced);
        Assert.Equal(ScenarioFill.Parts.None, ScenarioFill.Fill(pack, data, Content, 5, ScenarioFill.Parts.All));   // 決めたところは埋め直さない
        pack.Save(Content);

        (List<List<string>> Rosters, string Named, string Marks) NewGame(ulong seed)
        {
            var content = ContentLibrary.Load(TestPaths.Content, folder);
            var d = GameData.Load(content);
            var loaded = DungeonLoader.ScenarioDungeon(content)!;
            var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, seed), loaded);
            var enc = EncounterSettings.Load(content);
            s.BuildRosters(d, enc);
            s.ApplyScenario(d, content, enc, MazeGenSettings.Load(content));
            string marks = string.Join(",", loaded.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>());
            return (s.FloorRosters, string.Join(",", s.Named.Select(n => $"{n.Id}@{n.Floor}")), marks);
        }
        var a = NewGame(1);
        var b = NewGame(12345);
        Assert.Equal(a.Rosters, b.Rosters);   // 遊ぶたびに選び直さない
        Assert.Equal(a.Named, b.Named);
        Assert.Equal(a.Marks, b.Marks);
        Assert.Equal(pack.Rosters.Take(4), a.Rosters);

        // 選び直すと変わる(イベントは、散らす印を消して置き直す)
        var before = string.Join(",", pack.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>());
        ScenarioFill.Fill(pack, data, Content, 7, ScenarioFill.Parts.Events, redo: true);
        Assert.NotEqual(before, string.Join(",", pack.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()));
    }

    [Fact]
    public void AScenarioCanKeepTheStandardEventsOut()
    {
        using var dir = new TestPaths.TempDir();
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(8, 3, FloorSizeMode.Fixed(14, 14))).Dungeon;
        string folder = Path.Combine(dir.Path, "quiet");
        var pack = ScenarioPack.New(folder, "quiet", dungeon.Floors);
        foreach (var f in pack.Floors)
            foreach (var c in f.AllCells().Where(c => f[c].Mark?.StartsWith(EventPlacer.MarkPrefix) == true)) f[c].Mark = null;
        pack.Events.Add(new EventDef
        {
            Id = "quiet_well", Once = "place", Scatter = new EventScatter { Weight = 1 },
            Steps = [new EventStep { Type = "text", Text = LocalizedText.Of("A well.", "井戸がある。") }],
        });
        List<string> Placed(bool standard)
        {
            pack.Manifest.StandardEvents = standard;
            pack.Save(Content);
            var content = ContentLibrary.Load(TestPaths.Content, folder);
            var data = GameData.Load(content);
            var loaded = DungeonLoader.ScenarioDungeon(content)!;
            var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, 3) with { Seed = 11 }, loaded);
            s.BuildRosters(data, EncounterSettings.Load(content));
            s.PlaceEvents(data, MazeGenSettings.Load(content), EncounterSettings.Load(content), EventPlacer.ScenarioOnly(content));
            return loaded.Floors.SelectMany(f => f.AllCells().Select(c => f[c].Mark)).OfType<string>()
                .Where(m => m.StartsWith(EventPlacer.MarkPrefix)).Select(m => m[EventPlacer.MarkPrefix.Length..]).Distinct().ToList();
        }
        Assert.Contains(Placed(true), id => id != "quiet_well");   // 混ぜると、標準のイベントも置かれる
        Assert.Equal(["quiet_well"], Placed(false));   // 混ぜなければ、このシナリオのイベントだけ
    }

    [Fact]
    public void ARosterMixesFixedMonstersWithKindsDrawnPerWorld()
    {
        var enc = EncounterSettings.Load(Content);
        string[] spec = ["type:undead:3", "type:beast:2", "goblin"];
        List<string> Draw(ulong seed) => RosterSpec.Resolve(Data, enc, 0.3, 6, spec, new Core.Rng.Pcg32(seed, 1));
        var a = Draw(1);
        Assert.Contains("goblin", a);   // 決めた敵は必ず入る
        Assert.Equal(3, a.Count(id => Data.Monsters[id].CreatureType == "undead"));
        Assert.Equal(2, a.Count(id => Data.Monsters[id].CreatureType == "beast"));
        Assert.Equal(a.Count, a.Distinct().Count());
        var fit = FloorAdvisor.Monsters(Data, enc, 0.3, 6).Select(x => x.Monster.Id).ToHashSet();
        Assert.All(a.Where(id => id != "goblin"), id => Assert.Contains(id, fit));   // 種類から選ぶのは、その階に合う敵だけ
        Assert.Contains(Enumerable.Range(2, 20), s => !Draw((ulong)s).Order().SequenceEqual(a.Order()));   // 世界ごとに変わる
        Assert.True(RosterSpec.IsType(RosterSpec.Type("undead", 4), out string t, out int n) && t == "undead" && n == 4);
    }

    [Fact]
    public void FixedFloorRostersReplaceTheDrawOnlyWhereWritten()
    {
        using var dir = new TestPaths.TempDir();
        var settings = MazeGenSettings.Load(Content);
        var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(9, 3, FloorSizeMode.Fixed(10, 10))).Dungeon;
        string folder = Path.Combine(dir.Path, "zoo");
        var pack = ScenarioPack.New(folder, "zoo", dungeon.Floors);
        pack.RosterOf(1).AddRange(["goblin", "wolf"]);   // B2 だけ決める
        pack.Save(Content);
        var back = ScenarioPack.Load(folder, []);
        Assert.Empty(back.Rosters[0]);
        Assert.Equal(["goblin", "wolf"], back.Rosters[1]);

        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        var loaded = DungeonLoader.ScenarioDungeon(content)!;
        var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, 3), loaded);
        s.BuildRosters(data, EncounterSettings.Load(content));
        var auto0 = s.FloorRosters[0].ToList();
        s.ApplyRosters(data, EncounterSettings.Load(content), DungeonLoader.ScenarioRosters(content));
        Assert.Equal(["goblin", "wolf"], s.FloorRosters[1]);
        Assert.Equal(auto0, s.FloorRosters[0]);   // 書かない階は自動のまま
        Assert.NotEmpty(s.FloorRosters[2]);

        // 無いモンスターは読み込みの問題
        back.RosterOf(0).Add("no_such_monster");
        back.Save(Content);
        var bad = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(bad);
        Assert.Contains(bad.Problems, p => p.Contains("no_such_monster"));
    }

    [Fact]
    public void DataAndTextsWrittenByTheEditorWorkInTheGame()
    {
        using var dir = new TestPaths.TempDir();
        var dungeon = new MazeGenerator(MazeGenSettings.Load(Content)).Generate(new GenerationRequest(4, 2, FloorSizeMode.Fixed(10, 10))).Dungeon;
        string folder = Path.Combine(dir.Path, "kit");
        var pack = ScenarioPack.New(folder, "kit", dungeon.Floors);
        pack.Items.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id": "kit_brand", "extends": "longsword", "name": {"en": "Brand", "ja": "焼き印の剣"}}""")!.AsObject());
        pack.Monsters.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id": "kit_ghoul", "extends": "ghoul", "name": {"en": "Pale One", "ja": "青白いもの"}}""")!.AsObject());
        pack.Spells.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id": "fire_bolt", "name": {"en": "Ember Dart", "ja": "熾火の矢"}}""")!.AsObject());
        pack.Texts["ja"] = new() { ["kit.sign"] = "看板がある。" };
        pack.Texts["en"] = new() { ["kit.sign"] = "A sign." };
        pack.RosterOf(0).Add("kit_ghoul");
        pack.Save(Content);

        var back = ScenarioPack.Load(folder, []);
        Assert.Equal("kit_brand", (string?)Assert.Single(back.Items)["id"]);
        Assert.Equal("ghoul", (string?)Assert.Single(back.Monsters)["extends"]);
        Assert.Equal("看板がある。", back.Texts["ja"]["kit.sign"]);

        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        var data = GameData.Load(content);
        Assert.Equal("焼き印の剣", data.Items["kit_brand"].Name.Get("ja"));
        Assert.Equal("kit", data.Items["kit_brand"].OriginScenario);   // オリジナルの品
        Assert.Equal("青白いもの", data.Monsters["kit_ghoul"].Name.Get("ja"));
        Assert.Equal("ghoul", data.Monsters["kit_ghoul"].ArtFrom);   // 写したモンスターは元の絵を使う
        Assert.Equal(data.Monsters["ghoul"].Cr, data.Monsters["kit_ghoul"].Cr);
        Assert.Equal("熾火の矢", data.Spells["fire_bolt"].Name.Get("ja"));
        Assert.Equal("看板がある。", content.Texts("ja")["kit.sign"]);
    }

    [Fact]
    public void PartOneCanBeSavedAsAnotherScenarioWithoutLosingAnything()
    {
        using var dir = new TestPaths.TempDir();
        var problems = new List<string>();
        var part1 = ScenarioPack.Load(TestPaths.Gravekeeper, problems);
        Assert.Empty(problems);
        string folder = Path.Combine(dir.Path, "my_gk");
        part1.SaveAs(folder, "my_gk", Content);

        // 元のパック(リポジトリの第1部)は触っていない
        Assert.Equal(TestPaths.Gravekeeper, ScenarioPack.Load(TestPaths.Gravekeeper, []).Directory);
        Assert.False(File.Exists(Path.Combine(folder, "maps", "gravekeeper_b1.txt")));   // 迷宮は新しい名前に付け替えた
        Assert.True(File.Exists(Path.Combine(folder, "maps", "my_gk_b1.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "prefabs.json")));   // エディタが触らないものも写した
        Assert.True(File.Exists(Path.Combine(folder, "worlds.json")));   // 決まった物語の文(序文と最下層の主)も写した

        var content = ContentLibrary.Load(TestPaths.Content, folder);
        DungeonLoader.ValidateAll(content);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        Assert.Equal("my_gk", content.Scenario!.Id);
        Assert.NotEqual("6f1c0e2a-9d4b-4b8e-9a51-2f7d3c8e4a10", content.Scenario.Guid);   // 別のシナリオ
        var data = GameData.Load(content);
        var original = GameData.Load(ContentLibrary.Load(TestPaths.Content, TestPaths.Gravekeeper));
        Assert.Equal(original.Events.Count, data.Events.Count);
        // オルドの名前と文、序文は、エディタが書くのと同じ worlds.json にある(新しいゲームで世界の文になる)
        var copy = ScenarioPack.Load(folder, []);
        Assert.Equal("最初の墓守オルド", copy.Text.Boss!.Name!.Get("ja"));
        Assert.StartsWith("町は、地の底へ", copy.Text.Prologue!.Get("ja"));
        var loaded = DungeonLoader.ScenarioDungeon(content)!;
        var s = GameSession.New(0, DungeonLoader.RequestFor(loaded, 6), loaded);
        WorldPackDef.Of(content)!.ApplyTo(s);
        var boss = s.Boss(data, GameSession.FinalBossId)!;
        Assert.Equal("最初の墓守オルド", boss.Fighters(data.Monsters)[0].Def.Name.Get("ja"));
        Assert.Equal("付き従う墓守", boss.Fighters(data.Monsters)[1].Def.Name.Get("ja"));   // 従う者の名前は組に
        Assert.StartsWith("祭壇の上に", boss.IntroText!.Get("ja"));
        Assert.Equal("has_spiral_heart", boss.RewardFlag);
    }

    [Fact]
    public void DraftCheckCatchesMistakes()
    {
        var drafts = new[]
        {
            ("d", JsonDocument.Parse("""{"id": "bad_field", "place": "maze", "scatter": {}, "stepz": []}""").RootElement),
            ("d", JsonDocument.Parse("""{"id": "no_scatter", "place": "maze", "steps": [{"type": "text", "text": {"en": "a", "ja": "あ"}}]}""").RootElement),
        };
        var r = EventDraftCheck.Check(Data, Content, drafts);
        Assert.Contains(r.Problems, p => p.Contains("bad_field"));
        Assert.Contains(r.Problems, p => p.Contains("no_scatter") && p.Contains("scatter"));
        // 手で置くイベント(エディタ)は scatter が無くてもよい
        Assert.DoesNotContain(EventDraftCheck.Check(Data, Content, drafts, handPlaced: true).Problems, p => p.Contains("no_scatter"));
    }
}
