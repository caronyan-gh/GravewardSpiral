using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// エンディング(仮。物語の中身は M7)。文を1つずつ見せ、そのあとクレジットを流す。
/// 決定で次へ進み、終わったら「クリアした」の印を立ててセーブし、タイトルへ戻る。
/// </summary>
public sealed class EndingScene : Scene
{
    private const double FadeSeconds = 1.2;

    /// <summary>エンディングの BGM(「The Keeper's Silent Rite」)。</summary>
    public const string Bgm = "audio/bgm/ending.mp3";

    private readonly List<string> _pages = [];
    private int _page;
    private double _time;

    public override void Enter()
    {
        var s = Services.Strings;
        for (int i = 1; s["ending." + i] != $"[ending.{i}]"; i++) _pages.Add(s["ending." + i]);
        _pages.Add(s["ending.credits"]);
        _pages.Add(s["ending.end"]);
        Services.Audio.PlayBgm(Bgm);
    }

    public override void Update(GameTime time, InputState input)
    {
        _time += time.ElapsedGameTime.TotalSeconds;
        if (_time < FadeSeconds * 0.5) return;
        if (!(input.Pressed(GameAction.Confirm) || input.LeftClicked)) return;
        _page++;
        _time = 0;
        if (_page < _pages.Count) return;
        var session = Services.Session;
        if (session != null)
        {
            session.RecordClear(Services.Content.Scenario);   // クリアした世界の数(周回)と、シナリオのクリアの印
            Services.SaveSession();
        }
        Services.Session = null;
        Stack.Replace(new TitleScene());
    }

    public override void Draw(Draw2D d)
    {
        if (_page >= _pages.Count) return;
        float a = (float)Math.Clamp(_time / FadeSeconds, 0, 1);
        var color = Color.Lerp(Color.Black, Theme.Title, a);
        float y = 180;
        foreach (var line in _pages[_page].Split('\n'))
        {
            d.Text(line, new Vector2(640, y), 28, color, TextAlign.Center, FontStyle.Story);
            y += 48;
        }
        if (a >= 1) d.Text(Services.Strings["ending.next"], new Vector2(640, 660), 16, Theme.Dim, TextAlign.Center);
    }
}
