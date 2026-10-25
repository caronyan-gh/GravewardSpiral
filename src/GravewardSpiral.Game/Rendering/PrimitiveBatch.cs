using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// 三角形で面と線を描く(迷宮の線画用)。足した順に描くので、奥から手前へ足せば手前の面が奥の線を隠す。
/// 座標は 1280x720 の座標で受け取り、拡大率を掛けて描く。clip を渡すと、その四角の外は描かない。
/// </summary>
public sealed class PrimitiveBatch
{
    private static readonly RasterizerState Scissor = new() { CullMode = CullMode.None, ScissorTestEnable = true, MultiSampleAntiAlias = true };

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly List<VertexPositionColor> _vertices = [];
    private float _scale = 1f;
    private RectangleF? _clip;

    public PrimitiveBatch(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false };
    }

    public void Begin(float scale, RectangleF? clip)
    {
        _scale = scale;
        _clip = clip;
        _vertices.Clear();
    }

    private Vector3 P(Vector2 p) => new(p.X * _scale, p.Y * _scale, 0f);

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        _vertices.Add(new VertexPositionColor(P(a), color));
        _vertices.Add(new VertexPositionColor(P(b), color));
        _vertices.Add(new VertexPositionColor(P(c), color));
    }

    /// <summary>四角形(a→b→c→d の順に周る4点)を塗る。</summary>
    public void FillQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    /// <summary>上の辺(a-b)から下の辺(d-c)へ、色がなめらかに変わる四角形。</summary>
    public void GradientQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color top, Color bottom)
    {
        _vertices.Add(new VertexPositionColor(P(a), top));
        _vertices.Add(new VertexPositionColor(P(b), top));
        _vertices.Add(new VertexPositionColor(P(c), bottom));
        _vertices.Add(new VertexPositionColor(P(a), top));
        _vertices.Add(new VertexPositionColor(P(c), bottom));
        _vertices.Add(new VertexPositionColor(P(d), bottom));
    }

    /// <summary>太さのある線。太さは 1280x720 の座標での太さ(実際の画面では 1 ピクセルより細くしない)。</summary>
    public void Line(Vector2 a, Vector2 b, Color color, float thickness)
    {
        var dir = b - a;
        float len = dir.Length();
        if (len < 0.001f) return;
        float half = MathF.Max(0.5f / _scale, thickness / 2f);
        var n = new Vector2(-dir.Y, dir.X) / len * half;
        FillQuad(a + n, b + n, b - n, a - n, color);
    }

    public void Outline(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color, float thickness)
    {
        Line(a, b, color, thickness);
        Line(b, c, color, thickness);
        Line(c, d, color, thickness);
        Line(d, a, color, thickness);
    }

    public void End()
    {
        if (_vertices.Count == 0) return;
        var vp = _device.Viewport;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);
        _effect.View = Matrix.Identity;
        _effect.World = Matrix.Identity;

        var oldRaster = _device.RasterizerState;
        var oldScissor = _device.ScissorRectangle;
        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.None;
        if (_clip is { } c)
        {
            // ScissorRectangle は画面全体での座標なので、ビューポートの位置を足す
            _device.ScissorRectangle = new Rectangle(vp.X + (int)(c.X * _scale), vp.Y + (int)(c.Y * _scale),
                (int)MathF.Ceiling(c.Width * _scale), (int)MathF.Ceiling(c.Height * _scale));
            _device.RasterizerState = Scissor;
        }
        else
        {
            _device.RasterizerState = RasterizerState.CullNone;
        }

        var data = _vertices.ToArray();
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, data, 0, data.Length / 3);
        }
        _device.RasterizerState = oldRaster;
        _device.ScissorRectangle = oldScissor;
        _vertices.Clear();
    }
}
