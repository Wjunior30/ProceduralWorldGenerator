namespace ProceduralWorld.Core;

// Island style: a sea-bound island with a snowy central peak and one principal river that runs from the peak,
// through forest, cuts a terraced canyon, spreads into a swamp delta and reaches the sea across the beach.
// Regions are laid out along the river bearing so the river always crosses every biome.
public sealed partial class TerrainGenerator
{
    private const int RiverPoints = 96;
    private const int TributaryPoints = 24;
    private readonly List<((double X, double Z)[] Course, double JoinS)> _tributaries = new();
    private double CoastRadius => _settings.CoastRadius;
    private double RiverStart => _settings.RiverStart;
    private double RiverLength => _settings.RiverLength;
    private (double X, double Z)[] _islandRiver = Array.Empty<(double, double)>();
    private double _riverBearing;
    private bool[] _riverBed = Array.Empty<bool>();

    private bool IsIsland => _settings.TerrainStyle == TerrainStyle.Island;

    private void PrepareIsland(SurfaceMap map)
    {
        double extent = (map.Size - 1) * map.CellSize;
        _riverBearing = (_seed % 3600) / 3600.0 * Math.PI * 2;
        double dx = Math.Cos(_riverBearing), dz = Math.Sin(_riverBearing), wiggle = (_seed >> 20) % 628 / 100.0;
        _islandRiver = new (double, double)[RiverPoints];
        for (int k = 0; k < RiverPoints; k++)
        {
            double s = k / (RiverPoints - 1.0), radius = (RiverStart + RiverLength * s) * extent;
            double lateral = (Math.Sin(s * Math.PI * 4.5 + wiggle) * .055 + Math.Sin(s * Math.PI * 9 + wiggle * 2) * .015) * Math.Min(1, s * 4) * extent;
            _islandRiver[k] = (extent * .5 + dx * radius - dz * lateral, extent * .5 + dz * radius + dx * lateral);
        }
        // Short side rivers: each rises inland on alternating banks and meets the main river at an angle, downstream.
        _tributaries.Clear();
        int count = _settings.Tributaries; double coastS = (CoastRadius - RiverStart) / RiverLength;
        for (int t = 0; t < count; t++)
        {
            double joinS = Math.Min(coastS - .08, .18 + (coastS - .3) * (t + .5) / count);
            int j = Math.Max(1, Math.Min(RiverPoints - 2, (int)Math.Round(joinS * (RiverPoints - 1))));
            var join = _islandRiver[j]; var before = _islandRiver[j - 1]; var after = _islandRiver[j + 1];
            double fx = after.X - before.X, fz = after.Z - before.Z, fl = Math.Sqrt(fx * fx + fz * fz); fx /= fl; fz /= fl;
            double side = t % 2 == 0 ? 1 : -1, length = (.07 + (_seed >> (8 + t * 3)) % 50 / 1000.0) * extent;
            double hx = join.X + (-fz * side * .85 - fx * .5) * length, hz = join.Z + (fx * side * .85 - fz * .5) * length;
            double cx = hx / extent - .5, cz = hz / extent - .5, d = Math.Sqrt(cx * cx + cz * cz), limit = CoastRadius - .07;
            if (d > limit) { hx = (.5 + cx * limit / d) * extent; hz = (.5 + cz * limit / d) * extent; }
            var course = new (double X, double Z)[TributaryPoints]; double bend = ((_seed >> (5 + t)) % 100 / 100.0 - .5) * .25 * length;
            for (int k = 0; k < TributaryPoints; k++)
            {
                double s = k / (TributaryPoints - 1.0), curve = Math.Sin(s * Math.PI) * bend;
                course[k] = (hx + (join.X - hx) * s - fz * curve, hz + (join.Z - hz) * s + fx * curve);
            }
            _tributaries.Add((course, joinS));
        }
    }

    private double IslandHeight(double h, double px, double pz, double extent, double featureA, double featureB)
    {
        double nx = px / extent - .5, nz = pz / extent - .5, distance = IslandDistance(nx, nz, featureA), sea = _settings.SeaLevel;
        double land = Smooth(CoastRadius + .03, CoastRadius - .03, distance);
        // Peak with ridges from the natural relief; elevation fades toward the coast to leave lowlands and beaches.
        double peak = _settings.MountainHeight * _settings.ReliefStrength * .40 * Math.Exp(-Math.Pow(distance / .15, 2)) * (.75 + .25 * featureB);
        double inland = Smooth(CoastRadius, CoastRadius - .14, distance);
        // Hills only near the mountain; the outer island becomes broad lowland plains for settlements.
        double hills = Math.Max(0, h - _settings.BaseElevation * .6) * (.16 + .30 * Smooth(.30, .10, distance));
        double landHeight = sea + 2 + (hills + peak) * inland;
        double delta = DeltaMask(nx, nz, distance);
        landHeight += (sea + 5.5 + featureB * 1.0 - landHeight) * delta;
        double canyon = CanyonMask(nx, nz, distance);
        if (canyon > 0)
        {
            // Raised plateau cut into strata terraces (mesas); the river later incises the gorge.
            double plateau = landHeight + 55 * canyon, t = (plateau - sea) / 16;
            double terraced = sea + (Math.Floor(t) + Smooth(.72, 1, t - Math.Floor(t))) * 16;
            landHeight = plateau + (terraced - plateau) * canyon;
        }
        double seabed = sea - 22 + featureB * 6;
        return seabed + (landHeight - seabed) * land;
    }

