namespace ProceduralWorld.Core;

public struct SplitMix64
{
    private ulong _state;
    public SplitMix64(ulong seed) => _state = seed;
    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));
    public int Next(int maximum) => maximum > 0 ? (int)(Next() % (uint)maximum) : throw new ArgumentOutOfRangeException(nameof(maximum));
}

public static class PositionalHash
{
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public static ulong Of(ulong seed, long x, long z, ulong salt = 0) =>
        Mix(seed ^ Mix((ulong)x * 0x9E3779B97F4A7C15UL) ^ Mix((ulong)z + 0x632BE59BD9B4E019UL) ^ salt * 0xD1B54A32D192ED03UL);
}

