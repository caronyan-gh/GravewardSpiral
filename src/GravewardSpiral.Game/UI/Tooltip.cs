using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 言葉の上にマウスを載せたときの説明の窓。画面を描くときに Hover で言葉の場所と説明を渡し、
/// 全部の画面を描いたあとに Draw で一番上に出す(最後に渡したものが勝つので、上に重なる画面のものが出る)。
/// </summary>
public static class Tooltip
{
    /// <summary>今のマウスの場所(1280x720 の座標。窓の外なら null)。毎フレーム、画面を動かす前に入れる。</summary>
    public static Vector2? Mouse { get; set; }

    private static (RectangleF Area, string Title, string Body)? _hit;

    /// <summary>言葉の場所にマウスが載っていれば、その説明を出すことにする。載っていれば true(言葉を明るくする用)。</summary>
    public static bool Hover(RectangleF area, string title, string body)
    {
        if (Mouse is not { } m || !area.Contains(m)) return false;
        _hit = (area, title, body);
        return true;
    }

    /// <summary>それまでに渡された説明を消す(上に別の画面を重ねて描く前に)。</summary>
    public static void Clear() => _hit = null;

    /// <summary>言葉の場所にマウスが載っているか(説明は出さない)。</summary>
    public static bool Over(RectangleF area) => Mouse is { } m && area.Contains(m);

    /// <summary>説明の窓を描く(言葉のすぐ下。画面の端では内側へ寄せ、下に入らなければ上に出す)。</summary>
    public static void Draw(Draw2D d)
    {
        if (_hit is not { } h) return;
        _hit = null;
        const float width = 400f, pad = 12f, titleSize = 19f, gap = 5f;
        var title = d.WrapLines(h.Title, width - 2 * pad, titleSize);
        // 長い説明は、画面に収まるまで字を小さくする(12 まで)
        float bodySize = 17f;
        List<string> body;
        float Height() => pad * 2 + title.Count * (titleSize + gap) + 1 + body.Count * (bodySize + gap) - gap;
        while (true)
        {
            body = d.WrapLines(h.Body, width - 2 * pad, bodySize);
            if (Height() <= VirtualScreen.Height - 16 || bodySize <= 12) break;
            bodySize -= 1;
        }
        // 一番小さくしても入らなければ、入るところまでにして「…」で終える
        if (Height() > VirtualScreen.Height - 16)
        {
            int fit = Math.Max(1, (int)((VirtualScreen.Height - 16 - pad * 2 - title.Count * (titleSize + gap) - 1 + gap) / (bodySize + gap)));
            body = [.. body.Take(fit - 1), "…"];
        }
        float height = Height();
        float x = Math.Clamp(h.Area.X, 8, VirtualScreen.Width - width - 8);
        float y = h.Area.Y + h.Area.Height + 6;
        if (y + height > VirtualScreen.Height - 8) y = Math.Max(8, h.Area.Y - height - 6);
        var box = new RectangleF(x, y, width, height);
        d.FillRect(box, new Color(16, 22, 32));
        d.Rect(box, Theme.SelectedFrame, 1f);
        float ly = y + pad;
        foreach (var line in title)
        {
            d.Text(line, new Vector2(x + pad, ly), titleSize, Theme.Title);
            ly += titleSize + gap;
        }
        ly += 1;
        foreach (var line in body)
        {
            d.Text(line, new Vector2(x + pad, ly), bodySize, Theme.Text);
            ly += bodySize + gap;
        }
    }
}

/// <summary>説明の窓を出せる言葉(説明が無ければ、ただの文字)。</summary>
public sealed record LinkWord(string Text, string? Title = null, string? Body = null)
{
    public bool IsLink => !string.IsNullOrWhiteSpace(Body);
}

/// <summary>
/// 言葉を区切りでつないで、欄の幅で折り返しながら並べる(言葉の途中では折り返さない)。
/// 説明のある言葉は点線の下線を引き、マウスを載せると明るくして説明の窓を出す。
/// </summary>
public static class LinkFlow
{
    /// <summary>
    /// 並べて描き、次の行の y を返す。lead は先頭に付ける見出し(「1 レベル: 」など。説明は無い)。
    /// draw が false なら描かずに、次の行の y だけを返す(高さを測る)。
    /// </summary>
    public static float Draw(Draw2D d, IReadOnlyList<LinkWord> words, string separator, Vector2 position, float width, float size, Color color, float lineGap = 6f, string? lead = null, bool draw = true)
    {
        float x = position.X, y = position.Y;
        if (lead != null)
        {
            if (draw) d.Text(lead, position, size, color);
            x += d.Fonts.Measure(lead, size).X;
        }
        float sepW = d.Fonts.Measure(separator, size).X;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            float ww = d.Fonts.Measure(w.Text, size).X;
            if (x > position.X && (i > 0 || lead == null) && x + ww > position.X + width)
            {
                x = position.X;
                y += size + lineGap;
            }
            if (draw) Word(d, w, new Vector2(x, y), size, color);
            x += ww;
            if (i < words.Count - 1)
            {
                if (draw) d.Text(separator, new Vector2(x, y), size, color);
                x += sepW;
            }
        }
        return y + size + lineGap;
    }

    /// <summary>言葉を1つ描く(説明があれば下線と説明の窓)。描いた幅を返す。</summary>
    public static float Word(Draw2D d, LinkWord w, Vector2 at, float size, Color color)
    {
        float ww = d.Fonts.Measure(w.Text, size).X;
        if (!w.IsLink)
        {
            d.Text(w.Text, at, size, color);
            return ww;
        }
        var area = new RectangleF(at.X - 2, at.Y - 2, ww + 4, size + 6);
        bool over = Tooltip.Hover(area, w.Title ?? w.Text, w.Body!);
        var c = over ? Theme.Title : color;
        d.Text(w.Text, at, size, c);
        // 点線の下線(説明が見られる印)
        float uy = at.Y + size + 2;
        var line = over ? Theme.Title : Theme.Frame;
        for (float ux = at.X; ux < at.X + ww; ux += 6) d.Line(new Vector2(ux, uy), new Vector2(Math.Min(ux + 3, at.X + ww), uy), line, 1f);
        return ww;
    }
}
