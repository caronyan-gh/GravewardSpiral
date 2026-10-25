using System.Globalization;
using System.Text;

namespace GravewardSpiral.Core.Rng;

/// <summary>
/// 元のシードから、用途ごと(生成・宝・遭遇・戦闘など)の乱数を作る。
/// 用途を分けるので、戦闘でダイスを多く振っても、宝の抽選の並びは変わらない。
/// </summary>
public sealed class RngStreams
{
    public static class Names
    {
        public const string MazeGen = "mazegen";
        public const string Loot = "loot";
        public const string Encounter = "encounter";
        public const string Combat = "combat";
        public const string Chargen = "chargen";
    }

    private readonly Dictionary<string, Pcg32> _streams = new(StringComparer.Ordinal);

    public ulong MasterSeed { get; }

    public RngStreams(ulong masterSeed) => MasterSeed = masterSeed;

    public IRandom this[string name] => Get(name);

    public IRandom Get(string name)
    {
        if (!_streams.TryGetValue(name, out var rng))
        {
            rng = Create(MasterSeed, name);
            _streams[name] = rng;
        }
        return rng;
    }

    /// <summary>元のシードと用途の名前から、新しい乱数を作る(状態は持たない使い捨て用)。</summary>
    public static Pcg32 Create(ulong masterSeed, string name)
    {
        ulong mixed = SplitMix64(masterSeed ^ Fnv1a64(name));
        ulong stream = SplitMix64(mixed);
        return new Pcg32(mixed, stream);
    }

    public Dictionary<string, PcgState> Save() =>
        _streams.ToDictionary(kv => kv.Key, kv => kv.Value.Save(), StringComparer.Ordinal);

    public static RngStreams Restore(ulong masterSeed, IReadOnlyDictionary<string, PcgState> saved)
    {
        var streams = new RngStreams(masterSeed);
        foreach (var (name, state) in saved)
            streams._streams[name] = Pcg32.FromState(state);
        return streams;
    }

    /// <summary>
    /// 新規ゲームの画面で入力されたシードを数にする。数ならそのまま、文字ならハッシュにする。
    /// 空なら null(呼ぶ側が時刻などから決める)。
    /// </summary>
    public static ulong? ParseSeed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number;
        return Fnv1a64(trimmed);
    }

    public static ulong Fnv1a64(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash = unchecked(hash * 1099511628211UL);
        }
        return hash;
    }

    public static ulong SplitMix64(ulong x)
    {
        x = unchecked(x + 0x9E3779B97F4A7C15UL);
        x = unchecked((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL);
        x = unchecked((x ^ (x >> 27)) * 0x94D049BB133111EBUL);
        return x ^ (x >> 31);
    }
}
