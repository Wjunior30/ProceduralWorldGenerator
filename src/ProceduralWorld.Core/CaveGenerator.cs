namespace ProceduralWorld.Core;

public sealed class CaveGenerator
{
    private readonly ulong _seed;
    private readonly FractalNoise _chambers, _tunnelA, _tunnelB;

    public CaveGenerator(long seed)
    {
        _seed = PositionalHash.Mix((ulong)seed);
        var random = new SplitMix64(_seed);
        _chambers = new(random.Next(), 3, .052);
        _tunnelA = new(random.Next(), 2, .072);
        _tunnelB = new(random.Next(), 2, .072);
    }

    public CaveVolume Generate(SurfaceMap surface, WorldSettings settings)
    {
        int size = surface.Size, depth = settings.CaveDepthCells;
        var caves = new CaveVolume(size, depth, size);
        for (int z = 1; z < size - 1; z++) for (int x = 1; x < size - 1; x++)
        {
            int surfaceIndex = surface.Index(x, z);
            GeologicalProfile profile = GeologicalProfiles.For(surface.Geology[surfaceIndex]);
            double density = settings.CaveDensity * (.55 + profile.CaveDensity * .65);
            double chamberThreshold = .355 - Math.Min(.105, Math.Max(0, density - .55) * .075);
            double tunnelThreshold = .038 + Math.Min(.032, Math.Max(0, density - .5) * .018);
            for (int y = 2; y < depth - 2; y++)
            {
                double chamber = _chambers.Sample(x, y * 1.28, z);
                double spaghetti = Math.Max(Math.Abs(_tunnelA.Sample(x, y, z)), Math.Abs(_tunnelB.Sample(x, y, z)));
                double depthBias = .045 * (1 - y / (double)depth);
                if (chamber > chamberThreshold - depthBias || spaghetti < tunnelThreshold) caves.SetSolid(x, y, z, false);
            }
        }

        var random = new SplitMix64(PositionalHash.Mix(_seed ^ 0xC0A7EUL));
        var chosen = new List<(int X, int Z)>();
        int wanted = Math.Max(1, settings.GuaranteedCaveEntrances);
        for (int attempts = 0; chosen.Count < wanted && attempts < wanted * 240; attempts++)
        {
            int x = 4 + random.Next(Math.Max(1, size - 8));
            int z = 4 + random.Next(Math.Max(1, size - 8));
            int i = surface.Index(x, z);
            if (surface.WaterDepth[i] > .05f) continue;
            int spacing = Math.Max(36, size * size / Math.Max(32, wanted * 18));
            if (chosen.Any(p => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < spacing)) continue;
            float slope = surface.Slope[i];
            float maximumSlope = attempts < wanted * 150 ? .68f : .86f;
            if (slope < .08f || slope > maximumSlope) continue;
            (int fx, int fz) = DownhillFacing(surface, x, z);
            int targetY = 4 + random.Next(Math.Max(1, depth / 2 - 4));
            int surfaceY = (int)Math.Round(surface.HeightAt(x, z) / surface.CellSize);
            GeologicalProfile profile = GeologicalProfiles.For(surface.Geology[i]);
            int radius = profile.Identity == GeologicalIdentity.KarstHighland ? 3 : profile.Identity == GeologicalIdentity.VolcanicPlateau ? 2 : 2;
            CarveRamp(caves, x, z, depth - 1, targetY, fx, fz, radius);
            CarveEntranceGallery(caves, x, targetY, z, fx, fz, radius);
            CarveChamber(caves, x + fx * 7, targetY, z + fz * 7, radius + 2, Math.Max(2, radius), radius + 2);
            caves.Entrances.Add(new(x, z, surfaceY, targetY, fx, fz, radius));
            chosen.Add((x, z));
        }

        EnsureEntranceCount(caves, surface, settings, random, chosen);
        ConnectEntranceNetwork(caves);
        RetainReachableAir(caves);
        return caves;
    }

    private static void EnsureEntranceCount(CaveVolume caves, SurfaceMap surface, WorldSettings settings, SplitMix64 random, List<(int X, int Z)> chosen)
    {
        int wanted = Math.Max(1, settings.GuaranteedCaveEntrances);
        int size = surface.Size;
        for (int attempts = 0; chosen.Count < wanted && attempts < wanted * 300; attempts++)
        {
            int x = 3 + random.Next(Math.Max(1, size - 6));
            int z = 3 + random.Next(Math.Max(1, size - 6));
            int i = surface.Index(x, z);
            if (surface.WaterDepth[i] > .1f || chosen.Any(p => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < 25)) continue;
            (int fx, int fz) = DownhillFacing(surface, x, z);
            int targetY = Math.Max(3, caves.Height / 3);
            int surfaceY = (int)Math.Round(surface.HeightAt(x, z) / surface.CellSize);
            int radius = surface.Geology[i] == GeologicalIdentity.KarstHighland ? 3 : 2;
            CarveRamp(caves, x, z, caves.Height - 1, targetY, fx, fz, radius);
            CarveEntranceGallery(caves, x, targetY, z, fx, fz, radius);
            CarveChamber(caves, x + fx * 6, targetY, z + fz * 6, radius + 2, radius, radius + 2);
            caves.Entrances.Add(new(x, z, surfaceY, targetY, fx, fz, radius));
            chosen.Add((x, z));
        }
    }

