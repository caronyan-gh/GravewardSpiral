using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Rng;

namespace GravewardSpiral.Core.Save;

public enum Location { Town, Maze }

/// <summary>
/// 遊んでいる1つのゲーム(セーブの1つの枠)。名簿、生成した迷宮、迷宮の中の状態、乱数を持つ。
/// </summary>
public sealed class GameSession
{
    public required int Slot { get; init; }
    public required GenerationRequest Request { get; init; }
    public required Dungeon Dungeon { get; init; }
    public required RngStreams Rng { get; init; }
    public Roster Roster { get; init; } = new();

    /// <summary>商店に売った品(店に並ぶ)。</summary>
    public List<Items.ItemInstance> ShopSold { get; init; } = [];

    /// <summary>階ごとの顔ぶれ(その階に出るモンスターの id)。新しいゲームで抽選する。</summary>
    public List<List<string>> FloorRosters { get; set; } = [];

    /// <summary>
    /// 世界のレベル帯(新しいゲームで決める。強いクランを連れて始めたら、その強さに合わせてずらしてある)。
    /// null ならシナリオの encounters.json のまま(古いセーブも)。
    /// </summary>
    public double? FirstLevel { get; set; }
    public double? LastLevel { get; set; }

    /// <summary>世界のレベル帯を、シナリオの帯からいくつずらしたか(決まった敵を強くするのに使う)。</summary>
    public double LevelShift { get; set; }

    /// <summary>クランの強さに合わせて、世界のレベル帯を決める(新しいゲームを作るとき)。</summary>
    public void SetWorldLevel(Combat.EncounterSettings scenario, double? clanPower)
    {
        var (first, last) = WorldLevel.Band(scenario.FirstLevel, scenario.LastLevel, clanPower);
        (FirstLevel, LastLevel, LevelShift) = (first, last, first - scenario.FirstLevel);
    }

    /// <summary>決まった敵(ボス、イベントの戦闘)に足す強化。</summary>
    public Combat.MonsterScale FixedScale(Combat.EncounterSettings s) => Combat.Encounters.ShiftScale(s, LevelShift);

    /// <summary>このクランが見たイベント(前の世界の分はクランファイルから。新しい世界では出にくくする)。</summary>
    public HashSet<string> EventsSeen { get; set; } = new(StringComparer.Ordinal);

    /// <summary>このクランがクリアした世界の数(クランファイルから引き継ぎ、エンディングで 1 増える)。</summary>
    public int Clears { get; set; }

    /// <summary>遭遇の設定に、この世界のレベル帯を当てたもの。</summary>
    public Combat.EncounterSettings WorldSettings(Combat.EncounterSettings s) =>
        FirstLevel is double f && LastLevel is double l ? s.WithLevels(f, l) : s;

    /// <summary>階ごとの顔ぶれを抽選する(6人のパーティの予算で)。</summary>
    public void BuildRosters(Characters.GameData data, Combat.EncounterSettings s)
    {
        s = WorldSettings(s);
        var rng = RngStreams.Create(Request.Seed, "rosters");
        int floors = Dungeon.Floors.Count;
        FloorRosters = Enumerable.Range(0, floors)
            .Select(i => Combat.Encounters.Roster(data, s, floors <= 1 ? 0 : (double)i / (floors - 1), Characters.Roster.MaxParty, rng)).ToList();
        // 落とし物の味付け(仕組みだけ。生成ではまだ全部の階を ×1 にする)
        FloorDrops = Enumerable.Range(0, floors).Select(_ => new Items.DropBoost()).ToList();
        PlaceNamed(data, RngStreams.Create(Request.Seed, "named"));
    }

    /// <summary>この世界にいる名のある敵(0〜2 体)。</summary>
    public List<Monsters.NamedSpawn> Named { get; set; } = [];

