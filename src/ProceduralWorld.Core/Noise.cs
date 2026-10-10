namespace ProceduralWorld.Core;

public sealed class PerlinNoise
{
    private readonly int[] _permutation = new int[512];
    private readonly double _ox, _oy, _oz;

    public PerlinNoise(ulong seed)
    {
        var random = new SplitMix64(seed);
        _ox = random.NextDouble() * 256; _oy = random.NextDouble() * 256; _oz = random.NextDouble() * 256;
        var values = Enumerable.Range(0, 256).ToArray();
        for (int i = 255; i > 0; i--) { int j = random.Next(i + 1); (values[i], values[j]) = (values[j], values[i]); }
        for (int i = 0; i < 512; i++) _permutation[i] = values[i & 255];
    }

    public double Sample(double x, double z) => Sample(x, 0, z);
    public double Sample(double x, double y, double z)
    {
        x += _ox; y += _oy; z += _oz;
        double floorX = Math.Floor(x), floorY = Math.Floor(y), floorZ = Math.Floor(z);
        int ix = (int)floorX & 255, iy = (int)floorY & 255, iz = (int)floorZ & 255;
        x -= floorX; y -= floorY; z -= floorZ;
        double u = Fade(x), v = Fade(y), w = Fade(z);
        int a = _permutation[ix] + iy, aa = _permutation[a] + iz, ab = _permutation[a + 1] + iz;
        int b = _permutation[ix + 1] + iy, ba = _permutation[b] + iz, bb = _permutation[b + 1] + iz;
        return Lerp(w,
            Lerp(v, Lerp(u, Grad(_permutation[aa], x, y, z), Grad(_permutation[ba], x - 1, y, z)), Lerp(u, Grad(_permutation[ab], x, y - 1, z), Grad(_permutation[bb], x - 1, y - 1, z))),
            Lerp(v, Lerp(u, Grad(_permutation[aa + 1], x, y, z - 1), Grad(_permutation[ba + 1], x - 1, y, z - 1)), Lerp(u, Grad(_permutation[ab + 1], x, y - 1, z - 1), Grad(_permutation[bb + 1], x - 1, y - 1, z - 1))));
    }
    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double t, double a, double b) => a + t * (b - a);
    private static double Grad(int hash, double x, double y, double z)
    {
        int h = hash & 15; double u = h < 8 ? x : y; double v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}

public sealed class FractalNoise
{
    private readonly PerlinNoise[] _octaves;
    private readonly double _frequency, _persistence, _normalization;
    public FractalNoise(ulong seed, int octaves, double frequency, double persistence = 0.5)
    {
        if (octaves <= 0) throw new ArgumentOutOfRangeException(nameof(octaves));
        _frequency = frequency; _persistence = persistence; _octaves = new PerlinNoise[octaves];
        var random = new SplitMix64(seed); double amplitude = 1;
        for (int i = 0; i < octaves; i++) { _octaves[i] = new PerlinNoise(random.Next()); _normalization += amplitude; amplitude *= persistence; }
    }
    public double Sample(double x, double z) => SampleInternal(x, 0, z, false);
    public double Sample(double x, double y, double z) => SampleInternal(x, y, z, true);
    private double SampleInternal(double x, double y, double z, bool threeDimensions)
    {
        double sum = 0, amplitude = 1, frequency = _frequency;
        foreach (var octave in _octaves)
        {
            sum += amplitude * (threeDimensions ? octave.Sample(x * frequency, y * frequency, z * frequency) : octave.Sample(x * frequency, z * frequency));
            amplitude *= _persistence; frequency *= 2;
        }
        return sum / _normalization;
    }
}

public sealed class RidgedNoise
{
    private readonly PerlinNoise[] _octaves;
    private readonly double _frequency;
    public RidgedNoise(ulong seed, int octaves, double frequency)
    {
        _frequency = frequency; _octaves = new PerlinNoise[octaves]; var random = new SplitMix64(seed);
        for (int i = 0; i < octaves; i++) _octaves[i] = new PerlinNoise(random.Next());
    }
    public double Sample(double x, double z)
    {
        double sum = 0, norm = 0, amplitude = 0.5, frequency = _frequency, weight = 1;
        foreach (var octave in _octaves)
        {
            double sample = 1 - Math.Abs(octave.Sample(x * frequency, z * frequency));
            sample *= sample * weight; weight = Math.Max(0, Math.Min(1, sample * 2));
            sum += sample * amplitude; norm += amplitude; amplitude *= 0.5; frequency *= 2;
        }
        return sum / norm;
    }
}

