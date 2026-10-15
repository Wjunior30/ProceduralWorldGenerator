namespace ProceduralWorld.Core;

public enum Biome : byte { River, Lake, Beach, Desert, Savanna, Grassland, Forest, Jungle, Swamp, Taiga, Tundra, Rock, Snow }
public enum WaterMode { None, RiversOnly, RiversAndLakes }
public enum TerrainStyle { Natural, Alpine, Canyonlands, Karst, RollingPlains, Glacial, Volcanic }
public enum LandformKind : byte { Plain, Hill, Massif, Valley, Basin, Ridge, Cirque, Mesa, Escarpment, Sinkhole, DryValley, Floodplain, Terrace, Moraine, VolcanicCone, Caldera, LavaField }

public sealed class WorldSettings
{
    public int GenerationVersion { get; set; } = 6;
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
    public TerrainStyle TerrainStyle { get; set; } = TerrainStyle.Natural;
    public int ErosionIterations { get; set; } = 24;
    public float ErosionStrength { get; set; } = .18f;
    public float RiverWidthMultiplier { get; set; } = 1;
    public float MainRiverWidth { get; set; } = 42;
    public float MainRiverDepth { get; set; } = 5.5f;
    public float TributaryThresholdMultiplier { get; set; } = 2.25f;
    public int LagoonCount { get; set; } = 2;
    public float LagoonDepth { get; set; } = 6;
    public float CaveDensity { get; set; } = 1;
    public float BiomeRegionScale { get; set; } = 1;
    public Biome[] EnabledBiomes { get; set; } = (Biome[])Enum.GetValues(typeof(Biome));
    public float WindX { get; set; } = 1;
    public float WindZ { get; set; } = 0;
    public void Validate()
    {
        if (GenerationVersion < 1 || Size < 17) throw new ArgumentOutOfRangeException(nameof(Size));
        if (CellSize <= 0 || MountainHeight < 0 || CaveDepthCells < 8 || ReliefStrength < 0 || RiverWidthMultiplier <= 0 || MainRiverWidth <= 0 || MainRiverDepth <= 0 || TributaryThresholdMultiplier < 1 || LagoonCount < 0 || LagoonDepth <= 0 || CaveDensity <= 0 || BiomeRegionScale <= 0)
            throw new ArgumentOutOfRangeException("World dimensions and multipliers must be positive.");
        if (!float.IsFinite(WindX) || !float.IsFinite(WindZ) || Math.Abs(WindX) + Math.Abs(WindZ) < .001f)
            throw new ArgumentOutOfRangeException(nameof(WindX), "Wind must be a finite, non-zero 2D vector.");
        if (ErosionIterations < 0 || ErosionIterations > 200) throw new ArgumentOutOfRangeException(nameof(ErosionIterations));
        if (EnabledBiomes is null || EnabledBiomes.Length == 0 || !EnabledBiomes.Any(x => x is not Biome.River and not Biome.Lake))
            throw new ArgumentException("At least one terrestrial biome must be enabled.", nameof(EnabledBiomes));
        EnabledBiomes = EnabledBiomes.Distinct().ToArray();
    }
}

public sealed class SurfaceMap
{
    public SurfaceMap(int size, float cellSize)
    {
        Size = size; CellSize = cellSize; int count = checked(size * size);
        Height = new float[count]; Slope = new float[count]; Temperature = new float[count]; Moisture = new float[count];
        Flow = new float[count]; WaterDepth = new float[count]; LagoonSurface = new float[count]; Sediment = new float[count]; Ridge = new float[count]; Continentalness = new float[count]; RockHardness = new float[count]; Strata = new float[count];
        Curvature = new float[count]; SlopeSaturation = new float[count]; BasinId = new int[count]; StreamOrder = new byte[count]; CaveOpening = new bool[count]; Landforms = new LandformKind[count];
        DrainageReceiver = Enumerable.Repeat(-1, count).ToArray(); RiverChannel = new bool[count]; MainRiverChannel = new bool[count]; LagoonBasin = new bool[count]; Biomes = new Biome[count]; Geology = new GeologicalIdentity[count];
    }
    public int Size { get; }
    public float CellSize { get; }
    public float[] Height { get; }
    public float[] Slope { get; }
    public float[] Temperature { get; }
    public float[] Moisture { get; }
    public float[] Flow { get; }
    public float[] WaterDepth { get; }
    public float[] LagoonSurface { get; }
    public float[] Sediment { get; }
    public float[] Ridge { get; }
    public float[] Continentalness { get; }
    public float[] RockHardness { get; }
    public float[] Strata { get; }
    public float[] Curvature { get; }
    public float[] SlopeSaturation { get; }
    public int[] BasinId { get; }
    public byte[] StreamOrder { get; }
    public bool[] CaveOpening { get; }
    public LandformKind[] Landforms { get; }
    public double ErosionMassDelta { get; internal set; }
    public int[] DrainageReceiver { get; }
    public bool[] RiverChannel { get; }
    public bool[] MainRiverChannel { get; }
    public bool[] LagoonBasin { get; }
    public Biome[] Biomes { get; }
    public GeologicalIdentity[] Geology { get; }
    public int Index(int x, int z) => checked(z * Size + x);
    public float HeightAt(int x, int z) => Height[Index(x, z)];
}

