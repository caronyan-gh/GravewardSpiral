using GravewardSpiral.Core.Characters;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// クラスの成長表(訓練場でクラスを選んでいるときに開く)。L1〜20 で得るもの(ClassGrowth.Table)を並べ、
/// 今のレベルまでは暗く、次に上げるレベルは金色にする。上下・ホイールで送り、取消・決定・地図のキー・クリックで閉じる。
/// </summary>
public sealed class ClassGrowthScene(ClassDef cls, int nowLevel) : Scene
{
    private static readonly RectangleF Panel = new(140, 40, 1000, 640);
    private const float LineSize = 19f, LineGap = 6f, LevelWidth = 70f;

    private List<(int Level, List<LinkWord> Words)> _rows = [];
    private float _scroll;
    private float _contentHeight;

    public override bool IsOverlay => true;

    public override void Enter() => _rows = ClassGrowth.Table(Services, cls);

    public override void Update(GameTime time, InputState input)
    {
        if (input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Map) || input.LeftClicked)
        {
            Services.Audio.PlaySe(Audio.Sounds.Cancel);
            Stack.Pop();
            return;
        }
        float max = Math.Max(0, _contentHeight - (Panel.Height - 110));
        if (input.Repeat(GameAction.Down)) _scroll += 60;
        if (input.Repeat(GameAction.Up)) _scroll -= 60;
        if (input.WheelDelta != 0) _scroll -= input.WheelDelta * 60;
        _scroll = Math.Clamp(_scroll, 0, max);
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 235));   // 下の訓練場の字が透けないよう濃く
        Theme.Panel(d, Panel);
        d.Text(s.Format("growth.title", Services.Name(cls.Name)), new Vector2(Panel.X + 24, Panel.Y + 16), 26, Theme.Title);
        d.Text(nowLevel > 0 ? s.Format("growth.now", nowLevel) : s["growth.new"], new Vector2(Panel.X + Panel.Width - 24, Panel.Y + 22), 18, Theme.Dim, TextAlign.Right);
        float top = Panel.Y + 64, bottom = Panel.Y + Panel.Height - 44, width = Panel.Width - 48 - LevelWidth;
        float y = top - _scroll;
        // 技と能力の名前の上にマウスを載せると、その説明
        string sep = s["common.list_sep"];
        foreach (var (level, words) in _rows)
        {
            var color = level <= nowLevel ? Theme.Dim : level == nowLevel + 1 ? Theme.Gold : Theme.Text;
            var at = new Vector2(Panel.X + 24 + LevelWidth, y);
            float h = LinkFlow.Draw(d, words, sep, at, width, LineSize, color, LineGap, draw: false) - y + 4;
            if (y >= top - 4 && y + h < bottom)
            {
                d.Text($"L{level}", new Vector2(Panel.X + 24, y), LineSize, color);
                LinkFlow.Draw(d, words, sep, at, width, LineSize, color, LineGap);
            }
            y += h;
        }
        _contentHeight = y + _scroll - top;
        if (_scroll > 0) d.Text("▲", new Vector2(Panel.X + Panel.Width / 2, Panel.Y + 46), 14, Theme.Dim, TextAlign.Center);
        if (_scroll < Math.Max(0, _contentHeight - (Panel.Height - 110))) d.Text("▼", new Vector2(Panel.X + Panel.Width / 2, bottom + 4), 14, Theme.Dim, TextAlign.Center);
        d.Text(s["growth.hint"], new Vector2(Panel.X + Panel.Width / 2, Panel.Y + Panel.Height - 28), 16, Theme.Dim, TextAlign.Center);
    }
}
