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

    /// <summary>
    /// このクランが酒場で聞いた伝説の噂(イベントの id → 聞いた世界の数)。クランファイルに入り、次に作る世界で、
    /// 聞いた回数だけ出やすくなる(EventPlacer.HeardMultiplier)。同じ世界で何度聞いても1回に数える。
    /// </summary>
    public Dictionary<string, int> LegendsHeard { get; set; } = new(StringComparer.Ordinal);

    /// <summary>伝説の噂を聞いたことにする(この世界でまだ数えていなければ、回数を1つ足す)。足したら true。</summary>
    public bool HearLegend(string eventId)
    {
        if (!Flags.Add("legend_heard:" + eventId)) return false;
        LegendsHeard[eventId] = LegendsHeard.GetValueOrDefault(eventId) + 1;
        return true;
    }

    /// <summary>伝説の噂が酒場で聞ける割合(世界ごと)。</summary>
    public const double LegendRumorChance = 0.5;

    /// <summary>
    /// この世界の酒場で聞ける伝説の噂(噂の文を持つイベントから、世界のシードで決める。無ければ null)。
    /// 噂はほかの世界の話なので、この世界に置いたかどうかとは関係ない。
    /// </summary>
    public Events.EventDef? LegendRumor(Characters.GameData data)
    {
        Rng.IRandom rng = RngStreams.Create(Request.Seed, "legend-rumor");
        if (rng.NextDouble() >= LegendRumorChance) return null;
        var pool = data.Events.Values.Where(e => e.Rumor != null).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        return pool.Count == 0 ? null : pool[rng.Next(0, pool.Count)];
    }

    /// <summary>このクランがクリアした世界の数(クランファイルから引き継ぎ、エンディングで 1 増える)。</summary>
    public int Clears { get; set; }

    /// <summary>このクランのクリアの印(ScenarioClears。シナリオ → クリアした世界の数)。</summary>
    public Dictionary<string, int> ClearedScenarios { get; set; } = new(StringComparer.Ordinal);

    /// <summary>イベントの手順 ending で立つ印。町に戻るとエンディングになる。</summary>
    public const string EndingFlag = "ending";

    /// <summary>
    /// エンディングを流すとき: まだクリアしておらず、最下層の主(final)を倒した証を持つか、イベントで ending に着いた。
    /// </summary>
    public bool EndingDue(Characters.GameData data) => !Flags.Contains("cleared")
        && (Flags.Contains(EndingFlag) || data.Bosses.GetValueOrDefault(FinalBossId)?.RewardFlag is { } heart && Flags.Contains(heart));

    /// <summary>この世界をクリアした(エンディング)。クリアの数を増やし、シナリオならクリアの印を付ける。初めてなら true。</summary>
    public bool RecordClear(Content.PackManifest? scenario)
    {
        if (!Flags.Add("cleared")) return false;
        Clears++;
        if (scenario != null)
        {
            string key = ScenarioClears.Key(scenario);
            ClearedScenarios[key] = ClearedScenarios.GetValueOrDefault(key) + 1;
        }
        return true;
    }

    /// <summary>遭遇の設定に、この世界のレベル帯を当てたもの。</summary>
    public Combat.EncounterSettings WorldSettings(Combat.EncounterSettings s) =>
        FirstLevel is double f && LastLevel is double l ? s.WithLevels(f, l) : s;

    /// <summary>階ごとの顔ぶれを抽選する(6人のパーティの予算で)。</summary>
    /// <summary>
    /// シナリオで決めた階ごとの顔ぶれにする(空の階はそのまま。BuildRosters のあとに呼ぶ)。
    /// 決めた敵は必ず入れ、種類の項目(type:種類:数)は、この世界のシードでその種類から選ぶ(Combat.RosterSpec)。
    /// </summary>
    public void ApplyRosters(Characters.GameData data, Combat.EncounterSettings s, IReadOnlyList<List<string>> fixedRosters)
    {
        s = WorldSettings(s);
        var rng = RngStreams.Create(Request.Seed, "rosters-fixed");
        int floors = Dungeon.Floors.Count;
        for (int i = 0; i < fixedRosters.Count && i < FloorRosters.Count; i++)
        {
            if (fixedRosters[i].Count == 0) continue;
            double depth = floors <= 1 ? 0 : (double)i / (floors - 1);
            FloorRosters[i] = Combat.RosterSpec.Resolve(data, s, depth, Characters.Roster.MaxParty, fixedRosters[i], rng);
        }
    }

    public void BuildRosters(Characters.GameData data, Combat.EncounterSettings s)
    {
        s = WorldSettings(s);
        var rng = RngStreams.Create(Request.Seed, "rosters");
        int floors = Dungeon.Floors.Count;
        FloorRosters = Enumerable.Range(0, floors)
            .Select(i =>
            {
                double depth = floors <= 1 ? 0 : (double)i / (floors - 1);
                return Combat.Encounters.Roster(data, s, depth, Characters.Roster.MaxParty, rng, ThemeAt(data, depth));
            }).ToList();
        // 落とし物の味付け(仕組みだけ。生成ではまだ全部の階を ×1 にする)
        FloorDrops = Enumerable.Range(0, floors).Select(_ => new Items.DropBoost()).ToList();
        PlaceNamed(data, RngStreams.Create(Request.Seed, "named"));
        PickBosses(data, s);
    }

    /// <summary>auto のボス(自由な迷宮の主)に、この世界で選んだ敵の組(ボスの id → 組)。</summary>
    public Dictionary<string, List<Monsters.BossDef.BossGroup>> BossPicks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>auto のボスの組を、世界の最後のレベル帯と最下層の顔ぶれから選ぶ。</summary>
    public void PickBosses(Characters.GameData data, Combat.EncounterSettings s)
    {
        BossPicks.Clear();
        var rng = RngStreams.Create(Request.Seed, "boss");
        var deepest = FloorRosters.Count > 0 ? FloorRosters[^1] : [];
        foreach (var boss in data.Bosses.Values.Where(b => b.Auto).OrderBy(b => b.Id, StringComparer.Ordinal))
            BossPicks[boss.Id] = Monsters.BossDef.PickGroups(data, s.LastLevel, deepest, rng, ThemeAt(data, 1.0));
    }

    /// <summary>最下層の主の id(bosses.json)。世界の文の主の名前・出会い・倒したときの文は、この主に当てる。</summary>
    public const string FinalBossId = "final";

    /// <summary>ボス(auto のボスは、この世界で選んだ組に差し替えた写し)。無ければ null。</summary>
    public Monsters.BossDef? Boss(Characters.GameData data, string id)
    {
        if (!data.Bosses.TryGetValue(id, out var boss)) return null;
        var groups = boss.Auto && BossPicks.TryGetValue(id, out var picked) ? picked : boss.Groups;
        // 最下層の主に世界の文が無ければ、パックのまま(auto でなければ、決まった名前と文言のキー)
        if (!boss.Auto && (id != FinalBossId || World?.Boss == null)) return boss;
        if (boss.Auto && !BossPicks.ContainsKey(id)) return boss;
        // 主(最初の組)は、世界の文が付けた名前か、組の名前か、「迷宮の主」で出す
        // 従う者は、組の名前(シナリオで付けたもの)か、世界の文の名前
        var escorts = World?.Boss?.Escorts ?? [];
        var named = groups.Select((g, i) => i == 0 ? new Monsters.BossDef.BossGroup { Monster = g.Monster, Count = g.Count, Name = World?.Boss?.Name ?? g.Name ?? (boss.Auto ? boss.Name : null) }
            : g.Name == null && i - 1 < escorts.Count && escorts[i - 1] is { } escort ? new Monsters.BossDef.BossGroup { Monster = g.Monster, Count = g.Count, Name = escort } : g).ToList();
        var copy = boss.WithGroups(named);
        copy.IntroText = World?.Boss?.Intro;
        copy.VictoryText = World?.Boss?.Victory;
        return copy;
    }

    /// <summary>この世界にいる名のある敵(0〜2 体)。</summary>
    public List<Monsters.NamedSpawn> Named { get; set; } = [];

    /// <summary>自由な迷宮の由来(場所・災い・底などの部品)。出る敵の種類と、世界の文の元になる。シナリオの世界と古いセーブは null。</summary>
    public Ai.WorldOrigin? Origin { get; set; }

    /// <summary>その深さで出る敵の種類の重み。由来があれば由来から、無ければパックの階のテーマ。</summary>
    public Combat.FloorTheme? ThemeAt(Characters.GameData data, double depth) =>
        (Origin is { } o ? Ai.WorldBuilder.Theme(data, o, depth) : null) ?? Combat.FloorAdvisor.ThemeFor(data, depth);

    /// <summary>AI が書いたこの世界の文(背景・階の名前と文・書き付け)。AI を使わなかった世界と古いセーブは null。</summary>
    public Ai.WorldText? World { get; set; }

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
                var cells = LairCells(Dungeon.Floors[floor]);
                if (cells.Count == 0) continue;
                var at = cells[rng.Next(0, cells.Count)];
                (spawn.X, spawn.Y) = (at.X, at.Y);
            }
            Named.Add(spawn);
        }
    }

    /// <summary>名のある敵のねぐらにできるマス: 部屋の中か行き止まりの、何も無いマス(階段や印のある所は避ける。無ければ何も無いマス)。</summary>
    public static List<GridPos> LairCells(MazeFloor f)
    {
        var cells = f.AllCells().Where(p => !f[p].IsSolid && f[p].Feature == CellFeature.None && f[p].Mark == null
            && (f.RoomAt(p) != null || DirectionExtensions.All.Count(d => f[p][d] != EdgeType.Wall) == 1)).ToList();
        return cells.Count > 0 ? cells : f.AllCells().Where(p => !f[p].IsSolid && f[p].Feature == CellFeature.None && f[p].Mark == null).ToList();
    }

    /// <summary>そのマスをねぐらにしている、まだ倒していない名のある敵。</summary>
    public Monsters.NamedSpawn? NamedLairAt(int floor, GridPos cell) =>
        Named.FirstOrDefault(n => n.Lair && !n.Defeated && n.Floor == floor && n.X == cell.X && n.Y == cell.Y);

    /// <summary>その階をさまよっている、まだ倒していない名のある敵。</summary>
    public Monsters.NamedSpawn? NamedWandering(int floor) => Named.FirstOrDefault(n => !n.Lair && !n.Defeated && n.Floor == floor);

    /// <summary>
    /// 新しい世界に、シナリオで決めた中身を当てる(BuildRosters のあとに呼ぶ)。シナリオは出来上がった世界なので、書いてあるものはそのまま使う:
    /// 階の顔ぶれ(書いた階)、名のある敵(書いてあれば)、散らすイベント(置いてあれば、散らさずに印から連動する組のフラグを立てる)。
    /// 書いていないもの(古いシナリオ・自由な迷宮)は、今までどおり、この世界のシードで選ぶ。
    /// </summary>
    public void ApplyScenario(Characters.GameData data, Content.ContentLibrary content, Combat.EncounterSettings enc, MazeGenSettings gen)
    {
        var def = Maze.DungeonLoader.ScenarioDef(content);
        ApplyRosters(data, enc, def?.Rosters ?? []);
        if (def?.Named is { } named)
            Named = named.Select(n => new Monsters.NamedSpawn { Id = n.Id, Floor = n.Floor, Lair = n.Lair, X = n.X, Y = n.Y, Rumored = n.Rumored })
                .Where(n => data.NamedMonsters.ContainsKey(n.Id) && n.Floor < Dungeon.Floors.Count).ToList();
        if (def?.EventsPlaced == true) MarkPlacedChains(data);
        else PlaceEvents(data, gen, enc, Events.EventPlacer.ScenarioOnly(content));
    }

    /// <summary>迷宮に置いてある連動する組のイベントの印から、組のフラグ(chain:名前)を立てる(町の人の依頼などが見る)。</summary>
    public void MarkPlacedChains(Characters.GameData data)
    {
        foreach (var f in Dungeon.Floors)
            foreach (var p in f.AllCells())
                if (f[p].Mark is { } m && m.StartsWith(Events.EventPlacer.MarkPrefix)
                    && data.Events.GetValueOrDefault(m[Events.EventPlacer.MarkPrefix.Length..])?.Scatter?.Chain is { } chain)
                    Flags.Add(Events.EventPlacer.ChainFlag(chain));
    }

    /// <summary>迷宮にイベント(壁の文字、遺品、墓石など)を散らす。新しいゲームで1回だけ。</summary>
    /// <param name="allow">散らしてよいイベント(シナリオが標準のイベントを混ぜないとき。EventPlacer.ScenarioOnly)。</param>
    public int PlaceEvents(Characters.GameData data, MazeGenSettings s, Combat.EncounterSettings enc, Func<Events.EventDef, bool>? allow = null)
    {
        // 連動する組を先に置き、置いた組のフラグを立てる(町の人の依頼などが見る)
        var level = WorldSettings(enc).TargetLevel;
        var seen = EventsSeen.ToHashSet(StringComparer.Ordinal);   // 前の世界で見たもの(この世界で見る分は含まない)
        var chains = Events.EventPlacer.ScatterChains(data, Dungeon, s.ChainsPerDungeon, Request.Seed, level, s.QuestChainsMin, seen, LegendsHeard, allow);
        foreach (var c in chains) Flags.Add(Events.EventPlacer.ChainFlag(c));
        return Events.EventPlacer.Scatter(data, Dungeon, s.EventsPerFloor, Request.Seed, level, seen, LegendsHeard, allow);
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

    /// <summary>
    /// クランの id。同じクランが別の世界(シナリオ)を遊ぶと、セーブは「クラン × シナリオ」ごとに分かれ、続きからはクランで選ぶ。
    /// 持ち込んだクランはその id を引き継ぐ。id を持つ前のセーブは "slot&lt;枠&gt;"。
    /// </summary>
    public string ClanId { get; set; } = NewClanId();

    public static string NewClanId() => "c" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>世界の key(セーブのフォルダの名前)。シナリオの id、自由な迷宮は free-&lt;シード&gt;。</summary>
    public string WorldKey => Scenario ?? $"free-{Request.Seed}";

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
