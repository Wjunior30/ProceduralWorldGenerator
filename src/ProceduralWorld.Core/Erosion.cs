namespace ProceduralWorld.Core;

internal static class Erosion
{
    public static void ApplyThermal(SurfaceMap map, int iterations, float strength, TerrainStyle style)
    {
        int n = map.Size; var delta = new float[map.Height.Length]; var targets = new int[8]; var drops = new float[8];
        float talus = style switch { TerrainStyle.Alpine => .62f, TerrainStyle.Canyonlands => .56f, TerrainStyle.RollingPlains => .24f, TerrainStyle.Glacial => .34f, _ => .42f };
        for (int pass = 0; pass < iterations; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int i = map.Index(x, z); float totalDrop = 0; int count = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue; int j = map.Index(x + dx, z + dz); float distance = dx != 0 && dz != 0 ? 1.4142f : 1;
                    float drop = (map.Height[i] - map.Height[j]) / distance - talus * map.CellSize;
                    if (drop > 0) { targets[count] = j; drops[count++] = drop; totalDrop += drop; }
                }
                if (count == 0) continue; float moved = Math.Min(map.CellSize * .8f, totalDrop * strength / count);
                delta[i] -= moved; map.Sediment[i] += moved;
                for (int k = 0; k < count; k++) { float share = moved * drops[k] / totalDrop; delta[targets[k]] += share; map.Sediment[targets[k]] += share * .65f; }
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
        }
    }

    public static void LimitExtremeSlopes(SurfaceMap map, float maximumGradient, int passes)
    {
        int n = map.Size; float limit = maximumGradient * map.CellSize; var next = new float[map.Height.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Copy(map.Height, next, next.Length);
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int i = map.Index(x, z); float low = float.MaxValue, high = float.MinValue;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { float h = map.Height[map.Index(x + dx, z + dz)]; low = Math.Min(low, h); high = Math.Max(high, h); }
                if (map.Height[i] > low + limit) next[i] = Math.Max(low + limit, map.Height[i] * .72f + Average(map, x, z) * .28f);
                else if (map.Height[i] < high - limit) next[i] = Math.Min(high - limit, map.Height[i] * .84f + Average(map, x, z) * .16f);
            }
            Array.Copy(next, map.Height, next.Length);
        }
    }

    public static void ApplyFluvialFinishing(SurfaceMap map, WorldSettings settings)
    {
        int n = map.Size; var delta = new float[map.Height.Length];
        for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
        {
            int i = map.Index(x, z), receiver = map.DrainageReceiver[i]; if (receiver < 0 || map.Flow[i] < settings.RiverThreshold * .12f) continue;
            float gradient = Math.Max(0, (map.Height[i] - map.Height[receiver]) / map.CellSize);
            float power = (float)Math.Log(1 + map.Flow[i] / Math.Max(1, settings.RiverThreshold)) * gradient * .18f;
            float eroded = Math.Min(1.2f, power); delta[i] -= eroded; delta[receiver] += eroded * .28f; map.Sediment[receiver] += eroded * .72f;
        }
        for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
    }
    private static float Average(SurfaceMap map, int x, int z) { float sum = 0; int count = 0; for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { if (dx == 0 && dz == 0) continue; sum += map.Height[map.Index(x + dx, z + dz)]; count++; } return sum / count; }
}
