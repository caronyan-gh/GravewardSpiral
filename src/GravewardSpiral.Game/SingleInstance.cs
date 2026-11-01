using System.Collections.Concurrent;
using System.IO.Pipes;

namespace GravewardSpiral.Game;

/// <summary>
/// ダブルクリックで開かれたファイル(.gsclan のクラン、.gssnro のシナリオ)の受け渡し。
/// ゲームがもう動いていれば、2つ目はファイルを動いている方へ渡してすぐ終わる(同じセーブを2つのゲームで触らないため)。
/// 受け取ったファイルは Pending に入り、タイトル画面が開く(TitleScene)。
/// 確かめの起動(--screenshot・--saves)は、ほかのゲームと関わらない。
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\GravewardSpiral.Game";
    private const string PipeName = "GravewardSpiral.Open";

    /// <summary>開くのを待っているファイル。</summary>
    public static ConcurrentQueue<string> Pending { get; } = new();

    private static Mutex? _mutex;

    /// <summary>
    /// このゲームを動かしてよいか。ほかのゲームが動いていて、開くファイルを渡せたら false(このゲームは終わる)。
    /// 動かすなら、ほかのゲームからファイルを受け取る口を開ける。
    /// </summary>
    public static bool Claim(LaunchOptions options)
    {
        if (options.Open is { } open) Pending.Enqueue(open);
        if (options.ScreenshotPath != null || options.SavesDir != null) return true;
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            _mutex.Dispose();
            _mutex = null;
            // ファイルを渡せれば終わる。渡すものが無いか、渡せなければ、今までどおり動かす
            return options.Open == null || !Send(options.Open);
        }
        Task.Run(Listen);
        return true;
    }

    private static bool Send(string path)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task Listen()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                if (await reader.ReadLineAsync() is { Length: > 0 } line && File.Exists(line)) Pending.Enqueue(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }
    }
}
