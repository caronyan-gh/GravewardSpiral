namespace GravewardSpiral.Core.Content;

/// <summary>落とされたものが、そのままでは置けない理由。</summary>
public enum DropProblem
{
    None,
    /// <summary>フォルダでも zip でもない(か、もう無い)。</summary>
    NotScenario,
    /// <summary>zip が展開できない、または manifest.json が読めない。Detail に理由。</summary>
    Unreadable,
    /// <summary>シナリオだが迷宮が無い(一覧に出せない)。</summary>
    NoDungeon,
    /// <summary>ゲームに入っているシナリオと同じ id(置くと、入っている方が隠れる)。</summary>
    Bundled,
    /// <summary>もうシナリオの置き場所にある。</summary>
    AlreadyPlaced,
}

/// <summary>
/// 置く前に見た結果。Info は読んだシナリオ(置けないときは null)。
/// Existing は、置き場所にもうある同じ id のシナリオ(フォルダか zip。置くと消して入れ替える)。
/// </summary>
public sealed record DropCheck(DropProblem Problem, string? Detail, ScenarioInfo? Info, IReadOnlyList<string> Existing);

/// <summary>
/// タイトル画面に落とされたシナリオ(フォルダか zip)を、ユーザーのシナリオの置き場所へ置く。
/// 置く名前は id(zip は id.zip)。同じ id がもうあれば、置いてから古い方を消す(置けなければ古い方は残る。Install)。
/// </summary>
public static class ScenarioInstall
{
    public static DropCheck Check(string path, string userDir, IEnumerable<string> bundledDirs, string cacheDir)
    {
        string full = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        bool isDir = Directory.Exists(full);
        bool isZip = !isDir && File.Exists(full) && Path.GetExtension(full).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        if (!isDir && !isZip) return Fail(DropProblem.NotScenario);
        if (SamePath(Path.GetDirectoryName(full), userDir)) return Fail(DropProblem.AlreadyPlaced);

        string folder = full;
        if (isZip)
        {
            try
            {
                folder = ScenarioCatalog.Extract(full, cacheDir);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return Fail(DropProblem.Unreadable, ex.Message);
            }
        }
        var m = ContentLibrary.ReadManifest(folder, out var error);
        if (m == null) return error != null ? Fail(DropProblem.Unreadable, error) : Fail(DropProblem.NotScenario);
        if (m.Kind != ScenarioCatalog.Kind) return Fail(DropProblem.NotScenario);
        if (!ScenarioCatalog.HasDungeon(m)) return Fail(DropProblem.NoDungeon);
        if (bundledDirs.SelectMany(d => ScenarioCatalog.Scan(d, cacheDir)).Any(s => s.Id == m.Id)) return Fail(DropProblem.Bundled, m.Id);

        var existing = ScenarioCatalog.Scan(userDir, cacheDir).Where(s => s.Id == m.Id).Select(s => s.Source).ToList();
        return new DropCheck(DropProblem.None, null, new ScenarioInfo(m, full, folder), existing);

        static DropCheck Fail(DropProblem problem, string? detail = null) => new(problem, detail, null, []);
    }

    /// <summary>
    /// 置く(move なら元を消す)。置いた場所を返す。
    /// 古い方は、消す前に仮の名前へ移しておく。移せなければ(使っているファイルがあるなど)、古い方を戻して何も置かずに投げる。
    /// 仮の名前は "." で始まるので、一覧には出ない。消せずに残ったものは、次に置くときに片付ける。
    /// </summary>
    public static string Install(DropCheck check, string userDir, bool move)
    {
        var info = check.Info ?? throw new InvalidOperationException("置けないシナリオです");
        bool isZip = File.Exists(info.Source);
        Directory.CreateDirectory(userDir);
        CleanLeftovers(userDir, SafeName(info.Id));
        string name = SafeName(info.Id) + (isZip ? ".zip" : "");

        // まず仮の名前で置く
        string temp = Path.Combine(userDir, $".{name}.{Guid.NewGuid():N}{NewSuffix}");
        try
        {
            if (isZip)
            {
                if (move) File.Move(info.Source, temp);
                else File.Copy(info.Source, temp);
            }
            else if (move && SameVolume(info.Source, userDir))
            {
                Directory.Move(info.Source, temp);
            }
            else
            {
                CopyFolder(info.Source, temp);
                if (move) Directory.Delete(info.Source, recursive: true);
            }
        }
        catch
        {
            TryRemove(temp);
            throw;
        }

        // 古い方を仮の名前へ移す(移せなければ、移した分を戻し、置いたものを消して投げる)
        var moved = new List<(string From, string To)>();
        try
        {
            foreach (var old in check.Existing)
            {
                if (!File.Exists(old) && !Directory.Exists(old)) continue;
                string to = Path.Combine(userDir, $".{Path.GetFileName(old)}.{Guid.NewGuid():N}{OldSuffix}");
                Retry(() => MovePath(old, to));
                moved.Add((old, to));
            }
        }
        catch
        {
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                try { MovePath(moved[i].To, moved[i].From); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (move && isZip && !File.Exists(info.Source))
            {
                try { File.Move(temp, info.Source); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            TryRemove(temp);
            throw;
        }

        string dest = Path.Combine(userDir, name);
        for (int i = 2; File.Exists(dest) || Directory.Exists(dest); i++)
            dest = Path.Combine(userDir, $"{SafeName(info.Id)}-{i}{(isZip ? ".zip" : "")}");
        MovePath(temp, dest);
        foreach (var (_, to) in moved) TryRemove(to);
        return dest;
    }

    private const string NewSuffix = ".new", OldSuffix = ".old";

    /// <summary>
    /// 前に置いたときの残りを片付ける: 仮の名前のもの(".〜.new" / ".〜.old")と、
    /// id の名前のフォルダでファイルが1つも無いもの(消しかけで残った空のフォルダ)。消せなければそのまま。
    /// </summary>
    private static void CleanLeftovers(string userDir, string safeId)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(userDir))
        {
            string n = Path.GetFileName(path);
            if (n.StartsWith('.') && (n.EndsWith(NewSuffix, StringComparison.Ordinal) || n.EndsWith(OldSuffix, StringComparison.Ordinal))) TryRemove(path);
        }
        string bare = Path.Combine(userDir, safeId);
        if (Directory.Exists(bare) && !Directory.EnumerateFiles(bare, "*", SearchOption.AllDirectories).Any()) TryRemove(bare);
    }

    private static void MovePath(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    /// <summary>エクスプローラーや検索の索引が一瞬つかんでいることがあるので、少し待って何度か試す。</summary>
    private static void Retry(Action action)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (i < 4 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * (i + 1));
            }
        }
    }

    private static void TryRemove(string path)
    {
        try
        {
            Retry(() => Remove(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string SafeName(string id)
    {
        var bad = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => bad.Contains(c) ? '_' : c).ToArray());
    }

    private static bool SamePath(string? a, string b) =>
        a != null && Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static void Remove(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }
}
