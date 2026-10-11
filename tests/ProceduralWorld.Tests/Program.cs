using ProceduralWorld.Core;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static GeneratedWorld Generate(long seed, TerrainStyle style = TerrainStyle.Natural)
{
    var settings = TerrainPresetCatalog.Create(style, 65);
    settings.CaveDepthCells = 32; settings.LabyrinthSize = 31; settings.RiverThreshold = 70;
    return new WorldGenerator(seed, settings).Generate();
}

var a = Generate(42); var b = Generate(42); var c = Generate(43);
Check(a.Fingerprint() == b.Fingerprint(), "Same seed must reproduce the same world.");
Check(a.Fingerprint() != c.Fingerprint(), "Different seeds must produce different worlds.");
Check(a.Surface.Height.Max() - a.Surface.Height.Min() > 100, "Terrain must have meaningful vertical relief.");
Check(a.Surface.Biomes.Distinct().Count() >= 3, "World must contain multiple biomes.");
Check(a.Caves.AirCellCount > 0 && a.Caves.Entrances.Count == 4, "Caves must be volumetric and expose guaranteed entrances.");
Check(a.Caves.Entrances.All(e => !a.Caves.IsSolid(e.X, a.Caves.Height - 2, e.Z) && !a.Caves.IsSolid(e.X, e.CaveY, e.Z)), "Every cave entrance must connect surface and carved space.");
Check(a.Labyrinth.WalkableCount > a.Labyrinth.Width, "Labyrinth must contain a traversable network.");
var chunk = ChunkExtractor.Extract(a.Surface, 4, 4, 16); Check(chunk.Height.Length == 256, "World data must be extractable in engine-neutral chunks.");
var query = new GeneratedWorldQuery(a); Check(query.HeightAt(4, 4) == chunk.Height[0], "Engine query and chunk data must agree.");

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
var universal = new UniversalWorldGenerator(55, new WorldSettings { Size = 33, CaveDepthCells = 16, LabyrinthSize = 21, RiverThreshold = 30 }).Generate();
Check(universal.Geology.Count == 33 * 33 && universal.ImplicitCaves.Routes.Count == universal.Generated.Caves.Entrances.Count, "Universal facade must compose terrain, geology and cave graphs.");

foreach (long seed in new long[] { 1, 42, 2026, 9876, 173133397 })
{
    var world = Generate(seed); var report = TerrainDiagnostics.Analyze(world);
    Check(report.MinimumHeight >= 4 && float.IsFinite(report.MaximumHeight), $"Seed {seed} produced invalid heights.");
    Check(report.MaximumNeighbourRise < 260, $"Seed {seed} produced a wall-like terrain discontinuity.");
    Check(report.BrokenRiverCells == 0, $"Seed {seed} produced disconnected river cells.");
    Check(report.CaveEntrances == 4 && report.InaccessibleEntrances == 0, $"Seed {seed} produced inaccessible caves.");
}

var alpine = Generate(2026, TerrainStyle.Alpine); var plains = Generate(2026, TerrainStyle.RollingPlains);
Check(alpine.Fingerprint() != plains.Fingerprint(), "Terrain presets must generate distinct worlds.");
Check(alpine.Surface.Height.Max() - alpine.Surface.Height.Min() > plains.Surface.Height.Max() - plains.Surface.Height.Min(), "Alpine terrain should have more relief than rolling plains.");
var meshNear = TerrainMeshBuilder.Build(alpine.Surface, sampleStep: 1); var meshFar = TerrainMeshBuilder.Build(alpine.Surface, sampleStep: 4);
Check(meshNear.Vertices.Count > meshFar.Vertices.Count && meshNear.Indices.Count > 0, "Mesh LOD must reduce geometry with distance.");
Check(meshNear.Vertices.All(v => Math.Abs(v.Normal.Length() - 1) < .001f), "Terrain normals must be normalized.");

Console.WriteLine("ProceduralWorld.Tests: terrain v2 and migrated generator checks passed.");
