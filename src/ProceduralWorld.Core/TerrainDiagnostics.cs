namespace ProceduralWorld.Core;

public sealed record TerrainReport(
    float MinimumHeight, float MaximumHeight, float MaximumNeighbourRise,
    int BiomeCount, int RiverCells, int BrokenRiverCells, int CaveEntrances,
    int InaccessibleEntrances, int BiomeComponents);

public static class TerrainDiagnostics
{
    public static TerrainReport Analyze(GeneratedWorld world)
    {
        var map = world.Surface; int n = map.Size, riverCells = 0, broken = 0, components = 0;
        float maxRise = 0;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            if (x + 1 < n) maxRise = Math.Max(maxRise, Math.Abs(map.Height[i] - map.Height[map.Index(x + 1, z)]));
            if (z + 1 < n) maxRise = Math.Max(maxRise, Math.Abs(map.Height[i] - map.Height[map.Index(x, z + 1)]));
            if (!map.RiverChannel[i]) continue;
            riverCells++;
            int receiver = map.DrainageReceiver[i];
            if (receiver >= 0 && !map.RiverChannel[receiver] && map.Flow[receiver] >= map.Flow[i]) broken++;
        }
        var visited = new bool[map.Biomes.Length];
        for (int i = 0; i < visited.Length; i++) if (!visited[i]) { components++; FloodBiome(map, i, visited); }
        int inaccessible = world.Caves.Entrances.Count(e => world.Caves.IsSolid(e.X, world.Caves.Height - 2, e.Z) || world.Caves.IsSolid(e.X, e.CaveY, e.Z));
        return new TerrainReport(map.Height.Min(), map.Height.Max(), maxRise, map.Biomes.Distinct().Count(), riverCells, broken,
            world.Caves.Entrances.Count, inaccessible, components);
    }

    private static void FloodBiome(SurfaceMap map, int start, bool[] visited)
    {
        int n = map.Size; Biome target = map.Biomes[start]; var queue = new Queue<int>(); queue.Enqueue(start); visited[start] = true;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % n, z = i / n;
            if (x > 0) Add(i - 1); if (x + 1 < n) Add(i + 1); if (z > 0) Add(i - n); if (z + 1 < n) Add(i + n);
        }
        void Add(int i) { if (!visited[i] && map.Biomes[i] == target) { visited[i] = true; queue.Enqueue(i); } }
    }
}
