// シナリオエディタ(GRAVEWARD SPIRAL)。シナリオのパックのフォルダを開いて、迷宮・イベント・物語を直す。
//
//   GravewardSpiralEditor [--open DIR] [--new ID] [--tab 名前] [--floor N] [--screenshot PNG] [--scenarios DIR]
//
//   --open DIR        シナリオのフォルダ(か zip)を開く
//   --new ID          新しいシナリオを自動生成で作る(--scenarios の下に。--seed S、--floors F も使える)
//   --tab 名前        開くタブ(basic / maze / advice / events / data / music / text / story / check)
//   --floor N         迷宮のタブで見せる階(1 から)
//   --screenshot PNG  窓の中身を PNG に保存して終わる(確かめ用。デスクトップは撮らない)
//   --fake-ai         AI に書かせるとき、本物の API を呼ばずに決まった文を返す(確かめ用)
//   --ai-write        開いたら、物語のタブの「AI に書かせる」を押す(--fake-ai と使う)
//   --ai-draft 種類[@元]:希望  開いたら、AI に下書きを頼む窓(event / item / monster)で書かせて、その窓を撮る(--fake-ai と使う)。
//                     --screenshot が無ければ、希望を入れて窓を開くだけ(書かせない)
//   --ai-paste ファイル  --ai-draft で、書かせる代わりに、ファイルの中身をチャットの答えとして貼る
//   --scenarios DIR   新しく作るシナリオの置き場所(省くと %LOCALAPPDATA%\GravewardSpiral\Scenarios。ゲームと同じ)
//   --select x,y[,x2,y2]  迷宮のタブで、そのマス(か四角)を選んでおく(確かめ用)
//   --reopen N        開いたあと、全部のタブを N 回作り直す(名前を付けて保存などと同じ。確かめ用)
//   --picker 言葉     画面の保存で、標準のイベントから写す窓をこの言葉で探して撮る
//   --scroll PX       画面の保存の前に、タブの中身をこれだけ下へ送る
//   --event ID[/N]    イベントのタブで、そのイベントを選び、木の上から N 番目(0 が根)を選んでおく(確かめ用)
//   --data 種類[/N]   データのタブで、品(items)かモンスター(monsters)の N 番目を選んでおく(確かめ用)
//   --lang ja|en      画面の言語(省くと設定の言語。確かめの起動は日本語)
using System.Windows;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = EditorOptions.Parse(args);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // 思わぬエラーは知らせて続ける(直しかけのシナリオを失わないように)。確かめの起動では、エラーを出して終わる
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception);
            try
            {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GravewardSpiral", "editor-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.Exception}\n\n");
            }
            catch (IOException) { }
            if (options.Screenshot != null) return;
            MessageBox.Show(e.Exception.Message, T("シナリオエディタ: エラー", "Scenario Editor: Error"));
            e.Handled = true;
        };
        Theme.Apply(app);
        EditorSettings.Persist = options.Screenshot == null;   // 確かめの起動では、設定を読み書きしない
        var context = EditorContext.Create(options.ScenariosDir);
        Loc.Lang = options.Lang ?? context.Settings.Language;
        var window = new MainWindow(context, options);
        app.Run(window);
        return 0;
    }
}

/// <summary>起動の引数。</summary>
public sealed record EditorOptions(string? Open, string? NewId, string? Tab, int Floor, string? Screenshot, string? ScenariosDir, ulong Seed, int Floors, string? Lang)
{
    /// <summary>開いたら AI に書かせる(--ai-write)。</summary>
    public static bool AiWrite { get; private set; }

    /// <summary>
    /// 開いたら、AI に下書きを頼む窓を開いて書かせ、その窓を撮る(--ai-draft 種類[@元の id]:希望。種類は event / item / monster。確かめ用)。
    /// 元の id は、データの標準の写す元(省くと、選んでいるもの)。
    /// </summary>
    public static string? AiDraft { get; private set; }

    /// <summary>--ai-draft で、AI に書かせる代わりに、このファイルの中身をチャットの答えとして貼る(--ai-paste。確かめ用)。</summary>
    public static string? AiPaste { get; private set; }

    /// <summary>迷宮のタブで選んでおくマス(--select x,y か x0,y0,x1,y1。確かめ用)。</summary>
    public static int[]? Select { get; private set; }

    /// <summary>開いたあと、全部のタブを作り直す回数(--reopen。確かめ用)。</summary>
    public static int Reopen { get; private set; }

    /// <summary>画面の保存で、標準のイベントから写す窓を、この言葉で探して撮る(--picker)。</summary>
    public static string? Picker { get; private set; }

    /// <summary>画面の保存の前に、タブの中身を下へ送る量(--scroll。ピクセル)。</summary>
    public static double Scroll { get; private set; }

    /// <summary>イベントのタブで選んでおくイベントと、木の何番目(--event ID[/N]。確かめ用)。</summary>
    public static string? EventNode { get; private set; }

    /// <summary>データのタブで選んでおく種類と番号(--data items/0。確かめ用)。</summary>
    public static string? DataNode { get; private set; }

    public static EditorOptions Parse(string[] args)
    {
        string? open = null, newId = null, tab = null, shot = null, scenarios = null, lang = null;
        int floor = 1, floors = 0;
        ulong seed = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : "";
            switch (args[i])
            {
                case "--open": open = next; i++; break;
                case "--new": newId = next; i++; break;
                case "--tab": tab = next; i++; break;
                case "--floor": floor = int.Parse(next); i++; break;
                case "--screenshot": shot = next; i++; break;
                case "--scenarios": scenarios = next; i++; break;
                case "--seed": seed = Core.Rng.RngStreams.ParseSeed(next) ?? 0; i++; break;
                case "--floors": floors = int.Parse(next); i++; break;
                case "--fake-ai": EditorContext.FakeAi = new Tabs.FakeAiClient(); break;
                case "--ai-write": AiWrite = true; break;
                case "--ai-draft": AiDraft = next; i++; break;
                case "--ai-paste": AiPaste = next; i++; break;
                case "--select": Select = next.Split(',').Select(int.Parse).ToArray(); i++; break;
                case "--reopen": Reopen = int.Parse(next); i++; break;
                case "--picker": Picker = next; i++; break;
                case "--scroll": Scroll = double.Parse(next, System.Globalization.CultureInfo.InvariantCulture); i++; break;
                case "--event": EventNode = next; i++; break;
                case "--data": DataNode = next; i++; break;
                case "--lang": lang = next == "en" ? "en" : "ja"; i++; break;
            }
        }
        return new EditorOptions(open, newId, tab, floor, shot, scenarios, seed, floors, lang);
    }
}
