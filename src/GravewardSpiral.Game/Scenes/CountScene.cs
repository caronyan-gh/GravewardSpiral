using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 個数を選ぶ小さな窓(重ねた品を売る・渡すとき)。左右で ±1(端で回る)、上下で ±10、PageUp で全部・PageDown で 1、数字キーで直接打つ。決定で決める。
/// 値段を渡せば、その個数の値段も出す。マウスは ◀ ▶ のクリックとホイール。
/// </summary>
public sealed class CountScene : Scene
{
    private static readonly RectangleF Box = new(390, 250, 500, 220);

    private readonly string _title;
    private readonly int _max;
    private readonly Func<int, string>? _detail;
    private readonly Action<int> _onChosen;
    private int _count;
    private bool _fresh = true;   // 次の数字キーで、見えている数を置き換えるか(開いた直後と、矢印で動かした直後)

    /// <param name="detail">個数から、下に出す文(値段など)。省けば出さない。</param>
    public CountScene(string title, int max, Action<int> onChosen, Func<int, string>? detail = null, int start = 1)
    {
        _title = title;
        _max = Math.Max(1, max);
        _onChosen = onChosen;
        _detail = detail;
        _count = Math.Clamp(start, 1, _max);
    }

    public override bool IsOverlay => true;

    private static RectangleF LeftArrow => new(Box.X + 90, Box.Y + 78, 50, 50);
    private static RectangleF RightArrow => new(Box.X + Box.Width - 140, Box.Y + 78, 50, 50);

    public override void Update(GameTime time, InputState input)
    {
        int before = _count;
        // 数字キーで打つ。見えている数をそのまま書き換える(開いた直後と、矢印で動かした直後は置き換え、続けて打つと桁が増える。
        // 足すと多すぎれば、打った数字だけ)。BackSpace は見えている数の1桁を消す(1桁のときは 1 に戻し、何も打っていなければ取消)
        foreach (char c in input.TypedDigits)
        {
            int digit = c - '0';
            long typed = _fresh ? digit : _count * 10L + digit;
            if (typed > _max) typed = digit;   // 足すと持っている数を超えるなら、打った数字だけにする(10 個のとき 1 → 5 は 5)
            if (typed >= 1) _count = (int)Math.Min(typed, _max);
            _fresh = false;
        }
        if (input.BackspacePressed && (_count >= 10 || !_fresh))
        {
            if (_count >= 10) _count /= 10;
            else
            {
                _count = 1;
                _fresh = true;
            }
            Services.Audio.PlaySe(Sounds.Cursor);
            return;   // BackSpace は取消のキーでもあるので、消しているあいだは閉じない
        }
        if (input.Repeat(GameAction.Left) || input.Repeat(GameAction.Right) || input.Repeat(GameAction.Up) || input.Repeat(GameAction.Down)
            || input.Pressed(GameAction.PageUp) || input.Pressed(GameAction.PageDown)) _fresh = true;
        // 左右は端で回る(1 から左で全部。99 個あっても、全部はすぐ選べる)
        if (input.Repeat(GameAction.Left)) _count = _count <= 1 ? _max : _count - 1;
        if (input.Repeat(GameAction.Right)) _count = _count >= _max ? 1 : _count + 1;
        if (input.Repeat(GameAction.Down)) _count -= 10;
        if (input.Repeat(GameAction.Up)) _count += 10;
        if (input.Pressed(GameAction.PageUp)) _count = _max;
        if (input.Pressed(GameAction.PageDown)) _count = 1;
        _count += input.WheelDelta;
        if (input.LeftClicked && input.Mouse is { } m)
        {
            if (LeftArrow.Contains(m)) _count--;
            else if (RightArrow.Contains(m)) _count++;
            else if (Box.Contains(m))
            {
                Choose();
                return;
            }
        }
        _count = Math.Clamp(_count, 1, _max);
        if (_count != before) Services.Audio.PlaySe(Sounds.Cursor);
        if (input.Pressed(GameAction.Confirm)) Choose();
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Stack.Pop();
        }
    }

    private void Choose()
    {
        Stack.Pop();
        _onChosen(_count);
    }

    public override void Draw(Draw2D d)
    {
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 120));
        d.FillRect(Box, new Color(14, 18, 26, 245));
        d.Rect(Box, new Color(150, 190, 230), 1.5f);
        float titleSize = Math.Min(24f, 24f * (Box.Width - 40) / Math.Max(1f, d.Fonts.Measure(_title, 24).X));
        d.Text(_title, new Vector2(Box.X + Box.Width / 2, Box.Y + 24), titleSize, Theme.Title, TextAlign.Center);
        var dim = new Color(150, 160, 170);
        d.Text("◀", new Vector2(LeftArrow.X + LeftArrow.Width / 2, LeftArrow.Y + 8), 30, _count > 1 ? Color.White : dim, TextAlign.Center);
        d.Text("▶", new Vector2(RightArrow.X + RightArrow.Width / 2, RightArrow.Y + 8), 30, _count < _max ? Color.White : dim, TextAlign.Center);
        d.Text($"{_count} / {_max}", new Vector2(Box.X + Box.Width / 2, Box.Y + 84), 34, Color.White, TextAlign.Center);
        if (_detail != null) d.Text(_detail(_count), new Vector2(Box.X + Box.Width / 2, Box.Y + 140), 22, Theme.Gold, TextAlign.Center);
        d.Text(Services.Strings["count.hint"], new Vector2(Box.X + Box.Width / 2, Box.Y + Box.Height - 32), 15, dim, TextAlign.Center);
    }
}
