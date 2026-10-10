namespace ProceduralWorld.Core;

internal static class Hydrology
{
    public static void Apply(SurfaceMap map, WorldSettings settings)
    {
        if (settings.WaterMode == WaterMode.None) return;
        int n = map.Size, count = n * n; var filled = new float[count]; var receiver = new int[count];
        var order = new int[count]; var visited = new bool[count]; var heap = new MinHeap(count); int orderCount = 0;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            if (x != 0 && z != 0 && x != n - 1 && z != n - 1) continue;
            int i = map.Index(x, z); filled[i] = map.Height[i]; receiver[i] = -1; visited[i] = true; heap.Push(i, filled[i]);
        }
        while (heap.Count > 0)
        {
            int cell = heap.Pop(out float level); order[orderCount++] = cell; int cx = cell % n, cz = cell / n;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz; if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= n || z >= n) continue;
                int next = map.Index(x, z); if (visited[next]) continue; visited[next] = true;
                filled[next] = Math.Max(map.Height[next], level + 0.001f); receiver[next] = cell; heap.Push(next, filled[next]);
            }
        }
        Array.Fill(map.Flow, 1);
        for (int i = orderCount - 1; i >= 0; i--) { int cell = order[i]; if (receiver[cell] >= 0) map.Flow[receiver[cell]] += map.Flow[cell]; }
        for (int i = 0; i < count; i++)
        {
            float lake = filled[i] - map.Height[i];
            if (settings.WaterMode == WaterMode.RiversAndLakes && lake > 0.5f) map.WaterDepth[i] = lake;
            if (map.Flow[i] >= settings.RiverThreshold)
            {
                float depth = Math.Min(7, 0.8f + (float)Math.Log(map.Flow[i] / settings.RiverThreshold + 1, 2));
                map.Height[i] -= depth; map.WaterDepth[i] = Math.Max(map.WaterDepth[i], depth * 0.72f);
            }
        }
    }

    private sealed class MinHeap
    {
        private readonly List<(int Cell, float Priority)> _items;
        public MinHeap(int capacity) => _items = new List<(int, float)>(capacity);
        public int Count => _items.Count;
        public void Push(int cell, float priority)
        {
            _items.Add((cell, priority)); int i = _items.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (_items[p].Priority <= priority) break; _items[i] = _items[p]; i = p; }
            _items[i] = (cell, priority);
        }
        public int Pop(out float priority)
        {
            var root = _items[0]; var tail = _items[^1]; _items.RemoveAt(_items.Count - 1);
            if (_items.Count > 0)
            {
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1; if (left >= _items.Count) break; int right = left + 1;
                    int child = right < _items.Count && _items[right].Priority < _items[left].Priority ? right : left;
                    if (_items[child].Priority >= tail.Priority) break; _items[i] = _items[child]; i = child;
                }
                _items[i] = tail;
            }
            priority = root.Priority; return root.Cell;
        }
    }
}

