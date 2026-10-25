using System.IO.Compression;
using System.Text.Json;

namespace GravewardSpiral.Core.Content;

/// <summary>
/// 見つけたシナリオ1つ(パックのフォルダか zip)。Directory は読むフォルダ(zip なら展開したフォルダ)。
/// </summary>
public sealed record ScenarioInfo(PackManifest Manifest, string Source, string Directory)
{
    public string Id => Manifest.Id;
}

/// <summary>
/// シナリオを探す。シナリオは、manifest.json の kind が "scenario" のパックで、フォルダか、そのフォルダを zip にしたもの。
/// 探す場所: ゲームに入れて配るもの(Content/scenarios)と、ユーザーが置くもの(ドキュメント\GravewardSpiral\Scenarios)。
/// zip は、展開用のフォルダ(cacheDir)に一度だけ展開して、フォルダのパックとして読む(zip の大きさと日時が変われば展開し直す)。
/// 標準のパック(Content の直下)の上に、選んだシナリオを1つ重ねて読む。
/// </summary>
public static class ScenarioCatalog
{
    public const string Kind = "scenario";

    public static List<ScenarioInfo> Find(IEnumerable<string> searchDirs, string cacheDir, List<string>? problems = null)
    {
        var found = new Dictionary<string, ScenarioInfo>(StringComparer.Ordinal);
        foreach (var dir in searchDirs.Where(System.IO.Directory.Exists))
        {
            foreach (var sub in System.IO.Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
                Add(sub, sub);
            foreach (var zip in System.IO.Directory.EnumerateFiles(dir, "*.zip").Order(StringComparer.Ordinal))
            {
                try
                {
                    Add(zip, Extract(zip, cacheDir));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    problems?.Add($"{Path.GetFileName(zip)}: 展開できません: {ex.Message}");
                }
            }
        }
        return found.Values.OrderBy(s => s.Manifest.Order).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();

        void Add(string source, string folder)
        {
            var m = ContentLibrary.ReadManifest(folder, out var error);
            if (m == null)
            {
                if (error != null) problems?.Add($"{Path.GetFileName(source)}: {error}");
                return;
            }
            if (m.Kind != Kind) return;
            // 同じ id が2つあれば、後から見つけた方(ユーザーが置いたもの)を使う
            found[m.Id] = new ScenarioInfo(m, source, folder);
        }
    }

    /// <summary>
    /// zip を展開する(済んでいれば何もしない)。中身は、一番上に manifest.json があるか、1つのフォルダの中にあるかのどちらか。
    /// zip の外へ書き出すパス(../ など)は使わない。
    /// </summary>
    public static string Extract(string zip, string cacheDir)
    {
        var info = new FileInfo(zip);
        string key = $"{Path.GetFileNameWithoutExtension(zip)}-{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
        string target = Path.Combine(cacheDir, key);
        string done = Path.Combine(target, ".extracted");
        if (!File.Exists(done))
        {
            if (System.IO.Directory.Exists(target)) System.IO.Directory.Delete(target, recursive: true);
            System.IO.Directory.CreateDirectory(target);
            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries)
            {
                string dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"zip の外を指すパスがあります: {entry.FullName}");
                if (entry.FullName.EndsWith('/')) { System.IO.Directory.CreateDirectory(dest); continue; }
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
            File.WriteAllText(done, zip);
        }
        // 1つのフォルダに包まれた zip なら、その中を使う
        if (!File.Exists(Path.Combine(target, ContentLibrary.ManifestFile)))
        {
            var inner = System.IO.Directory.EnumerateDirectories(target).ToList();
            if (inner.Count == 1 && File.Exists(Path.Combine(inner[0], ContentLibrary.ManifestFile))) return inner[0];
        }
        return target;
    }
}
