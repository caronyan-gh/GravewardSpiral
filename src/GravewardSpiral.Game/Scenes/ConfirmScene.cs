using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>「はい / いいえ」を聞く小さな窓。下の画面に重ねて出す。</summary>
public sealed class ConfirmScene : Scene
{
    /// <summary>窓の大きさ(文の長さに合わせて、描くときに決め直す。長い文は幅 1000 まで広げ、それでも入らなければ折り返す)。</summary>
    private RectangleF Box = new(390, 270, 500, 180);

    private const float TextSize = 26f, LineGap = 8f;

    private readonly string _message;
    private readonly Action _onYes;
    private int _cursor = 1;   // 既定は「いいえ」

    public ConfirmScene(string message, Action onYes)
    {
        _message = message;
        _onYes = onYes;
    }

    public override bool IsOverlay => true;

    private RectangleF Button(int i) => new(Box.X + Box.Width / 2 - 180 + i * 200, Box.Y + Box.Height - 70, 160, 44);

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
        // 文に合わせて窓を広げる(幅は 500〜1000。入らなければ折り返して、高さを足す)
        string[] lines = _message.Split('\n');
        float textWidth = lines.Max(l => d.Fonts.Measure(l, TextSize).X);
        float width = Math.Clamp(textWidth + 80, 500, 1000);
        int rows = lines.Sum(l => Math.Max(1, (int)Math.Ceiling(d.Fonts.Measure(l, TextSize).X / (width - 80))));
        float height = 140 + rows * (TextSize + LineGap);
        Box = new RectangleF(640 - width / 2, 360 - height / 2, width, height);

        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 120));
        d.FillRect(Box, new Color(14, 18, 26, 245));
        d.Rect(Box, new Color(150, 190, 230), 1.5f);
        if (rows == 1) d.Text(_message, new Vector2(Box.X + Box.Width / 2, Box.Y + 40), TextSize, Color.White, TextAlign.Center);
        else d.TextWrapped(_message, new Vector2(Box.X + 40, Box.Y + 36), width - 80, TextSize, Color.White, LineGap);
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
