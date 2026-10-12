namespace ProceduralWorld.Core;

public sealed class TerrainGenerator
{
    private readonly WorldSettings _settings;
    private readonly ulong _seed;
    private readonly TerrainPreset _preset;
    private readonly FractalNoise _warpX, _warpZ, _continental, _regional, _faults, _plateaus, _basins, _detail, _temperature, _moisture, _featureA, _featureB;
    private readonly RidgedNoise _ridges;

    public TerrainGenerator(long seed, WorldSettings settings)
    {
        _settings = settings; _settings.Validate(); _seed = PositionalHash.Mix((ulong)seed); _preset = TerrainPresetCatalog.Get(settings.TerrainStyle);
        _warpX = new(Sub(1), 3, 1 / 720d); _warpZ = new(Sub(2), 3, 1 / 720d);
        _continental = new(Sub(3), 5, 1 / 1500d); _regional = new(Sub(4), 4, 1 / 680d);
        _ridges = new(Sub(5), 6, 1 / 3900d); _faults = new(Sub(6), 4, 1 / 920d);
        _plateaus = new(Sub(7), 4, 1 / 540d); _basins = new(Sub(8), 3, 1 / 460d);
        _detail = new(Sub(9), 4, 1 / 150d); _temperature = new(Sub(10), 3, 1 / 1150d);
        _moisture = new(Sub(11), 4, 1 / (980d * settings.BiomeRegionScale));
        _featureA = new(Sub(12), 4, 1 / 360d); _featureB = new(Sub(13), 3, 1 / 220d);
    }
    private ulong Sub(ulong salt) => PositionalHash.Mix(_seed ^ PositionalHash.Mix(salt));

