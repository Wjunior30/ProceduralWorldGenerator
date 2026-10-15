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
        Array.Clear(map.MainRiverChannel, 0, map.MainRiverChannel.Length);
        Array.Clear(map.LagoonBasin, 0, map.LagoonBasin.Length);
        Array.Clear(map.LagoonSurface, 0, map.LagoonSurface.Length);
        Array.Fill(map.DrainageReceiver, -1);
        if (settings.WaterMode == WaterMode.None) return;
        double targetMass = map.Height.Sum(x => (double)x);
        bool preserveLakes = settings.WaterMode == WaterMode.RiversAndLakes;

        DrainageSolution first = Solve(map);
        MarkMainRiver(map, first);
        MarkChannels(map, settings);
        CarveChannels(map, settings, first);

        if (preserveLakes && settings.LagoonCount > 0)
            CarveLagoons(map, settings);

        DrainageSolution second = Solve(map);
        FillClosedDepressions(map, second, preserveLakes);
        Erosion.RelaxSlopes(map, .90f, 32);
        DrainageSolution settled = Solve(map);
        FillClosedDepressions(map, settled, preserveLakes);
        Erosion.LimitExtremeSteps(map, map.CellSize * 1.80f, 24);
        DrainageSolution final = Solve(map);
        MarkMainRiver(map, final);
        MarkChannels(map, settings);
        AssignWater(map, settings, final);
        ClassifyBasinsAndStreams(map, final);
        RebalanceHeightMass(map, targetMass);
    }

    private static void ClassifyBasinsAndStreams(SurfaceMap map, DrainageSolution solution)
    {
        Array.Clear(map.BasinId, 0, map.BasinId.Length);
        Array.Clear(map.StreamOrder, 0, map.StreamOrder.Length);
        var outletLabels = new Dictionary<int, int>();
        int nextLabel = 0;
        for (int o = 0; o < solution.Count; o++)
        {
            int cell = solution.Order[o], receiver = map.DrainageReceiver[cell];
            if (receiver < 0) map.BasinId[cell] = outletLabels[cell] = ++nextLabel;
            else map.BasinId[cell] = map.BasinId[receiver];
        }

        var maximumIncoming = new byte[map.Height.Length];
        var maximumCount = new byte[map.Height.Length];
        for (int o = solution.Count - 1; o >= 0; o--)
        {
            int cell = solution.Order[o];
            if (!map.RiverChannel[cell]) continue;
            byte order = maximumIncoming[cell] == 0 ? (byte)1 :
                maximumCount[cell] >= 2 ? (byte)Math.Min(byte.MaxValue, maximumIncoming[cell] + 1) : maximumIncoming[cell];
            map.StreamOrder[cell] = order;
            int receiver = map.DrainageReceiver[cell];
            if (receiver < 0 || !map.RiverChannel[receiver]) continue;
            if (order > maximumIncoming[receiver]) { maximumIncoming[receiver] = order; maximumCount[receiver] = 1; }
            else if (order == maximumIncoming[receiver]) maximumCount[receiver]++;
        }
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
        var ranked = boundary.Select((cell, position) => (Cell: cell, Position: position, Height: map.Height[cell])).OrderBy(x => x.Height).ToArray();
        return new[] { ranked[0].Cell };
    }

    private static int CircularDistance(int a, int b, int length)
    {
        int direct = Math.Abs(a - b);
        return Math.Min(direct, length - direct);
    }

    private static void MarkChannels(SurfaceMap map, WorldSettings settings)
    {
        Array.Clear(map.RiverChannel, 0, map.RiverChannel.Length);
        float tributaryThreshold = settings.RiverThreshold * settings.TributaryThresholdMultiplier;
        for (int i = 0; i < map.Flow.Length; i++)
            map.RiverChannel[i] = map.MainRiverChannel[i] || (!map.LagoonBasin[i] && map.Flow[i] >= tributaryThreshold);
    }

    private static void MarkMainRiver(SurfaceMap map, DrainageSolution solution)
    {
        Array.Clear(map.MainRiverChannel, 0, map.MainRiverChannel.Length);
        var distance = new float[map.Height.Length];
        int source = solution.Order[0];
        float longest = -1;
        for (int o = 0; o < solution.Count; o++)
        {
            int cell = solution.Order[o], receiver = map.DrainageReceiver[cell];
            if (receiver >= 0)
            {
                int dx = cell % map.Size - receiver % map.Size;
                int dz = cell / map.Size - receiver / map.Size;
                distance[cell] = distance[receiver] + map.CellSize * (dx != 0 && dz != 0 ? 1.41421356f : 1);
            }
            if (distance[cell] > longest) { longest = distance[cell]; source = cell; }
        }
        var seen = new bool[map.Height.Length];
        for (int cell = source; cell >= 0 && !seen[cell]; cell = map.DrainageReceiver[cell])
        {
            seen[cell] = true;
            map.MainRiverChannel[cell] = true;
        }
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
            bool main = map.MainRiverChannel[i];
            float depth = main ? settings.MainRiverDepth : Math.Min(4, .55f + (float)Math.Log(1 + discharge, 2) * .75f);
            float bed = solution.Filled[i] - depth;
            int radius = main
                ? Math.Max(2, (int)Math.Ceiling(settings.MainRiverWidth / Math.Max(1, map.CellSize) * .5f))
                : Math.Max(1, Math.Min(3, (int)Math.Round((.75 + Math.Log(1 + discharge, 2) * .35) * settings.RiverWidthMultiplier)));
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

    private static void CarveLagoons(SurfaceMap map, WorldSettings settings)
    {
        int n = map.Size;
        int radius = Math.Clamp(n / 32, 3, 7);
        var candidates = Enumerable.Range(0, map.Height.Length)
            .Where(i => i % n > radius * 2 && i % n < n - radius * 2 && i / n > radius * 2 && i / n < n - radius * 2)
            .Where(i => !map.MainRiverChannel[i] && map.Slope[i] < .22f)
            .OrderBy(i => map.Height[i] + map.Slope[i] * 80)
            .ToArray();
        var chosen = new List<int>();
        foreach (int center in candidates)
        {
            int cx = center % n, cz = center / n;
            if (chosen.Any(i => { int dx = i % n - cx, dz = i / n - cz; return dx * dx + dz * dz < radius * radius * 25; })) continue;
            chosen.Add(center);
            float rim = float.PositiveInfinity;
            for (int dz = -radius - 1; dz <= radius + 1; dz++) for (int dx = -radius - 1; dx <= radius + 1; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d >= radius && d <= radius + 1.5f) rim = Math.Min(rim, map.Height[map.Index(cx + dx, cz + dz)]);
            }
            for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
            {
                float normalized = MathF.Sqrt(dx * dx + dz * dz) / radius;
                if (normalized > 1) continue;
                int cell = map.Index(cx + dx, cz + dz);
                float bowl = settings.LagoonDepth * (1 - normalized * normalized);
                map.Height[cell] = Math.Min(map.Height[cell], rim - bowl - .35f);
                map.LagoonBasin[cell] = true;
                map.LagoonSurface[cell] = rim - .35f;
            }
            if (chosen.Count >= settings.LagoonCount) break;
        }
    }

    private static void FillClosedDepressions(SurfaceMap map, DrainageSolution solution, bool preserveLagoons)
    {
        for (int i = 0; i < map.Height.Length; i++)
        {
            if (preserveLagoons && map.LagoonBasin[i]) continue;
            float deposit = solution.Filled[i] - map.Height[i];
            if (deposit <= .001f) continue;
            map.Height[i] = solution.Filled[i];
            map.Sediment[i] += deposit;
        }
    }

    private static void RebalanceHeightMass(SurfaceMap map, double targetMass)
    {
        double currentMass = map.Height.Sum(x => (double)x);
        float correction = (float)((targetMass - currentMass) / Math.Max(1, map.Height.Length));
        if (Math.Abs(correction) < 1e-7f) return;
        for (int i = 0; i < map.Height.Length; i++) map.Height[i] += correction;
    }

    private static void AssignWater(SurfaceMap map, WorldSettings settings, DrainageSolution solution)
    {
        int n = map.Size;
        Array.Clear(map.WaterDepth, 0, map.WaterDepth.Length);
        var channelSurface = new float[map.Height.Length];
        for (int o = 0; o < solution.Count; o++)
        {
            int i = solution.Order[o];
            if (!map.RiverChannel[i]) continue;
            float minimumDepth = map.MainRiverChannel[i] ? settings.MainRiverDepth : .18f;
            float surface = map.Height[i] + minimumDepth;
            int receiver = map.DrainageReceiver[i];
            if (receiver >= 0 && map.RiverChannel[receiver])
                surface = Math.Max(surface, channelSurface[receiver]);
            channelSurface[i] = surface;
        }
        if (settings.WaterMode == WaterMode.RiversAndLakes)
        {
            for (int i = 0; i < map.Height.Length; i++)
            {
                float lake = Math.Min(settings.LagoonDepth, map.LagoonSurface[i] - map.Height[i]);
                if (map.LagoonBasin[i] && lake > .25f) map.WaterDepth[i] = lake;
            }
        }
        for (int o = 0; o < solution.Count; o++)
        {
            int i = solution.Order[o];
            if (!map.RiverChannel[i]) continue;
            int cx = i % n, cz = i / n;
            float discharge = map.Flow[i] / Math.Max(1, settings.RiverThreshold);
            bool main = map.MainRiverChannel[i];
            int radius = main
                ? Math.Max(2, (int)Math.Ceiling(settings.MainRiverWidth / Math.Max(1, map.CellSize) * .5f))
                : Math.Max(1, Math.Min(3, (int)Math.Round((.75 + Math.Log(1 + discharge, 2) * .35) * settings.RiverWidthMultiplier)));
            float surface = channelSurface[i];
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
            map.WaterDepth[i] = surface - map.Height[i];
        }
        // Bank stamping can overlap a downstream core. Restore the exact
        // priority-flood surface on every channel cell so water never rises.
        for (int i = 0; i < map.Height.Length; i++)
            if (map.RiverChannel[i])
            {
                map.WaterDepth[i] = channelSurface[i] - map.Height[i];
            }
            else if (map.LagoonBasin[i])
            {
                map.WaterDepth[i] = Math.Max(0, Math.Min(settings.LagoonDepth, map.LagoonSurface[i] - map.Height[i]));
            }
            else map.WaterDepth[i] = 0;
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
