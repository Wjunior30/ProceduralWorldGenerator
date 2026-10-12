using ProceduralWorld.Core;

var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) failures.Add(message); }
static GeneratedWorld Generate(long seed, TerrainStyle style = TerrainStyle.Natural, int size = 65, float cellSize = 8)
{
    WorldSettings settings = TerrainPresetCatalog.Create(style, size);
    settings.CellSize = cellSize; settings.CaveDepthCells = 32; settings.LabyrinthSize = 31; settings.RiverThreshold = 70;
    return new WorldGenerator(seed, settings).Generate();
}

GeneratedWorld a = Generate(42), b = Generate(42), c = Generate(43);
Check(a.GenerationVersion == 4, "The structural geology pipeline must publish generation version 4.");
Check(a.Fingerprint() == b.Fingerprint(), "Same seed must reproduce the same world.");
Check(a.Fingerprint() != c.Fingerprint(), "Different seeds must produce different worlds.");
Check(a.Surface.Height.Max() - a.Surface.Height.Min() > 100, "Terrain must have meaningful vertical relief.");
Check(a.Surface.Biomes.Distinct().Count() >= 5, "World must contain multiple biomes.");
Check(a.Surface.Geology.Distinct().Count() >= 2, "Natural worlds must contain multiple geological provinces.");
Check(a.Surface.RockHardness.All(x => x is >= .1f and <= 1f), "Rock hardness must be finite and normalized.");
Check(a.Caves.AirCellCount > 0 && a.Caves.Entrances.Count == 4, "Caves must be volumetric and expose guaranteed entrances.");
Check(a.Caves.Entrances.All(e => !a.Caves.IsSolid(e.X, a.Caves.Height - 2, e.Z) && !a.Caves.IsSolid(e.X, e.CaveY, e.Z)), "Every cave entrance must connect surface and carved space.");
Check(a.Labyrinth.WalkableCount > a.Labyrinth.Width, "Labyrinth must contain a traversable network.");
SurfaceChunk chunk = ChunkExtractor.Extract(a.Surface, 4, 4, 16);
Check(chunk.Height.Length == 256, "World data must be extractable in engine-neutral chunks.");
var query = new GeneratedWorldQuery(a);
Check(query.HeightAt(4, 4) == chunk.Height[0], "Engine query and chunk data must agree.");

var riverNetwork = new RiverNetwork();
riverNetwork.Add(new RiverChannel("main", new[] { new WorldPoint(0, 20, 0), new WorldPoint(10, 18, 0), new WorldPoint(20, 15, 2) }, 4));
Check(riverNetwork.Carve(10, 0, 30) < 30, "Continuous rivers must carve terrain.");
Check(riverNetwork.Channels[0].Points.Zip(riverNetwork.Channels[0].Points.Skip(1), (from, to) =>
    (from.Y - to.Y) / Math.Max(.0001, from.DistanceXZ(to))).All(grade => grade <= RiverChannel.MaximumHydraulicGrade + .0001),
    "River profiles must not create vertical water walls.");

var implicitCaves = new ImplicitCaveSystem();
CaveRoute entranceRoute = CaveRouteFactory.DescendingEntrance(42, new WorldPoint(0, 30, 0), 25, 3, CaveProfile.Karst, 0, 1);
implicitCaves.AddRoute(entranceRoute);
implicitCaves.AddChamber(new CaveChamber(entranceRoute.Points[^1], new WorldPoint(8, 5, 7), CaveProfile.Dissolution));
Check(implicitCaves.HasNavigableEntrance(2.5) && implicitCaves.IsInside(entranceRoute.Points[3]), "Implicit caves must expose walkable connected entrances.");

IReadOnlyList<ChunkRequest> requests = new StreamingPlanner().Plan(0, 0, new StreamingSettings());
Check(requests.Count == 49 && requests.Count(x => x.NeedsCollision) == 9, "Streaming plan must preserve load and collision rings.");
var voxelA = new ProceduralWorld.Core.Voxels.VoxelWorldGenerator(7).GenerateChunk(0, 0);
var voxelB = new ProceduralWorld.Core.Voxels.VoxelWorldGenerator(7).GenerateChunk(0, 0);
Check(voxelA.SurfaceHeight.SequenceEqual(voxelB.SurfaceHeight), "Voxel chunks must be order-independent and deterministic.");
var layout = new ProceduralWorld.Core.Layout2D.PlainsLayout(180, 80, 7);
Check(layout.Fingerprint() == new ProceduralWorld.Core.Layout2D.PlainsLayout(180, 80, 7).Fingerprint(), "2D layouts must be deterministic.");
Check(layout.CaveMouth.Any(x => x), "2D layouts must include accessible cave mouths.");

var universal = new UniversalWorldGenerator(55, new WorldSettings { Size = 33, CaveDepthCells = 16, LabyrinthSize = 21, RiverThreshold = 30 }).Generate();
Check(universal.Geology.Count == 33 * 33 && universal.ImplicitCaves.Routes.Count >= universal.Generated.Caves.Entrances.Count, "Universal facade must compose terrain, geology and a shared cave graph.");
Check(universal.ImplicitCaves.HasNavigableEntrance(4), "Universal cave graph must expose navigable entrances.");
Check(universal.Geology.Zip(universal.Generated.Surface.Geology, (column, identity) => column.Identity == identity).All(x => x), "Universal geology must preserve the generated lithology.");