    private static (int X, int Z) DownhillFacing(SurfaceMap surface, int x, int z)
    {
        int bx = 0, bz = 1;
        float low = surface.HeightAt(x, z);
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            int nx = Math.Max(0, Math.Min(surface.Size - 1, x + dx));
            int nz = Math.Max(0, Math.Min(surface.Size - 1, z + dz));
            float h = surface.HeightAt(nx, nz);
            if (h < low) { low = h; bx = dx; bz = dz; }
        }
        return (bx, bz);
    }

    private static void CarveRamp(CaveVolume caves, int x, int z, int startY, int targetY, int fx, int fz, int radius)
    {
        int vertical = Math.Max(1, startY - targetY), steps = vertical + 12;
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            int y = (int)Math.Round(startY + (targetY - startY) * Smooth(t));
            int cx = Math.Max(radius + 1, Math.Min(caves.Width - radius - 2, x + fx * (int)Math.Round(7 * t)));
            int cz = Math.Max(radius + 1, Math.Min(caves.Depth - radius - 2, z + fz * (int)Math.Round(7 * t)));
            CarveEllipsoid(caves, cx, y, cz, radius + (s < 5 ? 1 : 0), radius, radius + 1);
        }
    }

    private static void CarveEntranceGallery(CaveVolume caves, int x, int y, int z, int fx, int fz, int radius)
    {
        for (int step = 0; step <= 7; step++)
            CarveEllipsoid(caves, x + fx * step, y, z + fz * step, radius, Math.Max(2, radius), radius);
    }
    private static void ConnectEntranceNetwork(CaveVolume caves)
    {
        if (caves.Entrances.Count == 0) return;
        int hubX = (int)Math.Round(caves.Entrances.Average(e => e.X + e.FacingX * 7));
        int hubZ = (int)Math.Round(caves.Entrances.Average(e => e.Z + e.FacingZ * 7));
        int hubY = Math.Max(3, (int)Math.Round(caves.Entrances.Average(e => e.CaveY)) - 2);
        hubX = Math.Max(4, Math.Min(caves.Width - 5, hubX));
        hubZ = Math.Max(4, Math.Min(caves.Depth - 5, hubZ));
        CarveChamber(caves, hubX, hubY, hubZ, 5, 3, 5);
        for (int i = 0; i < caves.Entrances.Count; i++)
        {
            CaveEntrance e = caves.Entrances[i];
            int sx = e.X + e.FacingX * 7, sz = e.Z + e.FacingZ * 7;
            CarveCurvedTunnel(caves, sx, e.CaveY, sz, hubX, hubY, hubZ, i);
        }
    }

    private static void CarveCurvedTunnel(CaveVolume caves, int ax, int ay, int az, int bx, int by, int bz, int salt)
    {
        int steps = Math.Max(8, Math.Max(Math.Abs(bx - ax), Math.Max(Math.Abs(by - ay), Math.Abs(bz - az))) * 2);
        double dx = bx - ax, dz = bz - az, length = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
        double normalX = -dz / length, normalZ = dx / length;
        double bend = Math.Sin((salt + 1) * 2.17) * Math.Min(9, length * .18);
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            double curve = Math.Sin(t * Math.PI) * bend;
            int x = (int)Math.Round(ax + dx * t + normalX * curve);
            int y = (int)Math.Round(ay + (by - ay) * t + Math.Sin(t * Math.PI * 2 + salt) * 1.2);
            int z = (int)Math.Round(az + dz * t + normalZ * curve);
            CarveEllipsoid(caves, x, y, z, 2, 2, 2);
        }
    }

    private static void RetainReachableAir(CaveVolume caves)
    {
        int w = caves.Width, h = caves.Height, d = caves.Depth;
        var reachable = new bool[checked(w * h * d)];
        var queue = new Queue<int>();
        foreach (CaveEntrance entrance in caves.Entrances)
        {
            int y = h - 2;
            if (caves.IsSolid(entrance.X, y, entrance.Z)) continue;
            int id = (y * d + entrance.Z) * w + entrance.X;
            if (!reachable[id]) { reachable[id] = true; queue.Enqueue(id); }
        }
        int[] dx = { 1, -1, 0, 0, 0, 0 }, dy = { 0, 0, 1, -1, 0, 0 }, dz = { 0, 0, 0, 0, 1, -1 };
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            int x = id % w, t = id / w, z = t % d, y = t / d;
            for (int k = 0; k < 6; k++)
            {
                int nx = x + dx[k], ny = y + dy[k], nz = z + dz[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || (uint)nz >= (uint)d || caves.IsSolid(nx, ny, nz)) continue;
                int next = (ny * d + nz) * w + nx;
                if (!reachable[next]) { reachable[next] = true; queue.Enqueue(next); }
            }
        }
        for (int y = 0; y < h; y++) for (int z = 0; z < d; z++) for (int x = 0; x < w; x++)
        {
            int id = (y * d + z) * w + x;
            if (!caves.IsSolid(x, y, z) && !reachable[id]) caves.SetSolid(x, y, z, true);
        }
    }

    private static void CarveChamber(CaveVolume caves, int x, int y, int z, int rx, int ry, int rz) => CarveEllipsoid(caves, x, y, z, rx, ry, rz);

    private static void CarveEllipsoid(CaveVolume caves, int cx, int cy, int cz, int rx, int ry, int rz)
    {
        for (int y = cy - ry; y <= cy + ry; y++) for (int z = cz - rz; z <= cz + rz; z++) for (int x = cx - rx; x <= cx + rx; x++)
        {
            if (x < 0 || y < 0 || z < 0 || x >= caves.Width || y >= caves.Height || z >= caves.Depth) continue;
            double value = (x - cx) * (x - cx) / (double)(rx * rx) + (y - cy) * (y - cy) / (double)(ry * ry) + (z - cz) * (z - cz) / (double)(rz * rz);
            if (value <= 1) caves.SetSolid(x, y, z, false);
        }
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
}
