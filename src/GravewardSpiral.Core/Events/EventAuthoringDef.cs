using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Events;

/// <summary>共通フラグの名前と説明(flags.json)。値は従来どおり世界の Flags に保存する。</summary>
public sealed class EventFlagDef
{
    public string Id { get; set; } = "";
    public LocalizedText? Name { get; set; }
    public string? Note { get; set; }
}

/// <summary>仕掛けのセット(event_sets.json)。地図に置く部品と内部処理を区別する。複製時は専用フラグ・OwnedFlags・OwnedCounters と部品間の参照を一緒に写す。共通フラグは変えない。</summary>
public sealed class EventSetDef
{
    public string Id { get; set; } = "";
    public LocalizedText? Name { get; set; }
    /// <summary>仕掛けの種類の表示名(例: 双子のスイッチ)。セットごとの名前を変えても残る。</summary>
    public LocalizedText? Kind { get; set; }
    public List<EventSetPart> Parts { get; set; } = [];
    public List<string> OwnedFlags { get; set; } = [];
    public List<string> OwnedCounters { get; set; } = [];
}

public sealed class EventSetPart
{
    /// <summary>event / gate / dest。</summary>
    public string Kind { get; set; } = "event";
    /// <summary>イベントの id、または門・着地点の印の名前。</summary>
    public string Target { get; set; } = "";
    public LocalizedText? Name { get; set; }
    /// <summary>false は地図に置かない内部処理(別のイベントから呼ぶ、町で起こるなど)。</summary>
    public bool Place { get; set; } = true;
    /// <summary>テンプレート内のイベント専用フラグ。既存イベントを変更せず、セットとして取り込むときに所有者を指定できる。</summary>
    public string? OwnFlag { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Mark => Kind + ":" + Target;
}
