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

    /// <summary>迷宮での後ろのキー: step(1歩下がる)/ turn(振り向く)。Shift を押しながらだと、もう一方の動きになる。</summary>
    public string BackKey { get; set; } = "step";

    public static readonly string[] BackKeys = ["step", "turn"];

    /// <summary>物語の書体(タイトル・迷宮のメッセージ・戦闘のログ・イベントの文): hina(Hina Mincho)/ shippori(しっぽり明朝)/ gothic(BIZ UDゴシック)。</summary>
    public string StoryFont { get; set; } = "hina";

    /// <summary>
    /// AI の手伝い(新しいゲームで、その世界の文を AI に書いてもらう)。キーが無ければ使わない。
    /// キーはここに置かず、資格情報マネージャーに置く(Ai.ApiKeyStore)。
    /// </summary>
    public bool AiEnabled { get; set; } = true;

    /// <summary>AI のモデル(空なら既定)。</summary>
    public string? AiModel { get; set; }

    public static readonly string[] AiModels = Core.Ai.ClaudeClient.Models;

    /// <summary>押し続けたときの繰り返しの間隔(ミリ秒)。カーソルはメニューなど、移動は迷宮で歩き続けるとき。繰り返しが始まるまでの待ちは RepeatDelayMs で決まる。</summary>
    public int CursorRepeatMs { get; set; } = 100;
    public int MoveRepeatMs { get; set; } = 300;

    public const int RepeatMinMs = 10, RepeatMaxMs = 1500;

    /// <summary>
    /// 繰り返しが始まるまでの待ち(ミリ秒)。間隔の 3 倍(10ms のとき)から、間隔が大きくなるほど倍率を下げ、1000ms 以上で 1 倍(倍率は間隔の対数で変える)。
    /// ただし RepeatDelayMinMs より短くはしない(短い間隔だと、軽く押しただけで繰り返してしまう)。
    /// </summary>
    public static int RepeatDelayMs(int intervalMs)
    {
        int i = Math.Clamp(intervalMs, RepeatMinMs, RepeatMaxMs);
        double factor = i >= 1000 ? 1 : 3 - Math.Log10(i / 10.0);
        return Math.Max(RepeatDelayMinMs, (int)Math.Round(i * factor));
    }

    /// <summary>繰り返しが始まるまでの、いちばん短い待ち(ミリ秒)。</summary>
    public const int RepeatDelayMinMs = 300;

    /// <summary>設定の画面で ◀ ▶ を押したときの一段(小さい値ほど細かく)。</summary>
    public static int RepeatStep(int ms, int step)
    {
        int at = step > 0 ? ms : ms - 1;
        int unit = at < 100 ? 5 : at < 300 ? 10 : at < 1000 ? 50 : 100;
        int next = step > 0 ? (ms / unit + 1) * unit : (ms - 1) / unit * unit;
        return Math.Clamp(next, RepeatMinMs, RepeatMaxMs);
    }

    /// <summary>迷宮の絵をマウスで押して動く(真ん中は調べる。まわりの8つの領域で、進む・横歩き・向きを変える・振り向く・下がる)。</summary>
    public bool MazeMouse { get; set; } = true;

    /// <summary>地図(小窓と全体の地図)のマスを押して、そのマスまで自動で歩く。</summary>
    public bool AutoWalk { get; set; } = true;

    /// <summary>迷宮のマウスの領域(区切りの線と矢印)を、絵の上に薄く重ねて見せる。</summary>
    public bool MazeMouseOverlay { get; set; }

    /// <summary>呪文を唱える画面のレベルのタブを、マウスを載せるだけで切り替える(切なら、クリックで切り替える)。</summary>
    public bool SpellTabHover { get; set; }

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
        CursorRepeatMs = Math.Clamp(CursorRepeatMs, RepeatMinMs, RepeatMaxMs);
        MoveRepeatMs = Math.Clamp(MoveRepeatMs, RepeatMinMs, RepeatMaxMs);
        (Input.InputState.RepeatDelay, Input.InputState.RepeatInterval) = (RepeatDelayMs(CursorRepeatMs) / 1000.0, CursorRepeatMs / 1000.0);
        (Input.InputState.MoveRepeatDelay, Input.InputState.MoveRepeatInterval) = (RepeatDelayMs(MoveRepeatMs) / 1000.0, MoveRepeatMs / 1000.0);
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
