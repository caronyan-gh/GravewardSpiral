using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 一覧の1行。Enabled が false の行は選べるが決定できない(灰色で出す)。
/// Icon は名前の左に、Badge(マルチクラスの呪文のクラスなど)は名前の右に出す。Badge の画像が無ければ BadgeText を出す。
/// </summary>
public sealed record MenuItem(string Label, string? Detail = null, bool Enabled = true, object? Tag = null,
    Texture2D? Icon = null, Texture2D? Badge = null, string? BadgeText = null);

/// <summary>
/// 上下で選んで決定する一覧。キーボード・ゲームパッド・マウス(載せると選ぶ、クリックで決定、ホイールで送る)で動く。
/// 行が多いときは、見えている分だけ出して送る。
/// </summary>
public sealed class MenuList
{
    public MenuList(RectangleF area, float rowHeight = 40f, float fontSize = 24f)
    {
        Area = area;
        RowHeight = rowHeight;
        FontSize = fontSize;
    }

    public RectangleF Area { get; set; }
    public float RowHeight { get; }
    public float FontSize { get; }
    public List<MenuItem> Items { get; private set; } = [];
    public int Cursor { get; set; }
    private int _top;

    public MenuItem? Current => Cursor >= 0 && Cursor < Items.Count ? Items[Cursor] : null;

    public int VisibleRows => Math.Max(1, (int)(Area.Height / RowHeight));

    public void SetItems(IEnumerable<MenuItem> items, int? cursor = null)
    {
        Items = items.ToList();
        Cursor = Math.Clamp(cursor ?? Cursor, 0, Math.Max(0, Items.Count - 1));
        KeepVisible();
    }

    private RectangleF RowRect(int visibleIndex) => new(Area.X, Area.Y + visibleIndex * RowHeight, Area.Width, RowHeight - 4);

    /// <summary>入力を処理する。決定された行の番号を返す(無ければ null)。取消は呼ぶ側で見る。</summary>
    public int? Update(InputState input, AudioPlayer audio)
    {
        if (Items.Count == 0) return null;
        int before = Cursor;
        if (input.Repeat(GameAction.Up)) Cursor = (Cursor + Items.Count - 1) % Items.Count;
        if (input.Repeat(GameAction.Down)) Cursor = (Cursor + 1) % Items.Count;
        if (input.Repeat(GameAction.PageUp)) Cursor = Math.Max(0, Cursor - VisibleRows);
        if (input.Repeat(GameAction.PageDown)) Cursor = Math.Min(Items.Count - 1, Cursor + VisibleRows);
        // ホイール: 全部見えている一覧ではカーソルを動かす(端で回る)。
        // スクロールする一覧では一覧をスクロールし、端でさらに回すと反対の端へ。選ぶのは、マウスが指している行
        bool scrolled = false;
        if (input.WheelDelta != 0 && Area.Contains(input.Mouse ?? new Vector2(-1)))
        {
            int maxTop = Math.Max(0, Items.Count - VisibleRows);
            if (maxTop == 0) Cursor = ((Cursor - input.WheelDelta) % Items.Count + Items.Count) % Items.Count;
            else
            {
                int top = _top - input.WheelDelta;
                top = top < 0 ? (_top == 0 ? maxTop : 0) : top > maxTop ? (_top == maxTop ? 0 : maxTop) : top;
                scrolled = top != _top;
                _top = top;
            }
        }

        int? hovered = null;
        if (input.Mouse is { } m)
        {
            for (int i = 0; i < VisibleRows && _top + i < Items.Count; i++)
            {
                if (RowRect(i).Contains(m)) hovered = _top + i;
            }
            if (hovered is int h && (input.MouseMoved || scrolled)) Cursor = h;
        }
        if (scrolled) Cursor = Math.Clamp(Cursor, _top, Math.Min(Items.Count - 1, _top + VisibleRows - 1));   // 見えている行に収める
        KeepVisible();
        if (Cursor != before) audio.PlaySe(Sounds.Cursor);

        bool clicked = input.LeftClicked && hovered == Cursor;
        if (input.Pressed(GameAction.Confirm) || clicked)
        {
            if (!Items[Cursor].Enabled)
            {
                audio.PlaySe(Sounds.Bump);
                return null;
            }
            audio.PlaySe(Sounds.Confirm);
            return Cursor;
        }
        return null;
    }

