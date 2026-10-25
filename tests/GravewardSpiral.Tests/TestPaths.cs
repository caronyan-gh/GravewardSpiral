namespace GravewardSpiral.Tests;

internal static class TestPaths
{
    /// <summary>リポジトリのルート(GravewardSpiral.slnx のあるフォルダ)。</summary>
    public static string Root { get; } = FindRoot();

    public static string Content => Path.Combine(Root, "Content");

    /// <summary>第1部(墓守の螺旋)のシナリオのパック。</summary>
    public static string Gravekeeper => Path.Combine(Content, "scenarios", "gravekeeper");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GravewardSpiral.slnx"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("GravewardSpiral.slnx が見つかりません");
    }

    /// <summary>使い捨てのフォルダ(テストの終わりに消す)。</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gw-test-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string Write(string relative, string text)
        {
            string full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
