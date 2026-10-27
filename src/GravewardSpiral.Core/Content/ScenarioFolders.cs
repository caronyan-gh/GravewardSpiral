namespace GravewardSpiral.Core.Content;

/// <summary>ユーザーのシナリオの置き場所(%LOCALAPPDATA%\GravewardSpiral\Scenarios)。ゲームとエディタで同じ所を使う。</summary>
public static class ScenarioFolders
{
    public static string Default { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GravewardSpiral", "Scenarios");
}
