using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>
/// 町の施設の画面のもと。上に題、左に一覧、右に説明、下にパーティの一覧とメッセージ。
/// 一覧の中身は Items() で作り直し、決定は Choose、取消は Cancel で受ける。
/// </summary>
public abstract class MenuScene : Scene
{
    protected static readonly RectangleF ListArea = new(80, 140, 460, 380);
    protected static readonly RectangleF InfoArea = new(600, 130, 600, 400);
    protected static readonly RectangleF PartyArea = new(40, 572, 1200, 116);

    protected readonly MenuList List;

    /// <param name="rowHeight">一覧の行の高さと字の大きさ(項目の多い画面は小さくして、全部が見えるようにする)。</param>
    protected MenuScene(float rowHeight = 46f, float fontSize = 26f) => List = new MenuList(ListArea, rowHeight, fontSize);
    protected string? Message { get; set; }

    protected abstract string Title { get; }
    protected abstract IEnumerable<MenuItem> Items();
    protected abstract void Choose(MenuItem item, int index);

    /// <summary>取消。既定は、ひとつ前の画面に戻る。</summary>
    protected virtual void Cancel() => Stack.Pop();

    /// <summary>右の説明の欄に描く。</summary>
    protected virtual void DrawInfo(Draw2D d, RectangleF area) { }

    protected virtual int? PartyHighlight => null;

    /// <summary>仲間を切り替えられる画面(持ち物・呪文の準備)。下の一覧の番号を F1〜F6 にし、見出しの右に ◀ ▶ を出す。</summary>
    protected virtual MemberSwitch? Switch => null;

    public override void Enter() => Refresh();

    /// <summary>一覧を作り直す(カーソルの位置は保つ)。</summary>
    protected void Refresh()
    {
        _shownLanguage = GameServices.Language;
        List.SetItems(Items());
    }

    /// <summary>一覧を作った言語(設定で言語を変えたら、戻ったときに一覧を作り直す)。</summary>
    private string? _shownLanguage;

