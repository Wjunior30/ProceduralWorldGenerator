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
        for (int i = 0; i < geology.Length; i++)
            geology[i] = new(generated.Surface.Geology[i], generated.Surface.Biomes[i], generated.Surface.Height[i], generated.Surface.Slope[i], generated.Surface.Strata[i]);
        RiverNetwork rivers = TraceRivers(generated.Surface);
        ImplicitCaveSystem caves = BuildCaveGraph(generated);
        return new(generated, geology, rivers, caves);
    }

    private RiverNetwork TraceRivers(SurfaceMap map)
    {
        var network = new RiverNetwork();
        int count = map.Height.Length;
        var incoming = new int[count];
        for (int i = 0; i < count; i++)
        {
            if (!map.RiverChannel[i]) continue;
            int receiver = map.DrainageReceiver[i];
            if (receiver >= 0 && map.RiverChannel[receiver]) incoming[receiver]++;
        }
        int[] sources = Enumerable.Range(0, count)
            .Where(i => map.RiverChannel[i] && incoming[i] == 0)
            .OrderByDescending(i => map.Flow[i]).Take(32).ToArray();
        var claimed = new HashSet<int>();
        int riverId = 0;
        foreach (int source in sources)
        {
            var points = new List<WorldPoint>();
            int current = source;
            double lastY = double.PositiveInfinity;
            for (int step = 0; step < map.Size * 3; step++)
            {
                if (!map.RiverChannel[current]) break;
                int x = current % map.Size, z = current / map.Size;
                double waterY = map.Height[current] + Math.Max(.05f, map.WaterDepth[current]);
                lastY = Math.Min(lastY, waterY);
                points.Add(new WorldPoint(x * map.CellSize, lastY, z * map.CellSize));
                claimed.Add(current);
                int receiver = map.DrainageReceiver[current];
                if (receiver < 0 || receiver == current || !map.RiverChannel[receiver]) break;
                current = receiver;
            }
            if (points.Count >= 2)
            {
                double width = map.CellSize * (1.1 + Math.Log(1 + map.Flow[source] / Math.Max(1, _settings.RiverThreshold), 2) * .35);
                network.Add(new RiverChannel($"river-{riverId++}", points, width));
            }
        }
        return network;
    }

    private ImplicitCaveSystem BuildCaveGraph(GeneratedWorld world)
    {
        var system = new ImplicitCaveSystem();
        var ends = new List<(WorldPoint Point, CaveProfile Profile)>();
        int i = 0;
        foreach (CaveEntrance entrance in world.Caves.Entrances)
        {
            int cell = world.Surface.Index(entrance.X, entrance.Z);
            CaveProfile profile = world.Surface.Geology[cell] switch
            {
                GeologicalIdentity.KarstHighland => CaveProfile.Karst,
                GeologicalIdentity.VolcanicPlateau => CaveProfile.LavaTube,
                GeologicalIdentity.BadlandsMesa => CaveProfile.SlotCanyon,
                GeologicalIdentity.GlacialRange => CaveProfile.Fracture,
                GeologicalIdentity.AlluvialBasin => CaveProfile.Dissolution,
                _ => CaveProfile.Fracture
            };
            var start = new WorldPoint(entrance.X * world.Surface.CellSize, entrance.SurfaceY * world.Surface.CellSize, entrance.Z * world.Surface.CellSize);
            CaveRoute route = CaveRouteFactory.DescendingEntrance(_seed + ++i, start, world.Surface.CellSize * 8, world.Surface.CellSize * 1.2, profile, entrance.FacingX, entrance.FacingZ);
            system.AddRoute(route);
            WorldPoint end = route.Points[^1];
            system.AddChamber(new CaveChamber(end, new WorldPoint(world.Surface.CellSize * 4, world.Surface.CellSize * 2.5, world.Surface.CellSize * 3.5), profile));
            ends.Add((end, profile));
        }
        if (ends.Count > 1)
        {
            WorldPoint hub = new(ends.Average(x => x.Point.X), ends.Average(x => x.Point.Y) - world.Surface.CellSize * 2, ends.Average(x => x.Point.Z));
            system.AddChamber(new CaveChamber(hub, new WorldPoint(world.Surface.CellSize * 5, world.Surface.CellSize * 3, world.Surface.CellSize * 5), CaveProfile.Dissolution));
            foreach (var end in ends) system.AddRoute(new CaveRoute(new[] { end.Point, hub }, world.Surface.CellSize * 1.1, end.Profile));
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
