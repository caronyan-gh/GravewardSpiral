using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Save;

public class ScenarioExportTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    /// <summary>新しいゲームと同じ手順で、自由な迷宮を作る(マップは、イベントを置いたあとの出来上がった世界のもの)。</summary>
    private static (GameSession Session, List<string> Maps) FreeWorld(ulong seed)
    {
        var settings = MazeGenSettings.Load(Content);
        var request = new GenerationRequest(seed, 3, FloorSizeMode.Fixed(12, 12));
        var dungeon = new MazeGenerator(settings).Generate(request).Dungeon;
        var s = GameSession.New(0, request, dungeon);
        var enc = EncounterSettings.Load(Content);
        s.Origin = WorldBuilder.Pick(Data, seed);
        s.BuildRosters(Data, enc);
        s.PlaceEvents(Data, settings, enc);
        s.World = WorldBuilder.Build(Data, s.Origin!, dungeon.Floors.Count, seed);
        return (s, ScenarioExport.MapTexts(dungeon));
    }

    [Fact]
    public void AFreeWorldBecomesAScenarioPackThatLoadsBackTheSameWorld()
    {
        using var dir = new TestPaths.TempDir();
        var (s, maps) = FreeWorld(321);
        string id = ScenarioExport.NewId(s.Request.Seed, x => x == "free-321");
        Assert.Equal("free-321-2", id);   // 同じ id のパックがあれば番号を付ける
        string pack = Path.Combine(dir.Path, id);
        ScenarioExport.Write(pack, id, s, maps, s.WorldSettings(EncounterSettings.Load(Content)), Content, Data);

        // 置いたシナリオとして見つかり、標準のパックに重ねて読める
        var found = ScenarioCatalog.Find([dir.Path], Path.Combine(dir.Path, "cache"));
        Assert.Equal(id, Assert.Single(found).Id);
        var content = ContentLibrary.Load(TestPaths.Content, pack);
        Assert.True(content.IsValid, string.Join("\n", content.Problems));
        Assert.Equal(id, content.Scenario!.Id);
        Assert.Contains("の迷宮", content.Scenario.Name!.Get("ja"));

        // 迷宮は書き出したときのまま(置いたイベントの印も)
        var dungeon = DungeonLoader.ScenarioDungeon(content)!;
        Assert.Equal(maps, dungeon.Floors.Select(MazeTextFormat.Write).ToList());

        // 由来・世界の文・主の組・レベル帯が戻る
        var world = WorldPackDef.Of(content)!;
        Assert.Equal(s.Origin!.Place, world.Origin!.Place);
        Assert.Equal(s.World!.Prologue!.Get("ja"), world.Text!.Prologue!.Get("ja"));
        Assert.Equal(s.BossPicks["final"].Select(g => g.Monster), world.BossPicks["final"].Select(g => g.Monster));
        var enc = EncounterSettings.Load(content);
        Assert.Equal(s.WorldSettings(EncounterSettings.Load(Content)).LastLevel, enc.LastLevel);

        // このシナリオで新しいゲームを始めると(シードは別)、同じ由来・文・主・顔ぶれ・名のある敵・イベントの出来上がった世界
        var data = GameData.Load(content);
        var request = DungeonLoader.RequestFor(dungeon, 999);
        var again = GameSession.New(0, request, dungeon);
        again.Scenario = id;
        world.ApplyTo(again);
        again.BuildRosters(data, enc);
        world.ApplyBossPicks(again);
        again.ApplyScenario(data, content, enc, MazeGenSettings.Load(content));
        Assert.Equal(maps, again.Dungeon.Floors.Select(MazeTextFormat.Write).ToList());   // イベントを置き直さない
        Assert.Equal(s.FloorRosters, again.FloorRosters);
        Assert.Equal(s.Named.Select(n => (n.Id, n.Floor, n.X, n.Y)), again.Named.Select(n => (n.Id, n.Floor, n.X, n.Y)));
        Assert.All(s.Flags.Where(f => f.StartsWith("chain:")), f => Assert.Contains(f, again.Flags));   // 連動する組のフラグも印から立つ
        Assert.Equal(s.World.Prologue.Get("ja"), again.World!.Prologue!.Get("ja"));
        Assert.NotSame(world.Text, again.World);   // パックの文は写して使う
        Assert.Equal(s.Boss(Data, "final")!.Fighters(Data.Monsters).Select(f => f.Def.Id),
            again.Boss(data, "final")!.Fighters(data.Monsters).Select(f => f.Def.Id));
        Assert.Equal(id, again.WorldKey);
    }
}
