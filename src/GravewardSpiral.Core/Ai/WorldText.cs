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

    /// <summary>自由な迷宮の主(名前・出会ったときの文・倒したときの文)。無ければ「迷宮の主」とパックの決まった文。</summary>
    public WorldBossText? Boss { get; set; }

    /// <summary>
    /// 途中の階のボス(id → 名前と文)。シナリオエディタが AI に書かせて、パックのボス(bosses.json と文言)に入れる。
    /// ゲームの世界の文には持たない(セーブにも worlds.json にも書かない)。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, WorldBossText> OtherBosses { get; set; } = new(StringComparer.Ordinal);

    /// <summary>酒場の噂(シナリオエディタが AI に書かせて、パックの rumors.json に入れる。世界の文には持たない)。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<LocalizedText> Rumors { get; set; } = [];

    public WorldFloorText? Floor(int index) => index >= 0 && index < Floors.Count ? Floors[index] : null;
}

public sealed class WorldBossText
{
    public LocalizedText? Name { get; set; }
    public LocalizedText? Intro { get; set; }
    public LocalizedText? Victory { get; set; }

    /// <summary>従う者の組の名前(主の組の2組目から順に。足りない組は元の敵の名前)。</summary>
    public List<LocalizedText?> Escorts { get; set; } = [];
}

public sealed class WorldFloorText
{
    public LocalizedText? Name { get; set; }
    public LocalizedText? Enter { get; set; }

    /// <summary>この階で拾える書き付け(手記の切れ端、墓碑の文など)。イベントの手順 world_doc で出す。</summary>
    public List<LocalizedText> Documents { get; set; } = [];
}
