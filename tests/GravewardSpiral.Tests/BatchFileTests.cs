namespace GravewardSpiral.Tests;

/// <summary>
/// Windows のバッチ(.cmd・.bat)は、手元のファイルも CRLF でなければならない。LF だと cmd.exe が日本語の行を読み違えて、
/// ダブルクリックで動かない(.gitattributes の eol=crlf は取り出すときにしか効かず、道具で書き換えたファイルは LF のまま残る)。
/// </summary>
public class BatchFileTests
{
    [Fact]
    public void BatchFilesUseCrlf()
    {
        var skip = new[] { "bin", "obj", "out", ".git", "node_modules" };
        var files = Directory.EnumerateFiles(TestPaths.Root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(TestPaths.Root, f).Split(Path.DirectorySeparatorChar).Any(skip.Contains));
        var bad = files.Where(f =>
        {
            var bytes = File.ReadAllBytes(f);
            return bytes.Select((b, i) => b == '\n' && (i == 0 || bytes[i - 1] != '\r')).Any(x => x);
        }).Select(f => Path.GetRelativePath(TestPaths.Root, f)).ToList();
        Assert.True(bad.Count == 0, "LF の改行があるバッチ(CRLF にする): " + string.Join(", ", bad));
    }
}
