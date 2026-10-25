using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// 画面の配置は 1280x720 の座標で決め、描くときはウィンドウの実際の大きさに合わせて拡大する。
/// 16:9 でないウィンドウでは、上下か左右に帯を出す。
/// 拡大は行列でまとめて掛けず、座標と文字の大きさに掛けてから描く(線と文字がにじまない)。
/// </summary>
public sealed class VirtualScreen
{
    public const int Width = 1280;
    public const int Height = 720;

    public float Scale { get; private set; } = 1f;

    /// <summary>実際の画面の上で、ゲームの画面が描かれる四角。</summary>
    public Rectangle Area { get; private set; } = new(0, 0, Width, Height);

    public void Update(int backBufferWidth, int backBufferHeight)
    {
        Scale = Math.Min(backBufferWidth / (float)Width, backBufferHeight / (float)Height);
        int w = (int)MathF.Round(Width * Scale);
        int h = (int)MathF.Round(Height * Scale);
        Area = new Rectangle((backBufferWidth - w) / 2, (backBufferHeight - h) / 2, w, h);
    }

    public Viewport Viewport => new(Area);

    /// <summary>実際の画面の座標(マウスなど)を 1280x720 の座標にする。画面の外なら null。</summary>
    public Vector2? ToVirtual(Point screen)
    {
        if (!Area.Contains(screen)) return null;
        return new Vector2((screen.X - Area.X) / Scale, (screen.Y - Area.Y) / Scale);
    }
}
