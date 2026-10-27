using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// 取消 (Esc) で開く小さなメニュー。下の画面に重ねて出す。「(その画面の項目)」「ヘルプ」「設定」「やめる(中断してタイトルへ など)」「閉じる」。
/// </summary>
public sealed class SystemMenuScene : Scene
{
    private static readonly RectangleF Box = new(440, 230, 400, 230);

    private readonly string _quitLabel;
    private readonly Action _quit;
    private readonly MenuList _menu = new(new RectangleF(Box.X + 30, Box.Y + 60, Box.Width - 60, Box.Height - 70), 46f, 24f);
    private readonly (string Label, Action Run)[] _extra;

    /// <param name="quitLabel">やめる項目の名前(「中断してタイトルへ」など)。</param>
    /// <param name="quit">やめる項目を選んだとき(このメニューを降ろしてから呼ぶ)。</param>
    /// <param name="extra">一番上に足す項目(迷宮のキャンプなど。このメニューを降ろしてから呼ぶ)。</param>
    public SystemMenuScene(string quitLabel, Action quit, params (string Label, Action Run)[] extra)
    {
        _quitLabel = quitLabel;
        _quit = quit;
        _extra = extra;
    }

    private RectangleF BoxRect => new(Box.X, Box.Y - (_extra.Length + 1) * 23, Box.Width, Box.Height + (_extra.Length + 1) * 46);

    public override bool IsOverlay => true;

    public override void Enter()
    {
        var s = Services.Strings;
        var b = BoxRect;
        _menu.Area = new RectangleF(b.X + 30, b.Y + 60, b.Width - 60, b.Height - 70);
        _menu.SetItems(_extra.Select((x, i) => new MenuItem(x.Label, Tag: i))
            .Concat([new MenuItem(s["help.title"], Tag: "help"), new MenuItem(s["options.title"], Tag: "options"), new MenuItem(_quitLabel, Tag: "quit"), new MenuItem(s["system.close"], Tag: "close")]), 0);
    }

    public override void Update(GameTime time, InputState input)
    {
        if (_menu.Update(input, Services.Audio) is int i)
        {
            switch (_menu.Items[i].Tag)
            {
                case "help":
                    Stack.Pop();
                    Stack.Push(new HelpScene());
                    break;
                case "options":
                    // 設定を閉じたら、このメニューにも戻らずに元の画面へ
                    Stack.Pop();
                    Stack.Push(new OptionsScene(overlay: true));
                    break;
                case "quit":
                    Stack.Pop();
                    _quit();
                    break;
                case int extra:
                    Stack.Pop();
                    _extra[extra].Run();
                    break;
                default:
                    Stack.Pop();
                    break;
            }
        }
        else if (input.Pressed(GameAction.Cancel))
        {
            Services.Audio.PlaySe(Sounds.Cancel);
            Stack.Pop();
        }
    }

    public override void Draw(Draw2D d)
    {
        d.FillRect(new RectangleF(0, 0, 1280, 720), new Color(0, 0, 0, 120));
        var b = BoxRect;
        Theme.Panel(d, b);
        d.Text(Services.Strings["system.title"], new Vector2(b.X + 24, b.Y + 16), 22, Theme.Title);
        _menu.Draw(d);
    }
}
