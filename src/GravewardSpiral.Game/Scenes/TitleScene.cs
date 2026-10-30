using GravewardSpiral.Core.Content;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// タイトル画面(仮)。後ろに、迷宮の線画の雰囲気だけを出す。
/// シナリオ(フォルダか zip)を窓に落とすと、コピーか移動かを聞いて、シナリオの置き場所へ置く。
/// 前回遊んだ世界のシナリオに、タイトル用の絵(art/title/logo.png・background.png)や曲(audio/bgm/title.mp3、manifest の music の title)があれば、それに入れ替える。
/// </summary>
public sealed class TitleScene : Scene
{
    private static readonly string[] MenuKeys = ["title.new", "title.continue", "title.options", "title.help", "title.quit"];
    private const float MenuX = 640f;
    private const float MenuTop = 420f;
    private const float MenuStep = 44f;
    private const float MenuWidth = 320f;
    private const string LogoPath = "srd/art/title/logo.png";
    private static readonly RectangleF LogoArea = new(200, 36, 880, 360);

    public const string Bgm = "audio/bgm/title.mp3";

    private int _cursor;
    private string? _message;
    private string? _logo;         // ロゴの絵(前回のシナリオのものか、標準)
    private string? _background;   // 後ろの絵(前回のシナリオにあれば。無ければ通路の線画)
    private double _messageTime;
    private double _time;

    public override void Enter()
    {
        Services.UseScenario(null);   // シナリオを遊んだあとは、標準のパックに戻す
        var look = LastScenarioLook();
        _logo = look.Logo ?? LogoPath;
        _background = look.Background;
        Services.Audio.PlayBgm(look.Bgm ?? Bgm);
        if (Services.Saves.HasAny()) _cursor = Array.IndexOf(MenuKeys, "title.continue");   // セーブがあれば「続きから」に合わせておく
    }

    public override void Update(GameTime time, InputState input)
    {
        int before = _cursor;
        double dt = time.ElapsedGameTime.TotalSeconds;
        _time += dt;
        if (_message != null && (_messageTime -= dt) <= 0) _message = null;
        if (Services.DroppedFiles.Count > 0)
        {
            Dropped(Services.DroppedFiles[0]);
            return;
        }

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

    /// <summary>
    /// 前回遊んだ世界(一番新しくセーブした世界)のシナリオの、タイトル用の絵と曲(絶対パス。無いものは null)。
    /// 絵はシナリオのフォルダの art/title/logo.png と background.png、曲は audio/bgm/title.mp3 か、manifest の music の title(棚の曲)。
    /// </summary>
    private (string? Logo, string? Background, string? Bgm) LastScenarioLook()
    {
        try
        {
            string? id = Services.Saves.ListClans().Select(c => c.Latest).OfType<Core.Save.SaveStore.WorldSummary>()
                .OrderByDescending(w => w.SavedAt).FirstOrDefault()?.Scenario;
            if (id == null || Services.FindScenarios().FirstOrDefault(x => x.Id == id) is not { } info) return default;
            string? Own(string relative) => Path.Combine(info.Directory, relative) is var full && File.Exists(full) ? full : null;
            string? bgm = Own(Bgm);
            // 曲の割り当て(music の title)は棚の曲を引くので、そのときだけシナリオを重ねて読む
            if (bgm == null && info.Manifest.Music.ContainsKey("title"))
                bgm = ContentLibrary.Load(AppPaths.Content, info.Directory).FindFile(Bgm);
            return (Own(LogoPath["srd/".Length..]), Own("art/title/background.png"), bgm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"前回のシナリオのタイトルが読めません: {ex.Message}");
            return default;
        }
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
        if (key == "title.help")
        {
            Stack.Push(new HelpScene());
            return;
        }
        if (key == "title.options")
        {
            Stack.Push(new OptionsScene());
            return;
        }
        if (key == "title.continue")
        {
            if (Services.Saves.HasAny())
            {
                Stack.Replace(new ContinueScene());
                return;
            }
            _message = Services.Strings["title.no_save"];
            _messageTime = 2.0;
            return;
        }
        _message = Services.Strings["title.not_yet"];
        _messageTime = 2.0;
    }

    private void Show(string message, double seconds)
    {
        _message = message;
        _messageTime = seconds;
    }

    /// <summary>落とされたものを見て、シナリオなら置き方を聞く。</summary>
    private void Dropped(string path)
    {
        var s = Services.Strings;
        var check = ScenarioInstall.Check(path, Services.UserScenarios, [AppPaths.BundledScenarios], AppPaths.ScenarioCache);
        if (check.Info is not { } info)
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Show(check.Problem switch
            {
                DropProblem.Unreadable => s.Format("title.drop_unreadable", check.Detail ?? ""),
                DropProblem.NoDungeon => s["title.drop_no_dungeon"],
                DropProblem.Bundled => s["title.drop_bundled"],
                DropProblem.AlreadyPlaced => s["title.drop_already"],
                _ => s["title.drop_not_scenario"],
            }, 4.0);
            return;
        }
        string name = info.Manifest.Name is { } n ? Services.Name(n) : info.Id;
        Services.Audio.PlaySe(Sounds.Confirm);
        Stack.Push(new ConfirmScene(s.Format("title.drop_ask", name), [s["title.drop_copy"], s["title.drop_move"], s["title.drop_cancel"]], choice =>
        {
            if (choice == 2) return;
            bool move = choice == 1;
            if (check.Existing.Count == 0) Place(check, name, move);
            else Stack.Push(new ConfirmScene(s.Format("title.drop_overwrite", name), () => Place(check, name, move)));
        }, cancelIndex: 2));
    }

    private void Place(DropCheck check, string name, bool move)
    {
        try
        {
            string dest = ScenarioInstall.Install(check, Services.UserScenarios, move);
            Console.WriteLine($"scenario placed: {dest}");
            Services.ForgetScenarioNames();
            Show(Services.Strings.Format("title.drop_done", name), 4.0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Show(Services.Strings.Format("title.drop_failed", ex.Message), 5.0);
        }
    }

    public override void Draw(Draw2D d)
    {
        // 前回のシナリオの後ろの絵があれば、それを画面いっぱいに(メニューが読めるように暗くする)。無ければ通路の線画
        if (_background != null && Services.Textures.Get(_background) is { } back)
        {
            float k = Math.Max(1280f / back.Width, 720f / back.Height);   // 縦横の比を保って画面を覆う(はみ出た所は画面の外)
            d.Image(back, new RectangleF((1280 - back.Width * k) / 2, (720 - back.Height * k) / 2, back.Width * k, back.Height * k), Color.White);
            // メニューの所を暗くする。上の縁は 60px かけて少しずつ濃くする(横の線に見えないように。1枚のグラデーションで、帯を重ねない)
            d.FadeDown(new RectangleF(0, 320, 1280, 60), new Color(0, 0, 0, 150));
            d.FillRect(new RectangleF(0, 380, 1280, 340), new Color(0, 0, 0, 150));
        }
        else DrawCorridor(d);
        var s = Services.Strings;

        var logo = Services.Textures.Get(_logo ?? LogoPath);
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
