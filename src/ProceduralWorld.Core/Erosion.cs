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

    public static void RelaxSlopes(SurfaceMap map, float maximumGradient, int passes)
    {
        int n = map.Size;
        var delta = new float[map.Height.Length];
        int scaledPasses = Math.Max(4, Math.Min(192, (int)Math.Round(passes * 8f / map.CellSize)));
        for (int pass = 0; pass < scaledPasses; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            double movedTotal = 0;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                int i = map.Index(x, z);
                if (x + 1 < n) Transfer(i, map.Index(x + 1, z), 1f);
                if (z + 1 < n) Transfer(i, map.Index(x, z + 1), 1f);
                if (x + 1 < n && z + 1 < n) Transfer(i, map.Index(x + 1, z + 1), 1.41421356f);
                if (x > 0 && z + 1 < n) Transfer(i, map.Index(x - 1, z + 1), 1.41421356f);
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
            if (movedTotal < map.Height.Length * .00005) break;

            void Transfer(int a, int b, float distance)
            {
                float difference = map.Height[a] - map.Height[b];
                if (Math.Abs(difference) < .0001f) return;
                int high = difference > 0 ? a : b, low = difference > 0 ? b : a;
                float hardness = (map.RockHardness[high] + map.RockHardness[low]) * .5f;
                float localLimit = maximumGradient * (.67f + hardness * .23f) * map.CellSize * distance;
                float excess = Math.Abs(difference) - localLimit;
                if (excess <= 0) return;
                float transfer = Math.Min(map.CellSize * .18f, excess * (.16f + (1 - hardness) * .12f));
                delta[high] -= transfer;
                delta[low] += transfer;
                map.Sediment[low] += transfer;
                movedTotal += transfer;
            }
        }
    }

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
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int count = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int j = map.Index(x + dx, z + dz);
                    neighbours[count] = map.Height[j]; indices[count] = j; count++;
                }
                Array.Sort(neighbours);
                float median = (neighbours[3] + neighbours[4]) * .5f;
                int i = map.Index(x, z);
                float excess = map.Height[i] - median - allowance * (.82f + map.RockHardness[i] * .22f);
                if (excess <= 0) continue;
                float moved = excess * .45f;
                delta[i] -= moved;
                float share = moved / 8f;
                for (int k = 0; k < 8; k++) { delta[indices[k]] += share; map.Sediment[indices[k]] += share; }
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
        }
    }
}
