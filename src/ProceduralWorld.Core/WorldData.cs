namespace ProceduralWorld.Core;

public enum Biome : byte
{
    River, Lake, Beach, Desert, Savanna, Grassland, Forest, Jungle, Swamp, Taiga, Tundra, Rock, Snow
}

public enum WaterMode { None, RiversOnly, RiversAndLakes }

public sealed class WorldSettings
{
    public int Size { get; set; } = 129;
    public float CellSize { get; set; } = 8;
    public float BaseElevation { get; set; } = 80;
    public float MountainHeight { get; set; } = 720;
    public float ReliefStrength { get; set; } = 1;
    public float RiverThreshold { get; set; } = 280;
    public int GuaranteedCaveEntrances { get; set; } = 4;
    public int CaveDepthCells { get; set; } = 48;
    public int LabyrinthSize { get; set; } = 41;
    public WaterMode WaterMode { get; set; } = WaterMode.RiversOnly;
    public void Validate()
    {
        if (Size < 17) throw new ArgumentOutOfRangeException(nameof(Size));
        if (CellSize <= 0 || MountainHeight < 0 || CaveDepthCells < 8) throw new ArgumentOutOfRangeException("World dimensions must be positive.");
    }
}

public sealed class SurfaceMap
{
    public SurfaceMap(int size, float cellSize)
    {
        Size = size; CellSize = cellSize; int count = checked(size * size);
        Height = new float[count]; Slope = new float[count]; Temperature = new float[count]; Moisture = new float[count];
        Flow = new float[count]; WaterDepth = new float[count]; Biomes = new Biome[count];
    }
    public int Size { get; }
    public float CellSize { get; }
    public float[] Height { get; }
    public float[] Slope { get; }
    public float[] Temperature { get; }
    public float[] Moisture { get; }
    public float[] Flow { get; }
    public float[] WaterDepth { get; }
    public Biome[] Biomes { get; }
    public int Index(int x, int z) => checked(z * Size + x);
    public float HeightAt(int x, int z) => Height[Index(x, z)];
}

public sealed class CaveEntrance
{
    public CaveEntrance(int x, int z, int surfaceY, int caveY) { X = x; Z = z; SurfaceY = surfaceY; CaveY = caveY; }
    public int X { get; }
    public int Z { get; }
    public int SurfaceY { get; }
    public int CaveY { get; }
}

public sealed class CaveVolume
{
    private readonly bool[] _solid;
    public CaveVolume(int width, int height, int depth)
    { Width = width; Height = height; Depth = depth; _solid = Enumerable.Repeat(true, checked(width * height * depth)).ToArray(); }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public List<CaveEntrance> Entrances { get; } = new();
    public bool IsSolid(int x, int y, int z) => _solid[(y * Depth + z) * Width + x];
    public void SetSolid(int x, int y, int z, bool solid) => _solid[(y * Depth + z) * Width + x] = solid;
    public long AirCellCount => _solid.LongCount(x => !x);
}

public enum LabyrinthCell : byte { Wall, Corridor, Room, Arena, Niche }

public sealed class LabyrinthPlan
{
    private readonly LabyrinthCell[] _cells;
    public LabyrinthPlan(int width, int height) { Width = width; Height = height; _cells = new LabyrinthCell[width * height]; }
    public int Width { get; }
    public int Height { get; }
    public LabyrinthCell this[int x, int y] { get => _cells[y * Width + x]; set => _cells[y * Width + x] = value; }
    public int WalkableCount => _cells.Count(x => x != LabyrinthCell.Wall);
}

public sealed class GeneratedWorld
{
    public GeneratedWorld(long seed, SurfaceMap surface, CaveVolume caves, LabyrinthPlan labyrinth)
    { Seed = seed; Surface = surface; Caves = caves; Labyrinth = labyrinth; }
    public long Seed { get; }
    public SurfaceMap Surface { get; }
    public CaveVolume Caves { get; }
    public LabyrinthPlan Labyrinth { get; }
    public ulong Fingerprint()
    {
        ulong hash = PositionalHash.Mix((ulong)Seed);
        for (int i = 0; i < Surface.Height.Length; i += Math.Max(1, Surface.Height.Length / 1024))
            hash = PositionalHash.Mix(hash ^ (ulong)(uint)BitConverter.SingleToInt32Bits(Surface.Height[i]) ^ ((ulong)Surface.Biomes[i] << 56));
        hash = PositionalHash.Mix(hash ^ (ulong)Caves.AirCellCount ^ ((ulong)Labyrinth.WalkableCount << 32));
        return hash;
    }
}

