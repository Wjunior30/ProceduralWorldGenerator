namespace ProceduralWorld.Core;

internal static class Hydrology
{
    private sealed class DrainageSolution
    {
        public DrainageSolution(float[] filled, int[] order, int count) { Filled = filled; Order = order; Count = count; }
        public float[] Filled { get; }
        public int[] Order { get; }
        public int Count { get; }
    }

    public static void Apply(SurfaceMap map, WorldSettings settings)
    {
        Array.Clear(map.Flow, 0, map.Flow.Length);
        Array.Clear(map.WaterDepth, 0, map.WaterDepth.Length);
        Array.Clear(map.RiverChannel, 0, map.RiverChannel.Length);
        Array.Fill(map.DrainageReceiver, -1);
        if (settings.WaterMode == WaterMode.None) return;

        DrainageSolution first = Solve(map);
        MarkChannels(map, settings);
        CarveChannels(map, settings, first);

        DrainageSolution second = Solve(map);
        FillClosedDepressions(map, second);
        Erosion.RelaxSlopes(map, .90f, 24);
        DrainageSolution settled = Solve(map);
        FillClosedDepressions(map, settled);
        DrainageSolution final = Solve(map);
        MarkChannels(map, settings);
        AssignWater(map, settings, final);
    }

    private static DrainageSolution Solve(SurfaceMap map)
    {
        int n = map.Size, count = n * n;
        Array.Clear(map.Flow, 0, map.Flow.Length);
        Array.Fill(map.DrainageReceiver, -1);
        var filled = new float[count];
        var order = new int[count];
        var visited = new bool[count];
        var heap = new MinHeap(count);
        int orderCount = 0;
        foreach (int outlet in SelectOutlets(map))
        {
            filled[outlet] = map.Height[outlet];
            visited[outlet] = true;
            heap.Push(outlet, filled[outlet]);
        }
        while (heap.Count > 0)
        {
            int cell = heap.Pop(out float level);
            order[orderCount++] = cell;
            int cx = cell % n, cz = cell / n;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= n || z >= n) continue;
                int next = map.Index(x, z);
                if (visited[next]) continue;
                visited[next] = true;
                float distance = dx != 0 && dz != 0 ? 1.41421356f : 1;
                filled[next] = Math.Max(map.Height[next], level + map.CellSize * .00035f * distance);
                map.DrainageReceiver[next] = cell;
                heap.Push(next, filled[next]);
            }
        }
        for (int i = 0; i < count; i++) map.Flow[i] = .65f + Math.Max(0, map.Continentalness[i]) * .55f;
        for (int i = orderCount - 1; i >= 0; i--)
        {
            int cell = order[i], receiver = map.DrainageReceiver[cell];
            if (receiver >= 0) map.Flow[receiver] += map.Flow[cell];
        }
        return new DrainageSolution(filled, order, orderCount);
    }

    private static IReadOnlyList<int> SelectOutlets(SurfaceMap map)
    {
        int n = map.Size;
        var boundary = new List<int>(n * 4 - 4);
        for (int x = 0; x < n; x++) boundary.Add(map.Index(x, 0));
        for (int z = 1; z < n; z++) boundary.Add(map.Index(n - 1, z));
        for (int x = n - 2; x >= 0; x--) boundary.Add(map.Index(x, n - 1));
        for (int z = n - 2; z > 0; z--) boundary.Add(map.Index(0, z));
        int wanted = Math.Max(4, Math.Min(8, 3 + n / 64));
        int minimumSpacing = Math.Max(8, boundary.Count / (wanted * 2));
        var ranked = boundary.Select((cell, position) => (Cell: cell, Position: position, Height: map.Height[cell])).OrderBy(x => x.Height).ToArray();
        var selected = new List<(int Cell, int Position)>();
        foreach (var candidate in ranked)
        {
            bool separated = selected.All(item => CircularDistance(item.Position, candidate.Position, boundary.Count) >= minimumSpacing);
            if (!separated) continue;
            selected.Add((candidate.Cell, candidate.Position));
            if (selected.Count == wanted) break;
        }
        if (selected.Count == 0) selected.Add((ranked[0].Cell, ranked[0].Position));
        return selected.Select(x => x.Cell).ToArray();
    }

    private static int CircularDistance(int a, int b, int length)
    {
        int direct = Math.Abs(a - b);
        return Math.Min(direct, length - direct);
    }

    private static void MarkChannels(SurfaceMap map, WorldSettings settings)
    {
        Array.Clear(map.RiverChannel, 0, map.RiverChannel.Length);
        for (int i = 0; i < map.Flow.Length; i++) map.RiverChannel[i] = map.Flow[i] >= settings.RiverThreshold;
    }

    private static void CarveChannels(SurfaceMap map, WorldSettings settings, DrainageSolution solution)
    {
        int n = map.Size;
        float[] original = map.Height.ToArray();
        for (int o = 0; o < solution.Count; o++)
        {
            int i = solution.Order[o];
            if (!map.RiverChannel[i]) continue;
            int cx = i % n, cz = i / n;
            float discharge = map.Flow[i] / Math.Max(1, settings.RiverThreshold);
            float depth = Math.Min(7, .7f + (float)Math.Log(1 + discharge, 2) * 1.15f);
            float bed = solution.Filled[i] - depth;
            int radius = Math.Max(1, Math.Min(7, (int)Math.Round((1 + Math.Log(1 + discharge, 2) * .56) * settings.RiverWidthMultiplier)));
            double outer = Math.Max(2, radius * 2.4);
            for (int dz = -(int)Math.Ceiling(outer); dz <= outer; dz++) for (int dx = -(int)Math.Ceiling(outer); dx <= outer; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                double distance = Math.Sqrt(dx * dx + dz * dz);
                if (distance > outer) continue;
                float t = (float)(1 - distance / outer); t = t * t * (3 - 2 * t);
                int j = map.Index(x, z);
                float bank = bed + (float)Math.Min(radius * .72, distance * .58);
                float target = Math.Min(original[j], bank);
                float carved = original[j] + (target - original[j]) * t;
                if (carved < map.Height[j])
                {
                    map.Sediment[i] += map.Height[j] - carved;
                    map.Height[j] = carved;
                }
            }
        }
    }

    private static void FillClosedDepressions(SurfaceMap map, DrainageSolution solution)
    {
        for (int i = 0; i < map.Height.Length; i++)
        {
            float deposit = solution.Filled[i] - map.Height[i];
            if (deposit <= .001f) continue;
            map.Height[i] = solution.Filled[i];
            map.Sediment[i] += deposit;
        }
    }

    private static void AssignWater(SurfaceMap map, WorldSettings settings, DrainageSolution solution)
    {
        int n = map.Size;
        Array.Clear(map.WaterDepth, 0, map.WaterDepth.Length);
        if (settings.WaterMode == WaterMode.RiversAndLakes)
        {
            for (int i = 0; i < map.Height.Length; i++)
            {
                float lake = solution.Filled[i] - map.Height[i];
                if (lake > .25f) map.WaterDepth[i] = lake;
            }
        }
        for (int o = 0; o < solution.Count; o++)
        {
            int i = solution.Order[o];
            if (!map.RiverChannel[i]) continue;
            int cx = i % n, cz = i / n;
            float discharge = map.Flow[i] / Math.Max(1, settings.RiverThreshold);
            int radius = Math.Max(1, Math.Min(7, (int)Math.Round((1 + Math.Log(1 + discharge, 2) * .56) * settings.RiverWidthMultiplier)));
            float surface = solution.Filled[i] + .18f;
            double wetRadius = Math.Max(.75, radius * .78);
            int extent = (int)Math.Ceiling(wetRadius);
            for (int dz = -extent; dz <= extent; dz++) for (int dx = -extent; dx <= extent; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                double distance = Math.Sqrt(dx * dx + dz * dz);
                if (distance > wetRadius) continue;
                int j = map.Index(x, z);
                float edgeFalloff = (float)(1 - distance / Math.Max(.001, wetRadius));
                float localSurface = surface - (1 - edgeFalloff) * .10f;
                if (localSurface <= map.Height[j]) continue;
                map.WaterDepth[j] = Math.Max(map.WaterDepth[j], localSurface - map.Height[j]);
                map.Flow[j] = Math.Max(map.Flow[j], settings.RiverThreshold * edgeFalloff);
            }
            map.WaterDepth[i] = Math.Max(.18f, surface - map.Height[i]);
        }
        // Bank stamping can overlap a downstream core. Restore the exact
        // priority-flood surface on every channel cell so water never rises.
        for (int i = 0; i < map.Height.Length; i++)
            if (map.RiverChannel[i]) map.WaterDepth[i] = Math.Max(.18f, solution.Filled[i] + .18f - map.Height[i]);
    }

    private sealed class MinHeap
    {
        private readonly List<(int Cell, float Priority)> _items;
        public MinHeap(int capacity) => _items = new(capacity);
        public int Count => _items.Count;
        public void Push(int cell, float priority)
        {
            _items.Add((cell, priority));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_items[p].Priority <= priority) break;
                _items[i] = _items[p]; i = p;
            }
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
                    int left = i * 2 + 1; if (left >= _items.Count) break;
                    int right = left + 1;
                    int child = right < _items.Count && _items[right].Priority < _items[left].Priority ? right : left;
                    if (_items[child].Priority >= tail.Priority) break;
                    _items[i] = _items[child]; i = child;
                }
                _items[i] = tail;
            }
            priority = root.Priority; return root.Cell;
        }
    }
}
