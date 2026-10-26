using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.MazeGen;

/// <summary>
/// 迷宮の生成の設定。パックの mazegen.json([{"id": "default", ...}])から読む。
/// 深さはすべて「何階目か」ではなく割合(0 が最初の階、1 が最下層)で書くので、階層の数が変わっても使える。
/// </summary>
public sealed class MazeGenSettings
{
    public const string Collection = "mazegen";
    public const string DefaultId = "default";

    public string Id { get; set; } = DefaultId;

    public int DefaultFloors { get; set; } = 10;
    public int DefaultWidth { get; set; } = 20;
    public int DefaultHeight { get; set; } = 20;
    public int MinSize { get; set; } = 8;
    public int MaxSize { get; set; } = 64;
    public int MaxFloors { get; set; } = 99;

    /// <summary>部屋の一辺の長さ。</summary>
    public IntRange RoomSize { get; set; } = new(2, 5);

    /// <summary>部屋が占める面積の割合の目安。</summary>
    public double RoomDensity { get; set; } = 0.25;

    /// <summary>道をつないだあとに、壁を追加で開ける割合(ループ)。</summary>
    public double LoopChance { get; set; } = 0.08;

    /// <summary>行き止まりを岩盤で埋める割合(迷路のこまかさを減らす)。</summary>
    public double DeadEndFill { get; set; } = 0.5;

    /// <summary>部屋の出入り口を扉にする割合。</summary>
    public double DoorChance { get; set; } = 0.7;

    /// <summary>扉を隠し扉にする割合(遠回りの道がある扉だけ)。</summary>
    public double SecretDoorChance { get; set; } = 0.12;

    public FeatureRule OneWay { get; set; } = new() { MinDepth = 0.2, Count = new(0, 2) };
    public RegionRule Dark { get; set; } = new() { MinDepth = 0.1, Count = new(0, 2), Size = new(4, 12) };
    public RegionRule Fog { get; set; } = new() { MinDepth = 0.2, Count = new(0, 2), Size = new(5, 14) };
    public RegionRule AntiMagic { get; set; } = new() { MinDepth = 0.4, Count = new(0, 1), Size = new(4, 10) };

    /// <summary>暗闇と霧が重なってよい深さ(これより浅い階では重ねない)。</summary>
    public double DarkFogOverlapMinDepth { get; set; } = 0.6;

    public FeatureRule Spinners { get; set; } = new() { MinDepth = 0.1, Count = new(0, 3) };

    /// <summary>暗闇の中に回転床を置いてよい深さ。</summary>
    public double SpinnerInDarkMinDepth { get; set; } = 0.5;

    public FeatureRule Teleporters { get; set; } = new() { MinDepth = 0.2, Count = new(0, 2) };

    /// <summary>テレポーターでしか入れない閉じた部屋を作る割合(テレポーターを置く階で)。</summary>
    public double TeleportRoomChance { get; set; } = 0.3;

    public FeatureRule Pits { get; set; } = new() { MinDepth = 0.1, Count = new(0, 3) };
    public FeatureRule Chutes { get; set; } = new() { MinDepth = 0.3, Count = new(0, 1) };

    /// <summary>錠のある扉(その階の鍵で開く。鍵は錠より手前に置く)。</summary>
    public FeatureRule Locks { get; set; } = new() { MinDepth = 0.15, Count = new(0, 1) };

    /// <summary>エレベーターを置く階の組の大きさ(この数の階ごとに1つの組。階が elevator_min_floors より少なければ置かない)。</summary>
    public int ElevatorEvery { get; set; } = 4;
    public int ElevatorMinFloors { get; set; } = 6;

    /// <summary>端のループを開ける割合と、開けてよい深さ。</summary>
    public double WrapChance { get; set; } = 0.15;
    public double WrapMinDepth { get; set; } = 0.3;

    /// <summary>1つの階に散らすイベント(壁の文字、遺品、墓石など。events.json の scatter)の数。</summary>
    public IntRange EventsPerFloor { get; set; } = new(1, 3);

    /// <summary>1つの迷宮に置く、連動するイベントの組(events.json の scatter.chain)の数。</summary>
    public IntRange ChainsPerDungeon { get; set; } = new(1, 3);

