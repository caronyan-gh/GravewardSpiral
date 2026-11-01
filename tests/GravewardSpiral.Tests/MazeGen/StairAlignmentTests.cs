using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Maze;
using GravewardSpiral.Core.MazeGen;
using GravewardSpiral.Core.Save;

namespace GravewardSpiral.Tests.MazeGen;

/// <summary>階段とシュートの上下の座標をそろえる(align_stairs)。</summary>
public class StairAlignmentTests
{
    private static readonly ContentLibrary Content = ContentLibrary.Load(TestPaths.Content);

    private static Dungeon Generate(ulong seed, int floors, FloorSizeMode size, bool align)
    {
        var s = MazeGenSettings.Load(Content);
        s.AlignStairs = align;
        s.Chutes = new FeatureRule { MinDepth = 0, Count = new(1, 2) };   // シュートも確かめる
        return new MazeGenerator(s).Generate(new GenerationRequest(seed, floors, size)).Dungeon;
    }

    [Fact]
    public void GeneratedStairsAndChutesLineUp()
    {
        // 大きさが階ごとに違っても、下りた真下に出る。迷宮は今までどおり検査とボットを通る
        int chutes = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var d = Generate(seed, 8, new FloorSizeMode(12, 24, 12, 24), align: true);
            Assert.Empty(StairAlignment.Gaps(d.Floors));
            Assert.True(ReachabilityValidator.Validate(d).Ok);
            Assert.True(MazeBot.Explore(d).Ok);
            chutes += d.Floors.Sum(f => f.AllCells().Count(p => f[p].Feature == CellFeature.Chute));
        }
        Assert.True(chutes > 0);
    }

    [Fact]
    public void WithoutAlignmentFloorsAreIndependent()
    {
        // そろえない設定では、前のように階ごとに別々の場所になる(どこかはずれる)
        Assert.Contains(Enumerable.Range(1, 6), seed => StairAlignment.Gaps(Generate((ulong)seed, 6, FloorSizeMode.Fixed(20, 20), align: false).Floors).Any());
    }

    [Fact]
    public void MovingStairsMovesThePartnerStairs()
    {
        var floors = Generate(3, 3, FloorSizeMode.Fixed(16, 16), align: true).Floors.ToList();
        var p = new GridPos(2, 2);
        MazeEdit.SetFeature(floors[0], floors[0].FindFeature(CellFeature.StairsDown)!.Value, CellFeature.None);
        MazeEdit.SetFeature(floors[0], p, CellFeature.StairsDown);
        Assert.Equal(1, StairAlignment.MovePartner(floors, 0, p, CellFeature.StairsDown));
        Assert.Equal(p, floors[1].FindFeature(CellFeature.StairsUp));
        Assert.Single(floors[1].AllCells(), c => floors[1][c].Feature == CellFeature.StairsUp);

        // 下の階の上り階段を動かすと、上の階の下り階段も動く
        var q = new GridPos(5, 7);
        MazeEdit.SetFeature(floors[2], floors[2].FindFeature(CellFeature.StairsUp)!.Value, CellFeature.None);
        MazeEdit.SetFeature(floors[2], q, CellFeature.StairsUp);
        Assert.Equal(1, StairAlignment.MovePartner(floors, 2, q, CellFeature.StairsUp));
        Assert.Equal(q, floors[1].FindFeature(CellFeature.StairsDown));

        // B1 の上り(町への出口)と最下層の下りには相手がいない
        Assert.Null(StairAlignment.MovePartner(floors, 0, p, CellFeature.StairsUp));
        Assert.Null(StairAlignment.MovePartner(floors, 2, p, CellFeature.StairsDown));
    }

    [Fact]
    public void RegeneratingOneFloorKeepsItsNeighbours()
    {
        // エディタの「この階を自動で作り直す」: 上下の階の階段の座標に合わせて作る
        var floors = Generate(4, 3, FloorSizeMode.Fixed(16, 16), align: true).Floors.ToList();
        var up = floors[0].FindFeature(CellFeature.StairsDown);
        var down = floors[2].FindFeature(CellFeature.StairsUp);
        floors[1] = new MazeGenerator(MazeGenSettings.Load(Content)).Floor(99, 1, 3, 16, 16, up, down);
        Assert.Empty(StairAlignment.Gaps(floors));
        Assert.True(ReachabilityValidator.Validate(new Dungeon("x", floors)).Ok);
    }

    [Fact]
    public void TheSettingIsSavedAndCheckedOnlyWhenOn()
    {
        using var dir = new TestPaths.TempDir();
        string folder = Path.Combine(dir.Path, "aligned");
        var d = Generate(5, 3, FloorSizeMode.Fixed(12, 12), align: false);
        var pack = ScenarioPack.New(folder, "aligned", d.Floors);
        Assert.True(pack.AlignStairs);   // 新しく作るシナリオはそろえる
        pack.Save(Content);
        var back = ScenarioPack.Load(folder, []);
        Assert.True(back.AlignStairs);

        bool gaps = StairAlignment.Gaps(back.Floors).Any();
        Assert.True(gaps);   // そろえずに作った迷宮なので、ずれがある
        var content = ContentLibrary.Load(TestPaths.Content, folder);
        var lines = ScenarioCheck.Run(back, content, GameData.Load(content), Content, (ja, _) => ja, "ja");
        Assert.Equal(gaps, lines.Any(l => l.Level == CheckLevel.Caution && l.Text.Contains("上下の座標をそろえる迷宮")));

        back.AlignStairs = false;
        back.Save(Content);
        var off = ScenarioPack.Load(folder, []);
        Assert.False(off.AlignStairs);
        lines = ScenarioCheck.Run(off, ContentLibrary.Load(TestPaths.Content, folder), GameData.Load(content), Content, (ja, _) => ja, "ja");
        Assert.DoesNotContain(lines, l => l.Text.Contains("上下の座標をそろえる迷宮"));
    }
}
