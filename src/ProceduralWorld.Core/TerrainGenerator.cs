namespace ProceduralWorld.Core;

public sealed class TerrainGenerator
{
    private readonly WorldSettings _settings;
    private readonly ulong _seed;
    private readonly FractalNoise _warpX, _warpZ, _continental, _regional, _detail, _temperature, _moisture;
    private readonly RidgedNoise _ridges;

    public TerrainGenerator(long seed, WorldSettings settings)
    {
        _settings = settings; _settings.Validate(); _seed = PositionalHash.Mix((ulong)seed);
        double n = settings.Size, cell = settings.CellSize;
        _warpX = new FractalNoise(Sub(1), 3, 1 / (n * 0.28)); _warpZ = new FractalNoise(Sub(2), 3, 1 / (n * 0.28));
        _continental = new FractalNoise(Sub(3), 5, 1 / (n * 0.6)); _regional = new FractalNoise(Sub(4), 4, 1 / (n * 0.32));
        _ridges = new RidgedNoise(Sub(5), 6, cell / 3200.0); _detail = new FractalNoise(Sub(6), 4, cell / 850.0);
        _temperature = new FractalNoise(Sub(7), 3, 1 / (n * 0.45)); _moisture = new FractalNoise(Sub(8), 4, 1 / (n * 0.4));
    }
    private ulong Sub(ulong salt) => PositionalHash.Mix(_seed ^ PositionalHash.Mix(salt));

    public SurfaceMap Generate()
    {
        var map = new SurfaceMap(_settings.Size, _settings.CellSize); int n = map.Size;
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                double wx = x + _warpX.Sample(x, z) * n * 0.09, wz = z + _warpZ.Sample(x, z) * n * 0.09;
                double continental = _continental.Sample(wx, wz) * 0.7;
                double region = Smooth(0.08, 0.48, _regional.Sample(wx, wz) * 0.8 + continental + 0.22);
                double ridge = Math.Pow(_ridges.Sample(wx, wz), 1.7);
                double broad = _regional.Sample(wx * 0.55, wz * 0.55) * 150;
                double h = _settings.BaseElevation + broad + region * ridge * _settings.MountainHeight * _settings.ReliefStrength;
                h += _detail.Sample(wx, wz) * (22 + region * 42);
                map.Height[map.Index(x, z)] = (float)Math.Max(3, h);
            }
        });
        Erosion.Apply(map); CalculateSlopes(map); Hydrology.Apply(map, _settings); ClassifyClimate(map); return map;
    }

    private void CalculateSlopes(SurfaceMap map)
    {
        int n = map.Size;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int x0 = Math.Max(0, x - 1), x1 = Math.Min(n - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(n - 1, z + 1);
            float gx = (map.HeightAt(x1, z) - map.HeightAt(x0, z)) / Math.Max(map.CellSize, (x1 - x0) * map.CellSize);
            float gz = (map.HeightAt(x, z1) - map.HeightAt(x, z0)) / Math.Max(map.CellSize, (z1 - z0) * map.CellSize);
            map.Slope[map.Index(x, z)] = (float)Math.Sqrt(gx * gx + gz * gz);
        }
    }

    private void ClassifyClimate(SurfaceMap map)
    {
        int n = map.Size;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z); float altitude = map.Height[i], slope = map.Slope[i];
            float latitude = Math.Abs(z / (float)(n - 1) * 2 - 1);
            float temperature = 28 - latitude * 24 - altitude * 0.018f + (float)_temperature.Sample(x, z) * 7;
            float moisture = Clamp01(0.48f + (float)_moisture.Sample(x, z) * 0.8f + (map.WaterDepth[i] > 0 ? 0.3f : 0));
            map.Temperature[i] = temperature; map.Moisture[i] = moisture;
            if (map.WaterDepth[i] > 0) map.Biomes[i] = map.Flow[i] >= _settings.RiverThreshold ? Biome.River : Biome.Lake;
            else if (temperature < -4) map.Biomes[i] = Biome.Snow;
            else if (slope > 0.95f) map.Biomes[i] = Biome.Rock;
            else if (temperature < 3) map.Biomes[i] = moisture > 0.45f ? Biome.Taiga : Biome.Tundra;
            else if (temperature > 24) map.Biomes[i] = moisture < 0.22f ? Biome.Desert : moisture > 0.72f ? Biome.Jungle : Biome.Savanna;
            else if (moisture > 0.82f && slope < 0.12f) map.Biomes[i] = Biome.Swamp;
            else if (moisture > 0.56f) map.Biomes[i] = Biome.Forest;
            else map.Biomes[i] = Biome.Grassland;
        }
    }
    private static float Clamp01(float x) => Math.Max(0, Math.Min(1, x));
    private static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
}

