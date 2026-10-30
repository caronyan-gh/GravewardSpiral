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
        DrawInfo(d, InfoArea);
        if (Message != null)
        {
            // 何行かあるときは、下から積む。長い行は欄の幅で折り返す
            const float size = 20f, gap = 6f;
            float width = InfoArea.Width - 40;
            var lines = Message.Split('\n');
            int rows = lines.Sum(l => Math.Max(1, (int)Math.Ceiling(d.Fonts.Measure(l, size).X / width)));
            float y = InfoArea.Y + InfoArea.Height - 40 - (rows - 1) * (size + gap);
            foreach (var line in lines) y = d.TextWrapped(line, new Vector2(InfoArea.X + 20, y), width, size, Theme.Gold, gap);
        }
        if (Services.Session != null) PartyPanel.Draw(d, Services, PartyArea, PartyHighlight, fKeys: Switch is { CanSwitch: true, PartyArea: not null }, usable: Switch is { } sw ? sw.Allows : null);
    }

    /// <summary>説明の欄に、行を順に描く道具(欄の幅で折り返す)。次の行の y を返す。</summary>
    protected static float Line(Draw2D d, RectangleF area, float y, string text, Color color, float size = 20f) =>
        d.TextWrapped(text, new Vector2(area.X + 20, y), area.Width - 70, size, color) + 2;   // 測った幅より少し広く描かれることがあるので、枠に余裕を持たせる
}
