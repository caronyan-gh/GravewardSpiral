using GravewardSpiral.Core.Content;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// ヘルプ。左に目次(話題と小見出し)、右に本文。目次を上下で選ぶと、本文がその見出しへ飛ぶ。
/// 本文は PageUp/PageDown・左右・ホイールで動かす。取消・メニューのキーで閉じる。
/// 中身は <c>Content/help/&lt;言語&gt;/*.md</c>(<see cref="HelpDoc"/>)。Esc のメニューとタイトルから開く。
/// </summary>
public sealed class HelpScene : Scene
{
    private static readonly RectangleF TocArea = new(40, 40, 330, 610);
    private static readonly RectangleF BodyArea = new(390, 40, 850, 610);
    private const float TocRow = 30f;
    private const float Pad = 28f;

    private HelpDoc _doc = new([]);
    private int[] _toc = [];
    private int _cursor;
    private int _tocTop;
    private float _scroll;

    // 本文を折り返した行(Draw で一度だけ作る)。Y は本文の頭からの位置
    private List<(string Text, float Size, Color Color, float X, float Y, FontStyle Style)>? _lines;
    private Dictionary<int, float> _headingY = [];
    private float _height;

    public override bool IsOverlay => true;

    private int TocVisible => (int)((TocArea.Height - 90) / TocRow);
    private float BodyView => BodyArea.Height - 70;
    private float MaxScroll => Math.Max(0, _height - BodyView);

    public override void Enter()
    {
        _doc = HelpDoc.Load(Path.Combine(AppPaths.Content, "help"), GameServices.Language);
        _toc = _doc.TocIndexes().ToArray();
    }

