using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>「はい / いいえ」を聞く小さな窓。下の画面に重ねて出す。</summary>
public sealed class ConfirmScene : Scene
{
    private static readonly RectangleF Box = new(390, 270, 500, 180);

    private readonly string _message;
    private readonly Action _onYes;
    private int _cursor = 1;   // 既定は「いいえ」

    public ConfirmScene(string message, Action onYes)
    {
        _message = message;
        _onYes = onYes;
    }

    public override bool IsOverlay => true;

    private static RectangleF Button(int i) => new(Box.X + 70 + i * 200, Box.Y + 110, 160, 44);

    public override void Update(GameTime time, InputState input)
    {
        int before = _cursor;
        if (input.Repeat(GameAction.Left) || input.Repeat(GameAction.Right) || input.Repeat(GameAction.Up) || input.Repeat(GameAction.Down))
            _cursor = 1 - _cursor;
        for (int i = 0; i < 2; i++)
        {
            if (input.Mouse is { } m && Button(i).Contains(m))
            {
                if (input.MouseMoved) _cursor = i;
                if (input.LeftClicked) Choose(i);
            }
        }
        if (input.Pressed(GameAction.Confirm)) Choose(_cursor);
        else if (input.Pressed(GameAction.Cancel)) Choose(1);
        else if (_cursor != before) Services.Audio.PlaySe(Sounds.Cursor);
    }

    private void Choose(int i)
    {
        Services.Audio.PlaySe(i == 0 ? Sounds.Confirm : Sounds.Cancel);
        Stack.Pop();
        if (i == 0) _onYes();
    }

    public override void Draw(Draw2D d)
    {
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 120));
        d.FillRect(Box, new Color(14, 18, 26, 245));
        d.Rect(Box, new Color(150, 190, 230), 1.5f);
        d.Text(_message, new Vector2(Box.X + Box.Width / 2, Box.Y + 40), 26, Color.White, TextAlign.Center);
        string[] labels = [Services.Strings["common.yes"], Services.Strings["common.no"]];
        for (int i = 0; i < 2; i++)
        {
            var r = Button(i);
            if (i == _cursor)
            {
                d.FillRect(r, new Color(40, 60, 80, 200));
                d.Rect(r, new Color(150, 190, 230), 1.5f);
            }
            d.Text(labels[i], new Vector2(r.X + r.Width / 2, r.Y + 8), 24, i == _cursor ? Color.White : new Color(150, 160, 170), TextAlign.Center);
        }
    }
}
