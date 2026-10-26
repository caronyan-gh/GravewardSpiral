using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game.Rendering;

/// <summary>
/// 1280x720 の座標で線・四角・文字を描く。座標と太さには、描く直前に拡大率を掛ける。
/// 迷宮の線画もこれで描く。
/// </summary>
public sealed class Draw2D
{
    private readonly Texture2D _pixel;

    public SpriteBatch Batch { get; }
    public TextRenderer Fonts { get; }
    public float Scale { get; private set; } = 1f;

    private readonly PrimitiveBatch _primitives;

    public Draw2D(GraphicsDevice device, TextRenderer fonts)
    {
        Batch = new SpriteBatch(device);
        Fonts = fonts;
        _primitives = new PrimitiveBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);
    }

    public void Begin(float scale)
    {
        Scale = scale;
        Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, RasterizerState.CullNone);
    }

    public void End() => Batch.End();

    /// <summary>
    /// 三角形で描く(迷宮の線画など)。ここまでに足した絵を先に描いてから描き、終わったら続きを描けるようにする。
    /// </summary>
    public void Primitives(RectangleF? clip, Action<PrimitiveBatch> draw)
    {
        Batch.End();
        _primitives.Begin(Scale, clip);
        draw(_primitives);
        _primitives.End();
        Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, RasterizerState.CullNone);
    }

    public Vector2 ToScreen(Vector2 p) => p * Scale;

    public void FillRect(RectangleF r, Color color)
    {
        var a = ToScreen(new Vector2(r.X, r.Y));
        var b = ToScreen(new Vector2(r.X + r.Width, r.Y + r.Height));
        Batch.Draw(_pixel, new Rectangle((int)MathF.Round(a.X), (int)MathF.Round(a.Y),
            (int)MathF.Round(b.X - a.X), (int)MathF.Round(b.Y - a.Y)), color);
    }

    public void Rect(RectangleF r, Color color, float thickness = 1f)
    {
        FillRect(new RectangleF(r.X, r.Y, r.Width, thickness), color);
        FillRect(new RectangleF(r.X, r.Y + r.Height - thickness, r.Width, thickness), color);
        FillRect(new RectangleF(r.X, r.Y, thickness, r.Height), color);
        FillRect(new RectangleF(r.X + r.Width - thickness, r.Y, thickness, r.Height), color);
    }

    public void Line(Vector2 from, Vector2 to, Color color, float thickness = 1f)
    {
        var a = ToScreen(from);
        var b = ToScreen(to);
        var delta = b - a;
        float length = delta.Length();
        if (length < 0.01f) return;
        float t = MathF.Max(1f, thickness * Scale);
        float angle = MathF.Atan2(delta.Y, delta.X);
        Batch.Draw(_pixel, a, null, color, angle, new Vector2(0f, 0.5f), new Vector2(length, t), SpriteEffects.None, 0f);
    }

    /// <summary>画像を四角に合わせて描く(縦横比はそのまま、四角の中に収める)。</summary>
    public void Image(Texture2D texture, RectangleF area, Color tint)
    {
        float fit = MathF.Min(area.Width / texture.Width, area.Height / texture.Height);
        float w = texture.Width * fit, h = texture.Height * fit;
        var a = ToScreen(new Vector2(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2));
        var size = new Vector2(w, h) * Scale;
        Batch.Draw(texture, new Rectangle((int)MathF.Round(a.X), (int)MathF.Round(a.Y), (int)MathF.Round(size.X), (int)MathF.Round(size.Y)), tint);
    }

    /// <summary>
    /// 幅に収まるように折り返して描く(日本語は1文字ずつ、改行はそのまま)。描いたあとの次の行の y を返す。
    /// </summary>
    public float TextWrapped(string text, Vector2 position, float width, float size, Color color, float lineGap = 8f,
        FontStyle style = FontStyle.Ui)
    {
        float y = position.Y;
        foreach (var line in WrapLines(text, width, size, style))
        {
            Text(line, new Vector2(position.X, y), size, color, style: style);
            y += size + lineGap;
        }
        return y;
    }

    /// <summary>
    /// 幅に収まるように折り返した行(日本語は1文字ずつ、英語などは語の切れ目で。改行はそのまま、空の段落は空の行)。
    /// 句読点や閉じ括弧は行の頭に出さず、前の行の終わりにぶら下げる(禁則)。
    /// </summary>
    public List<string> WrapLines(string text, float width, float size, FontStyle style = FontStyle.Ui)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            bool byWord = paragraph.Contains(' ') && !paragraph.Any(c => c >= 0x3000);
            var pieces = byWord ? paragraph.Split(' ').Select((w, i) => i == 0 ? w : " " + w) : paragraph.Select(c => c.ToString());
            foreach (var piece in pieces)
            {
                bool hang = !byWord && piece.Length == 1 && NoLineStart.Contains(piece[0]);
                if (line.Length > 0 && !hang && Fonts.Measure(line.ToString() + piece, size, style).X > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    line.Append(piece.TrimStart());
                    continue;
                }
                line.Append(piece);
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>行の頭に置かない文字(句読点、閉じ括弧、小さい仮名、長音など)。</summary>
    private const string NoLineStart = "、。，．,.・：；？！?!」』）)〕］]｝}〉》】ーぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ…‥々";

    public void Text(string text, Vector2 position, float size, Color color, TextAlign align = TextAlign.Left,
        FontStyle style = FontStyle.Ui) =>
        Fonts.Draw(this, text, position, size, color, align, style);
}

/// <summary>1280x720 の座標での四角(小数あり)。</summary>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    public bool Contains(Vector2 p) => p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
}
