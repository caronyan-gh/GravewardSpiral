namespace GravewardSpiral.Core.Content;

/// <summary>
/// 標準の曲の棚(パックの music.json)の1曲。棚に置いただけでは流れない。
/// シナリオは manifest の music で「流すところ(town、maze_b3、イベントの曲の名前など) → 棚の曲の id」を割り当てて使う。
/// </summary>
public sealed class MusicTrack
{
    public const string Collection = "music";

    public string Id { get; set; } = "";

    /// <summary>曲の題(元の題のまま。訳さない)。</summary>
    public string Title { get; set; } = "";

    /// <summary>パックの中のファイル(例: audio/bgm/library/sleep_in_the_tide.mp3)。</summary>
    public string File { get; set; } = "";

    /// <summary>どんな曲か(エディタで選ぶときに出す)。</summary>
    public LocalizedText? Note { get; set; }

    /// <summary>歌声の入った曲の歌詞(歌っている言葉のまま。エディタで選ぶときに見せる)。</summary>
    public string? Lyrics { get; set; }
}
