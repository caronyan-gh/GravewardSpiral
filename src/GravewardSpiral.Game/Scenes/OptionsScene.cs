using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 設定の画面。上下で項目を選び、左右(か決定)で値を変える。変えたらすぐ効き、取消で戻るときに保存する。
/// タイトルとキャンプから開く。
/// </summary>
public sealed class OptionsScene : Scene
{
    private enum Row { Language, Bgm, Se, LogSpeed, Wireframe, StoryFont, Map, Ai, Back }

    private static readonly Row[] Rows = Enum.GetValues<Row>();
    private static readonly RectangleF Area = new(240, 40, 800, 640);
    private const float RowHeight = 56f;

    private readonly bool _overlay;
    private int _cursor;

    /// <param name="overlay">下の画面に重ねて出す(キャンプから開いたとき)。</param>
    public OptionsScene(bool overlay = false) => _overlay = overlay;

    public override bool IsOverlay => _overlay;

    private GameOptions O => Services.Options;

    private RectangleF RowRect(int i) => new(Area.X + 24, Area.Y + 70 + i * RowHeight, Area.Width - 48, RowHeight - 8);

    /// <summary>音量のバー(クリックか引きずりで、その位置の音量にする)。</summary>
    private RectangleF BarRect(int i)
    {
        var r = RowRect(i);
        return new RectangleF(r.X + 300, r.Y + 22, 220, 12);
    }

    private int? _dragging;   // 引きずっているバーの行

    /// <summary>値の左右の矢印(位置は値の長さによらず決まっている。クリックで −1 / +1)。値はそのあいだの真ん中に出す。</summary>
    private RectangleF LeftArrow(int i)
    {
        var r = RowRect(i);
        return new RectangleF(r.X + r.Width - 250, r.Y + 4, 40, r.Height - 8);
    }

    private RectangleF RightArrow(int i)
    {
        var r = RowRect(i);
        return new RectangleF(r.X + r.Width - 50, r.Y + 4, 40, r.Height - 8);
    }

