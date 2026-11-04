using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// 一覧の1行。Enabled が false の行は選べるが決定できない(灰色で出す)。
/// Icon は名前の左に、Badge(マルチクラスの呪文のクラスなど)は名前の右に出す。Badge の画像が無ければ BadgeText を出す。
/// DetailMark は Detail の右下に小さく添える字(名簿の「↑」の LV・ST など)。
/// Dim は、選べるが灰色で出す行(遊べない世界を選んで消す、など)。
/// </summary>
public sealed record MenuItem(string Label, string? Detail = null, bool Enabled = true, object? Tag = null,
    Texture2D? Icon = null, Texture2D? Badge = null, string? BadgeText = null, string? DetailMark = null, bool Dim = false);

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

    /// <summary>行が見えている数より多いとき、一覧を輪にする(一番下の次に一番上が続き、端で飛ばずに帯のように回る)。
    /// つなぎ目に線は引かない。</summary>
    public bool Loop { get; set; }

    public MenuItem? Current => Cursor >= 0 && Cursor < Items.Count ? Items[Cursor] : null;

    public int VisibleRows => Math.Max(1, (int)(Area.Height / RowHeight));

    private bool Ring => Loop && Items.Count > VisibleRows;

    /// <summary>見えている i 行目に出す項目の番号(見えている分を越えれば null)。</summary>
    private int? IndexAt(int visibleIndex)
    {
        if (visibleIndex >= VisibleRows) return null;
        if (Ring) return (_top + visibleIndex) % Items.Count;
        int index = _top + visibleIndex;
        return index < Items.Count ? index : null;
    }

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
        if (input.Repeat(GameAction.PageUp)) Cursor = Ring ? (Cursor - VisibleRows % Items.Count + Items.Count) % Items.Count : Math.Max(0, Cursor - VisibleRows);
        if (input.Repeat(GameAction.PageDown)) Cursor = Ring ? (Cursor + VisibleRows) % Items.Count : Math.Min(Items.Count - 1, Cursor + VisibleRows);
        // ホイール: 全部見えている一覧ではカーソルを動かす(端で回る)。
        // スクロールする一覧では一覧をスクロールし、端でさらに回すと反対の端へ。選ぶのは、マウスが指している行
        bool scrolled = false;
        if (input.WheelDelta != 0 && Area.Contains(input.Mouse ?? new Vector2(-1)))
        {
            int maxTop = Math.Max(0, Items.Count - VisibleRows);
            if (maxTop == 0) Cursor = ((Cursor - input.WheelDelta) % Items.Count + Items.Count) % Items.Count;
            else if (Ring)
            {
                _top = ((_top - input.WheelDelta) % Items.Count + Items.Count) % Items.Count;
                scrolled = true;
            }
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
            for (int i = 0; IndexAt(i) is int index; i++)
            {
                if (RowRect(i).Contains(m)) hovered = index;
            }
            if (hovered is int h && (input.MouseMoved || scrolled || input.LeftClicked)) Cursor = h;
        }
        // 見えている行に収める
        if (scrolled && Ring)
        {
            int offset = (Cursor - _top + Items.Count) % Items.Count;
            if (offset >= VisibleRows) Cursor = offset - VisibleRows < Items.Count - offset ? (_top + VisibleRows - 1) % Items.Count : _top;
        }
        else if (scrolled) Cursor = Math.Clamp(Cursor, _top, Math.Min(Items.Count - 1, _top + VisibleRows - 1));
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
        if (Ring)
        {
            // 輪の上で、カーソルが窓の外なら、近い側から窓を寄せる(上へ外れたら一番上の行に、下へ外れたら一番下の行に)
            _top = (_top % Items.Count + Items.Count) % Items.Count;
            int offset = (Cursor - _top + Items.Count) % Items.Count;
            if (offset < VisibleRows) return;
            int below = offset - (VisibleRows - 1), above = Items.Count - offset;
            _top = above <= below ? Cursor : (Cursor - VisibleRows + 1 + Items.Count) % Items.Count;
            return;
        }
        if (Cursor < _top) _top = Cursor;
        if (Cursor >= _top + VisibleRows) _top = Cursor - VisibleRows + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, Items.Count - VisibleRows));
    }

    public void Draw(Draw2D d, bool focused = true)
    {
        bool hasIcons = Items.Any(i => i.Icon != null);
        for (int i = 0; IndexAt(i) is int index; i++)
        {
            var item = Items[index];
            var r = RowRect(i);
            bool selected = index == Cursor && focused;
            if (selected)
            {
                d.FillRect(r, Theme.SelectedFill);
                d.Rect(r, Theme.SelectedFrame, 1.5f);
            }
            var color = !item.Enabled || item.Dim ? Theme.Disabled : selected ? Color.White : Theme.Text;
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
            float markSize = FontSize * 0.45f;
            float markWidth = item.DetailMark != null ? d.Fonts.Measure(item.DetailMark, markSize).X + 1 : 0;
            float room = r.X + r.Width - x - 14 - markWidth - (item.Detail != null ? d.Fonts.Measure(item.Detail, FontSize * 0.85f).X + 16 : 0)
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
            if (item.Detail != null) d.Text(item.Detail, new Vector2(r.X + r.Width - 14 - markWidth, ty), FontSize * 0.85f, color, TextAlign.Right);
            if (item.DetailMark != null) d.Text(item.DetailMark, new Vector2(r.X + r.Width - 14 - markWidth + 1, ty + FontSize * 0.85f - markSize * 0.95f), markSize, color);
        }
        if (_top > 0 || Ring) d.Text("▲", new Vector2(Area.X + Area.Width / 2, Area.Y - 22), 16, Theme.Dim, TextAlign.Center);
        if (_top + VisibleRows < Items.Count || Ring) d.Text("▼", new Vector2(Area.X + Area.Width / 2, Area.Y + VisibleRows * RowHeight - 2), 16, Theme.Dim, TextAlign.Center);
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

    /// <summary>HP の色: 半分以下(きけん)と、4分の1以下(危険が危ない)。満タンは Good、それより上はふつうの字の色。</summary>
    public static readonly Color HpCaution = new(240, 215, 100);
    public static readonly Color HpDanger = new(255, 105, 95);

    /// <summary>戦闘の文で、行動した者の名前の色(味方は青、敵は赤。冷気の水色・火の赤の行でも見分けられる濃さ)。</summary>
    public static readonly Color AllyName = new(105, 160, 255);
    public static readonly Color EnemyName = new(255, 95, 95);

    /// <summary>HP の割合の色(パーティの欄)。</summary>
    public static Color Hp(int hp, int max) =>
        hp >= max ? Good : hp * 4 <= max ? HpDanger : hp * 2 <= max ? HpCaution : Text;
    public static readonly Color Gold = new(230, 200, 120);
    /// <summary>注意してほしい品(習熟の無い武器など)。濃いめの黄色。</summary>
    public static readonly Color Caution = new(240, 200, 40);

    public static void Panel(Draw2D d, RectangleF r)
    {
        d.FillRect(r, PanelFill);
        d.Rect(r, Frame, 1f);
    }
}
