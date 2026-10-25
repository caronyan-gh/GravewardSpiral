namespace GravewardSpiral.Game;

/// <summary>ゲームが読み書きする場所。</summary>
public static class AppPaths
{
    /// <summary>出力フォルダの Content(コンテンツパックと同梱フォント)。</summary>
    public static string Content { get; } = Path.Combine(AppContext.BaseDirectory, "Content");

    /// <summary>設定とセーブの置き場所(%LOCALAPPDATA%/GravewardSpiral)。</summary>
    public static string UserData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GravewardSpiral");

    /// <summary>ユーザーが置くシナリオ(パックのフォルダか zip)と、書き出したクラン(ドキュメント\GravewardSpiral の下)。</summary>
    public static string Documents { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GravewardSpiral");
    public static string Scenarios { get; } = Path.Combine(Documents, "Scenarios");
    public static string Clans { get; } = Path.Combine(Documents, "Clans");

    /// <summary>ゲームに入れて配るシナリオ(Content/scenarios)。</summary>
    public static string BundledScenarios { get; } = Path.Combine(Content, "scenarios");

    /// <summary>zip のシナリオを展開しておく場所。</summary>
    public static string ScenarioCache { get; } = Path.Combine(UserData, "scenario_cache");

    public static string UserFile(string name)
    {
        Directory.CreateDirectory(UserData);
        return Path.Combine(UserData, name);
    }
}