long[] auditSeeds = { 1, 42, 2026, 9876, 173133397 };
foreach (TerrainStyle style in Enum.GetValues<TerrainStyle>())
foreach (long seed in auditSeeds)
{
    GeneratedWorld world = Generate(seed, style);
    TerrainReport report = TerrainDiagnostics.Analyze(world);
    Check(report.MinimumHeight > -1000 && report.MaximumHeight < 5000 && float.IsFinite(report.MinimumHeight) && float.IsFinite(report.MaximumHeight), $"{style}/{seed} produced invalid heights ({report.MinimumHeight:F3}..{report.MaximumHeight:F3}).");
    Check(report.MaximumNeighbourRise <= world.Surface.CellSize * 2f, $"{style}/{seed} produced a wall-like discontinuity ({report.MaximumNeighbourRise:F2}).");
    Check(report.MaximumLocalApex <= world.Surface.CellSize * .9f, $"{style}/{seed} produced an isolated geological needle ({report.MaximumLocalApex:F2}).");
    Check(report.BrokenRiverCells == 0 && report.UphillRiverCells == 0, $"{style}/{seed} produced a broken or ascending river.");
    Check(report.UphillDrainageCells == 0, $"{style}/{seed} retained uphill drainage receivers.");
    Check(report.CaveEntrances == 4 && report.InaccessibleEntrances == 0 && report.ReachableCaveRatio >= .999, $"{style}/{seed} produced disconnected caves.");
    Check(report.RepeatedSteepGradeRatio < .04, $"{style}/{seed} saturated too many edges at one grade ({report.RepeatedSteepGradeRatio:P1}).");
    Check(report.DominantBiomeRatio <= .78, $"{style}/{seed} is dominated by one biome ({report.DominantBiomeRatio:P1}).");
    Check(report.DrainageOutlets is >= 2 and <= 12, $"{style}/{seed} produced an invalid outlet count ({report.DrainageOutlets}).");
    Check(report.ErosionMassRelativeError < 1e-5, $"{style}/{seed} did not conserve erosion mass ({report.ErosionMassRelativeError:E2}).");
    Check(report.BasinCount == report.DrainageOutlets, $"{style}/{seed} basin and outlet counts disagree.");
    Check(report.MaximumStreamOrder >= 1, $"{style}/{seed} did not classify its river hierarchy.");
    Check(report.CaveComponents == 1, $"{style}/{seed} retained disconnected cave components.");
    Check(world.Caves.Entrances.All(e => world.Surface.CaveOpening[world.Surface.Index(e.X, e.Z)]), $"{style}/{seed} has a cave entrance without a surface opening.");
}

GeneratedWorld alpine = Generate(2026, TerrainStyle.Alpine);
GeneratedWorld plains = Generate(2026, TerrainStyle.RollingPlains);
Check(alpine.Fingerprint() != plains.Fingerprint(), "Terrain presets must generate distinct worlds.");
Check(alpine.Surface.Height.Max() - alpine.Surface.Height.Min() > plains.Surface.Height.Max() - plains.Surface.Height.Min(), "Alpine terrain should have more relief than rolling plains.");
Check(alpine.Surface.Geology.Contains(GeologicalIdentity.GlacialRange), "Alpine terrain must expose glacial geology.");
Check(Generate(2026, TerrainStyle.Canyonlands).Surface.Geology.Contains(GeologicalIdentity.BadlandsMesa), "Canyonlands must expose stratified badlands geology.");
Check(Generate(2026, TerrainStyle.Karst).Surface.Geology.Contains(GeologicalIdentity.KarstHighland), "Karst terrain must expose limestone highlands.");
Check(plains.Surface.Geology.Contains(GeologicalIdentity.AlluvialBasin), "Rolling plains must expose alluvial geology.");
Check(Generate(2026, TerrainStyle.Volcanic).Surface.Geology.Contains(GeologicalIdentity.VolcanicPlateau), "Volcanic terrain must expose volcanic geology.");

TerrainMeshData meshNear = TerrainMeshBuilder.Build(alpine.Surface, sampleStep: 1, includeSkirts: true);
TerrainMeshData meshFar = TerrainMeshBuilder.Build(alpine.Surface, sampleStep: 4, includeSkirts: true);
Check(meshNear.Vertices.Count > meshFar.Vertices.Count && meshNear.Indices.Count > 0, "Mesh LOD must reduce geometry with distance.");
Check(meshNear.SkirtVertexCount > 0 && meshFar.SkirtVertexCount > 0, "LOD meshes must provide optional crack-hiding skirts.");
Check(meshNear.Vertices.All(v => Math.Abs(v.Normal.Length() - 1) < .001f), "Terrain normals must be normalized.");
TerrainMeshData seam = TerrainSeamBuilder.Stitch(meshFar.Edge(TerrainEdge.North), meshNear.Edge(TerrainEdge.North));
Check(seam.Indices.Count == (meshFar.GridWidth + meshNear.GridWidth - 2) * 3, "Mixed-LOD edges must produce a complete transition seam.");

GeneratedWorld coarse = Generate(42, TerrainStyle.Natural, 65, 16);
GeneratedWorld fine = Generate(42, TerrainStyle.Natural, 129, 8);
double squaredError = 0;
for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++)
{
    double difference = coarse.Surface.HeightAt(x, z) - fine.Surface.HeightAt(x * 2, z * 2);
    squaredError += difference * difference;
}
double resolutionRmse = Math.Sqrt(squaredError / (65 * 65));
Check(resolutionRmse < 8, $"Changing mesh resolution changed the geography too much (RMSE {resolutionRmse:F2}m).");

if (failures.Count > 0)
{
    foreach (string failure in failures) Console.Error.WriteLine("FAIL: " + failure);
    throw new AggregateException($"{failures.Count} procedural-world checks failed.", failures.Select(x => new Exception(x)));
}
Console.WriteLine("ProceduralWorld.Tests: all geological, hydraulic, cave, LOD and resolution checks passed.");
