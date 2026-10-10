using ProceduralWorld.Core;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
GeneratedWorld Generate(long seed) => new WorldGenerator(seed, new WorldSettings { Size = 65, CaveDepthCells = 32, LabyrinthSize = 31, RiverThreshold = 80 }).Generate();

var a = Generate(42); var b = Generate(42); var c = Generate(43);
Check(a.Fingerprint() == b.Fingerprint(), "Same seed must reproduce the same world.");
Check(a.Fingerprint() != c.Fingerprint(), "Different seeds must produce different worlds.");
Check(a.Surface.Height.Max() - a.Surface.Height.Min() > 100, "Terrain must have meaningful vertical relief.");
Check(a.Surface.Biomes.Distinct().Count() >= 3, "World must contain multiple biomes.");
Check(a.Caves.AirCellCount > 0 && a.Caves.Entrances.Count == 4, "Caves must be volumetric and expose guaranteed entrances.");
Check(a.Caves.Entrances.All(e => !a.Caves.IsSolid(e.X, e.CaveY, e.Z)), "Every cave entrance must reach carved space.");
Check(a.Labyrinth.WalkableCount > a.Labyrinth.Width, "Labyrinth must contain a traversable network.");
var chunk = ChunkExtractor.Extract(a.Surface, 4, 4, 16); Check(chunk.Height.Length == 256, "World data must be extractable in engine-neutral chunks.");
var query = new GeneratedWorldQuery(a); Check(query.HeightAt(4, 4) == chunk.Height[0], "Engine query and chunk data must agree.");
Console.WriteLine("ProceduralWorld.Tests: 9 checks passed.");


var riverNetwork = new RiverNetwork();
riverNetwork.Add(new RiverChannel("main", new[] { new WorldPoint(0, 20, 0), new WorldPoint(10, 18, 0), new WorldPoint(20, 15, 2) }, 4));
Check(riverNetwork.Carve(10, 0, 30) < 30, "Continuous rivers must carve terrain.");

var implicitCaves = new ImplicitCaveSystem();
var entranceRoute = CaveRouteFactory.DescendingEntrance(42, new WorldPoint(0, 30, 0), 25, 3, CaveProfile.Karst); implicitCaves.AddRoute(entranceRoute);
implicitCaves.AddChamber(new CaveChamber(entranceRoute.Points[^1], new WorldPoint(8, 5, 7), CaveProfile.Dissolution));
Check(implicitCaves.HasNavigableEntrance(2.5) && implicitCaves.IsInside(entranceRoute.Points[3]), "Implicit caves must expose walkable connected entrances.");

var requests = new StreamingPlanner().Plan(0, 0, new StreamingSettings());
Check(requests.Count == 49 && requests.Count(x => x.NeedsCollision) == 9, "Streaming plan must preserve load and collision rings.");

var voxelA = new ProceduralWorld.Core.Voxels.VoxelWorldGenerator(7).GenerateChunk(0, 0);
var voxelB = new ProceduralWorld.Core.Voxels.VoxelWorldGenerator(7).GenerateChunk(0, 0);
Check(voxelA.SurfaceHeight.SequenceEqual(voxelB.SurfaceHeight), "Voxel chunks must be order-independent and deterministic.");

var layout = new ProceduralWorld.Core.Layout2D.PlainsLayout(180, 80, 7);
Check(layout.Fingerprint() == new ProceduralWorld.Core.Layout2D.PlainsLayout(180, 80, 7).Fingerprint(), "2D layouts must be deterministic.");
Check(layout.CaveMouth.Any(x => x), "2D layouts must include accessible cave mouths.");

Console.WriteLine("ProceduralWorld.Tests: migrated generator checks passed.");


var universal = new UniversalWorldGenerator(55, new WorldSettings { Size = 33, CaveDepthCells = 16, LabyrinthSize = 21, RiverThreshold = 30 }).Generate();
Check(universal.Geology.Count == 33 * 33 && universal.ImplicitCaves.Routes.Count == universal.Generated.Caves.Entrances.Count, "Universal facade must compose terrain, geology and cave graphs.");

