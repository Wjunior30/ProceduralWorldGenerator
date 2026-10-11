namespace ProceduralWorld.Core;

internal static class Hydrology
{
    public static void Apply(SurfaceMap map, WorldSettings settings)
    {
        Array.Clear(map.Flow, 0, map.Flow.Length); Array.Clear(map.WaterDepth, 0, map.WaterDepth.Length); Array.Clear(map.RiverChannel, 0, map.RiverChannel.Length); Array.Fill(map.DrainageReceiver, -1);
        if (settings.WaterMode == WaterMode.None) return;
        int n = map.Size, count = n * n; var filled = new float[count]; var order = new int[count]; var visited = new bool[count]; var heap = new MinHeap(count); int orderCount = 0;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            if (x != 0 && z != 0 && x != n - 1 && z != n - 1) continue;
            int i = map.Index(x, z); filled[i] = map.Height[i]; visited[i] = true; heap.Push(i, filled[i]);
        }
        while (heap.Count > 0)
        {
            int cell = heap.Pop(out float level); order[orderCount++] = cell; int cx = cell % n, cz = cell / n;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz; if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= n || z >= n) continue;
                int next = map.Index(x, z); if (visited[next]) continue; visited[next] = true;
                float diagonal = dx != 0 && dz != 0 ? 1.4142f : 1; filled[next] = Math.Max(map.Height[next], level + .0005f * diagonal); map.DrainageReceiver[next] = cell; heap.Push(next, filled[next]);
            }
        }
        for (int i = 0; i < count; i++) map.Flow[i] = .65f + Math.Max(0, map.Continentalness[i]) * .55f;
        for (int i = orderCount - 1; i >= 0; i--) { int cell = order[i], receiver = map.DrainageReceiver[cell]; if (receiver >= 0) map.Flow[receiver] += map.Flow[cell]; }
        if (settings.WaterMode == WaterMode.RiversAndLakes)
            for (int i = 0; i < count; i++) { float lake = filled[i] - map.Height[i]; if (lake > .35f) map.WaterDepth[i] = lake; }
        CarveConnectedChannels(map, settings, order, orderCount);
    }

    private static void CarveConnectedChannels(SurfaceMap map, WorldSettings settings, int[] order, int orderCount)
    {
        int n = map.Size; float[] original = map.Height.ToArray(), bed = Enumerable.Repeat(float.PositiveInfinity, map.Height.Length).ToArray();
        for (int o = orderCount - 1; o >= 0; o--)
        {
            int i = order[o]; if (map.Flow[i] < settings.RiverThreshold) continue; map.RiverChannel[i] = true; int receiver = map.DrainageReceiver[i];
            float discharge = map.Flow[i] / Math.Max(1, settings.RiverThreshold);
            float depth = Math.Min(8, .75f + (float)Math.Log(1 + discharge, 2) * 1.35f);
            float downstream = receiver >= 0 ? Math.Min(original[i], original[receiver]) : original[i] - .1f;
            bed[i] = downstream - depth;
        }
        for (int i = 0; i < bed.Length; i++)
        {
            if (float.IsPositiveInfinity(bed[i])) continue; int cx = i % n, cz = i / n;
            float discharge = map.Flow[i] / Math.Max(1, settings.RiverThreshold);
            int radius = Math.Max(1, Math.Min(6, (int)Math.Round((1 + Math.Log(1 + discharge, 2) * .62) * settings.RiverWidthMultiplier)));
            for (int dz = -radius * 2; dz <= radius * 2; dz++) for (int dx = -radius * 2; dx <= radius * 2; dx++)
            {
                int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= n || z >= n) continue;
                double distance = Math.Sqrt(dx * dx + dz * dz), outer = radius * 2d; if (distance > outer) continue;
                float t = (float)(1 - distance / outer); t = t * t * (3 - 2 * t); int j = map.Index(x, z);
                float bank = bed[i] + (float)Math.Min(radius * .9, distance * .72); float target = Math.Min(original[j], bank);
                map.Height[j] = Math.Min(map.Height[j], original[j] + (target - original[j]) * t);
                if (distance <= radius * .72)
                {
                    float waterSurface = bed[i] + Math.Min(2.2f, .55f + radius * .22f); map.WaterDepth[j] = Math.Max(map.WaterDepth[j], Math.Max(.15f, waterSurface - map.Height[j]));
                    map.Flow[j] = Math.Max(map.Flow[j], settings.RiverThreshold);
                }
            }
        }
    }

    private sealed class MinHeap
    {
        private readonly List<(int Cell, float Priority)> _items;
        public MinHeap(int capacity) => _items = new(capacity);
        public int Count => _items.Count;
        public void Push(int cell, float priority) { _items.Add((cell, priority)); int i = _items.Count - 1; while (i > 0) { int p = (i - 1) / 2; if (_items[p].Priority <= priority) break; _items[i] = _items[p]; i = p; } _items[i] = (cell, priority); }
        public int Pop(out float priority)
        {
            var root = _items[0]; var tail = _items[^1]; _items.RemoveAt(_items.Count - 1);
            if (_items.Count > 0) { int i = 0; while (true) { int left = i * 2 + 1; if (left >= _items.Count) break; int right = left + 1; int child = right < _items.Count && _items[right].Priority < _items[left].Priority ? right : left; if (_items[child].Priority >= tail.Priority) break; _items[i] = _items[child]; i = child; } _items[i] = tail; }
            priority = root.Priority; return root.Cell;
        }
    }
}
