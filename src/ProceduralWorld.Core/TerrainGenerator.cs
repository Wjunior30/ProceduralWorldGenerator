namespace ProceduralWorld.Core;

public sealed class TerrainGenerator
{
    private readonly WorldSettings _settings;
    private readonly ulong _seed;
    private readonly TerrainPreset _preset;
    private readonly FractalNoise _warpX, _warpZ, _continental, _regional, _faults, _plateaus, _basins, _detail, _temperature, _moisture;
    private readonly RidgedNoise _ridges;

    public TerrainGenerator(long seed, WorldSettings settings)
    {
        _settings = settings; _settings.Validate(); _seed = PositionalHash.Mix((ulong)seed); _preset = TerrainPresetCatalog.Get(settings.TerrainStyle);
        double n = settings.Size, cell = settings.CellSize;
        _warpX = new(Sub(1), 3, 1 / (n * .42)); _warpZ = new(Sub(2), 3, 1 / (n * .42));
        _continental = new(Sub(3), 5, 1 / (n * .82)); _regional = new(Sub(4), 4, 1 / (n * .38));
        _ridges = new(Sub(5), 6, cell / 3900d); _faults = new(Sub(6), 4, 1 / (n * .55));
        _plateaus = new(Sub(7), 4, 1 / (n * .31)); _basins = new(Sub(8), 3, 1 / (n * .28));
        _detail = new(Sub(9), 4, cell / 1050d); _temperature = new(Sub(10), 3, 1 / (n * .62)); _moisture = new(Sub(11), 4, 1 / (n * .58 * settings.BiomeRegionScale));
    }
    private ulong Sub(ulong salt) => PositionalHash.Mix(_seed ^ PositionalHash.Mix(salt));

    public SurfaceMap Generate()
    {
        var map = new SurfaceMap(_settings.Size, _settings.CellSize); int n = map.Size;
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                double wx = x + _warpX.Sample(x, z) * n * .075, wz = z + _warpZ.Sample(x, z) * n * .075;
                double continent = _continental.Sample(wx, wz), region = _regional.Sample(wx, wz);
                double ridgeNoise = _ridges.Sample(wx, wz), faultBand = 1 - Math.Min(1, Math.Abs(_faults.Sample(wx, wz)) * 2.2);
                double belt = Smooth(.18, .78, ridgeNoise) * (.45 + .55 * Smooth(-.55, .38, region + continent * .35)) * (.7 + .3 * faultBand);
                double foothills = Smooth(.08, .62, belt), core = Math.Pow(Smooth(.35, .94, belt), _preset.RidgePower);
                double baseLand = _settings.BaseElevation + continent * 92 + region * 46 - Math.Pow(Math.Max(0, _basins.Sample(wx, wz)), 2) * 36 * _preset.ValleyStrength;
                double mountain = (foothills * .28 + core * .72) * _settings.MountainHeight * _settings.ReliefStrength * _preset.MountainScale;
                double plateauNoise = Smooth(-.15, .55, _plateaus.Sample(wx, wz));
                double plateau = Math.Round(plateauNoise * 5) / 5d * _settings.MountainHeight * .22 * _preset.PlateauStrength;
                double detailAmplitude = 8 + foothills * 30 + core * 18;
                double h = baseLand + mountain + plateau + _detail.Sample(wx, wz) * detailAmplitude;
                if (_settings.TerrainStyle == TerrainStyle.Volcanic)
                {
                    double cone = Math.Pow(Math.Max(0, _ridges.Sample(wx * 1.7 + 41, wz * 1.7 - 29) - .58) / .42, 2.1);
                    h += cone * _settings.MountainHeight * .52;
                }
                int i = map.Index(x, z); map.Height[i] = (float)Math.Max(4, h); map.Ridge[i] = (float)belt; map.Continentalness[i] = (float)continent;
            }
        });

        Erosion.ApplyThermal(map, _settings.ErosionIterations, _settings.ErosionStrength, _settings.TerrainStyle);
        Erosion.LimitExtremeSlopes(map, _settings.TerrainStyle == TerrainStyle.Alpine ? 1.42f : _settings.TerrainStyle == TerrainStyle.Canyonlands ? 1.18f : .96f, 5);
        Hydrology.Apply(map, _settings);
        Erosion.ApplyFluvialFinishing(map, _settings);
        CalculateSlopes(map); ClassifyClimate(map); ClassifyGeology(map); return map;
    }

    private static void CalculateSlopes(SurfaceMap map)
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
        int n = map.Size; var rainShadow = new float[map.Height.Length];
        for (int z = 0; z < n; z++)
        {
            float airborne = .55f;
            for (int x = 0; x < n; x++)
            {
                int i = map.Index(x, z); float rise = x == 0 ? 0 : Math.Max(0, map.Height[i] - map.Height[map.Index(x - 1, z)]);
                airborne = Math.Max(.08f, Math.Min(.95f, airborne + .012f - rise / 850f)); rainShadow[i] = airborne;
            }
        }
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z); float altitude = map.Height[i], slope = map.Slope[i];
            float latitude = Math.Abs(z / (float)(n - 1) * 2 - 1);
            float temperature = 29 - latitude * 23 - altitude * .017f + (float)_temperature.Sample(x, z) * 5;
            float broadMoisture = .48f + (float)_moisture.Sample(x, z) * .64f;
            float moisture = Clamp01(broadMoisture * .62f + rainShadow[i] * .38f + (map.WaterDepth[i] > 0 ? .25f : 0));
            map.Temperature[i] = temperature; map.Moisture[i] = moisture;
            if (map.WaterDepth[i] > 0) map.Biomes[i] = map.Flow[i] >= _settings.RiverThreshold ? Biome.River : Biome.Lake;
            else if (temperature < -5 || altitude > _settings.BaseElevation + _settings.MountainHeight * .72f) map.Biomes[i] = Biome.Snow;
            else if (slope > .78f || map.Ridge[i] > .82f) map.Biomes[i] = Biome.Rock;
            else if (temperature < 3) map.Biomes[i] = moisture > .46f ? Biome.Taiga : Biome.Tundra;
            else if (temperature > 24) map.Biomes[i] = moisture < .25f ? Biome.Desert : moisture > .70f ? Biome.Jungle : Biome.Savanna;
            else if (moisture > .80f && slope < .10f) map.Biomes[i] = Biome.Swamp;
            else if (moisture > .55f) map.Biomes[i] = Biome.Forest;
            else map.Biomes[i] = Biome.Grassland;
        }
        MarkBeaches(map);
    }

    private static void MarkBeaches(SurfaceMap map)
    {
        int n = map.Size;
        for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
        {
            int i = map.Index(x, z); if (map.WaterDepth[i] > 0 || map.Slope[i] > .16f || map.Biomes[i] is Biome.Rock or Biome.Snow) continue;
            bool nearWater = false; for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) nearWater |= map.WaterDepth[map.Index(x + dx, z + dz)] > 0;
            if (nearWater) map.Biomes[i] = Biome.Beach;
        }
    }
    private static void ClassifyGeology(SurfaceMap map) { for (int i = 0; i < map.Geology.Length; i++) map.Geology[i] = GeologicalProfiles.For(map.Biomes[i]).Identity; }
    private static float Clamp01(float x) => Math.Max(0, Math.Min(1, x));
    private static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
}
