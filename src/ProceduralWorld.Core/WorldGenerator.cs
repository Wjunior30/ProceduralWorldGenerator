namespace ProceduralWorld.Core;

public sealed class WorldGenerator
{
    private readonly long _seed;
    private readonly WorldSettings _settings;
    public WorldGenerator(long seed, WorldSettings? settings = null) { _seed = seed; _settings = settings ?? new WorldSettings(); }
    public GeneratedWorld Generate()
    {
        SurfaceMap surface = new TerrainGenerator(_seed, _settings).Generate();
        CaveVolume caves = new CaveGenerator(_seed).Generate(surface, _settings);
        LabyrinthPlan labyrinth = new LabyrinthGenerator(_seed).Generate(_settings.LabyrinthSize);
        return new GeneratedWorld(_seed, surface, caves, labyrinth);
    }
}

