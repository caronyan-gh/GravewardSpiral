using System.Text.Json;
using GravewardSpiral.Core.Ai;

namespace GravewardSpiral.Editor;

/// <summary>
/// シナリオエディタの設定(%LOCALAPPDATA%\GravewardSpiral\editor.json)。ゲームの設定とは別に持つ(API キーだけはゲームと共通)。
/// </summary>
public sealed class EditorSettings
{
    /// <summary>画面の言語("ja" か "en")。はじめは Windows の言語から。</summary>
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";

    /// <summary>物語の「AI に書かせる」のモデル。</summary>
    public string AiModel { get; set; } = ClaudeClient.DefaultModel;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GravewardSpiral", "editor.json");

    /// <summary>確かめの起動(--screenshot)では読み書きしない。</summary>
    public static bool Persist { get; set; } = true;

    public static EditorSettings Load()
    {
        if (!Persist) return new EditorSettings { Language = "ja" };   // 確かめの起動は日本語(--lang で変える)
        try
        {
            var s = File.Exists(FilePath) ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) : null;
            if (s != null && !ClaudeClient.Models.Contains(s.AiModel)) s.AiModel = ClaudeClient.DefaultModel;
            if (s != null && s.Language is not ("ja" or "en")) s.Language = "ja";
            return s ?? new EditorSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new EditorSettings();
        }
    }

    public void Save()
    {
        if (!Persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
    }
}
