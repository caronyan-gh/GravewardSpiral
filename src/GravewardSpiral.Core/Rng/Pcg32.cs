namespace GravewardSpiral.Core.Rng;

/// <summary>
/// PCG32 (XSH RR)。System.Random は版によって並びが変わりうるので使わない。
/// 状態は (State, Increment) の2つで、そのままセーブに書ける。
/// </summary>
public sealed class Pcg32 : IRandom
{
    private const ulong Multiplier = 6364136223846793005UL;

    public ulong State { get; private set; }
    public ulong Increment { get; }

    public Pcg32(ulong seed, ulong stream)
    {
        Increment = (stream << 1) | 1UL;
        State = 0;
        NextUInt();
        State += seed;
        NextUInt();
    }

    private Pcg32(PcgState saved)
    {
        State = saved.State;
        Increment = saved.Increment | 1UL;
    }

    public static Pcg32 FromState(PcgState saved) => new(saved);

    public PcgState Save() => new(State, Increment);

    public uint NextUInt()
    {
        ulong old = State;
        State = unchecked(old * Multiplier + Increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    public int Next(int min, int maxExclusive)
    {
        if (maxExclusive <= min)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), $"範囲が空です: [{min}, {maxExclusive})");
        uint range = (uint)((long)maxExclusive - min);
        // Lemire の方法で偏りなく範囲に落とす
        ulong m = (ulong)NextUInt() * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = unchecked(0u - range) % range;
            while (low < threshold)
            {
                m = (ulong)NextUInt() * range;
                low = (uint)m;
            }
        }
        return min + (int)(m >> 32);
    }
}

public readonly record struct PcgState(ulong State, ulong Increment);
