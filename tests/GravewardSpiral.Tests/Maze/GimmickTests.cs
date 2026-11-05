using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Items;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Tests.Maze;

public class GimmickTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    private const string Locked = """
        floor lock_test
        size 3 1
        wrap none
        start 0 0 E
        walls
        +-+-+-+
        |uL  e|
        +-+-+-+
        mark 2 0 key
        mark 2 0 message:maze.nothing
        """;

    [Fact]
    public void LockedDoorsBlockUntilUnlockedAndRoundTrip()
    {
        var floor = MazeTextFormat.Parse(Locked);
        Assert.Equal(EdgeType.Locked, floor[0, 0][Direction.East]);
        Assert.Equal(CellFeature.Elevator, floor[2, 0].Feature);
        Assert.Equal("message:maze.nothing", floor[2, 0].Mark);   // 後から書いた印が勝つ
        var again = MazeTextFormat.Parse(MazeTextFormat.Write(floor));
        Assert.Equal(EdgeType.Locked, again[1, 0][Direction.West]);

        var state = new ExplorationState(new Dungeon("t", [floor]), new PartyPosition(0, new GridPos(0, 0), Direction.East));
        var nav = new MazeNavigator(state);
        Assert.Equal(MoveResult.Locked, nav.Forward());
        state.Unlock(0, new GridPos(0, 0), Direction.East);
        Assert.Equal(EdgeType.Door, state.Apparent(0, new GridPos(1, 0), Direction.West));
        Assert.Equal(MoveResult.Moved, nav.Forward());
    }

    /// <summary>テレポーターで飛ばされると、地図にその行き先を覚える(同じ階は Floor なし、別の階は階の番号)。霧の中では覚えない。</summary>
    [Fact]
    public void TeleportsRememberWhereTheyLed()
    {
        var b1 = MazeTextFormat.Parse("""
            floor tp_b1
            size 4 1
            wrap none
            start 0 0 E
            walls
            +-+-+-+-+
            |  t t  |
            +-+-+-+-+
            teleport 1 0 3 0
            teleport 2 0 0 0 B2
            """);
        var b2 = MazeTextFormat.Parse("""
            floor tp_b2
            size 2 1
            wrap none
            start 0 0 E
            walls
            +-+-+
            |   |
            +-+-+
            """);
        var state = new ExplorationState(new Dungeon("t", [b1, b2]), new PartyPosition(0, new GridPos(0, 0), Direction.East));
        var nav = new MazeNavigator(state);
        Assert.Equal(MoveResult.Teleported, nav.Forward());
        Assert.Equal(new WarpTarget(null, new GridPos(3, 0)), state.AutoMap.For(0).Teleports[new GridPos(1, 0)]);
        Assert.Contains(new GridPos(3, 0), state.AutoMap.ArrivalsOn(0));
        state.Position = new PartyPosition(0, new GridPos(1, 0), Direction.East);
        Assert.Equal(MoveResult.Teleported, nav.Forward());
        Assert.Equal(new WarpTarget(1, new GridPos(0, 0)), state.AutoMap.For(0).Teleports[new GridPos(2, 0)]);
        Assert.Equal([new GridPos(0, 0)], state.AutoMap.ArrivalsOn(1));
    }

    [Fact]
    public void GeneratedKeysAreBeforeTheirLocks()
    {
        var settings = MazeGenSettings.Load(Content);
        int locks = 0, elevators = 0;
        for (ulong seed = 1; seed <= 15; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 8, FloorSizeMode.Fixed(16, 16))).Dungeon;
            foreach (var f in dungeon.Floors)
            {
                elevators += f.AllCells().Count(p => f[p].Feature == CellFeature.Elevator);
                // イベントで開く扉(印 gate:)は鍵と関係ないので数えない
                bool Gate(GridPos p, Direction d) => f[p].Mark?.StartsWith("gate:") == true || f.Neighbor(p, d) is { } n && f[n].Mark?.StartsWith("gate:") == true;
                if (!f.AllCells().Any(p => DirectionExtensions.All.Any(d => f[p][d] == EdgeType.Locked && !Gate(p, d)))) continue;
                locks++;
                // 上り階段から、錠を通らずに鍵へ行ける
                var seen = new HashSet<GridPos> { f.Start };
                var queue = new Queue<GridPos>([f.Start]);
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    foreach (var d in DirectionExtensions.All)
                    {
                        var e = f[p][d];
                        if (e is EdgeType.Wall or EdgeType.Locked or EdgeType.OneWayIn) continue;
                        if (f.Neighbor(p, d) is { } n && !f[n].IsSolid && seen.Add(n)) queue.Enqueue(n);
                    }
                }
                Assert.Contains(f.AllCells().Where(p => f[p].Mark == "key"), seen.Contains);
            }
            Assert.Contains(dungeon.Floors[^1].AllCells(), p => dungeon.Floors[^1][p].Mark == "boss:final");   // 最下層にボスのねぐら
        }
        Assert.True(locks > 0);
        Assert.True(elevators > 0);
    }

    [Fact]
    public void BotWalksEveryGeneratedDungeon()
    {
        var settings = MazeGenSettings.Load(Content);
        for (ulong seed = 100; seed < 110; seed++)
        {
            var dungeon = new MazeGenerator(settings).Generate(new GenerationRequest(seed, 10, FloorSizeMode.Fixed(16, 16))).Dungeon;
            var r = MazeBot.Explore(dungeon);
            Assert.True(r.Ok, $"seed {seed}: {string.Join(" / ", r.Problems)}");
        }
    }

    [Fact]
    public void ElevatorsMoveBetweenFloorsOfTheGroup()
    {
        var a = MazeTextFormat.Parse(Locked);
        var b = MazeTextFormat.Parse(Locked.Replace("lock_test", "lock_test2"));
        a[2, 0].Mark = "elevator:0";
        b[2, 0].Mark = "elevator:0";
        var state = new ExplorationState(new Dungeon("t", [a, b]), new PartyPosition(0, new GridPos(2, 0), Direction.West));
        var nav = new MazeNavigator(state);
        Assert.Equal([0, 1], nav.ElevatorFloors());
        Assert.Equal(MoveResult.TookElevator, nav.TakeElevator(1));
        Assert.Equal(1, state.Position.Floor);
    }

    [Fact]
    public void LightLetsYouSeeInTheDark()
    {
        var floor = MazeTextFormat.Parse(Locked);
        floor[0, 0].Zone = Zone.Dark;
        var state = new ExplorationState(new Dungeon("t", [floor]), new PartyPosition(0, new GridPos(0, 0), Direction.East));
        Assert.True(state.InDark);
        state.LightMinutes = 2;
        Assert.False(state.InDark);
        state.Tick();
        state.Tick();
        Assert.True(state.InDark);
    }

    [Fact]
    public void DeepFloorsScaleMonstersButKeepAcAndHitSmall()
    {
        var s = EncounterSettings.Load(Content);
        Assert.True(Encounters.Scale(s, 13).IsNone);
        var deep = Encounters.Scale(s, 40);
        Assert.True(deep.HpMult > 4);
        Assert.True(deep.AcBonus <= s.MaxAcHitBonus);
        var unit = new MonsterUnit(Data.Monsters["goblin"], 10, deep);
        Assert.True(unit.MaxHp > 40);
        Assert.Equal(Data.Monsters["goblin"].Ac + deep.AcBonus, unit.Ac);
    }

    [Fact]
    public void RostersLimitWhoAppears()
    {
        var s = EncounterSettings.Load(Content);
        var roster = Encounters.Roster(Data, s, 0.3, 6, new Pcg32(1, 1));
        Assert.Equal(s.RosterSize, roster.Count);
        for (ulong seed = 0; seed < 50; seed++)
        {
            var e = Encounters.Build(Data, s, 0.3, 6, new Pcg32(seed, 2), roster);
            Assert.All(e.Groups, g => Assert.Contains(g.Def.Id, roster));
        }
    }

    [Fact]
    public void HighEnchantmentsAppearOnlyDeepAndCarryElements()
    {
        var t = TreasureSettings.Load(Content);
        var rng = new Pcg32(9, 9);
        Assert.All(Enumerable.Range(0, 200), _ => Assert.Equal(0, Treasure.ExtraBonus(t, 13, rng)));
        var items = Enumerable.Range(0, 400).Select(_ => Treasure.Roll(Data, t, "very_rare", rng, 50)).ToList();
        var high = items.Where(i => i.Bonus >= ItemInstance.ElementFrom).ToList();
        Assert.NotEmpty(high);
        Assert.All(high, i => Assert.NotNull(i.Element));
        Assert.All(items, i => Assert.True(i.Bonus <= ItemInstance.MaxBonus));
        var sword = new ItemInstance { ItemId = "longsword", Bonus = 20, Element = "fire" };
        Assert.Equal("7d6", sword.ElementDice(Data.Items["longsword"]));
    }

    [Fact]
    public void ClassStopsAt20AndTheNextLevelGoesToAnotherClass()
    {
        var c = CharacterFactory.QuickCreate(Data, "E", "human", "fighter", 1);
        foreach (var a in Abilities.All) c.Scores[a] = 14;   // どのクラスのマルチクラスの条件も満たす
        c.Xp = Progression.XpForLevel(22);
        Assert.Equal(21, c.LevelUp(Data));
        Assert.Equal(20, c.LevelIn("fighter"));
        Assert.Equal(2, c.Level - c.LevelIn("fighter"));   // 21 からは別のクラス
        Assert.False(c.CanTakeClass(Data, "fighter"));
        var second = c.ClassIds.Last();
        Assert.Equal(15, c.ClassCap(second));   // 2つ目のクラスは 15 まで
        Assert.Equal(0, c.EpicPoints);   // 恩恵は 4 クラスを全部上限にしてから
        Assert.False(c.TakeBoon("hp"));
    }

    [Fact]
    public void AtMostFourClassesCappedInTheOrderTaken()
    {
        var c = CharacterFactory.QuickCreate(Data, "E", "human", "fighter", 1);
        foreach (var a in Abilities.All) c.Scores[a] = 14;
        foreach (var id in new[] { "barbarian", "cleric", "wizard" })
        {
            c.Xp = Progression.XpForLevel(c.Level + 1);
            Assert.Equal(1, c.LevelUp(Data, id, maxLevels: 1));
        }
        Assert.Equal(4, c.ClassIds.Count());
        Assert.Equal([20, 15, 10, 5], c.ClassIds.Select(c.ClassCap).ToArray());
        c.Xp = Progression.XpForLevel(c.Level + 10);
        Assert.Equal(4, c.LevelUp(Data, "wizard"));   // 4つ目は 5 で止まる
        Assert.Equal(5, c.LevelIn("wizard"));
        Assert.False(c.CanTakeClass(Data, "wizard"));
        Assert.False(c.CanTakeClass(Data, "bard"));
        c.Xp = Progression.XpForLevel(c.Level + 1);
        Assert.Equal(0, c.LevelUp(Data, "bard"));
        Assert.True(c.CanTakeClass(Data, "cleric"));   // 上限に届いていないクラスは上げられる
    }

    [Fact]
    public void AfterAllClassesAtTheirCapsLevelsGiveFlatHpAndBoons()
    {
        var c = CharacterFactory.QuickCreate(Data, "E", "human", "fighter", 1);
        foreach (var a in Abilities.All) c.Scores[a] = 20;
        c.Xp = Progression.XpForLevel(Character.EpicFrom);
        c.LevelUp(Data);
        Assert.Equal(Character.EpicFrom, c.Level);
        Assert.Equal(Character.MaxClasses, c.ClassIds.Count());
        Assert.All(c.ClassIds, id => Assert.Equal(c.ClassCap(id), c.LevelIn(id)));
        Assert.True(c.IsEpic);
        Assert.Equal(0, c.EpicPoints);

        int hp = c.MaxHp;
        c.Xp = Progression.XpForLevel(Character.EpicFrom + 2);
        Assert.Equal(2, c.LevelUp(Data));
        Assert.Equal(hp + 2 * (Character.EpicHpBase + 5), c.MaxHp);   // 一律 5 + 耐久力の修正値(20 で +5)
        Assert.All(c.ClassIds, id => Assert.Equal(c.ClassCap(id), c.LevelIn(id)));
        Assert.Equal(2, c.EpicPoints);

        hp = c.MaxHp;
        Assert.True(c.TakeBoon("hp"));
        Assert.Equal(hp + 10, c.MaxHp);
        Assert.True(c.TakeBoon("spell"));
        Assert.Equal(1, c.BoonCount("spell"));
        Assert.False(c.TakeBoon("damage"));   // もう無い
        Assert.False(c.TakeBoon("save"));     // セーヴの恩恵は無い
    }

    [Fact]
    public void AbilityIncreaseGoesTo30OnlyWhenAllSixAre20()
    {
        var c = CharacterFactory.QuickCreate(Data, "E", "human", "fighter", 1);
        foreach (var a in Abilities.All) c.Scores[a] = 20;
        c.Scores[Ability.Cha] = 19;
        Assert.Equal(Character.MaxIncreasedScore, c.IncreaseCap());
        c.Scores[Ability.Cha] = 20;
        Assert.Equal(Character.MaxScore, c.IncreaseCap());
    }
}