    /// <summary>そのうち、町の依頼の組を必ず置く数(置ける組があれば)。</summary>
    public int QuestChainsMin { get; set; } = 1;

    /// <summary>はめ込む部屋(プレハブ)。</summary>
    public List<PrefabRule> Prefabs { get; set; } = [];

    /// <summary>1つの階を作り直す回数の上限(検査に通らないとき)。</summary>
    public int MaxAttempts { get; set; } = 30;

    public static MazeGenSettings Load(ContentLibrary content, string id = DefaultId)
    {
        var item = content.Find(Collection, id);
        if (item == null) return new MazeGenSettings();
        var s = item.As<MazeGenSettings>();
        foreach (var p in s.Prefabs) p.ResolvedPath = item.ResolvePath(p.File);
        // シナリオなどのパックが足すはめ込む部屋(prefabs.json。物語の部屋)。ファイルはそのパックのフォルダから探す
        foreach (var extra in content.All(PrefabRule.Collection))
        {
            var p = extra.As<PrefabRule>();
            p.ResolvedPath = extra.ResolvePath(p.File);
            s.Prefabs.Add(p);
        }
        return s;
    }
}

/// <summary>最小と最大(両方を含む)。JSON では [2, 5] と書く。</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RangeConverter))]
public readonly record struct IntRange(int Min, int Max)
{
    public int Pick(Rng.IRandom rng) => Max <= Min ? Min : rng.Next(Min, Max + 1);
}

public sealed class RangeConverter : System.Text.Json.Serialization.JsonConverter<IntRange>
{
    public override IntRange Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        var a = System.Text.Json.JsonSerializer.Deserialize<int[]>(ref reader, options) ?? [];
        return a.Length switch
        {
            1 => new IntRange(a[0], a[0]),
            2 => new IntRange(Math.Min(a[0], a[1]), Math.Max(a[0], a[1])),
            _ => throw new System.Text.Json.JsonException("範囲は [最小, 最大] で書きます"),
        };
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, IntRange value, System.Text.Json.JsonSerializerOptions options) =>
        System.Text.Json.JsonSerializer.Serialize(writer, new[] { value.Min, value.Max }, options);
}

/// <summary>仕掛けを置く数。MinDepth より浅い階には置かない。</summary>
public sealed class FeatureRule
{
    public double MinDepth { get; set; }
    public IntRange Count { get; set; } = new(0, 0);
}

/// <summary>区域(暗闇・霧・魔法禁止)。Size より小さい区域は作らない。</summary>
public sealed class RegionRule
{
    public double MinDepth { get; set; }
    public IntRange Count { get; set; } = new(0, 0);
    public IntRange Size { get; set; } = new(4, 10);
}

/// <summary>はめ込む部屋。File はパックのフォルダからの相対パス(マップのテキスト形式で、entrance を書く)。</summary>
public sealed class PrefabRule
{
    /// <summary>mazegen.json の外で、パックが足すはめ込む部屋の集まり(prefabs.json。項目には id が要る)。</summary>
    public const string Collection = "prefabs";

    public string? Id { get; set; }
    public string File { get; set; } = "";

    /// <summary>はめ込む深さの範囲(割合)。</summary>
    public double MinDepth { get; set; }
    public double MaxDepth { get; set; } = 1;

    /// <summary>その深さの階ではめ込む割合(1 なら必ず)。</summary>
    public double Chance { get; set; } = 1;

    /// <summary>
    /// 迷宮全体で1つだけ(深さの範囲の階から1つを選んで、はめ込む)。物語のイベントの部屋や、階をまたぐ仕掛けの部屋に使う。
    /// chance は、その迷宮に入れるかの割合(1 なら必ず)。
    /// </summary>
    public bool Once { get; set; }

    /// <summary>
    /// 仕掛けの組の名前(once の部屋だけ)。同じ組の部屋は、入れるかを1回の抽選でまとめて決める(割合は組の最初の部屋の chance)。
    /// 紋章の祠と封印の扉のように、一部だけ入ると解けない仕掛けに使う。
    /// </summary>
    public string? Group { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ResolvedPath { get; set; } = "";
}