    public SurfaceMap Generate()
    {
        var map = new SurfaceMap(_settings.Size, _settings.CellSize);
        int n = map.Size;
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                double px = x * map.CellSize, pz = z * map.CellSize;
                double warpScale = 105;
                double wx = px + _warpX.Sample(px, pz) * warpScale;
                double wz = pz + _warpZ.Sample(px, pz) * warpScale;
                double continent = _continental.Sample(wx, wz);
                double region = _regional.Sample(wx, wz);
                double ridgeNoise = _ridges.Sample(wx, wz);
                double fault = _faults.Sample(wx, wz);
                double plateauNoise = _plateaus.Sample(wx, wz);
                double basinNoise = _basins.Sample(wx, wz);
                double detail = _detail.Sample(wx, wz);
                double featureA = _featureA.Sample(wx, wz);
                double featureB = _featureB.Sample(wx, wz);
                double ridge = Smooth(.16, .83, ridgeNoise) * (.68 + .32 * Smooth(-.5, .45, region + continent * .25));
                double basin = Smooth(.18, .76, basinNoise);
                double strata = Smooth(-.8, .8, plateauNoise + featureB * .18);
                GeologicalIdentity geology = SelectGeology(_settings.TerrainStyle, continent, region, ridge, fault, basin, featureA);
                GeologicalProfile profile = GeologicalProfiles.For(geology);
                double hardness = Math.Max(.12, Math.Min(.98, profile.Hardness + featureB * .08));
                double h = FormHeight(_settings.TerrainStyle, continent, region, ridge, fault, plateauNoise, basin, detail, featureA, featureB, hardness);
                int i = map.Index(x, z);
                map.Height[i] = (float)Math.Max(4, h);
                map.Ridge[i] = (float)ridge;
                map.Continentalness[i] = (float)continent;
                map.Geology[i] = geology;
                map.RockHardness[i] = (float)hardness;
                map.Strata[i] = (float)strata;
            }
        });

        Erosion.ApplyThermal(map, _settings.ErosionIterations, _settings.ErosionStrength, _settings.TerrainStyle);
        float maximumGradient = _settings.TerrainStyle switch
        {
            TerrainStyle.Alpine => 1.12f,
            TerrainStyle.Canyonlands => .96f,
            TerrainStyle.RollingPlains => .46f,
            TerrainStyle.Glacial => .82f,
            _ => .78f
        };
        float maximumApex = _settings.TerrainStyle is TerrainStyle.Alpine or TerrainStyle.Volcanic ? .72f : .54f;
        Erosion.RelaxSlopes(map, maximumGradient, 30);
        Erosion.SuppressIsolatedApexes(map, maximumApex, 3);
        Hydrology.Apply(map, _settings);
        CalculateSlopes(map);
        ClassifyClimate(map);
        return map;
    }

    private double FormHeight(TerrainStyle style, double continent, double region, double ridge, double fault, double plateauNoise, double basin, double detail, double featureA, double featureB, double hardness)
    {
        double mountainHeight = _settings.MountainHeight * _settings.ReliefStrength;
        double baseLand = _settings.BaseElevation + continent * 58 + region * 24;
        double faultBand = Math.Pow(1 - Smooth(.04, .62, Math.Abs(fault)), 2);
        double fine = detail * (5 + ridge * 13) * (.72 + hardness * .28);
        return style switch
        {
            TerrainStyle.Alpine => baseLand + Math.Pow(ridge, 1.42) * mountainHeight * 1.02 - faultBand * mountainHeight * .16 - basin * 22 + fine,
            TerrainStyle.Canyonlands => baseLand + GeologicalShapes.Mesa(Smooth(-.52, .72, plateauNoise), Smooth(-.8, .8, featureB)) * mountainHeight * .48 - faultBand * mountainHeight * .20 - basin * 28 + fine * .55,
            TerrainStyle.Karst => baseLand + Math.Pow(ridge, 1.18) * mountainHeight * .52 - Math.Pow(Smooth(.42, .86, featureA), 2) * mountainHeight * .12 - basin * 42 + fine * .72,
            TerrainStyle.RollingPlains => baseLand * .72 + _settings.BaseElevation * .28 + region * 24 + Math.Pow(ridge, 1.8) * mountainHeight * .14 - basin * 18 + fine * .42,
            TerrainStyle.Glacial => baseLand + Math.Pow(ridge, 1.34) * mountainHeight * .84 - faultBand * mountainHeight * .25 - basin * 34 + fine * .58,
            TerrainStyle.Volcanic => VolcanicHeight(baseLand, mountainHeight, ridge, plateauNoise, basin, detail, featureA, featureB),
            _ => baseLand + (ridge * .30 + Math.Pow(ridge, 1.55) * .42) * mountainHeight * _preset.MountainScale - basin * 38 * _preset.ValleyStrength + fine
        };
    }

    private static double VolcanicHeight(double baseLand, double mountainHeight, double ridge, double plateauNoise, double basin, double detail, double featureA, double featureB)
    {
        double broad = Smooth(.26, .83, featureA * .72 + featureB * .28);
        double cone = Math.Pow(broad, 1.55);
        double caldera = Math.Pow(Smooth(.86, .98, broad), 2);
        double lavaPlateau = Smooth(-.42, .52, plateauNoise) * mountainHeight * .13;
        return baseLand + ridge * mountainHeight * .22 + cone * mountainHeight * .62 - caldera * mountainHeight * .16 + lavaPlateau - basin * 22 + detail * (5 + cone * 12);
    }

    private static GeologicalIdentity SelectGeology(TerrainStyle style, double continent, double region, double ridge, double fault, double basin, double feature)
    {
        return style switch
        {
            TerrainStyle.Alpine => basin > .58 ? GeologicalIdentity.AlluvialBasin : ridge > .62 ? GeologicalIdentity.GlacialRange : GeologicalIdentity.ErodedMassif,
            TerrainStyle.Canyonlands => basin > .68 || Math.Abs(fault) < .08 ? GeologicalIdentity.AlluvialBasin : GeologicalIdentity.BadlandsMesa,
            TerrainStyle.Karst => basin > .60 ? GeologicalIdentity.AlluvialBasin : ridge > .68 ? GeologicalIdentity.ErodedMassif : GeologicalIdentity.KarstHighland,
            TerrainStyle.RollingPlains => ridge > .58 ? GeologicalIdentity.ErodedMassif : GeologicalIdentity.AlluvialBasin,
            TerrainStyle.Glacial => basin > .62 ? GeologicalIdentity.AlluvialBasin : ridge > .58 ? GeologicalIdentity.GlacialRange : GeologicalIdentity.ErodedMassif,
            TerrainStyle.Volcanic => basin > .64 ? GeologicalIdentity.AlluvialBasin : ridge < .22 && feature < -.12 ? GeologicalIdentity.ErodedMassif : GeologicalIdentity.VolcanicPlateau,
            _ => basin > .76 ? GeologicalIdentity.AlluvialBasin
                : ridge > .72 ? GeologicalIdentity.ErodedMassif
                : feature > .46 ? GeologicalIdentity.KarstHighland
                : region < -.35 ? GeologicalIdentity.BadlandsMesa
                : Math.Abs(fault) < .07 && continent > 0 ? GeologicalIdentity.VolcanicPlateau
                : GeologicalIdentity.ErodedMassif
        };
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
        int n = map.Size;
        var rainShadow = new float[map.Height.Length];
        bool alongX = (_seed & 1UL) == 0;
        bool reverse = (_seed & 2UL) != 0;
        for (int line = 0; line < n; line++)
        {
            float airborne = .62f;
            for (int step = 0; step < n; step++)
            {
                int position = reverse ? n - 1 - step : step;
                int x = alongX ? position : line;
                int z = alongX ? line : position;
                int previousPosition = reverse ? Math.Min(n - 1, position + 1) : Math.Max(0, position - 1);
                int px = alongX ? previousPosition : line;
                int pz = alongX ? line : previousPosition;
                int i = map.Index(x, z);
                float rise = step == 0 ? 0 : Math.Max(0, map.Height[i] - map.Height[map.Index(px, pz)]);
                float lift = Math.Min(.22f, rise / 420f);
                airborne = Math.Max(.08f, Math.Min(.96f, airborne + .010f * map.CellSize / 8f - lift));
                rainShadow[i] = Math.Min(1, airborne + lift * .65f);
            }
        }

        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            double px = x * map.CellSize, pz = z * map.CellSize;
            float altitude = map.Height[i], slope = map.Slope[i];
            float latitude = Math.Abs(z / (float)(n - 1) * 2 - 1);
            float temperature = 29 - latitude * 22 - altitude * .016f + (float)_temperature.Sample(px, pz) * 5;
            float broadMoisture = .45f + (float)_moisture.Sample(px, pz) * .78f;
            float moisture = Clamp01(broadMoisture * .72f + rainShadow[i] * .28f + (map.WaterDepth[i] > 0 ? .22f : 0));
            map.Temperature[i] = temperature;
            map.Moisture[i] = moisture;
            if (map.WaterDepth[i] > 0) map.Biomes[i] = map.RiverChannel[i] ? Biome.River : Biome.Lake;
            else if (temperature < -6 || altitude > _settings.BaseElevation + _settings.MountainHeight * .78f) map.Biomes[i] = Biome.Snow;
            else if (slope > .82f && map.Geology[i] != GeologicalIdentity.AlluvialBasin) map.Biomes[i] = Biome.Rock;
            else if (map.Geology[i] == GeologicalIdentity.VolcanicPlateau && ((map.Ridge[i] > .72f && map.Strata[i] > .68f) || map.Strata[i] < .24f || slope > .58f)) map.Biomes[i] = Biome.Rock;
            else if (map.Geology[i] == GeologicalIdentity.KarstHighland && map.Ridge[i] > .64f && slope > .22f) map.Biomes[i] = Biome.Rock;
            else if (map.Geology[i] == GeologicalIdentity.BadlandsMesa && moisture < .56f) map.Biomes[i] = Biome.Desert;
            else if (temperature < 3) map.Biomes[i] = moisture > .46f ? Biome.Taiga : Biome.Tundra;
            else if (temperature > 24) map.Biomes[i] = moisture < .30f ? Biome.Desert : moisture > .76f ? Biome.Jungle : Biome.Savanna;
            else if (moisture > .80f && slope < .10f) map.Biomes[i] = Biome.Swamp;
            else if (moisture > .64f) map.Biomes[i] = Biome.Forest;
            else map.Biomes[i] = Biome.Grassland;
        }
        MarkBeaches(map);
    }

    private static void MarkBeaches(SurfaceMap map)
    {
        int n = map.Size;
        for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
        {
            int i = map.Index(x, z);
            if (map.WaterDepth[i] > 0 || map.Slope[i] > .16f || map.Biomes[i] is Biome.Rock or Biome.Snow) continue;
            bool nearWater = false;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) nearWater |= map.WaterDepth[map.Index(x + dx, z + dz)] > 0;
            if (nearWater) map.Biomes[i] = Biome.Beach;
        }
    }

    private static float Clamp01(float x) => Math.Max(0, Math.Min(1, x));
    private static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
}
