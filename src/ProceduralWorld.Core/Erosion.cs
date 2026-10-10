namespace ProceduralWorld.Core;

internal static class Erosion
{
    // Erosao termica deterministica: material de encostas instaveis desce para os vizinhos.
    public static void Apply(SurfaceMap map, int iterations = 12, float talus = 0.42f, float strength = 0.16f)
    {
        int n = map.Size;
        var delta = new float[map.Height.Length];
        for (int pass = 0; pass < iterations; pass++)
        {
            Array.Clear(delta, 0, delta.Length);
            for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
            {
                int i = map.Index(x, z), lowest = i; float lowestHeight = map.Height[i];
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue; int candidate = map.Index(x + dx, z + dz);
                    if (map.Height[candidate] < lowestHeight) { lowest = candidate; lowestHeight = map.Height[candidate]; }
                }
                float difference = map.Height[i] - lowestHeight;
                float threshold = talus * map.CellSize;
                if (difference <= threshold) continue;
                float moved = (difference - threshold) * strength;
                delta[i] -= moved; delta[lowest] += moved;
            }
            for (int i = 0; i < map.Height.Length; i++) map.Height[i] += delta[i];
        }
    }
}

