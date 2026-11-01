namespace GravewardSpiral.Game;

/// <summary>フォルダを消す(ごみ箱へ移すか、完全に消すかは、消す人が選ぶ)。</summary>
public static class Trash
{
    public static void Delete(string dir, bool toRecycleBin)
    {
        if (!Directory.Exists(dir)) return;
        if (toRecycleBin && OperatingSystem.IsWindows())
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else Directory.Delete(dir, recursive: true);
    }
}
