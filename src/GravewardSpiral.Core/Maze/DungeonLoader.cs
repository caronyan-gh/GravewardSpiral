using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Maze;

/// <summary>
/// パックの dungeons.json から、手作りの迷宮を読む。
/// <code>[{"id": "devtest", "name": {"ja": "試しの迷宮"}, "floors": ["maps/devtest_b1.txt", ...]}]</code>
/// </summary>
public static class DungeonLoader
{
    public const string Collection = "dungeons";

    public sealed class DungeonDef
    {
        public string Id { get; set; } = "";
        public LocalizedText? Name { get; set; }
        public List<string> Floors { get; set; } = [];

        /// <summary>
        /// 階ごとの顔ぶれ(さまよう敵のモンスターの id。0 が B1)。書いた階は抽選せずにこの顔ぶれを使い、空の階・書かない階は自動で選ぶ。
        /// 顔ぶれに予算に合う敵がいなければ、遭遇は全体から選ぶ(Encounters.Build)。
        /// </summary>
        public List<List<string>> Rosters { get; set; } = [];

        /// <summary>
        /// 名のある敵(どれを、どの階に、ねぐらかさまようか、噂に乗るか)。書いてあれば、新しいゲームでも抽選せずにこれを使う
        /// (空の並びなら、名のある敵はいない)。省くと(古いシナリオ)、新しいゲームのたびに抽選する。
        /// </summary>
        public List<Monsters.NamedSpawn>? Named { get; set; }

        /// <summary>
        /// 散らすイベントを、もう迷宮のマスに置いてある(シナリオを作るときに置いた)。true なら、新しいゲームでは散らさずにマップの印のまま使う
        /// (連動する組のフラグは、置いてある印から立てる)。false(古いシナリオ)なら、新しいゲームのたびに散らす。
        /// </summary>
        public bool EventsPlaced { get; set; }

        /// <summary>エレベーターの縦穴(座標と上下の階)。読むときに、範囲の空いている階へ止まるマスを入れる。</summary>
        public List<ElevatorShaft> Elevators { get; set; } = [];

        /// <summary>
        /// Lord B の呪い: true なら、この迷宮では ACGRW の祝福(勝った戦闘の終わりに、倒した数で HP が戻る)が効かない。ふつうは省くか false。
        /// 置いた戦闘を通して、HP の消耗を作者が考えたとおりに管理させたいシナリオで使うかを考える。
        /// 「D&amp;D 風」や「さまよう敵が出ない」というだけの理由で、決まりごとのように true にしない。
        /// </summary>
        public bool CurseOfLordB { get; set; }

        public string? Note { get; set; }
    }

    /// <summary>選んでいるシナリオの迷宮の定義(シナリオが迷宮を持たなければ null)。</summary>
    public static DungeonDef? ScenarioDef(ContentLibrary content) =>
        content.Scenario?.Dungeon is { } id && content.Find(Collection, id)?.As<DungeonDef>() is { } def ? def : null;

    /// <summary>選んでいるシナリオの迷宮の、階ごとの決まった顔ぶれ(無ければ空)。</summary>
    public static List<List<string>> ScenarioRosters(ContentLibrary content) => ScenarioDef(content)?.Rosters ?? [];

    public static Dungeon Load(ContentLibrary content, string id)
    {
        var item = content.Find(Collection, id) ?? throw new KeyNotFoundException($"迷宮 '{id}' がありません");
        var def = item.As<DungeonDef>();
        var floors = def.Floors.Select(path =>
        {
            string full = item.ResolvePath(path);
            return MazeTextFormat.Parse(File.ReadAllText(full), $"{item.Pack.Id}/{path}");
        }).ToList();
        Elevators.ApplyAll(floors, def.Elevators);
        return new Dungeon(def.Id, floors);
    }

    /// <summary>選んでいるシナリオの迷宮(manifest の dungeon)。シナリオが迷宮を持たなければ null(生成する)。</summary>
    public static Dungeon? ScenarioDungeon(ContentLibrary content) =>
        content.Scenario?.Dungeon is { } id ? Load(content, id) : null;

    /// <summary>決まった迷宮で始めるときの生成の指定(シードは顔ぶれ・散らすイベントなどの抽選に使う)。</summary>
    public static MazeGen.GenerationRequest RequestFor(Dungeon dungeon, ulong seed) =>
        new(seed, dungeon.Floors.Count, MazeGen.FloorSizeMode.Fixed(dungeon.Floors[0].Width, dungeon.Floors[0].Height));

    /// <summary>パックにある手作りの迷宮を全部読んで、辻褄を調べる(起動時の検査)。</summary>
    public static void ValidateAll(ContentLibrary content)
    {
        foreach (var item in content.All(Collection))
        {
            try
            {
                var dungeon = Load(content, item.Id);
                content.Problems.AddRange(dungeon.Validate().Select(p => $"迷宮 '{item.Id}': {p}"));
                var def = item.As<DungeonDef>();
                foreach (var shaft in def.Elevators)
                {
                    int stops = Elevators.Stops(dungeon.Floors, shaft).Count;
                    if (stops < 2) content.Problems.Add($"迷宮 '{item.Id}' のエレベーター ({shaft.X},{shaft.Y}) B{shaft.From}〜B{shaft.To} が止まる階が {stops} しかありません(2 つ以上の階で空いているマスにする)");
                }
                for (int i = 0; i < def.Rosters.Count; i++)
                    foreach (var m in def.Rosters[i])
                    {
                        if (Combat.RosterSpec.IsType(m, out string type, out int count))
                        {
                            if (type.Length == 0 || count < 1) content.Problems.Add($"迷宮 '{item.Id}' の B{i + 1} の顔ぶれの '{m}' は type:種類:数 の形にしてください");
                        }
                        else if (!content.Has("monsters", m)) content.Problems.Add($"迷宮 '{item.Id}' の B{i + 1} の顔ぶれのモンスター '{m}' がありません");
                    }
            }
            catch (Exception ex) when (ex is FormatException or IOException or System.Text.Json.JsonException)
            {
                content.Problems.Add($"迷宮 '{item.Id}': {ex.Message}");
            }
        }
    }
}
