namespace ProceduralWorld.Core;

public sealed class WorldGenerator
{
    private readonly long _seed; private readonly WorldSettings _settings;
    public WorldGenerator(long seed, WorldSettings? settings = null) { _seed = seed; _settings = settings ?? new WorldSettings(); }
    public GeneratedWorld Generate()
    {
        SurfaceMap surface = new TerrainGenerator(_seed, _settings).Generate();
        CaveVolume caves = new CaveGenerator(_seed).Generate(surface, _settings);
        foreach (CaveEntrance entrance in caves.Entrances)
        {
            int radius = Math.Max(1, entrance.Radius);
            for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
            {
                int x = entrance.X + dx, z = entrance.Z + dz;
                if ((uint)x >= (uint)surface.Size || (uint)z >= (uint)surface.Size || dx * dx + dz * dz > radius * radius) continue;
                surface.CaveOpening[surface.Index(x, z)] = true;
            }
        }
        LabyrinthPlan labyrinth = new LabyrinthGenerator(_seed).Generate(_settings.LabyrinthSize);
        return new(_seed, surface, caves, labyrinth, _settings.GenerationVersion);
    }
}
