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
            if (map.Road[i]) { cost[i] = .35f; continue; } // roads (and their bridges) are the preferred way
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
        // Array-based A* with a binary heap and generation stamps: no per-search allocation of the grid.
        int count = Cost.Length;
        if (_g == null || _g.Length != count) { _g = new float[count]; _parent = new int[count]; _stamp = new int[count]; _closed = new int[count]; }
        int stamp = ++_search;
        var heap = new List<(float F, int Cell)>();
        void Push(float f, int cell)
        {
            heap.Add((f, cell)); int i = heap.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (heap[p].F <= heap[i].F) break; (heap[p], heap[i]) = (heap[i], heap[p]); i = p; }
        }
        (float F, int Cell) Pop()
        {
            var top = heap[0]; heap[0] = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1); int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < heap.Count && heap[l].F < heap[m].F) m = l;
                if (r < heap.Count && heap[r].F < heap[m].F) m = r;
                if (m == i) break; (heap[m], heap[i]) = (heap[i], heap[m]); i = m;
            }
            return top;
        }
        _stamp[start] = stamp; _g[start] = 0; _parent[start] = -1; Push(Heuristic(start, goal), start);
        int expanded = 0;
        while (heap.Count > 0 && expanded++ < maxExpanded)
        {
            int cell = Pop().Cell;
            if (_closed[cell] == stamp) continue;
            _closed[cell] = stamp;
            if (cell == goal)
            {
                var path = new List<(int, int)>();
                for (int c = goal; c >= 0; c = _parent[c]) path.Add((c % Size, c / Size));
                path.Reverse(); return path;
            }
            int x = cell % Size, z = cell / Size;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= Size || nz >= Size) continue;
                int next = nz * Size + nx;
                if (!float.IsFinite(Cost[next]) || _closed[next] == stamp) continue;
                float candidate = _g[cell] + (dx != 0 && dz != 0 ? 1.4142f : 1f) * (Cost[cell] + Cost[next]) * .5f;
                if (_stamp[next] == stamp && _g[next] <= candidate) continue;
                _stamp[next] = stamp; _g[next] = candidate; _parent[next] = cell;
                Push(candidate + Heuristic(next, goal) * .35f, next); // road cost is .35, keep the heuristic admissible
            }
        }
        return null;
    }
    private float[]? _g; private int[]? _parent, _stamp, _closed; private int _search;

    private float Heuristic(int a, int b) { int dx = Math.Abs(a % Size - b % Size), dz = Math.Abs(a / Size - b / Size); return Math.Max(dx, dz) + .4142f * Math.Min(dx, dz); }
}
