using System.Text.Json;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;

namespace GravewardSpiral.Game;

/// <summary>
/// 遊び方の設定(設定の画面で変える)。%LOCALAPPDATA%/GravewardSpiral/options.json に残す。
/// 音量は、今の大きさを 100% として下げる(ユーザーの指定)。
/// </summary>
public sealed class GameOptions
{
    public const string FileName = "options.json";

    /// <summary>100% のときの実際の音量(取り込んだ音の大きさに合わせた、今の大きさ)。</summary>
    private const float BgmFull = 0.7f, SeFull = 0.8f;

    public int BgmVolume { get; set; } = 100;
    public int SeVolume { get; set; } = 100;

    /// <summary>戦闘のログの速さ: slow / normal / fast / fastest。</summary>
    public string LogSpeed { get; set; } = "normal";

    /// <summary>迷宮の線画をモノクロ(白い線)にする。</summary>
    public bool MonoWireframe { get; set; }

    /// <summary>画面の言語(text/&lt;言語&gt;.json の名前)。空なら Windows の言語から決める。</summary>
    public string? Language { get; set; }

    /// <summary>
    /// 迷宮の地図: auto(右上の小さな地図と、M の全体の地図)/ map(M の地図だけ)/ none(地図なし。方眼紙で自分で描く)。
    /// オートマップの記録は、どの設定でも続ける(あとで戻せば見られる)。
    /// </summary>
    public string MapMode { get; set; } = "auto";

    public static readonly string[] MapModes = ["auto", "map", "none"];

    /// <summary>物語の書体(タイトル・迷宮のメッセージ・戦闘のログ・イベントの文): hina(Hina Mincho)/ shippori(しっぽり明朝)/ gothic(BIZ UDゴシック)。</summary>
    public string StoryFont { get; set; } = "hina";

    /// <summary>
    /// AI の手伝い(新しいゲームで、その世界の文を AI に書いてもらう)。キーが無ければ使わない。
    /// キーはここに置かず、資格情報マネージャーに置く(Ai.ApiKeyStore)。
    /// </summary>
    public bool AiEnabled { get; set; } = true;

    /// <summary>AI のモデル(空なら既定)。</summary>
    public string? AiModel { get; set; }

    public static readonly string[] AiModels = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"];

    public bool ShowMiniMap => MapMode == "auto";
    public bool AllowMap => MapMode != "none";

    public static readonly string[] LogSpeeds = ["slow", "normal", "fast", "fastest"];

    /// <summary>戦闘のログ1行を見せる時間(秒)。</summary>
    public double LogSeconds => LogSpeed switch { "slow" => 1.2, "fast" => 0.4, "fastest" => 0.15, _ => 0.75 };

    /// <summary>音量と線画の色を、ゲームに当てる。keepVolume なら音量はそのまま(起動の引数 --volume で決めたとき)。</summary>
    public void Apply(AudioPlayer audio, bool keepVolume = false)
    {
        if (!keepVolume)
        {
            audio.BgmVolume = BgmFull * Math.Clamp(BgmVolume, 0, 100) / 100f;
            audio.SeVolume = SeFull * Math.Clamp(SeVolume, 0, 100) / 100f;
        }
        WireframeRenderer.Monochrome = MonoWireframe;
        TextRenderer.StoryFont = TextRenderer.StoryFonts.Contains(StoryFont) ? StoryFont : "hina";
    }

    public static GameOptions Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<GameOptions>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException)
        {
            // 壊れていたら既定に戻す
        }
        return new GameOptions();
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
