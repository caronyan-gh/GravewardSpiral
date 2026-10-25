using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Ai;

/// <summary>
/// AI が新しい世界のために書いた文(背景の序文、階ごとの名前・入ったときの一文・迷宮で拾う書き付け)。
/// 新しいゲームのときに一度だけ書いてセーブに入れ、読み直しても呼び直さない。
/// 無い項目(null や空)は、パックの決まった文(floor_themes.json など)を使う。
/// </summary>
public sealed class WorldText
{
    /// <summary>書いた AI のモデル(記録用)。</summary>
    public string? Model { get; set; }

    /// <summary>この世界の背景(なぜ迷宮があるのか、何が眠っているのか)。新しいゲームの始めに見せる。</summary>
    public LocalizedText? Prologue { get; set; }

    /// <summary>階ごとの文(0 が B1)。</summary>
    public List<WorldFloorText> Floors { get; set; } = [];

    public WorldFloorText? Floor(int index) => index >= 0 && index < Floors.Count ? Floors[index] : null;
}

public sealed class WorldFloorText
{
    public LocalizedText? Name { get; set; }
    public LocalizedText? Enter { get; set; }

    /// <summary>この階で拾える書き付け(手記の切れ端、墓碑の文など)。イベントの手順 world_doc で出す。</summary>
    public List<LocalizedText> Documents { get; set; } = [];
}
