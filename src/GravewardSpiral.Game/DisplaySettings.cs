using System.Text.Json;

namespace GravewardSpiral.Game;

/// <summary>画面の設定。%LOCALAPPDATA%/GravewardSpiral/display.json に残す。</summary>
public sealed class DisplaySettings
{
    public const string FileName = "display.json";

    public bool Fullscreen { get; set; }
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;

    public static DisplaySettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException)
        {
            // 壊れていたら既定に戻す
        }
        return new DisplaySettings();
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
