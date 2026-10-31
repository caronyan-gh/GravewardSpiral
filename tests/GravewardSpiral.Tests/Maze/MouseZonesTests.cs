using GravewardSpiral.Core.Maze;

namespace GravewardSpiral.Tests.Maze;

/// <summary>迷宮の絵をマウスで押したときの領域(真ん中の円・上下左右の8つ)。</summary>
public class MouseZonesTests
{
    // 迷宮の画面の絵の枠と同じ大きさ(左上 24, 24、812 × 540)
    private static MouseMove At(float px, float py) => MouseZones.At(24, 24, 812, 540, px, py);

    private const float Cx = 24 + 406, Cy = 24 + 270;

    [Fact]
    public void TheSmallCircleUpperRightSearches()
    {
        // 右上の円(真ん中 24 + 812 × 0.70 = 592.4、24 + 540 × 0.13 = 94.2、半径 540 × 0.09 = 48.6)
        Assert.Equal(MouseMove.Search, At(592, 94));
        Assert.Equal(MouseMove.Search, At(592 + 40, 94 + 20));
        Assert.Equal(MouseMove.Forward, At(592 - 60, 94));
        // 真ん中はもう調べるではない(上は前進、下は下がる)
        Assert.Equal(MouseMove.Forward, At(Cx, Cy - 5));
        Assert.Equal(MouseMove.Back, At(Cx + 1, Cy + 5));
    }

    [Fact]
    public void EachSectorHasItsMove()
    {
        Assert.Equal(MouseMove.Forward, At(Cx, 40));
        Assert.Equal(MouseMove.TurnLeft, At(60, Cy - 20));
        Assert.Equal(MouseMove.TurnRight, At(800, Cy - 20));
        Assert.Equal(MouseMove.StrafeLeft, At(60, Cy + 20));
        Assert.Equal(MouseMove.StrafeRight, At(800, Cy + 20));
        Assert.Equal(MouseMove.TurnAround, At(Cx - 40, 550));
        Assert.Equal(MouseMove.Back, At(Cx + 40, 550));
    }

    [Fact]
    public void TheDiagonalsRunCornerToCorner()
    {
        // 左上の角の近く: 対角線の少し上は前進、少し下は左を向く
        Assert.Equal(MouseMove.Forward, At(24 + 100, 24 + 60));
        Assert.Equal(MouseMove.TurnLeft, At(24 + 100, 24 + 72));
        // 右下の角の近く: 対角線の少し下は下がる、少し上は右へ横歩き
        Assert.Equal(MouseMove.Back, At(836 - 100, 564 - 60));
        Assert.Equal(MouseMove.StrafeRight, At(836 - 100, 564 - 72));
    }

    [Fact]
    public void OutsideTheViewIsNothing()
    {
        Assert.Equal(MouseMove.None, At(10, Cy));
        Assert.Equal(MouseMove.None, At(Cx, 600));
        Assert.Equal(MouseMove.None, At(836, Cy));   // 右の端の線はもう外
        Assert.True(MouseZones.Repeats(MouseMove.Forward));
        Assert.False(MouseZones.Repeats(MouseMove.TurnLeft));
        Assert.False(MouseZones.Repeats(MouseMove.Search));
    }
}