    /// <summary>名のある敵を抽選して置く: 何体いるか(0〜2)、どの敵か(深さの合う中から重みで)、どの階か、ねぐらかさまようか、噂に乗るか。</summary>
    public void PlaceNamed(Characters.GameData data, IRandom rng)
    {
        Named = [];
        int floors = Dungeon.Floors.Count;
        double r = rng.NextDouble();
        int count = r < 0.3 ? 0 : r < 0.75 ? 1 : 2;
        var pool = data.NamedMonsters.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        for (int k = 0; k < count && pool.Count > 0; k++)
        {
            double total = pool.Sum(n => Math.Max(0, n.Weight)), pick = rng.NextDouble() * total;
            var def = pool.First(n => (pick -= Math.Max(0, n.Weight)) < 0 || n == pool[^1]);
            pool.Remove(def);
            var fits = Enumerable.Range(0, floors).Where(f =>
            {
                double depth = floors <= 1 ? 0 : (double)f / (floors - 1);
                return depth >= def.MinDepth - 1e-9 && depth <= def.MaxDepth + 1e-9;
            }).ToList();
            if (fits.Count == 0) continue;
            int floor = fits[rng.Next(0, fits.Count)];
            bool lair = def.Placement == "lair" || def.Placement == "either" && rng.NextDouble() < 0.5;
            var spawn = new Monsters.NamedSpawn { Id = def.Id, Floor = floor, Lair = lair, Rumored = rng.NextDouble() < 0.5 };
            if (lair)
            {
                // ねぐら: 部屋の中か行き止まりの、何も無いマス(階段や印のある所は避ける)
                var f = Dungeon.Floors[floor];
                var cells = f.AllCells().Where(p => !f[p].IsSolid && f[p].Feature == CellFeature.None && f[p].Mark == null
                    && (f.RoomAt(p) != null || DirectionExtensions.All.Count(d => f[p][d] != EdgeType.Wall) == 1)).ToList();
                if (cells.Count == 0) cells = f.AllCells().Where(p => !f[p].IsSolid && f[p].Feature == CellFeature.None && f[p].Mark == null).ToList();
                if (cells.Count == 0) continue;
                var at = cells[rng.Next(0, cells.Count)];
                (spawn.X, spawn.Y) = (at.X, at.Y);
            }
            Named.Add(spawn);
        }
    }

    /// <summary>そのマスをねぐらにしている、まだ倒していない名のある敵。</summary>
    public Monsters.NamedSpawn? NamedLairAt(int floor, GridPos cell) =>
        Named.FirstOrDefault(n => n.Lair && !n.Defeated && n.Floor == floor && n.X == cell.X && n.Y == cell.Y);

    /// <summary>その階をさまよっている、まだ倒していない名のある敵。</summary>
    public Monsters.NamedSpawn? NamedWandering(int floor) => Named.FirstOrDefault(n => !n.Lair && !n.Defeated && n.Floor == floor);

    /// <summary>迷宮にイベント(壁の文字、遺品、墓石など)を散らす。新しいゲームで1回だけ。</summary>
    public int PlaceEvents(Characters.GameData data, MazeGenSettings s, Combat.EncounterSettings enc)
    {
        // 連動する組を先に置き、置いた組のフラグを立てる(町の人の依頼などが見る)
        var level = WorldSettings(enc).TargetLevel;
        var seen = EventsSeen.ToHashSet(StringComparer.Ordinal);   // 前の世界で見たもの(この世界で見る分は含まない)
        var chains = Events.EventPlacer.ScatterChains(data, Dungeon, s.ChainsPerDungeon, Request.Seed, level, s.QuestChainsMin, seen);
        foreach (var c in chains) Flags.Add(Events.EventPlacer.ChainFlag(c));
        return Events.EventPlacer.Scatter(data, Dungeon, s.EventsPerFloor, Request.Seed, level, seen);
    }

    public IReadOnlyCollection<string>? RosterOf(int floor) => floor < FloorRosters.Count ? FloorRosters[floor] : null;

    /// <summary>
    /// 階ごとの落とし物の味付け(掘り場など)。新しいゲームで決める(今は全部の階が ×1)。古いセーブには無いので、無ければ ×1。
    /// </summary>
    public List<Items.DropBoost> FloorDrops { get; set; } = [];

    /// <summary>その階の落とし物の味付け(テーマと、生成で決めた階ごとの値を掛け合わせたもの)。</summary>
    public Items.DropBoost? DropBoostOf(Characters.GameData data, int floor)
    {
        int floors = Dungeon.Floors.Count;
        var theme = Combat.FloorAdvisor.ThemeFor(data, floors <= 1 ? 0 : (double)floor / (floors - 1));
        return Items.DropBoost.Combine(theme?.Drops, floor < FloorDrops.Count ? FloorDrops[floor] : null);
    }

    /// <summary>物語の印(ボスを倒した、クリアした など)。周回で戻すものは M8 で決める。</summary>
    public HashSet<string> Flags { get; init; } = [];

    /// <summary>クランハウス(倉庫と設備。クランファイルと一緒に持ち歩く)。</summary>
    public ClanHouse ClanHouse { get; set; } = new();