    private void KeepVisible()
    {
        if (Cursor < _top) _top = Cursor;
        if (Cursor >= _top + VisibleRows) _top = Cursor - VisibleRows + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, Items.Count - VisibleRows));
    }

    public void Draw(Draw2D d, bool focused = true)
    {
        bool hasIcons = Items.Any(i => i.Icon != null);
        for (int i = 0; i < VisibleRows && _top + i < Items.Count; i++)
        {
            int index = _top + i;
            var item = Items[index];
            var r = RowRect(i);
            bool selected = index == Cursor && focused;
            if (selected)
            {
                d.FillRect(r, Theme.SelectedFill);
                d.Rect(r, Theme.SelectedFrame, 1.5f);
            }
            var color = !item.Enabled ? Theme.Disabled : selected ? Color.White : Theme.Text;
            var tint = item.Enabled ? Color.White : Color.White * 0.4f;
            float ty = r.Y + (r.Height - FontSize) / 2 - 2;
            float x = r.X + 14;
            float icon = r.Height - 6;
            if (hasIcons)
            {
                // アイコンの列(無い行も幅をとって、名前の位置をそろえる)
                if (item.Icon != null) d.Image(item.Icon, new RectangleF(r.X + 8, r.Y + 3, icon, icon), tint);
                x = r.X + 14 + icon;
            }
            // 欄に収まらない名前は縮める(英語の長い名前など)。右の詳しい文と、印の分は空けておく
            float room = r.X + r.Width - x - 14 - (item.Detail != null ? d.Fonts.Measure(item.Detail, FontSize * 0.85f).X + 16 : 0)
                - (item.Badge != null || item.BadgeText != null ? FontSize * 2 : 0);
            float labelSize = FontSize;
            float labelWidth = d.Fonts.Measure(item.Label, FontSize).X;
            if (labelWidth > room && room > 0) labelSize = Math.Max(FontSize * 0.6f, FontSize * room * 0.95f / labelWidth);
            d.Text(item.Label, new Vector2(x, ty + (FontSize - labelSize) / 2), labelSize, color);
            if (item.Badge != null || item.BadgeText != null)
            {
                float bx = x + d.Fonts.Measure(item.Label, labelSize).X + 10;
                if (item.Badge != null) d.Image(item.Badge, new RectangleF(bx, r.Y + 3 + icon * 0.15f, icon * 0.7f, icon * 0.7f), tint);
                else d.Text(item.BadgeText!, new Vector2(bx, ty + FontSize * 0.15f), FontSize * 0.7f, Theme.Dim);
            }
            if (item.Detail != null) d.Text(item.Detail, new Vector2(r.X + r.Width - 14, ty), FontSize * 0.85f, color, TextAlign.Right);
        }
        if (_top > 0) d.Text("▲", new Vector2(Area.X + Area.Width / 2, Area.Y - 22), 16, Theme.Dim, TextAlign.Center);
        if (_top + VisibleRows < Items.Count) d.Text("▼", new Vector2(Area.X + Area.Width / 2, Area.Y + VisibleRows * RowHeight - 2), 16, Theme.Dim, TextAlign.Center);
    }
}

/// <summary>画面の色。</summary>
public static class Theme
{
    public static readonly Color PanelFill = new(10, 14, 20, 235);
    public static readonly Color Frame = new(70, 90, 110);
    public static readonly Color SelectedFill = new(40, 60, 80, 200);
    public static readonly Color SelectedFrame = new(150, 190, 230);
    public static readonly Color Text = new(190, 198, 206);
    public static readonly Color Dim = new(130, 140, 150);
    public static readonly Color Disabled = new(85, 92, 100);
    public static readonly Color Title = new(235, 225, 205);
    public static readonly Color Warn = new(255, 170, 150);
    public static readonly Color Good = new(150, 230, 160);
    public static readonly Color Gold = new(230, 200, 120);

    public static void Panel(Draw2D d, RectangleF r)
    {
        d.FillRect(r, PanelFill);
        d.Rect(r, Frame, 1f);
    }
}
