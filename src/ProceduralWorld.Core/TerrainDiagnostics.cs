namespace ProceduralWorld.Core;

public sealed record TerrainReport(
    float MinimumHeight, float MaximumHeight, float MaximumNeighbourRise, float MaximumLocalApex,
    int BiomeCount, int RiverCells, int BrokenRiverCells, int CaveEntrances,
    int InaccessibleEntrances, int BiomeComponents,
    double FlatAreaRatio, double SteepAreaRatio, double RepeatedSteepGradeRatio,
    int UphillDrainageCells, int UphillRiverCells, double ReachableCaveRatio,
    double DominantBiomeRatio, double DominantGeologyRatio, int DrainageOutlets,
    double ErosionMassRelativeError, int BasinCount, int MaximumStreamOrder, int CaveComponents);

public static class TerrainDiagnostics
{
    public static TerrainReport Analyze(GeneratedWorld world)
    {
        SurfaceMap map = world.Surface;
        int n = map.Size, riverCells = 0, broken = 0, components = 0, uphillDrainage = 0, uphillRiver = 0, outlets = 0;
        float maxRise = 0, maxApex = 0;
        long flatEdges = 0, steepEdges = 0, edgeCount = 0;
        var steepBuckets = new Dictionary<int, int>();
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            if (x + 1 < n) AddEdge(i, map.Index(x + 1, z));
            if (z + 1 < n) AddEdge(i, map.Index(x, z + 1));
            if (x > 0 && z > 0 && x + 1 < n && z + 1 < n)
            {
                var neighbours = new float[8]; int k = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    neighbours[k++] = map.Height[map.Index(x + dx, z + dz)];
                }
                Array.Sort(neighbours);
                float median = (neighbours[3] + neighbours[4]) * .5f;
                maxApex = Math.Max(maxApex, map.Height[i] - median);
            }
            int receiver = map.DrainageReceiver[i];
            if (receiver < 0) outlets++;
            else if (map.Height[receiver] > map.Height[i] + .01f) uphillDrainage++;
            if (!map.RiverChannel[i]) continue;
            riverCells++;
            if (receiver >= 0)
            {
                double sourceWater = map.Height[i] + map.WaterDepth[i];
                double receiverWater = map.Height[receiver] + map.WaterDepth[receiver];
                bool discontinuous = !map.RiverChannel[receiver] || map.Flow[receiver] + .001f < map.Flow[i];
                bool uphill = receiverWater > sourceWater + .01;
                if (discontinuous || uphill) broken++;
                if (uphill) uphillRiver++;
            }
        }
        var visited = new bool[map.Biomes.Length];
        for (int i = 0; i < visited.Length; i++) if (!visited[i]) { components++; FloodBiome(map, i, visited); }
        var cave = AnalyzeCaves(world.Caves);
        int inaccessible = world.Caves.Entrances.Count(e =>
            world.Caves.IsSolid(e.X, world.Caves.Height - 2, e.Z) ||
            world.Caves.IsSolid(e.X, e.CaveY, e.Z) || !cave.ReachableEntrances.Contains(e));
        double dominantBiome = map.Biomes.GroupBy(x => x).Max(x => x.Count()) / (double)map.Biomes.Length;
        double dominantGeology = map.Geology.GroupBy(x => x).Max(x => x.Count()) / (double)map.Geology.Length;
        int caveComponents = world.Caves.ComputeComponents().DefaultIfEmpty().Max();
        double terrainMass = map.Height.Sum(x => Math.Abs((double)x));
        int repeated = steepBuckets.Count == 0 ? 0 : steepBuckets.Values.Max();
        return new TerrainReport(map.Height.Min(), map.Height.Max(), maxRise, maxApex, map.Biomes.Distinct().Count(), riverCells, broken,
            world.Caves.Entrances.Count, inaccessible, components,
            flatEdges / (double)Math.Max(1, edgeCount), steepEdges / (double)Math.Max(1, edgeCount), repeated / (double)Math.Max(1, edgeCount),
            uphillDrainage, uphillRiver, cave.Air == 0 ? 1 : cave.Reachable / (double)cave.Air,
            dominantBiome, dominantGeology, outlets,
            Math.Abs(map.ErosionMassDelta) / Math.Max(1, terrainMass), map.BasinId.Distinct().Count(x => x > 0), map.StreamOrder.DefaultIfEmpty().Max(), caveComponents);

        void AddEdge(int a, int b)
        {
            float rise = Math.Abs(map.Height[a] - map.Height[b]);
            maxRise = Math.Max(maxRise, rise); edgeCount++;
            double grade = rise / map.CellSize;
            if (grade < .08) flatEdges++;
            if (grade > .60) steepEdges++;
            if (grade > .20)
            {
                int bucket = (int)Math.Round(grade * 100);
                steepBuckets[bucket] = steepBuckets.GetValueOrDefault(bucket) + 1;
            }
        }
    }

    private sealed class CaveAnalysis
    {
        public long Reachable;
        public long Air;
        public HashSet<CaveEntrance> ReachableEntrances { get; } = new();
    }

    private static CaveAnalysis AnalyzeCaves(CaveVolume caves)
    {
        int w = caves.Width, h = caves.Height, d = caves.Depth;
        var result = new CaveAnalysis { Air = caves.AirCellCount };
        var seen = new bool[checked(w * h * d)];
        var queue = new Queue<int>();
        foreach (CaveEntrance entrance in caves.Entrances)
        {
            int y = h - 2;
            if (caves.IsSolid(entrance.X, y, entrance.Z)) continue;
            int id = (y * d + entrance.Z) * w + entrance.X;
            if (!seen[id]) { seen[id] = true; queue.Enqueue(id); }
            result.ReachableEntrances.Add(entrance);
        }
        int[] dx = { 1, -1, 0, 0, 0, 0 }, dy = { 0, 0, 1, -1, 0, 0 }, dz = { 0, 0, 0, 0, 1, -1 };
        while (queue.Count > 0)
        {
            int id = queue.Dequeue(); result.Reachable++;
            int x = id % w, t = id / w, z = t % d, y = t / d;
            for (int k = 0; k < 6; k++)
            {
                int nx = x + dx[k], ny = y + dy[k], nz = z + dz[k];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || (uint)nz >= (uint)d || caves.IsSolid(nx, ny, nz)) continue;
                int next = (ny * d + nz) * w + nx;
                if (!seen[next]) { seen[next] = true; queue.Enqueue(next); }
            }
        }
        foreach (CaveEntrance entrance in caves.Entrances)
        {
            int id = (Math.Max(0, Math.Min(h - 1, entrance.CaveY)) * d + entrance.Z) * w + entrance.X;
            if (!seen[id]) result.ReachableEntrances.Remove(entrance);
        }
        return result;
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
