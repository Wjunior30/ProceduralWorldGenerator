namespace ProceduralWorld.Core;

public sealed partial class TerrainGenerator
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
        if (IsIsland) PrepareIsland(map);
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
                h += SignatureRelief(_settings.TerrainStyle, px, pz, (n - 1) * map.CellSize);
                if (IsIsland) h = IslandHeight(h, px, pz, (n - 1) * map.CellSize, featureA, featureB);
                int i = map.Index(x, z);
                map.Height[i] = (float)Math.Max(4, h);
                map.Ridge[i] = (float)ridge;
                map.Continentalness[i] = (float)continent;
                map.Geology[i] = geology;
                map.RockHardness[i] = (float)hardness;
                map.Strata[i] = (float)strata;
            }
        });
        if (IsIsland) CarveIslandRiver(map);

        double massBeforeErosion = map.Height.Sum(x => (double)x);
        Erosion.ApplyThermal(map, _settings.ErosionIterations, _settings.ErosionStrength, _settings.TerrainStyle);
        Erosion.ApplyStreamPower(map, 6, _settings.TerrainStyle switch
        {
            TerrainStyle.Canyonlands => .030f,
            TerrainStyle.RollingPlains or TerrainStyle.Karst => .012f,
            TerrainStyle.Glacial => .016f,
            _ => .022f
        });
        float maximumGradient = _settings.TerrainStyle switch
        {
            TerrainStyle.Alpine => .96f,
            TerrainStyle.Canyonlands => .96f,
            TerrainStyle.RollingPlains => .46f,
            TerrainStyle.Glacial => .82f,
            _ => .78f
        };
        float maximumApex = _settings.TerrainStyle is TerrainStyle.Alpine or TerrainStyle.Volcanic ? .72f : .54f;
        Erosion.RelaxSlopes(map, maximumGradient, 140);
        Erosion.SuppressIsolatedApexes(map, maximumApex, 12);
        if (IsIsland) FlattenSites(map); // after erosion so the sites stay level
        Hydrology.Apply(map, _settings);
        map.ErosionMassDelta = map.Height.Sum(x => (double)x) - massBeforeErosion;
        CalculateSlopes(map);
        CalculateCurvature(map, maximumGradient);
        ClassifyLandforms(map);
        ClassifyClimate(map);
        return map;
    }

    private double SignatureRelief(TerrainStyle style, double px, double pz, double extent)
    {
        double nx = px / Math.Max(1, extent) - .5, nz = pz / Math.Max(1, extent) - .5;
        double phase = ((_seed >> 16) & 0xffff) / 65535d * Math.PI * 2;
        double center = Math.Sin(nx * 5.2 + phase) * .13;
        double mountain = _settings.MountainHeight * _settings.ReliefStrength;
        return style switch
        {
            TerrainStyle.Alpine => Math.Exp(-Math.Pow((nz - center) / .17, 2)) * mountain * (.29 + .08 * Math.Cos(nx * 8 + phase)),
            TerrainStyle.Glacial => (Math.Exp(-Math.Pow((nz - center - .20) / .15, 2)) + Math.Exp(-Math.Pow((nz - center + .20) / .15, 2))) * mountain * .17
                - Math.Exp(-Math.Pow((nz - center) / .15, 4)) * mountain * .10,
            TerrainStyle.Volcanic => VolcanicSignature(nx, nz, phase, mountain),
            TerrainStyle.Natural => Math.Exp(-Math.Pow((nz - center) / .18, 2)) * mountain * .09,
            _ => 0
        };
    }

    private static double VolcanicSignature(double nx, double nz, double phase, double mountain)
    {
        double cx = Math.Cos(phase) * .13, cz = Math.Sin(phase) * .13;
        double radiusSquared = (nx - cx) * (nx - cx) + (nz - cz) * (nz - cz);
        double cone = Math.Exp(-radiusSquared / .032) * mountain * .27;
        double caldera = Math.Exp(-radiusSquared / .0022) * mountain * .10;
        return cone - caldera;
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
            TerrainStyle.Canyonlands => baseLand + GeologicalShapes.Mesa(Smooth(-.52, .72, plateauNoise), Smooth(-.8, .8, featureB)) * mountainHeight * .50 - faultBand * mountainHeight * .23 - basin * 30 + fine * .55,
            TerrainStyle.Karst => baseLand + Math.Pow(ridge, 1.18) * mountainHeight * .52 - Math.Pow(Smooth(.42, .86, featureA), 2) * mountainHeight * .12 - basin * 42 + fine * .72,
            TerrainStyle.RollingPlains => baseLand * .72 + _settings.BaseElevation * .28 + region * 20 + Math.Pow(ridge, 1.8) * mountainHeight * .11 - basin * 14 + fine * .34,
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
            TerrainStyle.Alpine => basin > .68 ? GeologicalIdentity.AlluvialBasin : ridge > .36 || region > .18 ? GeologicalIdentity.GlacialRange : GeologicalIdentity.ErodedMassif,
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

    private static void CalculateCurvature(SurfaceMap map, float referenceGradient)
    {
        int n = map.Size;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            int x0 = Math.Max(0, x - 1), x1 = Math.Min(n - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(n - 1, z + 1);
            float laplacian = map.HeightAt(x0, z) + map.HeightAt(x1, z) + map.HeightAt(x, z0) + map.HeightAt(x, z1) - 4 * map.Height[i];
            map.Curvature[i] = laplacian / Math.Max(.001f, map.CellSize * map.CellSize);
            map.SlopeSaturation[i] = Math.Min(1, map.Slope[i] / Math.Max(.001f, referenceGradient));
        }
    }

    private void ClassifyLandforms(SurfaceMap map)
    {
        for (int i = 0; i < map.Height.Length; i++)
        {
            float slope = map.Slope[i], curve = map.Curvature[i], ridge = map.Ridge[i], strata = map.Strata[i];
            bool wet = map.RiverChannel[i] || map.WaterDepth[i] > 0;
            map.Landforms[i] = _settings.TerrainStyle switch
            {
                TerrainStyle.Alpine when curve > .006f && ridge > .28f => LandformKind.Cirque,
                TerrainStyle.Alpine when ridge > .42f || slope > .38f => LandformKind.Ridge,
                TerrainStyle.Canyonlands when strata > .62f && slope < .34f => LandformKind.Mesa,
                TerrainStyle.Canyonlands when slope > .52f => LandformKind.Escarpment,
                TerrainStyle.Karst when curve > .008f && slope < .32f => LandformKind.Sinkhole,
                TerrainStyle.Karst when !wet && curve > .003f => LandformKind.DryValley,
                TerrainStyle.RollingPlains when wet && slope < .22f => LandformKind.Floodplain,
                TerrainStyle.RollingPlains when slope < .10f => LandformKind.Plain,
                TerrainStyle.RollingPlains when curve < -.003f && slope < .32f => LandformKind.Terrace,
                TerrainStyle.Glacial when curve < -.003f && (ridge > .28f || slope < .18f) => LandformKind.Moraine,
                TerrainStyle.Glacial when curve > .005f => LandformKind.Cirque,
                TerrainStyle.Glacial when ridge > .40f => LandformKind.Ridge,
                TerrainStyle.Volcanic when ridge > .48f && strata > .42f => LandformKind.VolcanicCone,
                TerrainStyle.Volcanic when curve > .006f && ridge > .30f => LandformKind.Caldera,
                TerrainStyle.Volcanic when strata > .42f => LandformKind.LavaField,
                _ when wet && slope < .18f => LandformKind.Floodplain,
                _ when ridge > .64f => LandformKind.Ridge,
                _ when slope > .48f => LandformKind.Massif,
                _ when curve > .008f && slope < .24f => LandformKind.Basin,
                _ when curve > .0025f => LandformKind.Valley,
                _ when slope < .11f => LandformKind.Plain,
                _ => LandformKind.Hill
            };
        }
    }

    /// <summary>Chamfer distance in cells to the nearest water cell (two-pass 3-4 approximation).</summary>
    private static float[] WaterDistance(SurfaceMap map)
    {
        int n = map.Size;
        var d = new float[n * n];
        for (int i = 0; i < d.Length; i++) d[i] = map.WaterDepth[i] > 0 ? 0 : 1e6f;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) Relax(x, z, -1);
        for (int z = n - 1; z >= 0; z--) for (int x = n - 1; x >= 0; x--) Relax(x, z, 1);
        return d;
        void Relax(int x, int z, int s)
        {
            int i = map.Index(x, z);
            Take(x + s, z, 1); Take(x, z + s, 1); Take(x + s, z + s, 1.41421356f); Take(x - s, z + s, 1.41421356f);
            void Take(int nx, int nz, float cost) { if (nx >= 0 && nz >= 0 && nx < n && nz < n) d[i] = Math.Min(d[i], d[map.Index(nx, nz)] + cost); }
        }
    }

    private void ClassifyClimate(SurfaceMap map)
    {
        int n = map.Size;
        var rainShadow = new float[map.Height.Length];
        double windLength = Math.Sqrt(_settings.WindX * _settings.WindX + _settings.WindZ * _settings.WindZ);
        double windX = _settings.WindX / windLength, windZ = _settings.WindZ / windLength;
        bool alongX = Math.Abs(windX) >= Math.Abs(windZ);
        bool reverse = alongX ? windX < 0 : windZ < 0;
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
        float[] waterDistance = WaterDistance(map);

        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            double px = x * map.CellSize, pz = z * map.CellSize;
            float altitude = map.Height[i], slope = map.Slope[i];
            float latitude = Math.Abs(z / (float)(n - 1) * 2 - 1);
            float ecology = (float)_featureA.Sample(px, pz) + (float)Math.Sin((px + pz) / 260d + (_seed & 1023) * .013) * .18f;
            float temperature = 29 - latitude * 22 - altitude * .016f + (float)_temperature.Sample(px, pz) * 5;
            float broadMoisture = .45f + (float)_moisture.Sample(px, pz) * .78f;
            float riparian = .24f * (float)Math.Exp(-waterDistance[i] / 3.5f); // wetter corridors along rivers and lakes
            float moisture = Clamp01(broadMoisture * .72f + rainShadow[i] * .28f + riparian);
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
            else if (moisture > .78f && slope < .10f) map.Biomes[i] = Biome.Swamp;
            else if (moisture > .53f + ecology * .16f) map.Biomes[i] = Biome.Forest;
            else if (moisture < .43f + ecology * .12f && slope < .34f) map.Biomes[i] = Biome.Savanna;
            else map.Biomes[i] = Biome.Grassland;
        }
        MarkBeaches(map);
        if (IsIsland) { ApplyIslandBiomes(map); BuildRoads(map); ComputeVegetation(map); return; }
        ApplyBiomeQuotas(map);
        ApplyBiomeSelection(map);
        ComputeVegetation(map);
    }

    private void ApplyBiomeSelection(SurfaceMap map)
    {
        var enabled = _settings.EnabledBiomes.ToHashSet();
        for (int i = 0; i < map.Biomes.Length; i++)
        {
            if (enabled.Contains(map.Biomes[i])) continue;
            bool wet = map.WaterDepth[i] > 0;
            Biome[] compatible = enabled.Where(x => wet ? x is Biome.River or Biome.Lake : x is not Biome.River and not Biome.Lake).ToArray();
            if (compatible.Length == 0) compatible = enabled.ToArray();
            map.Biomes[i] = compatible.OrderBy(x => BiomeCost(map, i, x)).ThenBy(x => x).First();
        }

        bool[] reserved = new bool[map.Biomes.Length];
        foreach (Biome biome in enabled.Where(x => x is not Biome.River and not Biome.Lake).OrderBy(x => x))
        {
            if (map.Biomes.Contains(biome)) continue;
            int[] candidates = Enumerable.Range(0, map.Biomes.Length)
                .Where(i => map.WaterDepth[i] <= 0 && !reserved[i])
                .OrderBy(i => BiomeCost(map, i, biome))
                .ThenBy(i => i)
                .Take(1)
                .ToArray();
            if (candidates.Length == 0) continue;
            int center = candidates[0];
            int cx = center % map.Size, cz = center / map.Size;
            int radius = Math.Max(1, map.Size / 64);
            for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= map.Size || z >= map.Size || dx * dx + dz * dz > radius * radius) continue;
                int i = map.Index(x, z);
                if (map.WaterDepth[i] > 0 || reserved[i]) continue;
                map.Biomes[i] = biome;
                reserved[i] = true;
            }
        }
    }

    private static float BiomeCost(SurfaceMap map, int i, Biome biome)
    {
        float temperature = map.Temperature[i], moisture = map.Moisture[i], slope = map.Slope[i];
        return biome switch
        {
            Biome.River => map.RiverChannel[i] ? 0 : 10,
            Biome.Lake => map.WaterDepth[i] > 0 && !map.RiverChannel[i] ? 0 : 10,
            Biome.Beach => (NearWater(map, i) ? 0 : 2.5f) + slope * 3,
            Biome.Desert => moisture * 2.2f + Math.Abs(temperature - 30) * .025f + slope * .25f,
            Biome.Savanna => Math.Abs(moisture - .36f) * 2 + Math.Abs(temperature - 24) * .025f + slope * .2f,
            Biome.Grassland => Math.Abs(moisture - .48f) * 1.6f + Math.Abs(temperature - 16) * .018f + slope * .35f,
            Biome.Forest => Math.Abs(moisture - .66f) * 1.8f + Math.Abs(temperature - 15) * .02f + slope * .18f,
            Biome.Jungle => Math.Abs(moisture - .88f) * 2.2f + Math.Abs(temperature - 28) * .035f + slope * .3f,
            Biome.Swamp => Math.Abs(moisture - .86f) * 2.1f + Math.Abs(temperature - 19) * .02f + slope * 3.2f,
            Biome.Taiga => Math.Abs(moisture - .58f) * 1.8f + Math.Abs(temperature - 1) * .04f + slope * .2f,
            Biome.Tundra => Math.Abs(moisture - .34f) * 1.5f + Math.Abs(temperature + 2) * .045f + slope * .25f,
            Biome.Rock => Math.Abs(slope - .85f) * 1.5f - map.Ridge[i] * .3f,
            Biome.Snow => Math.Abs(temperature + 9) * .055f - map.Height[i] * .0008f,
            _ => 10
        };
    }

    private static bool NearWater(SurfaceMap map, int i)
    {
        int x = i % map.Size, z = i / map.Size;
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, nz = z + dz;
            if (nx >= 0 && nz >= 0 && nx < map.Size && nz < map.Size && map.WaterDepth[map.Index(nx, nz)] > 0) return true;
        }
        return false;
    }

    // Land biome shares: Forest 30%, the other nine split the rest evenly (~7.7% each).
    // Beach, River and Lake keep following the terrain and are excluded from the quotas.
    private static readonly Biome[] QuotaBiomes = { Biome.Desert, Biome.Savanna, Biome.Grassland, Biome.Forest, Biome.Jungle, Biome.Swamp, Biome.Taiga, Biome.Tundra, Biome.Rock, Biome.Snow };
    private const double ForestShare = .30;

    /// <summary>
    /// Assigns land biomes to hit fixed area shares while respecting climate: each cell takes the biome with the
    /// lowest suitability cost minus a per-biome offset, and the offsets are tuned until every biome meets its share.
    /// A majority filter then removes speckle so regions stay contiguous.
    /// </summary>
    private static void ApplyBiomeQuotas(SurfaceMap map)
    {
        int[] cells = Enumerable.Range(0, map.Biomes.Length).Where(i => map.WaterDepth[i] <= 0 && map.Biomes[i] != Biome.Beach).ToArray();
        int m = cells.Length, k = QuotaBiomes.Length;
        if (m == 0) return;
        var cost = new float[m * k];
        for (int c = 0; c < m; c++) for (int b = 0; b < k; b++) cost[c * k + b] = BiomeCost(map, cells[c], QuotaBiomes[b]);
        var target = QuotaBiomes.Select(b => (b == Biome.Forest ? ForestShare : (1 - ForestShare) / (k - 1)) * m).ToArray();
        var offset = new double[k]; var count = new int[k]; var choice = new int[m];
        for (int iteration = 0; iteration < 240; iteration++)
        {
            Array.Clear(count, 0, k);
            for (int c = 0; c < m; c++)
            {
                int best = 0; double bestValue = double.MaxValue;
                for (int b = 0; b < k; b++) { double v = cost[c * k + b] - offset[b]; if (v < bestValue) { bestValue = v; best = b; } }
                choice[c] = best; count[best]++;
            }
            double step = .6 * Math.Pow(.975, iteration) + .004;
            for (int b = 0; b < k; b++) offset[b] += step * (target[b] - count[b]) / m * k;
        }
        for (int c = 0; c < m; c++) map.Biomes[cells[c]] = QuotaBiomes[choice[c]];

        int n = map.Size; var votes = new int[k];
        for (int pass = 0; pass < 4; pass++)
        {
            var next = (Biome[])map.Biomes.Clone();
            foreach (int i in cells)
            {
                Array.Clear(votes, 0, k);
                int x = i % n, z = i / n;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int b = Array.IndexOf(QuotaBiomes, map.Biomes[map.Index(nx, nz)]);
                    if (b >= 0) votes[b]++;
                }
                int majority = 0; for (int b = 1; b < k; b++) if (votes[b] > votes[majority]) majority = b;
                if (votes[majority] >= 5) next[i] = QuotaBiomes[majority];
            }
            Array.Copy(next, map.Biomes, next.Length);
        }
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
