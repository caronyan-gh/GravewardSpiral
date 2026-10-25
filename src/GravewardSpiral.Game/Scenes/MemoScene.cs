using GravewardSpiral.Core.Maze;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 書き留めたメモを読む画面(地図とキャンプから開く)。floor を渡せばその階だけ、渡さなければ全部の階を、
/// 今いる階を先にして並べる。上下で送り、取消か決定で閉じる。
/// </summary>
public sealed class MemoScene : Scene
{
    private static readonly RectangleF Panel = new(140, 60, 1000, 600);
    private const float LineSize = 20f, LineGap = 6f;

    private readonly ExplorationState _state;
    private readonly int? _floor;
    private float _scroll;
    private float _contentHeight;

    public MemoScene(ExplorationState state, int? floor = null)
    {
        _state = state;
        _floor = floor;
    }

    public override bool IsOverlay => true;

    private IEnumerable<MazeNote> Notes => _state.Notes
        .Where(n => _floor == null || n.Floor == _floor)
        .OrderBy(n => n.Floor == _state.Position.Floor ? 0 : 1).ThenBy(n => n.Floor).ThenBy(n => n.Cell.Y).ThenBy(n => n.Cell.X);

    public override void Update(GameTime time, InputState input)
    {
        if (input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Map) || input.LeftClicked)
        {
            Stack.Pop();
            return;
        }
        float view = Panel.Height - 104;
        float max = Math.Max(0, _contentHeight - view);
        if (input.Repeat(GameAction.Down)) _scroll += 60;
        if (input.Repeat(GameAction.Up)) _scroll -= 60;
        if (input.WheelDelta != 0) _scroll -= input.WheelDelta * 60;
        _scroll = Math.Clamp(_scroll, 0, max);
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 160));
        Theme.Panel(d, Panel);
        string title = _floor is int f ? s.Format("memo.title_floor", f + 1) : s["memo.title"];
        d.Text(title, new Vector2(Panel.X + 24, Panel.Y + 16), 26, Theme.Title);
        var notes = Notes.ToList();
        float top = Panel.Y + 60, bottom = Panel.Y + Panel.Height - 44;
        if (notes.Count == 0)
        {
            d.Text(s["memo.none"], new Vector2(Panel.X + 24, top + 10), LineSize, Theme.Dim);
            _contentHeight = 0;
        }
        else
        {
            // 見えるところだけ描く(枠の外にはみ出さないように)
            float y = top - _scroll;
            foreach (var n in notes)
            {
                if (y >= top - 4 && y < bottom - LineSize)
                    d.Text(s.Format("memo.place", n.Floor + 1, n.Cell.X, n.Cell.Y), new Vector2(Panel.X + 24, y), 16, Theme.Gold);
                y += 16 + LineGap;
                foreach (var line in n.Lines)
                {
                    string text = Services.Name(line);
                    int rows = Math.Max(1, (int)Math.Ceiling(d.Fonts.Measure(text, LineSize).X / (Panel.Width - 72)));
                    float h = rows * (LineSize + LineGap) + 2;
                    if (y >= top - 4 && y + h < bottom)
                        d.TextWrapped(text, new Vector2(Panel.X + 40, y), Panel.Width - 72, LineSize, Theme.Text, LineGap);
                    y += h;
                }
                y += 14;
            }
            _contentHeight = y + _scroll - top;
        }
        d.Text(s["memo.hint"], new Vector2(640, Panel.Y + Panel.Height - 32), 16, Theme.Dim, TextAlign.Center);
    }
}
