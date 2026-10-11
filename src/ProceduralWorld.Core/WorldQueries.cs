namespace ProceduralWorld.Core;

public interface IWorldQuery
{
    float HeightAt(int x, int z);
    Biome BiomeAt(int x, int z);
    bool IsWater(int x, int z);
    bool IsCaveSolid(int x, int y, int z);
}

public sealed class GeneratedWorldQuery : IWorldQuery
{
    private readonly GeneratedWorld _world;
    public GeneratedWorldQuery(GeneratedWorld world) => _world = world;
    public float HeightAt(int x, int z) => _world.Surface.HeightAt(x, z);
    public Biome BiomeAt(int x, int z) => _world.Surface.Biomes[_world.Surface.Index(x, z)];
    public bool IsWater(int x, int z) => _world.Surface.WaterDepth[_world.Surface.Index(x, z)] > 0;
    public bool IsCaveSolid(int x, int y, int z) => _world.Caves.IsSolid(x, y, z);
}

public sealed class SurfaceChunk
{
    public SurfaceChunk(int originX, int originZ, int size)
    {
        OriginX = originX; OriginZ = originZ; Size = size; int count = size * size;
        Height = new float[count]; Slope = new float[count]; Flow = new float[count]; WaterDepth = new float[count];
        Biomes = new Biome[count]; Geology = new GeologicalIdentity[count];
    }
    public int OriginX { get; }
    public int OriginZ { get; }
    public int Size { get; }
    public float[] Height { get; }
    public float[] Slope { get; }
    public float[] Flow { get; }
    public Biome[] Biomes { get; }
    public GeologicalIdentity[] Geology { get; }
    public float[] WaterDepth { get; }
}

public static class ChunkExtractor
{
    public static SurfaceChunk Extract(SurfaceMap map, int originX, int originZ, int size)
    {
        if (size <= 0 || originX < 0 || originZ < 0 || originX + size > map.Size || originZ + size > map.Size)
            throw new ArgumentOutOfRangeException(nameof(size));
        var chunk = new SurfaceChunk(originX, originZ, size);
        for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
        {
            int source = map.Index(originX + x, originZ + z), target = z * size + x;
            chunk.Height[target] = map.Height[source]; chunk.Slope[target] = map.Slope[source]; chunk.Flow[target] = map.Flow[source];
            chunk.Biomes[target] = map.Biomes[source]; chunk.Geology[target] = map.Geology[source]; chunk.WaterDepth[target] = map.WaterDepth[source];
        }
        return chunk;
    }
}