    public override void Update(GameTime time, InputState input)
    {
        if (_shownLanguage != GameServices.Language)
        {
            Message = null;   // 前の言語のメッセージは消す
            Refresh();
        }
        ScrollMessage(input);
        var chosen = List.Update(input, Services.Audio);
        if (chosen is int i)
        {
            Message = null;
            Choose(List.Items[i], i);
        }
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Cancel();
        }
    }

    public override void Draw(Draw2D d)
    {
        d.Text(Title, new Vector2(80, 50), 36, Theme.Title);
        // 説明の欄の左端にそろえて、いつも同じ場所に(名前の長さで動かない)。題が長くて届くときだけ、題のすぐ右に
        Switch?.Draw(d, new Vector2(Math.Max(InfoArea.X, 80 + d.Fonts.Measure(Title, 36).X + 24), 60));
        if (Services.Session != null)
        {
            var party = Services.Session.Roster.PartyMembers.ToList();
            d.Text(Services.Strings.Format("town.gold", party.Sum(c => c.Gold)), new Vector2(1200, 62), 20, Theme.Gold, TextAlign.Right);
        }
        Theme.Panel(d, new RectangleF(ListArea.X - 12, ListArea.Y - 12, ListArea.Width + 24, ListArea.Height + 24));
        List.Draw(d);
        Theme.Panel(d, InfoArea);
        if (Message == null) DrawInfo(d, InfoArea);
        else DrawMessage(d);
        if (Services.Session != null) PartyPanel.Draw(d, Services, PartyArea, PartyHighlight, fKeys: Switch is { CanSwitch: true, PartyArea: not null }, usable: Switch is { } sw ? sw.Allows : null);
    }

    /// <summary>長いメッセージの欄(説明の欄の下の部分)と、スクロールの位置(折り返した行の数)と、その一番下。</summary>
    private RectangleF _messageBox;
    private int _messageTop, _messageMax;
    private string? _messageFor;

    /// <summary>長いメッセージの欄の上でマウスのホイールを回すか、右の ▲ ▼ を押すと、メッセージだけがスクロールする。新しいメッセージは先頭から。</summary>
    private void ScrollMessage(InputState input)
    {
        if (Message != _messageFor)
        {
            _messageFor = Message;
            _messageTop = _messageMax = 0;
        }
        if (_messageMax == 0 || input.Mouse is not { } m || !_messageBox.Contains(m)) return;
        if (input.WheelDelta != 0) _messageTop = Math.Clamp(_messageTop - input.WheelDelta, 0, _messageMax);
        // 右の ▲ ▼ を押しても1行ずつ動く
        else if (input.LeftClicked && m.X >= _messageBox.X + _messageBox.Width - 44)
            _messageTop = Math.Clamp(_messageTop + (m.Y < _messageBox.Y + _messageBox.Height / 2 ? -1 : 1), 0, _messageMax);
    }

    /// <summary>
    /// メッセージを説明の欄に描く。短ければ説明の下に積む。長ければ、欄の下の部分を区切ってメッセージの欄にし、
    /// 入りきらない行はマウスのホイールでスクロールする(上の説明はそのまま)。
    /// </summary>
    private void DrawMessage(Draw2D d)
    {
        const float size = 20f, gap = 6f;
        float width = InfoArea.Width - 50;   // 測った幅より少し広く描かれることがあるので、余裕を持たせる
        var rows = d.WrapLines(Message!, width, size);
        DrawInfo(d, InfoArea);
        float height = rows.Count * (size + gap) - gap;
        if (height <= InfoArea.Height * 0.35f)
        {
            // 短い: 説明の下から積む
            _messageMax = 0;
            float y0 = InfoArea.Y + InfoArea.Height - 20 - height;
            foreach (var row in rows) y0 = d.TextWrapped(row, new Vector2(InfoArea.X + 20, y0), width, size, Theme.Gold, gap);
            return;
        }
        // 長い: 下の部分を区切り線で分けて、説明の続きを隠してから描く
        float top = InfoArea.Y + InfoArea.Height * 0.42f;
        _messageBox = new RectangleF(InfoArea.X + 2, top, InfoArea.Width - 4, InfoArea.Y + InfoArea.Height - top - 2);
        d.FillRect(_messageBox, new Color(10, 14, 20));
        d.Line(new Vector2(InfoArea.X + 12, top), new Vector2(InfoArea.X + InfoArea.Width - 12, top), Theme.Frame, 1f);
        int visible = Math.Max(1, (int)((_messageBox.Height - 20) / (size + gap)));
        _messageMax = Math.Max(0, rows.Count - visible);
        _messageTop = Math.Min(_messageTop, _messageMax);
        float y = top + 12;
        foreach (var row in rows.Skip(_messageTop).Take(visible)) y = d.TextWrapped(row, new Vector2(InfoArea.X + 20, y), width, size, Theme.Gold, gap);
        float ax = InfoArea.X + InfoArea.Width - 24;
        if (_messageTop > 0) d.Text("▲", new Vector2(ax, top + 8), 16, Theme.Dim, TextAlign.Center);
        if (_messageTop < _messageMax) d.Text("▼", new Vector2(ax, InfoArea.Y + InfoArea.Height - 26), 16, Theme.Dim, TextAlign.Center);
    }

    /// <summary>説明の欄に、行を順に描く道具(欄の幅で折り返す)。次の行の y を返す。見出しの色でなければ、決まりの言葉に説明を付ける。</summary>
    /// <summary>
    /// 説明の行をまとめて描く。窓の下に収まらなければ、収まるまで字を小さくする(追加効果の多いレアの品など)。
    /// </summary>
    protected static float Lines(Draw2D d, RectangleF area, float y, IEnumerable<string> lines, Color color, float size = 18f, float reserve = 0)
    {
        var list = lines.ToList();
        float bottom = area.Y + area.Height - 10 - reserve;
        for (; size > 13; size--)
        {
            float h = list.Sum(t => d.WrapLines(LinkText.Plain(t), area.Width - 70, size).Count * (size + 8) + 2);
            if (y + h <= bottom) break;
        }
        foreach (var line in list) y = Line(d, area, y, line, color, size);
        return y;
    }

    protected static float Line(Draw2D d, RectangleF area, float y, string text, Color color, float size = 20f) =>
        (color == Theme.Title ? d.TextWrapped(LinkText.Plain(text), new Vector2(area.X + 20, y), area.Width - 70, size, color)
            : LinkText.Draw(d, text, new Vector2(area.X + 20, y), area.Width - 70, size, color)) + 2;   // 測った幅より少し広く描かれることがあるので、枠に余裕を持たせる
}
