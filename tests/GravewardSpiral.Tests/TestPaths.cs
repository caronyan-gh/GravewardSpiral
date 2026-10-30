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

        /// <summary>
        /// 試しのパックの技・能力・回数に、名前の文言(キーそのもの)を日英で書く。クラスが指す技と能力に文言が無いと、読み込みの検査で落ちるため。
        /// 技は feature.&lt;id&gt; と feature.about.&lt;id&gt;、能力は passive.&lt;id&gt; と passive.about.&lt;id&gt;、回数は pool.&lt;id&gt;。
        /// </summary>
        public void NameFeatures(string pack)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            string root = System.IO.Path.Combine(Path, pack);
            foreach (var file in new[] { "features.json", "classes.json", "races.json" })
            {
                string full = System.IO.Path.Combine(root, file);
                if (!File.Exists(full)) continue;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(full));
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (file == "features.json" && e.TryGetProperty("id", out var id))
                        foreach (var k in new[] { "feature.", "feature.about.", "passive.", "passive.about." }) keys.Add(k + id.GetString());
                    if (e.TryGetProperty("pools", out var pools) && pools.ValueKind == System.Text.Json.JsonValueKind.Array)
                        foreach (var pool in pools.EnumerateArray()) keys.Add("pool." + pool.GetProperty("id").GetString());
                }
            }
            string json = System.Text.Json.JsonSerializer.Serialize(keys.ToDictionary(k => k, k => k));
            Write(System.IO.Path.Combine(pack, "text", "ja.json"), json);
            Write(System.IO.Path.Combine(pack, "text", "en.json"), json);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
