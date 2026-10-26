using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>この迷宮のいわれ(新しいゲームの始めに見せた序文)を読み返す。町とキャンプから開く。決定・取消・クリックで閉じる。</summary>
public sealed class StoryScene : Scene
{
    public override bool IsOverlay => true;

    /// <summary>読み返せる序文があるか。</summary>
    public static bool Available(GameServices services) => services.Session?.World?.Prologue != null;

    private TaleView? _tale;

    public override void Update(GameTime time, InputState input)
    {
        // 長いいわれはスクロールする(決定でページを送り、最後まで来たら閉じる。取消はすぐ閉じる)
        bool done = input.Pressed(GameAction.Cancel) || (_tale?.Update(input, Services.Audio) ?? (input.Pressed(GameAction.Confirm) || input.LeftClicked));
        if (done)
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Stack.Pop();
        }
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 200));
        var area = new RectangleF(140, 40, 1000, 610);
        Theme.Panel(d, area);
        d.Text(s["story.title"], new Vector2(area.X + 40, area.Y + 24), 26, Theme.Title, style: FontStyle.Story);
        if (Services.Session?.World?.Prologue is { } p)
        {
            _tale ??= new TaleView(Services.Name(p));
            _tale.Draw(d, new Vector2(area.X + 40, area.Y + 76), area.Width - 100, area.Height - 110);
        }
        d.Text(s["story.close"], new Vector2(640, 668), 16, Theme.Dim, TextAlign.Center);
    }
}
