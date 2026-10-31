using GravewardSpiral.Core.Maze;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 迷宮の絵をマウスで動くときの矢印(↑ ← → ↰ ↱ U ターン ↓ と、調べる虫めがね)。素材は使わず、起動して初めて要るときにコードで描く。
/// 白い形に黒いふちを付けた 32 × 32 の絵で、カーソルと、迷宮の絵に重ねるオーバーレイの両方に使う。
/// </summary>
public static class MazeCursors
{
    public const int Size = 32;

    private static readonly Dictionary<MouseMove, Texture2D> Textures = [];
    private static readonly Dictionary<MouseMove, MouseCursor> Cursors = [];

    /// <summary>形の部品: 太さのある線(端は丸い)と、塗った三角。</summary>
    private abstract record Part
    {
        public abstract bool Covers(float x, float y);
    }

    private sealed record Segment(Vector2 A, Vector2 B, float Half) : Part
    {
        public override bool Covers(float x, float y)
        {
            var p = new Vector2(x, y);
            var ab = B - A;
            float t = ab.LengthSquared() == 0 ? 0 : Math.Clamp(Vector2.Dot(p - A, ab) / ab.LengthSquared(), 0, 1);
            return Vector2.Distance(p, A + ab * t) <= Half;
        }
    }

    private sealed record Triangle(Vector2 A, Vector2 B, Vector2 C) : Part
    {
        public override bool Covers(float x, float y)
        {
            static float Side(Vector2 p, Vector2 a, Vector2 b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            var p = new Vector2(x, y);
            float d1 = Side(p, A, B), d2 = Side(p, B, C), d3 = Side(p, C, A);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }
    }

    private static Segment Line(float x1, float y1, float x2, float y2, float half = 2.5f) => new(new(x1, y1), new(x2, y2), half);

    private static Triangle Head(float x1, float y1, float x2, float y2, float x3, float y3) => new(new(x1, y1), new(x2, y2), new(x3, y3));

    /// <summary>形(上向きの矢印などを、32 × 32 の座標で)。左右・上下の向きは写して作る。</summary>
    private static List<Part> Shape(MouseMove m)
    {
        switch (m)
        {
            case MouseMove.Forward: return [Line(16, 27, 16, 12), Head(16, 3, 7, 14, 25, 14)];
            case MouseMove.Back: return Flip(Shape(MouseMove.Forward), x: false);
            case MouseMove.StrafeLeft: return [Line(27, 16, 12, 16), Head(3, 16, 14, 7, 14, 25)];
            case MouseMove.StrafeRight: return Flip(Shape(MouseMove.StrafeLeft), x: true);
            case MouseMove.TurnLeft: return [Line(22, 28, 22, 13), Line(22, 13, 12, 13), Head(4, 13, 13, 5, 13, 21)];
            case MouseMove.TurnRight: return Flip(Shape(MouseMove.TurnLeft), x: true);
            case MouseMove.TurnAround:
            {
                // 右の脚を上がって、上で丸く折り返し、左の脚を下りる(先が下向き)
                var parts = new List<Part> { Line(22, 28, 22, 14), Line(10, 14, 10, 19), Head(10, 28, 3, 19, 17, 19) };
                for (int i = 0; i < 12; i++)
                {
                    double a0 = Math.PI * i / 12, a1 = Math.PI * (i + 1) / 12;
                    parts.Add(Line(16 + 6 * (float)Math.Cos(a0), 14 - 6 * (float)Math.Sin(a0), 16 + 6 * (float)Math.Cos(a1), 14 - 6 * (float)Math.Sin(a1)));
                }
                return parts;
            }
            case MouseMove.Search:
            {
                // 虫めがね: 丸いふちと、右下への柄
                var parts = new List<Part> { Line(19, 19, 27, 27, 3f) };
                for (int i = 0; i < 24; i++)
                {
                    double a0 = Math.PI * 2 * i / 24, a1 = Math.PI * 2 * (i + 1) / 24;
                    parts.Add(Line(13 + 8 * (float)Math.Cos(a0), 13 + 8 * (float)Math.Sin(a0), 13 + 8 * (float)Math.Cos(a1), 13 + 8 * (float)Math.Sin(a1), 2f));
                }
                return parts;
            }
            default: return [];
        }
    }

    private static List<Part> Flip(List<Part> parts, bool x)
    {
        Vector2 F(Vector2 v) => x ? new(Size - 1 - v.X, v.Y) : new(v.X, Size - 1 - v.Y);
        return parts.Select(p => p switch
        {
            Segment s => (Part)(s with { A = F(s.A), B = F(s.B) }),
            Triangle t => t with { A = F(t.A), B = F(t.B), C = F(t.C) },
            _ => p,
        }).ToList();
    }

    /// <summary>その動きの絵(白い形に黒いふち)。</summary>
    public static Texture2D Texture(GraphicsDevice device, MouseMove m)
    {
        if (Textures.TryGetValue(m, out var cached)) return cached;
        var parts = Shape(m);
        var inside = new bool[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                inside[y * Size + x] = parts.Any(p => p.Covers(x + 0.5f, y + 0.5f));
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (inside[y * Size + x])
                {
                    pixels[y * Size + x] = Color.White;
                    continue;
                }
                // ふち: 2 ドット以内に形があれば黒
                bool edge = false;
                for (int dy = -2; dy <= 2 && !edge; dy++)
                    for (int dx = -2; dx <= 2 && !edge; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        edge = dx * dx + dy * dy <= 5 && nx >= 0 && ny >= 0 && nx < Size && ny < Size && inside[ny * Size + nx];
                    }
                pixels[y * Size + x] = edge ? Color.Black : Color.Transparent;
            }
        }
        var texture = new Texture2D(device, Size, Size);
        texture.SetData(pixels);
        Textures[m] = texture;
        return texture;
    }

    /// <summary>その動きのカーソル(押す点は真ん中)。</summary>
    public static MouseCursor Cursor(GraphicsDevice device, MouseMove m)
    {
        if (Cursors.TryGetValue(m, out var cached)) return cached;
        var cursor = MouseCursor.FromTexture2D(Texture(device, m), Size / 2, Size / 2);
        Cursors[m] = cursor;
        return cursor;
    }
}
