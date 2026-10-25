using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Monsters;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Game.Scenes;

/// <summary>確認用の起動(--start party / --start combat)。試しの迷宮に、おまかせの6人で入る。枠 0 なのでセーブしない。</summary>
public static class DebugStart
{
    private static readonly (string Race, string Class)[] Members =
    [
        ("human", "fighter"), ("dwarf", "fighter"), ("human", "paladin"),
        ("halfling", "rogue"), ("elf", "wizard"), ("human", "cleric"),
    ];

    public static void Party(GameServices services) => Party(services, MazeScene.TestDungeonId);

    /// <summary>
    /// 名のある敵の見本: 試しの迷宮で、1歩前のマスを名のある敵のねぐらにする(id を省くと最初の1体)。--actions "U" で戦う。
    /// </summary>
    public static void Named(GameServices services, string? id)
    {
        Party(services);
        var session = services.Session!;
        var def = id != null && services.Data.NamedMonsters.ContainsKey(id) ? id : services.Data.NamedMonsters.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
        var pos = session.Exploration!.Position;
        var ahead = session.Dungeon.Floors[pos.Floor].Neighbor(pos.Cell, pos.Facing) ?? pos.Cell;
        session.Named = [new Core.Monsters.NamedSpawn { Id = def, Floor = pos.Floor, Lair = true, X = ahead.X, Y = ahead.Y, Rumored = true }];
    }

    /// <summary>仕掛けの見本の階(散らすイベントと、連動する仕掛けを並べたもの)に入る。</summary>
    public static void Events(GameServices services) => Party(services, "devtest_events");

    private static void Party(GameServices services, string dungeonId)
    {
        var dungeon = DungeonLoader.Load(services.Content, dungeonId);
        Party(services, dungeon, new GenerationRequest(1, dungeon.Floors.Count, FloorSizeMode.Fixed(10, 10)));
    }

