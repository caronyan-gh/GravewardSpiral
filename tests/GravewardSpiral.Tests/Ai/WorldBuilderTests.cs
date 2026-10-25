using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.Ai;

public class WorldBuilderTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);
    private static readonly GameData Data = GameData.Load(Content);

    [Fact]
    public void ThereAreManyPartsOfEveryKind()
    {
        int Count(string kind) => Data.WorldParts.Values.Count(p => p.Kind == kind);
        Assert.True(Count("place") >= 40, $"place {Count("place")}");
        Assert.True(Count("calamity") >= 40, $"calamity {Count("calamity")}");
        Assert.True(Count("depth") >= 30, $"depth {Count("depth")}");
        Assert.True(Count("request") >= 20, $"request {Count("request")}");
        Assert.True(Count("town") >= 40, $"town {Count("town")}");
        // どの場所にも、合う災いと底がある
        foreach (var place in Data.WorldParts.Values.Where(p => p.Kind == "place"))
        {
            Assert.Contains(Data.WorldParts.Values, c => c.Kind == "calamity" && c.FitsPlace(place));
            Assert.Contains(Data.WorldParts.Values, d => d.Kind == "depth" && d.FitsPlace(place));
        }
    }

    [Fact]
    public void AnOriginBuildsTheWholeWorldTextWithoutAi()
    {
        var origins = new HashSet<string>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var origin = WorldBuilder.Pick(Data, seed)!;
            Assert.Equal(origin.Place, WorldBuilder.Pick(Data, seed)!.Place);   // 同じシードなら同じ由来
            origins.Add($"{origin.Place}/{origin.Calamity}/{origin.Depth}");
            var world = WorldBuilder.Build(Data, origin, 6, seed)!;
            string prologue = world.Prologue!.Get("ja");
            Assert.Contains("\n\n", prologue);
            Assert.DoesNotContain("{", prologue + world.Prologue.Get("en"));
            Assert.Equal(6, world.Floors.Count);
            Assert.All(world.Floors, f =>
            {
                Assert.NotNull(f.Name);
                Assert.NotNull(f.Enter);
                Assert.Equal(WorldWriter.DocumentsPerFloor, f.Documents.Count);
            });
            Assert.Equal(6, world.Floors.Select(f => f.Name!.Get("en")).Distinct().Count());   // 階の名前は重ならない
            // 出る敵の種類: 浅い階は場所、深い階は底が強く効く
            var place = Data.WorldParts[origin.Place];
            var bottom = Data.WorldParts[origin.Depth];
            var top = WorldBuilder.Theme(Data, origin, 0)!;
            var deep = WorldBuilder.Theme(Data, origin, 1)!;
            string placeMain = place.Types.MaxBy(t => t.Value).Key;
            string bottomMain = bottom.Types.MaxBy(t => t.Value).Key;
            Assert.True(top.Types[placeMain] > 0);
            Assert.True(deep.Types[bottomMain] >= top.Types.GetValueOrDefault(bottomMain));
        }
        Assert.True(origins.Count >= 30);   // 世界ごとにばらける
    }

    [Fact]
    public void AFreeWorldUsesItsOriginForMonstersAndText()
    {
        var settings = MazeGenSettings.Load(Content);
        var request = GenerationRequest.Default(settings, 12);
        var session = GameSession.New(1, request, new MazeGenerator(settings).Generate(request).Dungeon);
        session.Origin = WorldBuilder.Pick(Data, request.Seed);
        session.BuildRosters(Data, Core.Combat.EncounterSettings.Load(Content));
        var theme = session.ThemeAt(Data, 0)!;
        Assert.StartsWith("origin:", theme.Id);
        // 浅い階の顔ぶれは、由来の場所の種類に寄る
        var weights = Data.WorldParts[session.Origin!.Place].Types;
        Assert.Contains(session.FloorRosters[0], id => weights.ContainsKey(Data.Monsters[id].CreatureType));
        // AI に渡す頼みに、由来の説明が入る
        session.World = WorldBuilder.Build(Data, session.Origin, session.Dungeon.Floors.Count, request.Seed);
        var req = WorldWriter.BuildRequest(Data, WorldWriter.Prompt(Content)!, session.Dungeon.Floors.Count, null, request.Seed, session);
        Assert.Contains("This world's origin", req.User);
        Assert.DoesNotContain("# Inspiration", req.User);
        Assert.Contains(session.World!.Floor(0)!.Name!.Get("en"), req.User);
    }
}
