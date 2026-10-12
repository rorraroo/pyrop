using System.Numerics;

namespace Pyrop.Runtime;

public sealed class RandomSource
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public RandomSource()
        : this(System.Random.Shared.NextInt64(long.MinValue, long.MaxValue))
    {
    }

    public RandomSource(long seed)
    {
        Seed(seed);
    }

    internal RandomSource(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        (_s0, _s1, _s2, _s3) = (s0, s1, s2, s3);
    }

    internal ulong NextBits()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var shifted = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= shifted;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    public void Seed(long seed)
    {
        var state = unchecked((ulong)seed);
        _s0 = SplitMix(ref state);
        _s1 = SplitMix(ref state);
        _s2 = SplitMix(ref state);
        _s3 = SplitMix(ref state);
    }

    public double NextFraction() => (NextBits() >> 11) * (1.0 / (1UL << 53));

    public long NextInRange(long lower, long upper)
    {
        var range = unchecked((ulong)upper - (ulong)lower + 1);
        if (range == 0)
        {
            return unchecked((long)NextBits());
        }

        var threshold = (0 - range) % range;
        ulong bits;
        do
        {
            bits = NextBits();
        }
        while (bits < threshold);

        return unchecked(lower + (long)(bits % range));
    }

    private static ulong SplitMix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15;
        var mixed = state;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EB;
        return mixed ^ (mixed >> 31);
    }
}