    public override void Update(GameTime time, InputState input)
    {
        if (input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Menu))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Stack.Pop();
            return;
        }
        if (_toc.Length == 0) return;

        int before = _cursor;
        if (input.Repeat(GameAction.Up)) _cursor = (_cursor + _toc.Length - 1) % _toc.Length;
        if (input.Repeat(GameAction.Down)) _cursor = (_cursor + 1) % _toc.Length;
        if (input.Mouse is { } m && TocArea.Contains(m))
        {
            int row = (int)((m.Y - TocArea.Y - 60) / TocRow);
            int i = _tocTop + row;
            if (row >= 0 && row < TocVisible && i < _toc.Length)
            {
                if (input.MouseMoved) _cursor = i;
                if (input.LeftClicked) { _cursor = i; Jump(); }
            }
        }
        if (_cursor != before)
        {
            Services.Audio.PlaySe(Sounds.Cursor);
            Jump();
        }

        float scroll = _scroll;
        if (input.Repeat(GameAction.PageUp) || input.Repeat(GameAction.Left)) _scroll -= BodyView - 40;
        if (input.Repeat(GameAction.PageDown) || input.Repeat(GameAction.Right)) _scroll += BodyView - 40;
        _scroll -= input.WheelDelta * 60;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        // 本文を動かしたら、目次は今読んでいる見出しに合わせる
        if (_scroll != scroll && _lines != null)
        {
            _cursor = 0;
            for (int i = 0; i < _toc.Length; i++)
                if (_headingY.TryGetValue(_toc[i], out var y) && y <= _scroll + 4) _cursor = i;
        }
        _tocTop = Math.Clamp(_tocTop, Math.Max(0, _cursor - TocVisible + 1), _cursor);
    }

    private void Jump()
    {
        if (_lines != null && _headingY.TryGetValue(_toc[_cursor], out var y)) _scroll = Math.Clamp(y, 0, MaxScroll);
    }

    private void Layout(Draw2D d)
    {
        _lines = [];
        _headingY = [];
        float width = BodyArea.Width - Pad * 2 - 30;   // 測った幅より少し広く描かれることがあるので、余裕を持たせる
        float y = 0;
        for (int i = 0; i < _doc.Blocks.Count; i++)
        {
            var b = _doc.Blocks[i];
            var (size, color, indent, before, style) = b.Kind switch
            {
                HelpBlockKind.Topic => (28f, Theme.Title, 0f, i == 0 ? 0f : 32f, FontStyle.Ui),
                HelpBlockKind.Heading => (21f, Theme.Gold, 0f, 16f, FontStyle.Ui),
                HelpBlockKind.Bullet => (18f, Theme.Text, 22f, 2f, FontStyle.Ui),
                _ => (18f, Theme.Text, 0f, 8f, FontStyle.Ui),
            };
            y += before;
            if (b.Kind is HelpBlockKind.Topic or HelpBlockKind.Heading) _headingY[i] = y;
            if (b.Kind == HelpBlockKind.Bullet) _lines.Add(("・", size, Theme.Dim, 4f, y, style));
            foreach (var line in d.WrapLines(b.Text, width - indent, size, style))
            {
                _lines.Add((line, size, color, indent, y, style));
                y += size + 9;
            }
            if (b.Kind == HelpBlockKind.Topic) _lines.Add(("", 0, Theme.Frame, -1, y - 4, style));
        }
        _height = y;
        Jump();
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        if (_lines == null) Layout(d);
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 240));
        Theme.Panel(d, TocArea);
        Theme.Panel(d, BodyArea);
        d.Text(s["help.title"], new Vector2(TocArea.X + 20, TocArea.Y + 18), 24, Theme.Title, style: FontStyle.Story);

        // 目次
        for (int row = 0; row < TocVisible && _tocTop + row < _toc.Length; row++)
        {
            int i = _tocTop + row;
            var b = _doc.Blocks[_toc[i]];
            var r = new RectangleF(TocArea.X + 10, TocArea.Y + 60 + row * TocRow, TocArea.Width - 20, TocRow - 2);
            if (i == _cursor)
            {
                d.FillRect(r, Theme.SelectedFill);
                d.Rect(r, Theme.SelectedFrame);
            }
            bool topic = b.Kind == HelpBlockKind.Topic;
            float x = r.X + (topic ? 10 : 28);
            float size = topic ? 19 : 17;
            // 長い見出しは、欄に収まるまで字を小さくする
            size = Math.Min(size, size * (r.X + r.Width - 8 - x) / Math.Max(1f, d.Fonts.Measure(b.Text, size).X));
            d.Text(b.Text, new Vector2(x, r.Y + (TocRow - size) / 2 - 1), size, topic ? Theme.Title : Theme.Text);
        }
        if (_tocTop > 0) d.Text("▲", new Vector2(TocArea.X + TocArea.Width / 2, TocArea.Y + 44), 14, Theme.Dim, TextAlign.Center);
        if (_tocTop + TocVisible < _toc.Length) d.Text("▼", new Vector2(TocArea.X + TocArea.Width / 2, TocArea.Y + TocArea.Height - 22), 14, Theme.Dim, TextAlign.Center);

        // 本文(見えるところの行だけ)
        if (_toc.Length == 0) d.Text(s["help.empty"], new Vector2(BodyArea.X + Pad, BodyArea.Y + 40), 20, Theme.Dim);
        float top = BodyArea.Y + 35;
        foreach (var (text, size, color, x, y, style) in _lines!)
        {
            float at = y - _scroll;
            if (at < 0 || at + size > BodyView) continue;
            // 話題の題の下の線(Layout で、字の無い行として入れてある)
            if (x < 0) { d.Line(new Vector2(BodyArea.X + Pad, top + at), new Vector2(BodyArea.X + BodyArea.Width - Pad, top + at), color); continue; }
            d.Text(text, new Vector2(BodyArea.X + Pad + x, top + at), size, color, style: style);
        }
        if (_scroll > 0) d.Text("▲", new Vector2(BodyArea.X + BodyArea.Width / 2, BodyArea.Y + 12), 14, Theme.Dim, TextAlign.Center);
        if (_scroll < MaxScroll) d.Text("▼", new Vector2(BodyArea.X + BodyArea.Width / 2, BodyArea.Y + BodyArea.Height - 22), 14, Theme.Dim, TextAlign.Center);
        d.Text(s["help.hint"], new Vector2(640, 668), 16, Theme.Dim, TextAlign.Center);
    }
}
