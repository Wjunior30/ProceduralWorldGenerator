namespace ProceduralWorld.Core;

/// <summary>Walking cost per surface cell for NPC pathfinding. Infinity marks impassable cells (water, cliffs).</summary>
public sealed class NavigationGrid
{
    private NavigationGrid(int size, float cellSize, float[] cost) { Size = size; CellSize = cellSize; Cost = cost; }
    public int Size { get; }
    public float CellSize { get; }
    public float[] Cost { get; }

    public static NavigationGrid Build(SurfaceMap map, float maxSlope = 1.2f)
    {
        var cost = new float[map.Height.Length];
        for (int i = 0; i < cost.Length; i++)
        {
            float slope = map.Slope[i];
            if (map.WaterDepth[i] > 0 || slope > maxSlope) { cost[i] = float.PositiveInfinity; continue; }
            float biome = map.Biomes[i] switch
            {
                Biome.Swamp => 2.5f,
                Biome.Jungle or Biome.Taiga => 1.6f,
                Biome.Snow or Biome.Desert => 1.4f,
                _ => 1f,
            };
            cost[i] = biome * (1 + 3 * slope / maxSlope);
        }
        return new NavigationGrid(map.Size, map.CellSize, cost);
    }

    /// <summary>8-way A*; returns cells from start to goal inclusive, or null when unreachable.</summary>
    public IReadOnlyList<(int X, int Z)>? FindPath(int startX, int startZ, int goalX, int goalZ, int maxExpanded = 200_000)
    {
        int start = startZ * Size + startX, goal = goalZ * Size + goalX;
        if (!float.IsFinite(Cost[start]) || !float.IsFinite(Cost[goal])) return null;
        var g = new Dictionary<int, float> { [start] = 0 };
        var parent = new Dictionary<int, int>();
        var open = new SortedSet<(float F, int Cell)> { (Heuristic(start, goal), start) };
        int expanded = 0;
        while (open.Count > 0 && expanded++ < maxExpanded)
        {
            var (_, cell) = open.Min; open.Remove(open.Min);
            if (cell == goal)
            {
                var path = new List<(int, int)>();
                for (int c = goal; ; c = parent[c]) { path.Add((c % Size, c / Size)); if (c == start) break; }
                path.Reverse(); return path;
            }
            int x = cell % Size, z = cell / Size;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= Size || nz >= Size) continue;
                int next = nz * Size + nx;
                if (!float.IsFinite(Cost[next])) continue;
                float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * (Cost[cell] + Cost[next]) * .5f;
                float candidate = g[cell] + step;
                if (g.TryGetValue(next, out float known) && known <= candidate) continue;
                if (g.ContainsKey(next)) open.Remove((known + Heuristic(next, goal), next));
                g[next] = candidate; parent[next] = cell;
                open.Add((candidate + Heuristic(next, goal), next));
            }
        }
        return null;
    }

    private float Heuristic(int a, int b) { int dx = Math.Abs(a % Size - b % Size), dz = Math.Abs(a / Size - b / Size); return Math.Max(dx, dz) + .4142f * Math.Min(dx, dz); }
}