    public override void Update(GameTime time, InputState input)
    {
        int before = _cursor;
        if (input.Repeat(GameAction.Up)) _cursor = (_cursor + Rows.Length - 1) % Rows.Length;
        if (input.Repeat(GameAction.Down)) _cursor = (_cursor + 1) % Rows.Length;
        int? hovered = null;
        if (input.Mouse is { } m)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                if (RowRect(i).Contains(m)) hovered = i;
            }
            if (hovered is int h && input.MouseMoved) _cursor = h;
        }
        if (_cursor != before) Services.Audio.PlaySe(Sounds.Cursor);

        // 音量のバー: クリックした位置にし、押したまま動かせば追いかける
        if (!input.LeftHeld) _dragging = null;
        if (input.Mouse is { } mp)
        {
            if (input.LeftClicked)
            {
                for (int i = 0; i < Rows.Length; i++)
                {
                    var b = BarRect(i);
                    if (Rows[i] is Row.Bgm or Row.Se && new RectangleF(b.X - 8, b.Y - 14, b.Width + 16, b.Height + 28).Contains(mp)) _dragging = i;
                }
            }
            if (_dragging is int di)
            {
                _cursor = di;
                var b = BarRect(di);
                int vol = (int)Math.Round(Math.Clamp((mp.X - b.X) / b.Width, 0, 1) * 10) * 10;
                if (SetVolume(Rows[di], vol)) Services.Audio.PlaySe(Rows[di] == Row.Se ? Sounds.Hit : Sounds.Cursor);
                return;
            }
        }
        var row = Rows[_cursor];
        if (input.Pressed(GameAction.Cancel) || (row == Row.Back && (input.Pressed(GameAction.Confirm) || input.LeftClicked && hovered == _cursor)))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Close();
            return;
        }
        // AI の手伝いは、決定で別の画面を開いて決める
        if (row == Row.Ai && (input.Pressed(GameAction.Confirm) || input.LeftClicked && hovered == _cursor))
        {
            Services.Audio.PlaySe(Sounds.Confirm);
            Stack.Push(new AiOptionsScene());
            return;
        }
        bool clickLeft = input.LeftClicked && hovered == _cursor && input.Mouse is { } cm && LeftArrow(_cursor).Contains(cm);
        int step = input.Repeat(GameAction.Left) || clickLeft ? -1
            : input.Repeat(GameAction.Right) || input.Pressed(GameAction.Confirm) || input.LeftClicked && hovered == _cursor ? 1 : 0;
        if (step != 0) Change(row, step);
    }

    private void Change(Row row, int step)
    {
        switch (row)
        {
            case Row.Bgm:
                O.BgmVolume = Math.Clamp(O.BgmVolume + step * 10, 0, 100);
                break;
            case Row.Se:
                O.SeVolume = Math.Clamp(O.SeVolume + step * 10, 0, 100);
                break;
            case Row.LogSpeed:
            {
                int i = Array.IndexOf(GameOptions.LogSpeeds, O.LogSpeed);
                O.LogSpeed = GameOptions.LogSpeeds[((i < 0 ? 1 : i) + step + GameOptions.LogSpeeds.Length) % GameOptions.LogSpeeds.Length];
                break;
            }
            case Row.Language:
            {
                // パックの text/ にある言語を順に回す(言語のファイルを置けば増える)
                var langs = Services.Languages;
                if (langs.Count < 2) return;
                int i = Math.Max(0, langs.ToList().IndexOf(GameServices.Language));
                string next = langs[(i + step + langs.Count) % langs.Count];
                O.Language = next;
                Services.SetLanguage(next);
                break;
            }
            case Row.Wireframe:
                O.MonoWireframe = !O.MonoWireframe;
                break;
            case Row.StoryFont:
            {
                var fonts = Rendering.TextRenderer.StoryFonts;
                int i = Math.Max(0, Array.IndexOf(fonts, O.StoryFont));
                O.StoryFont = fonts[(i + step + fonts.Length) % fonts.Length];
                break;
            }
            case Row.Map:
            {
                int i = Math.Max(0, Array.IndexOf(GameOptions.MapModes, O.MapMode));
                O.MapMode = GameOptions.MapModes[(i + step + GameOptions.MapModes.Length) % GameOptions.MapModes.Length];
                break;
            }
            default:
                return;
        }
        Services.ApplyOptions();
        // 効果音の音量は、変えた大きさで鳴らして聞かせる
        Services.Audio.PlaySe(row == Row.Se ? Sounds.Hit : Sounds.Cursor);
    }

    /// <summary>音量を直接決める。変わったら true。</summary>
    private bool SetVolume(Row row, int vol)
    {
        int old = row == Row.Bgm ? O.BgmVolume : O.SeVolume;
        if (old == vol) return false;
        if (row == Row.Bgm) O.BgmVolume = vol;
        else O.SeVolume = vol;
        Services.ApplyOptions();
        return true;
    }

    private void Close()
    {
        Services.SaveOptions();
        Stack.Pop();
    }

    private string Value(Row row)
    {
        var s = Services.Strings;
        return row switch
        {
            Row.Language => s["language.name"],
            Row.Bgm => $"{O.BgmVolume}%",
            Row.Se => $"{O.SeVolume}%",
            Row.LogSpeed => s["options.log." + O.LogSpeed],
            Row.Wireframe => s[O.MonoWireframe ? "options.wire.mono" : "options.wire.color"],
            Row.StoryFont => s["options.storyfont." + O.StoryFont],
            Row.Map => s["options.map." + O.MapMode],
            Row.Ai => s[!O.AiEnabled ? "options.ai.off" : Ai.ApiKeyStore.Load(Ai.ApiKeyStore.Claude) != null ? "options.ai.on" : "options.ai.no_key"],
            _ => "",
        };
    }

    public override void Draw(Draw2D d)
    {
        var s = Services.Strings;
        if (_overlay) d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 140));
        else d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(6, 8, 12));
        Theme.Panel(d, Area);
        d.Text(s["options.title"], new Vector2(Area.X + 24, Area.Y + 18), 28, Theme.Title);
        for (int i = 0; i < Rows.Length; i++)
        {
            var r = RowRect(i);
            bool selected = i == _cursor;
            if (selected)
            {
                d.FillRect(r, Theme.SelectedFill);
                d.Rect(r, Theme.SelectedFrame, 1.5f);
            }
            var color = selected ? Color.White : Theme.Text;
            d.Text(s["options." + Rows[i].ToString().ToLowerInvariant()], new Vector2(r.X + 16, r.Y + 12), 24, color);
            if (Rows[i] == Row.Back) continue;
            if (Rows[i] == Row.Ai)
            {
                d.Text(Value(Rows[i]), new Vector2((LeftArrow(i).X + LeftArrow(i).Width + RightArrow(i).X) / 2, r.Y + 12), 24, color, TextAlign.Center);
                continue;
            }
            string v = Value(Rows[i]);
            var la = LeftArrow(i);
            var ra = RightArrow(i);
            if (selected)
            {
                d.Text("◀", new Vector2(la.X + la.Width / 2, r.Y + 12), 24, color, TextAlign.Center);
                d.Text("▶", new Vector2(ra.X + ra.Width / 2, r.Y + 12), 24, color, TextAlign.Center);
            }
            var valueStyle = Rows[i] == Row.StoryFont ? FontStyle.Story : FontStyle.Ui;   // 物語の書体は、その書体で見せる
            // 長い値は、矢印のあいだに収まるまで字を小さくする
            float room = ra.X - (la.X + la.Width) - 8;
            float valueSize = Math.Min(24f, 24f * room / Math.Max(1f, d.Fonts.Measure(v, 24, valueStyle).X));
            d.Text(v, new Vector2((la.X + la.Width + ra.X) / 2, r.Y + 12 + (24 - valueSize) / 2), valueSize, color, TextAlign.Center, valueStyle);
            if (Rows[i] is Row.Bgm or Row.Se)
            {
                int vol = Rows[i] == Row.Bgm ? O.BgmVolume : O.SeVolume;
                var bar = BarRect(i);
                d.Rect(bar, Theme.Frame, 1f);
                d.FillRect(new RectangleF(bar.X, bar.Y, bar.Width * vol / 100f, bar.Height), selected ? Theme.SelectedFrame : Theme.Dim);
            }
        }
        d.Text(s["options.hint"], new Vector2(Area.X + Area.Width / 2, Area.Y + Area.Height - 34), 16, Theme.Dim, TextAlign.Center);
    }
}
