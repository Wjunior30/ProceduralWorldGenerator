namespace ProceduralWorld.Core.Voxels;

public enum Block : byte { Air, Bedrock, Stone, Dirt, Grass, Sand, Sandstone, Gravel, Clay, Snow, Ice, Water, Lava, Wood, Leaves, Cactus }
public enum VoxelBiome : byte { River, Lake, Desert, Savanna, Grassland, Forest, Jungle, Swamp, Taiga, Tundra, Rock, Snow }
public readonly record struct VoxelColumn(int Height, VoxelBiome Biome, double River, double Temperature, double Moisture);

public sealed class VoxelChunk
{
    public const int Size = 16, Height = 256;
    private readonly Block[] _blocks = new Block[Size * Size * Height];
    public VoxelChunk(int x, int z) { X = x; Z = z; }
    public int X { get; }
    public int Z { get; }
    public VoxelBiome[] Biomes { get; } = new VoxelBiome[Size * Size];
    public int[] SurfaceHeight { get; } = new int[Size * Size];
    public Block Get(int x, int y, int z) => InBounds(x, y, z) ? _blocks[(y * Size + z) * Size + x] : Block.Air;
    public void Set(int x, int y, int z, Block value) { if (InBounds(x, y, z)) _blocks[(y * Size + z) * Size + x] = value; }
    private static bool InBounds(int x, int y, int z) => (uint)x < Size && (uint)z < Size && (uint)y < Height;
}

public sealed class VoxelWorldGenerator
{
    private readonly long _seed;
    private readonly FractalNoise _continent, _ridge, _detail, _climate, _humidity, _cavesA, _cavesB;
    public VoxelWorldGenerator(long seed)
    {
        _seed = seed; ulong s = PositionalHash.Mix((ulong)seed);
        _continent = new(s ^ 1, 5, .001); _ridge = new(s ^ 2, 5, .003); _detail = new(s ^ 3, 3, .025);
        _climate = new(s ^ 4, 4, .0012); _humidity = new(s ^ 5, 4, .0014); _cavesA = new(s ^ 6, 2, .018); _cavesB = new(s ^ 7, 2, .022);
    }
    public VoxelColumn SampleColumn(int x, int z)
    {
        double land = _continent.Sample(x, z), ridge = 1 - Math.Abs(_ridge.Sample(x, z));
        double h = 62 + land * 34 + Math.Pow(Math.Max(0, ridge), 2.2) * Math.Max(0, land + .25) * 100 + _detail.Sample(x, z) * 8;
        double riverNoise = Math.Abs(_continent.Sample(x + 8107, z - 3001)); double river = Smooth(.055, 0, riverNoise);
        if (river > 0) h = Math.Min(h, 61 - river * 3);
        double temp = _climate.Sample(x, z) - Math.Max(0, h - 110) / 100, humid = _humidity.Sample(x, z);
        int height = Math.Max(8, Math.Min(235, (int)Math.Round(h)));
        VoxelBiome biome = river > .6 ? VoxelBiome.River : height > 155 ? VoxelBiome.Snow : height > 125 ? VoxelBiome.Rock : temp > .35 && humid < -.1 ? VoxelBiome.Desert : temp > .35 && humid > .3 ? VoxelBiome.Jungle : temp < -.35 ? VoxelBiome.Tundra : humid > .18 ? VoxelBiome.Forest : VoxelBiome.Grassland;
        return new(height, biome, river, temp, humid);
    }
    public VoxelChunk GenerateChunk(int cx, int cz)
    {
        var chunk = new VoxelChunk(cx, cz);
        for (int z = 0; z < VoxelChunk.Size; z++) for (int x = 0; x < VoxelChunk.Size; x++)
        {
            int wx = cx * VoxelChunk.Size + x, wz = cz * VoxelChunk.Size + z; var col = SampleColumn(wx, wz); int i = z * VoxelChunk.Size + x;
            chunk.Biomes[i] = col.Biome; chunk.SurfaceHeight[i] = col.Height;
            Block top = col.Biome == VoxelBiome.Desert ? Block.Sand : col.Biome == VoxelBiome.Snow ? Block.Snow : col.Biome == VoxelBiome.Rock ? Block.Stone : Block.Grass;
            for (int y = 0; y <= col.Height; y++) chunk.Set(x, y, z, y == 0 ? Block.Bedrock : y == col.Height ? top : y > col.Height - 4 ? (top == Block.Sand ? Block.Sandstone : Block.Dirt) : Block.Stone);
            for (int y = 5; y < col.Height - 4; y++)
            {
                double cheese = _cavesA.Sample(wx, y * 1.7, wz), spaghetti = Math.Max(Math.Abs(_cavesA.Sample(wx + 97, y, wz)), Math.Abs(_cavesB.Sample(wx, y, wz - 53)));
                if (cheese > .47 || spaghetti < .055) chunk.Set(x, y, z, y < 10 ? Block.Lava : Block.Air);
            }
            for (int y = col.Height + 1; y <= 62; y++) chunk.Set(x, y, z, Block.Water);
        }
        return chunk;
    }
    private static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
}
