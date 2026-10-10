namespace ProceduralWorld.Core;

public sealed class UniversalWorld
{
    public UniversalWorld(GeneratedWorld generated, IReadOnlyList<GeologicalColumn> geology, RiverNetwork rivers, ImplicitCaveSystem implicitCaves)
    { Generated = generated; Geology = geology; Rivers = rivers; ImplicitCaves = implicitCaves; Query = new GeneratedWorldQuery(generated); }
    public GeneratedWorld Generated { get; }
    public GeneratedWorldQuery Query { get; }
    public IReadOnlyList<GeologicalColumn> Geology { get; }
    public RiverNetwork Rivers { get; }
    public ImplicitCaveSystem ImplicitCaves { get; }
    public IReadOnlyList<ChunkRequest> StreamingPlan(double x, double z, StreamingSettings? settings = null) => new StreamingPlanner().Plan(x, z, settings ?? new StreamingSettings());
}

public sealed class UniversalWorldGenerator
{
    private readonly long _seed;
    private readonly WorldSettings _settings;
    public UniversalWorldGenerator(long seed, WorldSettings? settings = null) { _seed = seed; _settings = settings ?? new WorldSettings(); }
    public UniversalWorld Generate()
    {
        GeneratedWorld generated = new WorldGenerator(_seed, _settings).Generate();
        var geology = new GeologicalColumn[generated.Surface.Height.Length];
        for (int i = 0; i < geology.Length; i++) geology[i] = new(generated.Surface.Biomes[i], generated.Surface.Height[i], generated.Surface.Slope[i]);
        RiverNetwork rivers = TraceRivers(generated.Surface);
        ImplicitCaveSystem caves = BuildCaveGraph(generated);
        return new(generated, geology, rivers, caves);
    }
    private RiverNetwork TraceRivers(SurfaceMap map)
    {
        var network = new RiverNetwork(); int size = map.Size, riverId = 0;
        for (int z = 2; z < size - 2; z += Math.Max(4, size / 8))
        {
            int bestX = -1; float bestFlow = 0;
            for (int x = 1; x < size - 1; x++) { float flow = map.Flow[map.Index(x, z)]; if (flow > bestFlow) { bestFlow = flow; bestX = x; } }
            if (bestX < 0 || bestFlow < _settings.RiverThreshold * .35f) continue;
            var points = new List<WorldPoint>(); int cx = bestX, cz = z; double lastY = map.HeightAt(cx, cz);
            for (int step = 0; step < size && cz < size - 1; step++)
            {
                points.Add(new(cx * map.CellSize, lastY, cz * map.CellSize)); int nx = cx, nz = cz; float lowest = map.HeightAt(cx, cz);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dz != 0)
                { int tx = Math.Max(0, Math.Min(size - 1, cx + dx)), tz = Math.Max(0, Math.Min(size - 1, cz + dz)); float h = map.HeightAt(tx, tz); if (h < lowest) { lowest = h; nx = tx; nz = tz; } }
                if (nx == cx && nz == cz) nz = Math.Min(size - 1, cz + 1); cx = nx; cz = nz; lastY = Math.Min(lastY, map.HeightAt(cx, cz));
                if (points.Count > 6 && (cz == size - 1 || map.WaterDepth[map.Index(cx, cz)] > 1)) break;
            }
            if (points.Count >= 2) network.Add(new RiverChannel($"river-{riverId++}", points, map.CellSize * 1.5));
        }
        return network;
    }
    private ImplicitCaveSystem BuildCaveGraph(GeneratedWorld world)
    {
        var system = new ImplicitCaveSystem(); int i = 0;
        foreach (var entrance in world.Caves.Entrances)
        {
            var start = new WorldPoint(entrance.X * world.Surface.CellSize, entrance.SurfaceY * world.Surface.CellSize, entrance.Z * world.Surface.CellSize);
            CaveProfile profile = (CaveProfile)(i++ % Enum.GetValues(typeof(CaveProfile)).Length);
            var route = CaveRouteFactory.DescendingEntrance(_seed + i, start, world.Surface.CellSize * 8, world.Surface.CellSize * 1.2, profile); system.AddRoute(route);
            var end = route.Points[route.Points.Count - 1]; system.AddChamber(new(end, new WorldPoint(world.Surface.CellSize * 4, world.Surface.CellSize * 2.5, world.Surface.CellSize * 3.5), profile));
        }
        return system;
    }
}

public interface IWorldAdapter
{
    void ApplySurfaceChunk(SurfaceChunk chunk);
    void ApplyCave(ImplicitCaveSystem caves);
    void ApplyLabyrinth(LabyrinthPlan labyrinth);
}
