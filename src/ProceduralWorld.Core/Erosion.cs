namespace ProceduralWorld.Core;

internal static class Erosion
{
    public static void ApplyThermal(SurfaceMap map, int iterations, float strength, TerrainStyle style)
    {
        int n = map.Size;
        var delta = new float[map.Height.Length];
        var targets = new int[8];
        var drops = new float[8];
        float baseTalus = style switch
        {
            TerrainStyle.Alpine => .52f,
            TerrainStyle.Canyonlands => .48f,
            TerrainStyle.RollingPlains => .18f,
            TerrainStyle.Glacial => .30f,
            TerrainStyle.Volcanic => .46f,
            _ => .34f
        };
        int scaledIterations = Math.Max(1, Math.Min(160, (int)Math.Round(iterations * 8f / map.CellSize)));
        for (int pass = 0; pass < scaledIterations; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int i = map.Index(x, z);
                float hardness = map.RockHardness[i];
                float talus = baseTalus * (.76f + hardness * .42f);
                float totalDrop = 0;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int j = map.Index(x + dx, z + dz);
                    float distance = dx != 0 && dz != 0 ? 1.41421356f : 1;
                    float pairTalus = talus * (.82f + map.RockHardness[j] * .18f);
                    float drop = (map.Height[i] - map.Height[j]) / distance - pairTalus * map.CellSize;
                    if (drop > 0) { targets[count] = j; drops[count++] = drop; totalDrop += drop; }
                }
                if (count == 0) continue;
                float mobility = .35f + (1 - hardness) * .65f;
                float moved = Math.Min(map.CellSize * .32f, totalDrop * strength * mobility / count);
                delta[i] -= moved;
                for (int k = 0; k < count; k++)
                {
                    float share = moved * drops[k] / totalDrop;
                    delta[targets[k]] += share;
                    map.Sediment[targets[k]] += share;
                }
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
        }
    }

    /// <summary>
    /// Detachment-limited stream power incision (implicit Braun-Willett): dh/dt = -K A^0.5 S, solved upstream from outlets.
    /// Large catchments cut valleys, ridges with tiny catchments barely change, hard rock resists.
    /// </summary>
    public static void ApplyStreamPower(SurfaceMap map, int iterations, float strength)
    {
        if (iterations <= 0 || strength <= 0) return;
        float[] bias = Hydrology.MeanderBias(map);
        int n = map.Size;
        double mass = map.Height.Sum(x => (double)x);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var solution = Hydrology.Solve(map, bias);
            for (int o = 0; o < solution.Count; o++)
            {
                int cell = solution.Order[o], receiver = map.DrainageReceiver[cell];
                if (receiver < 0) continue;
                float floor = map.Height[receiver];
                if (map.Height[cell] <= floor) continue;
                bool diagonal = cell % n != receiver % n && cell / n != receiver / n;
                float f = strength * (1.15f - map.RockHardness[cell]) * (float)Math.Sqrt(map.Flow[cell]) / (diagonal ? 1.41421356f : 1f);
                float eroded = (map.Height[cell] + f * floor) / (1 + f);
                map.Sediment[cell] += map.Height[cell] - eroded;
                map.Height[cell] = eroded;
            }
        }
        // Eroded material settles as a uniform alluvial blanket so the pipeline conserves mass.
        float correction = (float)((mass - map.Height.Sum(x => (double)x)) / map.Height.Length);
        for (int i = 0; i < map.Height.Length; i++) map.Height[i] += correction;
    }

    public static void RelaxSlopes(SurfaceMap map, float maximumGradient, int passes)
    {
        int n = map.Size;
        var delta = new float[map.Height.Length];
        // Position-only variation is invariant across every edge and relaxation pass.
        // Cache the same float expression once; traversal and transfer arithmetic remain unchanged.
        var variation = new float[map.Height.Length];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            variation[z * n + x] = .88f + .12f * (1 + (float)(Math.Sin(x * .37 + z * .23) * Math.Cos(z * .31 - x * .17)));
        var edges = new SlopeEdge[2 * n * (n - 1) + 2 * (n - 1) * (n - 1)]; int edgeCount = 0;
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int a = z * n + x;
            if (x + 1 < n) Add(a, a + 1, 1f);
            if (z + 1 < n) Add(a, a + n, 1f);
            if (x + 1 < n && z + 1 < n) Add(a, a + n + 1, 1.41421356f);
            if (x > 0 && z + 1 < n) Add(a, a + n - 1, 1.41421356f);
        }
        void Add(int a, int b, float distance)
        {
            float hardness = (map.RockHardness[a] + map.RockHardness[b]) * .5f;
            float limit = maximumGradient * (.67f + hardness * .23f) * map.CellSize * distance;
            limit *= variation[a];
            edges[edgeCount++] = new SlopeEdge { A = a, B = b, Limit = limit, Mobility = .16f + (1 - hardness) * .12f };
        }
        int scaledPasses = Math.Max(4, Math.Min(192, (int)Math.Round(passes * 8f / map.CellSize)));
        var activeEdges = new int[edgeCount]; int activeCount = edgeCount; bool sparse = false;
        var changedCells = new bool[map.Height.Length];
        for (int pass = 0; pass < scaledPasses; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            Array.Clear(changedCells, 0, changedCells.Length); int changedCount = 0;
            double movedTotal = 0;
            for (int e = 0; e < activeCount; e++)
            {
                var edge = edges[sparse ? activeEdges[e] : e]; int a = edge.A, b = edge.B;
                float difference = map.Height[a] - map.Height[b];
                if (Math.Abs(difference) < .0001f) continue;
                int high = difference > 0 ? a : b, low = difference > 0 ? b : a;
                float excess = Math.Abs(difference) - edge.Limit;
                if (excess <= 0) continue;
                float transfer = Math.Min(map.CellSize * .18f, excess * edge.Mobility);
                delta[high] -= transfer;
                delta[low] += transfer;
                map.Sediment[low] += transfer;
                movedTotal += transfer;
                if (!changedCells[a]) { changedCells[a] = true; changedCount++; }
                if (!changedCells[b]) { changedCells[b] = true; changedCount++; }
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
            if (movedTotal < map.Height.Length * .00005) break;
            // An unchanged endpoint pair cannot start transferring on the next pass. Preserve edge order.
            sparse = changedCount < map.Height.Length * .6;
            activeCount = 0;
            if (sparse) for (int e = 0; e < edgeCount; e++) { var edge = edges[e]; if (changedCells[edge.A] || changedCells[edge.B]) activeEdges[activeCount++] = e; }
            else activeCount = edgeCount;
        }
    }
    private struct SlopeEdge { public int A, B; public float Limit, Mobility; }

    public static void LimitExtremeSteps(SurfaceMap map, float maximumRise, int passes)
    {
        int n = map.Size;
        for (int pass = 0; pass < passes; pass++)
        {
            bool changed = false;
            for (int z = 0; z < n; z++)
            {
                bool reverse = ((z + pass) & 1) != 0;
                for (int q = 0; q < n - 1; q++)
                {
                    int x = reverse ? n - 2 - q : q;
                    changed |= Adjust(map.Height, map.Index(x, z), map.Index(x + 1, z), maximumRise);
                }
            }
            for (int x = 0; x < n; x++)
            {
                bool reverse = ((x + pass) & 1) == 0;
                for (int q = 0; q < n - 1; q++)
                {
                    int z = reverse ? n - 2 - q : q;
                    changed |= Adjust(map.Height, map.Index(x, z), map.Index(x, z + 1), maximumRise);
                }
            }
            if (!changed) break;
        }

        static bool Adjust(float[] height, int a, int b, float maximumRise)
        {
            float difference = height[a] - height[b];
            float excess = Math.Abs(difference) - maximumRise;
            if (excess <= .0001f) return false;
            float transfer = excess * .5f;
            if (difference > 0) { height[a] -= transfer; height[b] += transfer; }
            else { height[a] += transfer; height[b] -= transfer; }
            return true;
        }
    }

    public static void SuppressIsolatedApexes(SurfaceMap map, float maximumExcessGradient, int passes)
    {
        int n = map.Size;
        float allowance = maximumExcessGradient * map.CellSize;
        var delta = new float[map.Height.Length];
        var neighbours = new float[8];
        var indices = new int[8];
        bool[]? active = null; var nextActive = new bool[map.Height.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            Array.Clear(nextActive, 0, nextActive.Length); bool changed = false;
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int i = map.Index(x, z); if (active != null && !active[i]) continue;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int j = map.Index(x + dx, z + dz);
                    neighbours[count] = map.Height[j]; indices[count] = j; count++;
                }
                float limit = allowance * (.82f + map.RockHardness[i] * .22f);
                float minimum = neighbours[0]; for (int k = 1; k < 8; k++) minimum = Math.Min(minimum, neighbours[k]);
                // Median >= minimum. Use the original subtraction order so the rejection is also float-safe.
                if (map.Height[i] - minimum - limit <= 0) continue;
                // Exactly eight finite heights: avoid millions of generic Array.Sort calls on Mono.
                for (int k = 1; k < 8; k++)
                {
                    float value = neighbours[k]; int j = k - 1;
                    while (j >= 0 && neighbours[j] > value) { neighbours[j + 1] = neighbours[j]; j--; }
                    neighbours[j + 1] = value;
                }
                float median = (neighbours[3] + neighbours[4]) * .5f;
                float excess = map.Height[i] - median - limit;
                if (excess <= 0) continue;
                float moved = excess * .45f;
                changed = true;
                // A transfer changes this cell and its eight neighbours. Only their neighbours can
                // acquire a new excess next pass; preserve the original traversal for every active cell.
                for (int az = Math.Max(1, z - 2); az <= Math.Min(n - 2, z + 2); az++)
                for (int ax = Math.Max(1, x - 2); ax <= Math.Min(n - 2, x + 2); ax++) nextActive[az * n + ax] = true;
                delta[i] -= moved;
                float share = moved / 8f;
                for (int k = 0; k < 8; k++) { delta[indices[k]] += share; map.Sediment[indices[k]] += share; }
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
            if (!changed) break;
            var previous = active; active = nextActive; nextActive = previous ?? new bool[map.Height.Length];
        }
    }
}
