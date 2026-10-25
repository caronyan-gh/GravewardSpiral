using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 戦闘のログを読み返す。戦闘の中(メニューのキー)と、キャンプの「前の戦闘のログ」から開く。
/// 上下・PageUp/PageDown・ホイールで動かし、決定・取消・メニューのキーで閉じる。初めは一番下(新しい行)を見せる。
/// </summary>
public sealed class BacklogScene : Scene
{
    private static readonly RectangleF Area = new(140, 40, 1000, 610);
    private const float LineHeight = 30f;
    private const int Visible = 17;

    private readonly IReadOnlyList<(string Text, Color? Color)> _lines;
    private int _top;

    public BacklogScene(IReadOnlyList<(string Text, Color? Color)> lines)
    {
        _lines = lines;
        _top = MaxTop;
    }

    public override bool IsOverlay => true;

    private int MaxTop => Math.Max(0, _lines.Count - Visible);

    public override void Update(GameTime time, InputState input)
    {
        if (input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Menu) || input.LeftClicked)
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Stack.Pop();
            return;
        }
        int before = _top;
        if (input.Repeat(GameAction.Up)) _top--;
        if (input.Repeat(GameAction.Down)) _top++;
        if (input.Repeat(GameAction.PageUp) || input.Repeat(GameAction.Left)) _top -= Visible - 1;
        if (input.Repeat(GameAction.PageDown) || input.Repeat(GameAction.Right)) _top += Visible - 1;
        _top -= input.WheelDelta * 3;
        _top = Math.Clamp(_top, 0, MaxTop);
        if (_top != before) Services.Audio.PlaySe(Sounds.Cursor);
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 200));
        Theme.Panel(d, Area);
        d.Text(s["backlog.title"], new Vector2(Area.X + 40, Area.Y + 20), 26, Theme.Title, style: FontStyle.Story);
        if (_lines.Count > Visible)
            d.Text($"{_top + 1}-{Math.Min(_lines.Count, _top + Visible)} / {_lines.Count}", new Vector2(Area.X + Area.Width - 40, Area.Y + 26), 18, Theme.Dim, TextAlign.Right);
        if (_lines.Count == 0)
            d.Text(s["backlog.empty"], new Vector2(Area.X + 40, Area.Y + 80), 22, Theme.Dim, style: FontStyle.Story);
        for (int i = _top; i < Math.Min(_lines.Count, _top + Visible); i++)
        {
            var (text, color) = _lines[i];
            var at = new Vector2(Area.X + 40, Area.Y + 70 + (i - _top) * LineHeight);
            // 長い行は、枠に収まるまで字を小さくする
            float size = Math.Min(22f, 22f * (Area.Width - 80) / Math.Max(1f, d.Fonts.Measure(text, 22, FontStyle.Story).X));
            d.Text(text, at + new Vector2(0, (22 - size) / 2), size, color ?? Color.White, style: FontStyle.Story);
        }
        // まだ上や下に行があることを矢印で見せる
        if (_top > 0) d.Text("▲", new Vector2(Area.X + Area.Width / 2, Area.Y + 48), 16, Theme.Dim, TextAlign.Center);
        if (_top < MaxTop) d.Text("▼", new Vector2(Area.X + Area.Width / 2, Area.Y + Area.Height - 26), 16, Theme.Dim, TextAlign.Center);
        d.Text(s["backlog.hint"], new Vector2(640, 668), 16, Theme.Dim, TextAlign.Center);
    }
}