public sealed class CaveEntrance
{
    public CaveEntrance(int x, int z, int surfaceY, int caveY, int facingX = 0, int facingZ = 1, int radius = 2)
    { X = x; Z = z; SurfaceY = surfaceY; CaveY = caveY; FacingX = facingX; FacingZ = facingZ; Radius = radius; }
    public int X { get; }
    public int Z { get; }
    public int SurfaceY { get; }
    public int CaveY { get; }
    public int FacingX { get; }
    public int FacingZ { get; }
    public int Radius { get; }
}

public sealed class CaveVolume
{
    private readonly bool[] _solid;
    public CaveVolume(int width, int height, int depth) { Width = width; Height = height; Depth = depth; _solid = Enumerable.Repeat(true, checked(width * height * depth)).ToArray(); }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public List<CaveEntrance> Entrances { get; } = new();
    public bool IsSolid(int x, int y, int z) => _solid[(y * Depth + z) * Width + x];
    public void SetSolid(int x, int y, int z, bool solid) => _solid[(y * Depth + z) * Width + x] = solid;
    public long AirCellCount => _solid.LongCount(x => !x);
    public int[] ComputeComponents()
    {
        var components = new int[_solid.Length];
        int nextComponent = 0;
        int[] dx = { 1, -1, 0, 0, 0, 0 }, dy = { 0, 0, 1, -1, 0, 0 }, dz = { 0, 0, 0, 0, 1, -1 };
        var queue = new Queue<int>();
        for (int start = 0; start < _solid.Length; start++)
        {
            if (_solid[start] || components[start] != 0) continue;
            int label = ++nextComponent; components[start] = label; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue(), x = id % Width, t = id / Width, z = t % Depth, y = t / Depth;
                for (int k = 0; k < 6; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k], nz = z + dz[k];
                    if ((uint)nx >= (uint)Width || (uint)ny >= (uint)Height || (uint)nz >= (uint)Depth) continue;
                    int candidate = (ny * Depth + nz) * Width + nx;
                    if (_solid[candidate] || components[candidate] != 0) continue;
                    components[candidate] = label; queue.Enqueue(candidate);
                }
            }
        }
        return components;
    }
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
    public GeneratedWorld(long seed, SurfaceMap surface, CaveVolume caves, LabyrinthPlan labyrinth, int generationVersion = 2)
    { Seed = seed; Surface = surface; Caves = caves; Labyrinth = labyrinth; GenerationVersion = generationVersion; }
    public long Seed { get; }
    public int GenerationVersion { get; }
    public SurfaceMap Surface { get; }
    public CaveVolume Caves { get; }
    public LabyrinthPlan Labyrinth { get; }
    public ulong Fingerprint()
    {
        ulong hash = PositionalHash.Mix((ulong)Seed ^ (ulong)GenerationVersion);
        for (int i = 0; i < Surface.Height.Length; i += Math.Max(1, Surface.Height.Length / 1024))
            hash = PositionalHash.Mix(hash ^ (ulong)(uint)BitConverter.SingleToInt32Bits(Surface.Height[i]) ^ ((ulong)Surface.Biomes[i] << 56));
        return PositionalHash.Mix(hash ^ (ulong)Caves.AirCellCount ^ ((ulong)Labyrinth.WalkableCount << 32));
    }
}
