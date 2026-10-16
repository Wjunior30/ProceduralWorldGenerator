namespace ProceduralWorld.Core;

// Island style: a sea-bound island with a snowy central peak and one principal river that runs from the peak,
// through forest, cuts a terraced canyon, spreads into a swamp delta and reaches the sea across the beach.
// Regions are laid out along the river bearing so the river always crosses every biome.
public sealed partial class TerrainGenerator
{
    private const int RiverPoints = 96;
    private const int TributaryPoints = 24;
    private readonly List<((double X, double Z)[] Course, double JoinS, double Bend)> _tributaries = new();
    private double CoastRadius => _settings.CoastRadius;
    private double RiverStart => _settings.RiverStart;
    private double RiverLength => _settings.RiverLength;
    private (double X, double Z)[] _islandRiver = Array.Empty<(double, double)>();
    private double _riverBearing;
    private bool[] _riverBed = Array.Empty<bool>();
    // Valley-floor position of each cell along its course (-1 = none) when side rivers are carved: main course points
    // 0..RiverPoints-1, then each side river's points, as fractional indices.
    private float[] _valleyAt = Array.Empty<float>();

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
            double bend = ((_seed >> (5 + t)) % 100 / 100.0 - .5) * .25;
            _tributaries.Add((TributaryCourse((hx, hz), j, bend), joinS, bend));
        }
    }

    // A side river from its head to main-course point j, bowed sideways by bend (share of its length).
    private (double X, double Z)[] TributaryCourse((double X, double Z) head, int j, double bend)
    {
        var join = _islandRiver[j]; var before = _islandRiver[j - 1]; var after = _islandRiver[j + 1];
        double fx = after.X - before.X, fz = after.Z - before.Z, fl = Math.Sqrt(fx * fx + fz * fz); fx /= fl; fz /= fl;
        double length = Math.Sqrt((join.X - head.X) * (join.X - head.X) + (join.Z - head.Z) * (join.Z - head.Z));
        var course = new (double X, double Z)[TributaryPoints];
        for (int k = 0; k < TributaryPoints; k++)
        {
            double s = k / (TributaryPoints - 1.0), curve = Math.Sin(s * Math.PI) * bend * length;
            course[k] = (head.X + (join.X - head.X) * s - fz * curve, head.Z + (join.Z - head.Z) * s + fx * curve);
        }
        return course;
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
        if (_tributaries.Count > 0) { _valleyAt = new float[original.Length]; Array.Fill(_valleyAt, -1); }
        // Lowest original ground within a radius (cells) of a course point.
        double Ground((double X, double Z) p, int radius)
        {
            int cx = (int)Math.Round(p.X / map.CellSize), cz = (int)Math.Round(p.Z / map.CellSize); double low = double.MaxValue;
            for (int z = Math.Max(0, cz - radius); z <= Math.Min(n - 1, cz + radius); z++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(n - 1, cx + radius); x++)
                    if ((x - cx) * (x - cx) + (z - cz) * (z - cz) <= radius * radius) low = Math.Min(low, original[map.Index(x, z)]);
            return low < double.MaxValue ? low : original[map.Index(Math.Max(0, Math.Min(n - 1, cx)), Math.Max(0, Math.Min(n - 1, cz)))];
        }
        // With side rivers the floors also stay below the ground on both banks (bed plus two cells), so a valley that
        // crosses a slope is a real trough and the drained main river and side rivers keep to it.
        bool trough = _tributaries.Count > 0;
        // Valley floor along the course: never above the ground it crosses, always descending, deep inside the canyon.
        var floors = new double[RiverPoints];
        for (int k = 0; k < RiverPoints; k++)
        {
            double s = k / (RiverPoints - 1.0); var p = _islandRiver[k];
            double depth = 4 + 50 * CanyonAt(p, extent);
            int reach = trough ? (int)Math.Ceiling(1.6 + 2.4 * s + 4) : 0;
            double floor = Math.Min(Ground(p, reach) - depth, k > 0 ? floors[k - 1] - .05 : double.MaxValue);
            floors[k] = s < coastS ? Math.Max(floor, sea + .4) : sea - 3;
        }
        for (int k = 1; k < RiverPoints; k++) floors[k] = Math.Min(floors[k], floors[k - 1]);
        // Side-valley bed plus two cells, plus half the gap between course points so the sampled ground covers the valley.
        int TributaryReach(int k) => (int)Math.Ceiling(.8 + .6 * k / (TributaryPoints - 1.0) + 2) + 2;
        // Re-plan each side river on the real ground: slide it (head and mouth) along the main valley, nearest first, and
        // shorten it if needed, until the whole course stands above the mouth, so it can only drain into the main river.
        int lastJoin = Math.Max(1, Math.Min(RiverPoints - 2, (int)((coastS - .05) * (RiverPoints - 1))));
        for (int r = 0; r < _tributaries.Count; r++)
        {
            var (course, joinS, bend) = _tributaries[r];
            int j0 = Math.Max(1, Math.Min(RiverPoints - 2, (int)Math.Round(joinS * (RiverPoints - 1))));
            var origin = _islandRiver[j0]; bool placed = false;
            // Nearest mouths first, downstream before upstream.
            var joins = Enumerable.Range(RiverPoints / 16, lastJoin - RiverPoints / 16 + 1).OrderBy(j => Math.Abs(j - j0) * 2 + (j < j0 ? 1 : 0)).ToArray();
            for (double scale = 1; scale > .3 && !placed; scale -= .15)
            {
                foreach (int j in joins)
                {
                    double mouth = floors[j] + .6;
                    var head = (_islandRiver[j].X + (course[0].X - origin.X) * scale, _islandRiver[j].Z + (course[0].Z - origin.Z) * scale);
                    var candidate = TributaryCourse(head, j, bend);
                    // Keep mouth and head well above the sea: the final mass rebalance can lower low ground by several metres.
                    if (mouth < sea + 30 || Ground(candidate[1], TributaryReach(1)) - 3 < mouth + 4) continue;
                    bool above = true;
                    for (int k = 0; k < TributaryPoints && above; k++) above = Ground(candidate[k], TributaryReach(k)) >= mouth + .5;
                    // ...and clear of the main valley downstream of its mouth, so it cannot spill into a lower reach.
                    for (int k = 0; k < TributaryPoints - 4 && above; k++)
                        for (int q = j + 5; q < RiverPoints && above; q++)
                        {
                            double gx = candidate[k].X - _islandRiver[q].X, gz = candidate[k].Z - _islandRiver[q].Z, clear = (8 + TributaryReach(k)) * map.CellSize;
                            above = gx * gx + gz * gz > clear * clear;
                        }
                    if (above) { _tributaries[r] = (candidate, j / (RiverPoints - 1.0), bend); placed = true; break; }
                }
            }
        }
        // Each side river's floor: below the ground all along its course and falling on an even grade from its head to just above the main river.
        var tributaryFloors = _tributaries.Select(t =>
        {
            double mouth = floors[Math.Min(RiverPoints - 1, (int)Math.Round(t.JoinS * (RiverPoints - 1)))] + .6, level = double.MaxValue;
            double head = Ground(t.Course[0], TributaryReach(0)) - 3;
            var f = new double[TributaryPoints];
            for (int k = 0; k < TributaryPoints; k++)
            {
                level = Math.Min(Math.Min(level - .05, Ground(t.Course[k], TributaryReach(k)) - 3), head + (mouth - head) * k / (TributaryPoints - 1.0));
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
                if (_valleyAt.Length > 0 && distance <= halfWidth) _valleyAt[i] = (float)(s * (RiverPoints - 1));
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
                    if (_valleyAt.Length > 0 && _valleyAt[i] < 0 && tw <= 0) _valleyAt[i] = (float)(RiverPoints + r * TributaryPoints + at);
                }
            }
        });
        if (_tributaries.Count > 0)
        {
            map.TributarySources.Clear();
            foreach (var (course, _, _) in _tributaries)
            {
                int hx = Math.Max(1, Math.Min(n - 2, (int)Math.Round(course[1].X / map.CellSize))), hz = Math.Max(1, Math.Min(n - 2, (int)Math.Round(course[1].Z / map.CellSize)));
                map.TributarySources.Add(map.Index(hx, hz));
            }
        }
        _riverBed = new bool[map.Height.Length];
        for (int i = 0; i < original.Length; i++) _riverBed[i] = map.Height[i] < original[i] - .5f;
        if (_tributaries.Count > 0) map.PlannedRiverBed = _riverBed;
    }

    /// <summary>Slope relaxation slumps the gorge walls into the bed and dams it; with side rivers, lower every
    /// valley-floor cell to the lowest bed upstream so each valley keeps falling to the sea or the main river.</summary>
    private void ReopenIslandValleys(SurfaceMap map)
    {
        if (_valleyAt.Length == 0) return;
        var low = new float[RiverPoints + _tributaries.Count * TributaryPoints];
        Array.Fill(low, float.MaxValue);
        for (int i = 0; i < _valleyAt.Length; i++) if (_valleyAt[i] >= 0) { int k = (int)Math.Round(_valleyAt[i]); low[k] = Math.Min(low[k], map.Height[i]); }
        Fall(0, RiverPoints);
        for (int r = 0; r < _tributaries.Count; r++) Fall(RiverPoints + r * TributaryPoints, TributaryPoints);
        // A point's lowest bed lies near its downstream edge, so interpolate the levels half a point downstream:
        // only dams are cut, and the bed keeps its slope instead of turning into flat steps with drops between them.
        for (int i = 0; i < _valleyAt.Length; i++)
        {
            float at = _valleyAt[i]; if (at < 0) continue;
            int start = at < RiverPoints ? 0 : RiverPoints + (int)((at - RiverPoints) / TributaryPoints) * TributaryPoints;
            int count = start == 0 ? RiverPoints : TributaryPoints;
            float t = Math.Max(0, Math.Min(count - 1, at - start - .5f)); int k0 = Math.Min(count - 2, (int)t);
            map.Height[i] = Math.Min(map.Height[i], low[start + k0] + (low[start + k0 + 1] - low[start + k0]) * (t - k0));
        }

        void Fall(int start, int count)
        {
            float level = float.MaxValue;
            for (int k = start; k < start + count; k++) low[k] = level = Math.Min(low[k], level == float.MaxValue ? level : level - .05f);
            // Points with no bed cell take the level of the next one downstream.
            for (int k = start + count - 2; k >= start; k--) if (low[k] == float.MaxValue) low[k] = low[k + 1];
        }
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
