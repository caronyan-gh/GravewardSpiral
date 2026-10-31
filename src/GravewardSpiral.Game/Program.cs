using System.Runtime.InteropServices;
using GravewardSpiral.Game;

// --help: 使い方を出して、窓を開かずに終わる(エージェントが試しに起こしても、本物のセーブや設定に触れないように)
if (args.Any(a => a is "--help" or "-h" or "/?"))
{
    AttachConsole(-1);
    Console.OutputEncoding = System.Text.Encoding.UTF8;   // エディタと同じ。窓のアプリなので、起こしたコンソールに付ける
    Console.WriteLine(Help.Text);
    return 0;
}

using var game = new GravewardGame(LaunchOptions.Parse(args));
game.Run();
return 0;

[DllImport("kernel32.dll")]
static extern bool AttachConsole(int processId);

static class Help
{
    /// <summary>--help で出す使い方(シナリオを確かめるときに使うものだけ。ほかの確かめ用の引数は LaunchOptions の先頭の説明)。</summary>
    public const string Text = """
        GravewardSpiral(ゲーム / Game)

          GravewardSpiral --saves <DIR> [--start scenario:<フォルダか zip>[:階]] [--screenshot <PNG> [--frames N]] [--actions "U U R C"] [--lang ja|en] [--volume 0]

          --saves <DIR>        セーブと確かめの置き場所(道具やエージェントから起こすときは必ず使い捨てのフォルダを付ける)
                               where saves go (always pass a disposable folder when launched by a tool or agent)
          --start scenario:<DIR>[:N]
                               そのシナリオの B<N>(省くと B1)の入口から、おまかせの6人で始める(セーブしない。遭遇あり)
                               start the scenario at floor N with a quick party of six (not saved; encounters on)
          --screenshot <PNG>   画面を PNG に保存して終わる。--frames N で N フレーム目(省くと自動操作のあと)
                               save the screen to PNG and exit (at frame N, or after the actions)
          --actions "..."      順に押す動作。空白で区切り、全体を引用符で1つにする
                               U 前進 / D 後ろへ(設定で振り向き) / L R 向きを変える / Q E 横歩き / C 決定・調べる / X 取消 / M 地図 / T メニュー / . 待つ
                               (8 フレームに1つ。文の打ち出しは C で全部出し、もう一度 C で閉じる。
                                階段の確認は「いいえ」が選ばれて出るので、L か R で「はい」にしてから C。隠し扉は、前を向いて C で調べる)
          --lang ja|en         画面の言語 / UI language
          --volume 0           音を出さない / mute

        設定(窓の大きさなど)は、--screenshot を付けた起動では書かない。--screenshot なしで起こすと、閉じたときに本物の設定に書く。
        Settings are not written when --screenshot is given; without it, closing the game writes your real settings.
        画面なしで検査するなら、同じフォルダの ScenarioCheck.exe を使う(ScenarioCheck --help)。
        To check without a window, use ScenarioCheck.exe in the same folder (ScenarioCheck --help).
        """;
}
