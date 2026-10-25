namespace GravewardSpiral.Core.Rng;

/// <summary>ゲームの乱数。シードと状態が同じなら、必ず同じ並びを返す。</summary>
public interface IRandom
{
    uint NextUInt();

    /// <summary>min 以上 maxExclusive 未満の整数。</summary>
    int Next(int min, int maxExclusive);

    /// <summary>1 以上 sides 以下(ダイス1個)。</summary>
    int Roll(int sides) => Next(1, sides + 1);

    /// <summary>0 以上 1 未満。</summary>
    double NextDouble() => NextUInt() / 4294967296.0;
}
