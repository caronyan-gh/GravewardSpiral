using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>タイトル画面(仮)。後ろに、迷宮の線画の雰囲気だけを出す。</summary>
public sealed class TitleScene : Scene
{
    private static readonly string[] MenuKeys = ["title.new", "title.continue", "title.options", "title.quit"];
    private const float MenuX = 640f;
    private const float MenuTop = 420f;
    private const float MenuStep = 44f;
    private const float MenuWidth = 320f;
    private const string LogoPath = "srd/art/title/logo.png";
    private static readonly RectangleF LogoArea = new(200, 36, 880, 360);

    public const string Bgm = "audio/bgm/title.mp3";

    private int _cursor;
    private string? _message;
    private double _messageTime;
    private double _time;

    public override void Enter()
    {
        Services.UseScenario(null);   // シナリオを遊んだあとは、標準のパックに戻す
        Services.Audio.PlayBgm(Bgm);
    }

    public override void Update(GameTime time, InputState input)
    {
        int before = _cursor;
        double dt = time.ElapsedGameTime.TotalSeconds;
        _time += dt;
        if (_message != null && (_messageTime -= dt) <= 0) _message = null;

        if (input.Repeat(GameAction.Up)) _cursor = (_cursor + MenuKeys.Length - 1) % MenuKeys.Length;
        if (input.Repeat(GameAction.Down)) _cursor = (_cursor + 1) % MenuKeys.Length;

        int? hovered = HitTest(input.Mouse);
        if (hovered.HasValue && input.MouseMoved) _cursor = hovered.Value;

        if (input.Pressed(GameAction.Confirm) || (input.LeftClicked && hovered.HasValue))
        {
            Services.Audio.PlaySe(Sounds.Confirm);
            Choose(MenuKeys[_cursor]);
        }
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            _cursor = MenuKeys.Length - 1;
        }
        if (_cursor != before) Services.Audio.PlaySe(Sounds.Cursor);
    }

    private static int? HitTest(Vector2? mouse)
    {
        if (mouse is not { } m) return null;
        for (int i = 0; i < MenuKeys.Length; i++)
        {
            if (ItemRect(i).Contains(m)) return i;
        }
        return null;
    }

    private static RectangleF ItemRect(int i) =>
        new(MenuX - MenuWidth / 2, MenuTop + i * MenuStep - 6, MenuWidth, MenuStep - 4);

    private void Choose(string key)
    {
        if (key == "title.quit")
        {
            Services.Exit();
            return;
        }
        if (key == "title.new")
        {
            Stack.Replace(new NewGameScene());
            return;
        }
        if (key == "title.options")
        {
            Stack.Push(new OptionsScene());
            return;
        }
        if (key == "title.continue")
        {
            if (Services.Saves.List().Count > 0)
            {
                Stack.Replace(new SlotScene(SlotScene.Purpose.Load));
                return;
            }
            _message = Services.Strings["title.no_save"];
            _messageTime = 2.0;
            return;
        }
        _message = Services.Strings["title.not_yet"];
        _messageTime = 2.0;
    }

    public override void Draw(Draw2D d)
    {
        DrawCorridor(d);
        var s = Services.Strings;

        var logo = Services.Textures.Get(LogoPath);
        if (logo != null)
        {
            d.Image(logo, LogoArea, Color.White);
        }
        else
        {
            d.Text(s["title.name"], new Vector2(640, 170), 72, new Color(220, 228, 236), TextAlign.Center, FontStyle.Story);
            d.Line(new Vector2(380, 268), new Vector2(900, 268), new Color(120, 140, 160), 1.5f);
        }

        for (int i = 0; i < MenuKeys.Length; i++)
        {
            bool selected = i == _cursor;
            if (selected)
            {
                var r = ItemRect(i);
                d.FillRect(r, new Color(40, 60, 80, 180));
                d.Rect(r, new Color(150, 190, 230), 1.5f);
            }
            d.Text(s[MenuKeys[i]], new Vector2(MenuX, MenuTop + i * MenuStep), 28,
                selected ? Color.White : new Color(150, 160, 170), TextAlign.Center, FontStyle.Story);
        }

        if (_message != null)
            d.Text(_message, new Vector2(640, 610), 22, new Color(230, 200, 120), TextAlign.Center);
        d.Text(s["title.hint"], new Vector2(640, 660), 16, new Color(110, 120, 130), TextAlign.Center);
        d.Text(s["title.credit"], new Vector2(1268, 698), 12, new Color(80, 88, 96), TextAlign.Right);
    }

    /// <summary>奥へ続く通路の線画。奥行きの枠を、少しずつ奥へ流す。</summary>
    private void DrawCorridor(Draw2D d)
    {
        var center = new Vector2(640, 330);
        float phase = (float)(_time * 0.25 % 1.0);
        Vector2? prevTl = null, prevBr = null;
        for (int i = 0; i < 7; i++)
        {
            float depth = i + phase;
            float k = 1f / (1f + depth * 0.9f);
            var half = new Vector2(900 * k, 420 * k);
            var tl = center - half;
            var br = center + half;
            byte a = (byte)(90 * k + 10);
            var color = new Color((byte)60, (byte)90, (byte)120, a);
            d.Rect(new RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y), color, 1.2f);
            if (prevTl is { } ptl && prevBr is { } pbr)
            {
                d.Line(ptl, tl, color, 1.2f);
                d.Line(new Vector2(pbr.X, ptl.Y), new Vector2(br.X, tl.Y), color, 1.2f);
                d.Line(new Vector2(ptl.X, pbr.Y), new Vector2(tl.X, br.Y), color, 1.2f);
                d.Line(pbr, br, color, 1.2f);
            }
            prevTl = tl;
            prevBr = br;
        }
    }
}
