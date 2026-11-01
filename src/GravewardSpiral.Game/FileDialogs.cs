using System.Runtime.InteropServices;

namespace GravewardSpiral.Game;

/// <summary>
/// Windows の「名前を付けて保存」と「開く」の窓(クランファイルの書き出しと読み込み)。窓を閉じるまでゲームは止まる。
/// 窓は STA のスレッドで開く。持ち主の窓(ゲームの窓)は渡さない(ゲームのスレッドは窓が閉じるまで待っているので、
/// 持ち主にすると、窓が持ち主へ送る知らせに答えられず、両方が止まる)。全画面のあいだは、窓が隠れないようにいったん窓の表示に戻し、閉じたら戻す。
/// Windows 以外では開かず null を返す。
/// </summary>
public static class FileDialogs
{
    /// <summary>保存する場所を選ぶ。選ばなければ null。</summary>
    public static string? Save(IDisplay? display, string title, string filter, string initialDir, string fileName, string defaultExt) =>
        Show(display, title, filter, initialDir, fileName, defaultExt, save: true);

    /// <summary>開くファイルを選ぶ。選ばなければ null。</summary>
    public static string? Open(IDisplay? display, string title, string filter, string initialDir) =>
        Show(display, title, filter, initialDir, "", null, save: false);

    /// <summary>filter の形: 「表示名|*.拡張子|表示名|*.*」。</summary>
    private static string? Show(IDisplay? display, string title, string filter, string initialDir, string fileName, string? defaultExt, bool save)
    {
        if (!OperatingSystem.IsWindows()) return null;
        bool wasFull = display?.Fullscreen == true;
        if (wasFull) display!.SetFullscreen(false);
        string? result = null;
        var thread = new Thread(() => result = Run(IntPtr.Zero, title, filter, initialDir, fileName, defaultExt, save));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (wasFull) display!.SetFullscreen(true);
        return result;
    }

    private const int MaxPath = 4096;
    private const int OfnOverwritePrompt = 0x2, OfnNoChangeDir = 0x8, OfnPathMustExist = 0x800, OfnFileMustExist = 0x1000, OfnExplorer = 0x80000;

    private static string? Run(IntPtr owner, string title, string filter, string initialDir, string fileName, string? defaultExt, bool save)
    {
        IntPtr buffer = Marshal.AllocHGlobal(MaxPath * 2);
        try
        {
            var name = (fileName + '\0').ToCharArray();
            Marshal.Copy(name, 0, buffer, Math.Min(name.Length, MaxPath - 1));
            Marshal.WriteInt16(buffer, (MaxPath - 1) * 2, 0);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = filter.Replace('|', '\0') + "\0\0",
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = MaxPath,
                lpstrInitialDir = Directory.Exists(initialDir) ? initialDir : null,
                lpstrTitle = title,
                lpstrDefExt = defaultExt,
                Flags = OfnExplorer | OfnNoChangeDir | OfnPathMustExist | (save ? OfnOverwritePrompt : OfnFileMustExist),
            };
            bool ok = save ? GetSaveFileNameW(ref ofn) : GetOpenFileNameW(ref ofn);
            return ok ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);
}
