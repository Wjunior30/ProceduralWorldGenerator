namespace ProceduralWorld.Core;

// Plant cover 0..1 per cell for engines to scatter trees and bushes: dense in forests and jungles, sparse on savanna,
// thinned on steep or dry ground, opened by meadows (low-frequency clearings) and cleared on water, roads and sites.
public sealed partial class TerrainGenerator
{
    private void ComputeVegetation(SurfaceMap map)
    {
        int n = map.Size; var clearings = new FractalNoise(_seed ^ 0x5EED_F0E5u, 3, 1 / 900.0);
        double open = _settings.ForestClearings, density = _settings.VegetationDensity;
        var cleared = new bool[map.Height.Length];
        foreach (var site in map.Sites)
            for (int dz = -site.Radius - 2; dz <= site.Radius + 2; dz++) for (int dx = -site.Radius - 2; dx <= site.Radius + 2; dx++)
            {
                int x = site.X + dx, z = site.Z + dz;
                if (x >= 0 && z >= 0 && x < n && z < n && dx * dx + dz * dz <= (site.Radius + 2) * (site.Radius + 2)) cleared[map.Index(x, z)] = true;
            }
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                int i = map.Index(x, z);
                if (cleared[i] || map.Ocean[i] || map.Road[i] || map.RiverChannel[i] || map.WaterDepth[i] > 0) { map.Vegetation[i] = 0; continue; }
                var biome = map.Biomes[i];
                double cover = biome switch
                {
                    Biome.Jungle => 1, Biome.Forest => .9, Biome.Taiga => .75, Biome.Swamp => .5, Biome.Savanna => .22,
                    Biome.Grassland => .12, Biome.Tundra => .06, Biome.Beach => .03, Biome.Desert => .02, _ => 0,
                };
                if (biome is Biome.Forest or Biome.Jungle or Biome.Taiga)
                {
                    // Meadows: the lowest share 'open' of a smooth noise field becomes grass with a soft edge.
                    double field = clearings.Sample(x * map.CellSize, z * map.CellSize), cut = (open - .5) * .9;
                    cover *= .15 + .85 * Smooth(cut - .08, cut + .08, field);
                }
                cover *= (.6 + .4 * map.Moisture[i]) * (1 - Smooth(.35, .7, map.Slope[i])) * density;
                map.Vegetation[i] = (float)Math.Max(0, Math.Min(1, cover));
            }
        });
    }
}