    /// <summary>クランの名前(新しいゲームで付ける。町に出し、クランを書き出すときの既定の名前にする)。</summary>
    public string? ClanName { get; set; }

    /// <summary>遊んでいるシナリオ(パックの id と版。標準のパックだけなら null)。</summary>
    public string? Scenario { get; set; }
    public string? ScenarioVersion { get; set; }

    /// <summary>難易度(新規ゲームで選ぶ)。</summary>
    public Difficulty Difficulty { get; set; } = new();

    /// <summary>始まっていて終わっていない戦闘(厳しいセーブのとき)。</summary>
    public PendingCombat? PendingCombat { get; set; }

    /// <summary>町に戻ったときに知らせること(全滅など。セーブには入れない)。</summary>
    public string? TownNotice { get; set; }

    /// <summary>
    /// 全滅した。死体が残る難易度なら、パーティ全員が死んだまま今の場所に残り(名簿の LeftInMaze)、パーティは空になる。
    /// 残らない難易度なら、パーティは町へ運ばれ(倒れていた人は HP 1、死んだ人はそのまま)、金を半分失う。
    /// </summary>
    public void Wipe(Maze.PartyPosition at)
    {
        var party = Roster.PartyMembers.ToList();
        PendingCombat = null;
        if (Difficulty.BodiesRemain)
        {
            foreach (var c in party)
            {
                if (c.State is Characters.LifeState.Alive or Characters.LifeState.Unconscious) c.State = Characters.LifeState.Dead;
                c.Hp = 0;
                Roster.RemoveFromParty(c);
                Roster.LeftInMaze[c.Id] = at;
            }
        }
        else
        {
            foreach (var c in party)
            {
                if (c.State == Characters.LifeState.Unconscious) c.State = Characters.LifeState.Alive;
                if (c.State == Characters.LifeState.Alive) c.Hp = Math.Max(1, c.Hp);
                c.Gold /= 2;
            }
        }
        Location = Location.Town;
    }

    /// <summary>そのマスに残っている死体(パーティにいない人)。</summary>
    public List<Characters.Character> BodiesAt(Maze.PartyPosition at) => Roster.LeftInMaze
        .Where(kv => kv.Value.Floor == at.Floor && kv.Value.Cell == at.Cell)
        .Select(kv => Roster.Find(kv.Key)).OfType<Characters.Character>().ToList();

    /// <summary>そのマスの死体を、パーティの空きに入れて運ぶ(死んだまま)。運んだ人を返す。</summary>
    public List<Characters.Character> RecoverBodies(Maze.PartyPosition at)
    {
        var taken = new List<Characters.Character>();
        foreach (var c in BodiesAt(at))
        {
            if (Roster.Party.Count >= Characters.Roster.MaxParty) break;
            Roster.LeftInMaze.Remove(c.Id);
            Roster.AddToParty(c);
            taken.Add(c);
        }
        return taken;
    }

    /// <summary>イベントの数え上げ(スイッチを押した数など)。</summary>
    public Dictionary<string, int> Counters { get; init; } = new(StringComparer.Ordinal);

    /// <summary>迷宮の中の状態。迷宮に入るたびに作り、町に戻ったら残す(地図と見つけた隠し扉は次も使う)。</summary>
    public ExplorationState? Exploration { get; set; }

    public Location Location { get; set; } = Location.Town;
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>新しいゲーム。迷宮はここで作る。</summary>
    public static GameSession New(int slot, GenerationRequest request, Dungeon dungeon) => new()
    {
        Slot = slot,
        Request = request,
        Dungeon = dungeon,
        Rng = new RngStreams(request.Seed),
    };

    /// <summary>町から迷宮に入る。B1 の上り階段の上から始める。地図や見つけた隠し扉は前の分を引き継ぐ。</summary>
    public ExplorationState EnterMaze()
    {
        var b1 = Dungeon.Floors[0];
        var start = new PartyPosition(0, b1.FindFeature(CellFeature.StairsUp) ?? b1.Start, b1.StartFacing);
        if (Exploration == null) Exploration = new ExplorationState(Dungeon, start);
        else Exploration.Position = start;
        Exploration.RoomsFloor = -1;   // 入り直したら、部屋の敵は抽選し直す
        Exploration.StepsSinceCombat = 0;   // 入ってすぐは、うろつく敵に会わない(続けて出ないための間を使う)
        Location = Location.Maze;
        return Exploration;
    }

    public void ReturnToTown() => Location = Location.Town;
}
