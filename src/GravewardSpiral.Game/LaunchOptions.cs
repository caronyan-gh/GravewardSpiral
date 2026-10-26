using GravewardSpiral.Game.Input;

namespace GravewardSpiral.Game;

/// <summary>
/// 起動時の引数(確認用)。
/// --screenshot &lt;path&gt; [--frames N]: N フレーム目の画面を PNG に保存して終わる。N を省くと、自動操作が終わってから少し後。
/// --size WxH: ウィンドウの大きさ。
/// --start maze: タイトルを飛ばして、試しの迷宮から始める。--start newgame: 新規ゲームの画面から始める。
/// --start party: 試しの迷宮に、おまかせで作った6人のパーティで入る(遭遇あり。セーブしない)。
/// --start events: そのパーティで、仕掛けの見本の階(壁の文字・遺品・墓石・レバーなど)に入る(セーブしない)。
/// --start boss: そのパーティで、すぐ最下層のボスと戦う(ボス戦の曲で。セーブしない)。
/// --start town: そのパーティで町から始める(金を多めに持ち、宝の品が袋に入っている。セーブしない)。
/// --start scenario:フォルダか id[:階]: そのシナリオのその階(省くと B1)から、そのパーティで始める(エディタの試し遊び。セーブしない)。
/// --start combat[:goblinx3,wolf]: そのパーティで、すぐ戦闘を始める(敵を省くと B1 の遭遇表から)。
/// --start genmap[:シード[:階]]: シードから迷宮を作り、その階の地図を全部見えるようにして開く(自動生成の見本)。
/// --start named[:id]: 試しの迷宮で、1歩前を名のある敵のねぐらにする(--actions "U" で戦う)。
/// --debug: デバッグ用のキーを使えるようにする(迷宮で F5 すぐ戦闘 / F6 経験値 / F7 回復)。
/// --volume V: BGM と効果音の音量(0〜1)。--screenshot と一緒に渡すと、確認用の起動でも音の処理を動かす(0 なら無音で動かす)。
/// --saves DIR: セーブの置き場所を変える(確認用。いつものセーブを汚さない)。
/// --bgm-start S: 最初の BGM を S 秒目から流す(ループのつなぎ目を確かめる用)。
/// --actions "U U R C M": 動作を順に押す(U 上 / D 下 / L 左 / R 右 / C 決定 / X 取消 / M 地図 / T メニュー / Q E 横歩き / . 何もしない)。
/// </summary>
public sealed record LaunchOptions(string? ScreenshotPath, int? ScreenshotFrame, int? Width, int? Height, string? Start, IReadOnlyList<GameAction?> Actions, float? Volume, double? BgmStart, string? SavesDir, bool Debug = false, string? Language = null)
{
    /// <summary>自動操作で、1つの動作ごとに待つ Update の回数。</summary>
    public const int FramesPerAction = 8;

    public int EffectiveScreenshotFrame => ScreenshotFrame ?? 30 + FramesPerAction * (Actions.Count + 2);

    public static LaunchOptions Parse(string[] args)
    {
        string? shot = null, start = null, saves = null, lang = null;
        bool debug = false;
        int? frame = null, w = null, h = null;
        float? volume = null;
        double? bgmStart = null;
        var actions = new List<GameAction?>();
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : "";
            switch (args[i])
            {
                case "--screenshot":
                    shot = next;
                    i++;
                    break;
                case "--frames" when int.TryParse(next, out var n):
                    frame = Math.Max(1, n);
                    i++;
                    break;
                case "--size":
                    var parts = next.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var pw) && int.TryParse(parts[1], out var ph))
                    {
                        w = pw;
                        h = ph;
                    }
                    i++;
                    break;
                case "--volume" when float.TryParse(next, System.Globalization.CultureInfo.InvariantCulture, out var v):
                    volume = Math.Clamp(v, 0f, 1f);
                    i++;
                    break;
                case "--bgm-start" when double.TryParse(next, System.Globalization.CultureInfo.InvariantCulture, out var bs):
                    bgmStart = Math.Max(0, bs);
                    i++;
                    break;
                case "--saves":
                    saves = next;
                    i++;
                    break;
                case "--lang":
                    lang = next;
                    i++;
                    break;
                case "--debug":
                    debug = true;
                    break;
                case "--start":
                    start = next;
                    i++;
                    break;
                case "--actions":
                    foreach (var token in next.Split(' ', ',').Where(t => t.Length > 0))
                    {
                        actions.Add(token.ToUpperInvariant() switch
                        {
                            "U" => GameAction.Up,
                            "D" => GameAction.Down,
                            "L" => GameAction.Left,
                            "R" => GameAction.Right,
                            "C" => GameAction.Confirm,
                            "X" => GameAction.Cancel,
                            "M" => GameAction.Map,
                            "Q" => GameAction.StrafeLeft,
                            "E" => GameAction.StrafeRight,
                            "T" => GameAction.Menu,
                            "." => null,
                            _ => throw new ArgumentException($"--actions の '{token}' は読めません"),
                        });
                    }
                    i++;
                    break;
            }
        }
        return new LaunchOptions(shot, frame, w, h, start, actions, volume, bgmStart, saves, debug, lang);
    }
}