    /// <summary>
    /// 自動生成の見本: シードから迷宮を作り、floor 階(1 から)の地図を全部見えるようにして、地図の画面を開く。
    /// spec は "シード[:階]"(省くとシード 2026 の B3)。
    /// </summary>
    public static void GenMap(GameServices services, string? spec)
    {
        var parts = (spec ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        ulong seed = parts.Length > 0 && ulong.TryParse(parts[0], out var sd) ? sd : 2026;
        int floor = parts.Length > 1 && int.TryParse(parts[1], out var fl) ? fl - 1 : 2;
        var request = new GenerationRequest(seed, 6, FloorSizeMode.Fixed(20, 20));
        var dungeon = new MazeGenerator(MazeGenSettings.Load(services.Content)).Generate(request).Dungeon;
        floor = Math.Clamp(floor, 0, dungeon.Floors.Count - 1);
        Party(services, dungeon, request);
        var state = services.Session!.Exploration!;
        var f = dungeon.Floors[floor];
        var start = f.FindFeature(CellFeature.StairsUp) ?? f.Start;
        state.Position = new PartyPosition(floor, start, Direction.North);
        var map = state.AutoMap.For(floor);
        foreach (var p in f.AllCells().Where(p => !f[p].IsSolid))
        {
            map.Visited.Add(p);
            foreach (var d in DirectionExtensions.All) map.See(p, d, state.Apparent(floor, p, d));
        }
        services.Scenes.Push(new MapScene(state, start, state.Position));
    }

    private static void Party(GameServices services, Dungeon dungeon, GenerationRequest request)
    {
        var session = GameSession.New(0, request, dungeon);
        // 名前は名前辞書から(シードを決めてあるので、起動のたびに同じ6人になる)
        var names = new Pcg32(2026, 3);
        foreach (var (race, cls) in Members)
        {
            string name = RandomNames.Pick(services.Data, race, GameServices.Language, names, session.Roster.Characters.Select(m => m.Name)) ?? race;
            var c = CharacterFactory.QuickCreate(services.Data, name, race, cls);
            if (cls == "wizard")
            {
                // マルチクラスの術者を試せるように、ウィザード 1 / クレリック 1 にする(確認用なので条件は見ない)
                c.Xp = Core.Rules.Progression.XpForLevel(2);
                c.LevelUp(services.Data, "cleric", maxLevels: 1);
            }
            session.Roster.Characters.Add(c);
            session.Roster.AddToParty(c);
        }
        // レアな装備の見本(鑑定済み): 追加効果つきの +2 ロングソードと、固有の品
        var leader = session.Roster.Characters[0];
        var ts = Core.Items.TreasureSettings.Load(services.Content);
        var lrng = new Core.Rng.Pcg32(11, 11);
        var sword = new Core.Items.ItemInstance { ItemId = "longsword", Bonus = 2 };
        Core.Items.Loot.AddAffixes(services.Data, sword, services.Data.Items["longsword"], 2, 8, lrng);
        var unique = Core.Items.Loot.Make(services.Data, ts, services.Data.Items["hellbrand"], 8, lrng);
        unique.Identified = true;
        leader.Inventory.TryAdd(sword, services.Data.Items);
        leader.Inventory.TryAdd(unique, services.Data.Items);
        // 未鑑定の見本(キャンプの小休憩で調べられる)
        leader.Inventory.TryAdd(new Core.Items.ItemInstance { ItemId = "ring_of_protection", Identified = false }, services.Data.Items);
        session.Roster.Characters[3].Inventory.TryAdd(new Core.Items.ItemInstance { ItemId = "longsword", Bonus = 1, Identified = false }, services.Data.Items);
        session.Roster.Characters[1].Inventory.TryAdd(new Core.Items.ItemInstance { ItemId = "cursed_longsword_of_misfortune", Identified = false }, services.Data.Items);   // 呪い(鑑定しても +1 に見える)
        services.Session = session;
        session.BuildRosters(services.Data, EncounterSettings.Load(services.Content));   // 試しの迷宮(イベントは手で置いてある)
        var b1 = dungeon.Floors[0];
        session.Exploration = new ExplorationState(dungeon, new PartyPosition(0, b1.Start, b1.StartFacing));
        session.Location = Location.Maze;
        services.Scenes.Push(new MazeScene(session.Exploration, 1));
    }

    /// <summary>おまかせの6人で町から始める(金を多めに持たせ、宝箱の品をいくつか袋に入れ、1レベル上げられる経験値を持たせる)。</summary>
    public static void Town(GameServices services)
    {
        Party(services);
        var session = services.Session!;
        session.Location = Location.Town;
        var rng = session.Rng[RngStreams.Names.Loot];
        var s = Core.Items.TreasureSettings.Load(services.Content);
        foreach (var c in session.Roster.PartyMembers)
        {
            c.Gold = 2000;
            c.Xp = Core.Rules.Progression.XpForLevel(c.Level + 1);   // 訓練場のレベルアップ(上げるクラスを選ぶ)を試せるように
            foreach (var it in Core.Items.Treasure.RollMany(services.Data, s, 0.5, 2, rng)) c.Inventory.TryAdd(it, services.Data.Items);
        }
        services.Scenes.Replace(new Town.TownScene());
    }

    /// <summary>そのパーティの前に、宝箱を1つ出す(罠も中身も深さ 0.5 で抽選)。</summary>
    public static void Chest(GameServices services)
    {
        Party(services);
        var rng = services.Session!.Rng[RngStreams.Names.Loot];
        var chest = Core.Items.Chest.Generate(services.Data, Core.Items.TreasureSettings.Load(services.Content), 0.5, 6, rng);
        services.Scenes.Push(new ChestScene(chest, () => { }, () => { }));
    }

    /// <summary>エンディングを見る(ボスの証を持って町に戻ったところ)。</summary>
    public static void Ending(GameServices services)
    {
        Party(services);
        if (services.Session!.Boss(services.Data, "final")?.RewardFlag is { } flag) services.Session.Flags.Add(flag);
        services.Scenes.Replace(new Town.TownScene());
    }

    /// <summary>そのパーティで、すぐ最下層のボス(bosses.json の final)と戦う(ボス戦の曲で)。</summary>
    public static void Boss(GameServices services)
    {
        Party(services);
        var boss = services.Session!.Boss(services.Data, "final")!;
        var groups = boss.Fighters(services.Data.Monsters);
        services.Scenes.Push(new CombatScene(groups, scale: boss.Scale, bgm: CombatScene.BossBgm));
    }

    /// <summary>spec は "goblinx3,wolf" の形(省くと B1 の遭遇表から)。</summary>
    public static void Combat(GameServices services, string? spec)
    {
        Party(services);
        var groups = new List<(MonsterDef, int)>();
        foreach (var token in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('x');
            if (services.Data.Monsters.TryGetValue(parts[0], out var def))
                groups.Add((def, parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 1));
            else Console.Error.WriteLine($"モンスター '{parts[0]}' がありません");
        }
        if (groups.Count == 0)
        {
            var rng = services.Session!.Rng[RngStreams.Names.Encounter];
            groups.AddRange(Encounters.Build(services.Data, EncounterSettings.Load(services.Content), 0, Members.Length, rng).Groups);
        }
        services.Scenes.Push(new CombatScene(groups));
    }
}
