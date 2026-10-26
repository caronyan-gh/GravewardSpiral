using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 物語の長い文(序文、この迷宮のいわれ)を見せる。収まれば字を大きく(28 から 22 まで)、収まらなければ 24 の字でスクロールする。
/// 上下で1行、PageUp/PageDown とホイールで動かし、決定は「次のページ、最後まで来たら終わり」。続きがあれば ▲▼ を出す。
/// </summary>
public sealed class TaleView(string text)
{
    private const float ScrollSize = 24f, MinFitSize = 22f, MaxSize = 28f;

    private int _top;
    private int _lines, _visible = 1;

    /// <summary>最後の行まで見えているか。</summary>
    public bool AtEnd => _top + _visible >= _lines;

    /// <summary>入力を処理する。決定(かクリック)で、続きがあればページを送り、最後まで来ていれば true(閉じる)。</summary>
    public bool Update(InputState input, AudioPlayer audio)
    {
        int before = _top;
        if (input.Repeat(GameAction.Down)) _top++;
        if (input.Repeat(GameAction.Up)) _top--;
        if (input.Repeat(GameAction.PageDown)) _top += Math.Max(1, _visible - 1);
        if (input.Repeat(GameAction.PageUp)) _top -= Math.Max(1, _visible - 1);
        _top -= input.WheelDelta * 2;
        bool next = input.Pressed(GameAction.Confirm) || input.LeftClicked;
        if (next && !AtEnd)
        {
            _top += Math.Max(1, _visible - 1);
            next = false;
        }
        _top = Math.Clamp(_top, 0, Math.Max(0, _lines - _visible));
        if (_top != before) audio.PlaySe(Sounds.Cursor);
        return next;
    }

    public void Draw(Draw2D d, Vector2 at, float width, float height)
    {
        // 収まるいちばん大きな字を探し、22 でも収まらなければ 24 でスクロールする
        float size = MaxSize;
        List<string> lines;
        for (; ; size -= 1f)
        {
            lines = d.WrapLines(text, width, size, FontStyle.Story);
            if (lines.Count * (size * 1.5f) <= height || size <= MinFitSize) break;
        }
        if (lines.Count * (size * 1.5f) > height)
        {
            size = ScrollSize;
            lines = d.WrapLines(text, width, size, FontStyle.Story);
        }
        float step = size * 1.5f;
        _lines = lines.Count;
        _visible = Math.Max(1, (int)(height / step));
        _top = Math.Clamp(_top, 0, Math.Max(0, _lines - _visible));
        for (int i = 0; i < _visible && _top + i < _lines; i++)
            d.Text(lines[_top + i], new Vector2(at.X, at.Y + i * step), size, Color.White, style: FontStyle.Story);
        if (_top > 0) d.Text("▲", new Vector2(at.X + width / 2, at.Y - 26), 18, Theme.Dim, TextAlign.Center);
        if (!AtEnd) d.Text("▼", new Vector2(at.X + width / 2, at.Y + _visible * step + 2), 18, Theme.Dim, TextAlign.Center);
    }
}
