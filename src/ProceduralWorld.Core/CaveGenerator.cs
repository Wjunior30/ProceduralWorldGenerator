namespace ProceduralWorld.Core;

public sealed class CaveGenerator
{
    private readonly ulong _seed;
    private readonly FractalNoise _chambers, _tunnelA, _tunnelB;
    public CaveGenerator(long seed)
    {
        _seed = PositionalHash.Mix((ulong)seed); var random = new SplitMix64(_seed);
        _chambers = new FractalNoise(random.Next(), 3, 0.055); _tunnelA = new FractalNoise(random.Next(), 2, 0.075); _tunnelB = new FractalNoise(random.Next(), 2, 0.075);
    }

    public CaveVolume Generate(SurfaceMap surface, WorldSettings settings)
    {
        int size = surface.Size, depth = settings.CaveDepthCells; var caves = new CaveVolume(size, depth, size);
        for (int z = 1; z < size - 1; z++) for (int x = 1; x < size - 1; x++) for (int y = 2; y < depth - 2; y++)
        {
            double chamber = _chambers.Sample(x, y * 1.35, z);
            double spaghetti = Math.Max(Math.Abs(_tunnelA.Sample(x, y, z)), Math.Abs(_tunnelB.Sample(x, y, z)));
            double depthBias = 0.05 * (1 - y / (double)depth);
            bool air = chamber > 0.30 - depthBias || spaghetti < 0.045;
            if (air) caves.SetSolid(x, y, z, false);
        }

        var random = new SplitMix64(PositionalHash.Mix(_seed ^ 0xC0A7EUL));
        int entrances = Math.Max(1, settings.GuaranteedCaveEntrances);
        for (int e = 0; e < entrances; e++)
        {
            int x = 4 + random.Next(size - 8), z = 4 + random.Next(size - 8), targetY = 5 + random.Next(Math.Max(1, depth / 2 - 5));
            int surfaceY = (int)Math.Round(surface.HeightAt(x, z) / surface.CellSize);
            CarveShaft(caves, x, z, depth - 2, targetY, e % 2 == 0 ? 1 : -1);
            caves.Entrances.Add(new CaveEntrance(x, z, surfaceY, targetY));
        }
        ConnectEntrances(caves); return caves;
    }

    private static void CarveShaft(CaveVolume caves, int x, int z, int startY, int targetY, int drift)
    {
        int steps = Math.Max(1, startY - targetY);
        for (int i = 0; i <= steps; i++)
        {
            int y = startY - i, cx = Math.Max(2, Math.Min(caves.Width - 3, x + drift * i / 7));
            CarveSphere(caves, cx, y, z, 2);
        }
    }

    private static void ConnectEntrances(CaveVolume caves)
    {
        for (int i = 1; i < caves.Entrances.Count; i++)
        {
            CaveEntrance a = caves.Entrances[i - 1], b = caves.Entrances[i]; int steps = Math.Max(Math.Abs(b.X - a.X), Math.Max(Math.Abs(b.Z - a.Z), Math.Abs(b.CaveY - a.CaveY)));
            for (int s = 0; s <= steps; s++)
            {
                double t = steps == 0 ? 0 : s / (double)steps; int x = (int)Math.Round(a.X + (b.X - a.X) * t); int z = (int)Math.Round(a.Z + (b.Z - a.Z) * t);
                int y = (int)Math.Round(a.CaveY + (b.CaveY - a.CaveY) * t); CarveSphere(caves, x, y, z, 2);
            }
        }
    }

    private static void CarveSphere(CaveVolume caves, int cx, int cy, int cz, int radius)
    {
        for (int y = cy - radius; y <= cy + radius; y++) for (int z = cz - radius; z <= cz + radius; z++) for (int x = cx - radius; x <= cx + radius; x++)
        {
            if (x < 0 || y < 0 || z < 0 || x >= caves.Width || y >= caves.Height || z >= caves.Depth) continue;
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz) <= radius * radius) caves.SetSolid(x, y, z, false);
        }
    }
}