    // Coastline: noise plus a few seeded lobes so the island has capes and bays instead of a circle.
    private double IslandDistance(double nx, double nz, double featureA)
    {
        double angle = Math.Atan2(nz, nx), phase = (_seed >> 8) % 628 / 100.0;
        return Math.Sqrt(nx * nx + nz * nz) * (1 + featureA * .22 + Math.Sin(angle * 3 + phase) * .10 + Math.Sin(angle * 5 + phase * 2) * .06);
    }

    // Low, flat swamp where the river reaches the coast.
    private double DeltaMask(double nx, double nz, double distance)
        => Smooth(1.05, .55, BearingOffset(nx, nz)) * Smooth(.27, .33, distance);

    private double BearingOffset(double nx, double nz)
    {
        double d = Math.Abs(Math.Atan2(nz, nx) - _riverBearing) % (Math.PI * 2);
        return d > Math.PI ? Math.PI * 2 - d : d;
    }

    private double CanyonMask(double nx, double nz, double distance)
        => Smooth(.95, .45, BearingOffset(nx, nz)) * Smooth(.12, .18, distance) * Smooth(.34, .28, distance);

    /// <summary>Carves a continuously descending valley along the river course; inside the canyon it is a narrow, steep gorge.</summary>
    private void CarveIslandRiver(SurfaceMap map)
    {
        int n = map.Size; double extent = (n - 1) * map.CellSize, sea = _settings.SeaLevel;
        double coastS = (CoastRadius - RiverStart) / RiverLength;
        int sx = Math.Max(0, Math.Min(n - 1, (int)Math.Round(_islandRiver[2].X / map.CellSize)));
        int sz = Math.Max(0, Math.Min(n - 1, (int)Math.Round(_islandRiver[2].Z / map.CellSize)));
        map.RiverSource = map.Index(sx, sz);
        float[] original = (float[])map.Height.Clone();
        // Valley floor along the course: never above the ground it crosses, always descending, deep inside the canyon.
        var floors = new double[RiverPoints];
        for (int k = 0; k < RiverPoints; k++)
        {
            double s = k / (RiverPoints - 1.0); var p = _islandRiver[k];
            int cx = Math.Max(0, Math.Min(n - 1, (int)Math.Round(p.X / map.CellSize))), cz = Math.Max(0, Math.Min(n - 1, (int)Math.Round(p.Z / map.CellSize)));
            double depth = 4 + 50 * CanyonAt(p, extent);
            double floor = Math.Min(original[map.Index(cx, cz)] - depth, k > 0 ? floors[k - 1] - .05 : double.MaxValue);
            floors[k] = s < coastS ? Math.Max(floor, sea + .4) : sea - 3;
        }
        for (int k = 1; k < RiverPoints; k++) floors[k] = Math.Min(floors[k], floors[k - 1]);
        // Each side river's floor: below the ground all along its course and always falling, down to just above the main river.
        var tributaryFloors = _tributaries.Select(t =>
        {
            double mouth = floors[Math.Min(RiverPoints - 1, (int)Math.Round(t.JoinS * (RiverPoints - 1)))] + .6, level = double.MaxValue;
            var f = new double[TributaryPoints];
            for (int k = 0; k < TributaryPoints; k++)
            {
                var p = t.Course[k]; int px = Math.Max(0, Math.Min(n - 1, (int)Math.Round(p.X / map.CellSize))), pz = Math.Max(0, Math.Min(n - 1, (int)Math.Round(p.Z / map.CellSize)));
                level = Math.Min(level - .05, original[map.Index(px, pz)] - 3);
                f[k] = Math.Max(mouth, level);
            }
            return f;
        }).ToArray();
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                double px = x * map.CellSize, pz = z * map.CellSize, best = double.MaxValue, bestS = 0;
                for (int k = 0; k < RiverPoints - 1; k++)
                {
                    var a = _islandRiver[k]; var b = _islandRiver[k + 1];
                    double vx = b.X - a.X, vz = b.Z - a.Z, t = Math.Max(0, Math.Min(1, ((px - a.X) * vx + (pz - a.Z) * vz) / (vx * vx + vz * vz)));
                    double ex = a.X + vx * t - px, ez = a.Z + vz * t - pz, d = ex * ex + ez * ez;
                    if (d < best) { best = d; bestS = (k + t) / (RiverPoints - 1.0); }
                }
                double distance = Math.Sqrt(best), s = bestS;
                int k0 = Math.Min(RiverPoints - 2, (int)(s * (RiverPoints - 1)));
                double floor = floors[k0] + (floors[k0 + 1] - floors[k0]) * (s * (RiverPoints - 1) - k0);
                bool gorge = CanyonAt(_islandRiver[k0], extent) > .3;
                double halfWidth = (1.6 + 2.4 * s) * map.CellSize, wall = (gorge ? 2.2 : 7) * map.CellSize;
                double w = Math.Max(0, Math.Min(1, (distance - halfWidth) / wall));
                double profile = gorge ? Math.Sqrt(w) : w * w * (3 - 2 * w);
                int i = map.Index(x, z); double h = original[i];
                if (h > floor) map.Height[i] = (float)(floor + (h - floor) * profile);
                // Side rivers: narrow valleys whose floor falls from the head to just above the main river where they join.
                for (int r = 0; r < _tributaries.Count; r++)
                {
                    var course = _tributaries[r].Course;
                    double tBest = double.MaxValue, tS = 0;
                    for (int k = 0; k < TributaryPoints - 1; k++)
                    {
                        var a = course[k]; var b = course[k + 1];
                        double vx = b.X - a.X, vz = b.Z - a.Z, t = Math.Max(0, Math.Min(1, ((px - a.X) * vx + (pz - a.Z) * vz) / (vx * vx + vz * vz)));
                        double ex = a.X + vx * t - px, ez = a.Z + vz * t - pz, d = ex * ex + ez * ez;
                        if (d < tBest) { tBest = d; tS = (k + t) / (TributaryPoints - 1.0); }
                    }
                    double tw = Math.Max(0, Math.Min(1, (Math.Sqrt(tBest) - (.8 + .6 * tS) * map.CellSize) / (4 * map.CellSize)));
                    if (tw >= 1) continue;
                    var f = tributaryFloors[r]; double at = tS * (TributaryPoints - 1); int q0 = Math.Min(TributaryPoints - 2, (int)at);
                    double tFloor = f[q0] + (f[q0 + 1] - f[q0]) * (at - q0);
                    double current = map.Height[i];
                    if (current > tFloor) map.Height[i] = (float)Math.Min(current, tFloor + (current - tFloor) * tw * tw * (3 - 2 * tw));
                }
            }
        });
        if (_tributaries.Count > 0)
        {
            map.TributarySources.Clear();
            foreach (var (course, _) in _tributaries)
            {
                int hx = Math.Max(1, Math.Min(n - 2, (int)Math.Round(course[1].X / map.CellSize))), hz = Math.Max(1, Math.Min(n - 2, (int)Math.Round(course[1].Z / map.CellSize)));
                map.TributarySources.Add(map.Index(hx, hz));
            }
        }
        _riverBed = new bool[map.Height.Length];
        for (int i = 0; i < original.Length; i++) _riverBed[i] = map.Height[i] < original[i] - .5f;
    }

    private double CanyonAt((double X, double Z) p, double extent)
    {
        double nx = p.X / extent - .5, nz = p.Z / extent - .5;
        return CanyonMask(nx, nz, Math.Sqrt(nx * nx + nz * nz));
    }

    /// <summary>Five land biomes placed by region: Snow (peak), Beach (coast), Desert (canyon), Swamp (delta), Forest (the rest).</summary>
    private void ApplyIslandBiomes(SurfaceMap map)
    {
        int n = map.Size; double extent = (n - 1) * map.CellSize; float sea = _settings.SeaLevel;
        var oceanDistance = Distance(map, i => map.Ocean[i]);
        var lowerRiver = Distance(map, i => map.MainRiverChannel[i] && map.Height[i] < sea + 14);
        float maximum = sea;
        for (int i = 0; i < map.Height.Length; i++) if (!map.Ocean[i]) maximum = Math.Max(maximum, map.Height[i]);
        float snowLine = sea + .80f * (maximum - sea);
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            if (map.Ocean[i]) { map.Biomes[i] = Biome.Lake; continue; }
            if (map.WaterDepth[i] > 0) { map.Biomes[i] = map.RiverChannel[i] ? Biome.River : Biome.Lake; continue; }
            double px = x * map.CellSize, pz = z * map.CellSize, nx = px / extent - .5, nz = pz / extent - .5;
            double distance = IslandDistance(nx, nz, _featureA.Sample(px, pz));
            float h = map.Height[i];
            map.Biomes[i] = h > snowLine ? Biome.Snow
                : oceanDistance[i] <= 2.5f && h < sea + 6 ? Biome.Beach
                : CanyonMask(nx, nz, distance) > .35 ? Biome.Desert
                : (DeltaMask(nx, nz, distance) > .3 || lowerRiver[i] <= 7) && h < sea + 16 ? Biome.Swamp
                : Biome.Forest;
        }
    }

    private static float[] Distance(SurfaceMap map, Func<int, bool> source)
    {
        int n = map.Size; var d = new float[n * n];
        for (int i = 0; i < d.Length; i++) d[i] = source(i) ? 0 : 1e6f;
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
}
