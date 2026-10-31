namespace GravewardSpiral.Core.Maze;

/// <summary>迷宮の絵をマウスで押したときの動き。</summary>
public enum MouseMove { None, Search, Forward, StrafeLeft, StrafeRight, TurnLeft, TurnRight, TurnAround, Back }

/// <summary>
/// 迷宮の絵(3D の眺め)を、マウスで動くための領域に分ける(昔の 3D ダンジョンのような操作。ユーザーの絵のとおり)。
/// 角から真ん中への対角線2本と真ん中の横線で分け、上は前進、左上・右上は向きを変える、左下・右下は横歩き(遊んでみて、曲がるほうをよく使うので上にした)。
/// 下の三角は縦に2つに分け、左は振り向く、右は1歩下がる。調べるは、右上の小さな円(前は真ん中に置いたが、歩くときに邪魔だったので動かした)。
/// </summary>
public static class MouseZones
{
    /// <summary>調べる円の真ん中(絵の幅・高さに対する割合)と半径(高さに対する割合)。上の前進の領域の右寄り。</summary>
    public const float SearchX = 0.70f, SearchY = 0.13f, SearchRadius = 0.09f;

    /// <summary>絵(左上 x, y、幅 w、高さ h)の中の点 (px, py) が、どの動きの領域か。絵の外なら None。</summary>
    public static MouseMove At(float x, float y, float w, float h, float px, float py)
    {
        if (w <= 0 || h <= 0 || px < x || py < y || px >= x + w || py >= y + h) return MouseMove.None;
        float sx = px - (x + w * SearchX), sy = py - (y + h * SearchY), r = h * SearchRadius;
        if (sx * sx + sy * sy <= r * r) return MouseMove.Search;
        float dx = px - (x + w / 2), dy = py - (y + h / 2);
        // 対角線の内側(上か下の三角)か: |dx| / (w/2) <= |dy| / (h/2)
        bool vertical = Math.Abs(dx) * h <= Math.Abs(dy) * w;
        if (dy < 0) return vertical ? MouseMove.Forward : dx < 0 ? MouseMove.TurnLeft : MouseMove.TurnRight;
        if (vertical) return dx < 0 ? MouseMove.TurnAround : MouseMove.Back;
        return dx < 0 ? MouseMove.StrafeLeft : MouseMove.StrafeRight;
    }

    /// <summary>押したままで繰り返す動きか(歩く動き。向きを変えるのと調べるのは、1回押すごとに1回)。</summary>
    public static bool Repeats(MouseMove m) => m is MouseMove.Forward or MouseMove.Back or MouseMove.StrafeLeft or MouseMove.StrafeRight;
}
