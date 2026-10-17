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
Check(a.GenerationVersion == 7, "Quota-based biome shares publish generation version 7.");
Check(a.Fingerprint() == b.Fingerprint(), "Same seed must reproduce the same world.");
Check(a.Fingerprint() != c.Fingerprint(), "Different seeds must produce different worlds.");
Check(a.Surface.Height.Max() - a.Surface.Height.Min() > 100, "Terrain must have meaningful vertical relief.");
{
    // Faceted (low-poly) style: exact heights on corners and planar triangles, flat-shaded mesh, deterministic colours.
    var facets = new FacetedSurface(a.Surface, 8, seed: 7); var again = new FacetedSurface(a.Surface, 8, seed: 7);
    Check(Math.Abs(facets.Height(16, 24) - facets.Corner(2, 3)) < 1e-3f, "Faceted height must equal the corner height on lattice points.");
    float mid = facets.Height(20, 24), ends = (facets.Corner(2, 3) + facets.Corner(3, 3)) * .5f;
    Check(Math.Abs(mid - ends) < 1e-3f, "Faceted surface must be planar along facet edges.");
    var mesh = facets.BuildChunk(0, 0, 64);
    Check(mesh.Triangles == 8 * 8 * 2, "A 64 m chunk at 8 m facets must have 128 triangles.");
    bool up = true, colours = true, onSurface = true;
    for (int v = 0; v < mesh.Positions.Length / 3; v++)
    {
        up &= mesh.Normals[v * 3 + 1] > 0;
        colours &= mesh.Colors[v * 3] is >= 0 and <= 1 && mesh.Colors[v * 3 + 1] is >= 0 and <= 1 && mesh.Colors[v * 3 + 2] is >= 0 and <= 1;
        onSurface &= Math.Abs(facets.Height(mesh.Positions[v * 3], mesh.Positions[v * 3 + 2]) - mesh.Positions[v * 3 + 1]) < 1e-2f;
    }
    Check(up && colours && onSurface, "Faceted mesh: normals up, colours in range, vertices on the faceted surface.");
    Check(mesh.Colors.SequenceEqual(again.BuildChunk(0, 0, 64).Colors), "Faceted colours must be deterministic.");
    // Inside a triangle the height is the plane: the centroid equals the mean of its three corners.
    float cx = (mesh.Positions[0] + mesh.Positions[3] + mesh.Positions[6]) / 3, cz = (mesh.Positions[2] + mesh.Positions[5] + mesh.Positions[8]) / 3, cy = (mesh.Positions[1] + mesh.Positions[4] + mesh.Positions[7]) / 3;
    Check(Math.Abs(facets.Height(cx, cz) - cy) < 1e-2f, "Faceted height must follow the triangle plane.");
    Check(FacetedSurface.WaterPlane(0, 0, 64, 16, 5).Triangles == 32, "Faceted water plane must have two triangles per cell.");
}
Check(a.Surface.Biomes.Distinct().Count() >= 5, "World must contain multiple biomes.");
Biome[] terrestrialBiomes = ((Biome[])Enum.GetValues(typeof(Biome))).Where(x => x is not Biome.River and not Biome.Lake).ToArray();
Check(terrestrialBiomes.All(a.Surface.Biomes.Contains), "Default generation must represent every terrestrial biome.");
Check(a.Surface.Geology.Distinct().Count() >= 2, "Natural worlds must contain multiple geological provinces.");
{
    // Island climate: a cold north and a hot south give many biomes, none dominant; the relief is broad hills, not bumps.
    var islandSettings = TerrainPresetCatalog.Create(TerrainStyle.Island, 257); islandSettings.CellSize = 20; islandSettings.MountainHeight = 1100; // the game profile grid
    var island = new WorldGenerator(2026, islandSettings).Generate().Surface;
    var landBiomes = Enumerable.Range(0, island.Biomes.Length).Where(i => !island.Ocean[i] && island.WaterDepth[i] <= 0).Select(i => island.Biomes[i]).ToList();
    int kinds = landBiomes.GroupBy(b => b).Count(g => g.Count() > landBiomes.Count * .01);
    double top = landBiomes.GroupBy(b => b).Max(g => g.Count()) / (double)landBiomes.Count;
    Check(kinds >= 8, $"Island must hold at least 8 land biomes over 1% each (has {kinds}).");
    Check(top <= .45, $"Island must not be dominated by one biome ({top:P0}).");
    Check(landBiomes.Contains(Biome.Snow) && landBiomes.Contains(Biome.Jungle) && landBiomes.Contains(Biome.Taiga), "Island must have polar/alpine snow, tropical jungle and cold taiga.");
    int n = island.Size, peaks = 0, cells = 0;
    for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
    {
        int i = z * n + x; if (island.Ocean[i]) continue; cells++; bool peak = true;
        for (int dz = -1; dz <= 1 && peak; dz++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dz != 0) && island.Height[i + dz * n + dx] >= island.Height[i]) { peak = false; break; }
        if (peak) peaks++;
    }
    double km2 = cells * island.CellSize * island.CellSize / 1e6;
    Check(peaks / km2 < 15, $"Island relief must be broad hills (local peaks {peaks / km2:0.0}/km2).");
}
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
Check(requests.Count(x => x.Detail == ChunkDetail.High) == 16, "High detail ring must be reachable between collision and medium rings.");
IReadOnlyList<ChunkRequest> horizon = new StreamingPlanner().Plan(0, 0, new StreamingSettings { HorizonRadius = 5 });
Check(horizon.Count == 121 && horizon.Where(x => x.Detail == ChunkDetail.Horizon).All(x => x.SampleStep == 64), "Horizon rings must be planned beyond the load radius.");
SurfaceChunk left = ChunkExtractor.Extract(a.Surface, 0, 0, 17), right = ChunkExtractor.Extract(a.Surface, 16, 0, 17);
Check(Enumerable.Range(0, 17).All(z => left.Height[z * 17 + 16] == right.Height[z * 17]), "Neighbouring chunks extracted with a shared edge must not seam.");
var nav = NavigationGrid.Build(a.Surface);
Check(nav.Cost.Where((_, i) => a.Surface.WaterDepth[i] > 0).All(float.IsPositiveInfinity), "Water must be impassable for walking NPCs.");
Check(nav.Cost.Count(float.IsFinite) > nav.Cost.Length / 2, "Most land must be walkable.");
int startCell = Array.FindIndex(nav.Cost, float.IsFinite), goalCell = Array.FindLastIndex(nav.Cost, float.IsFinite);
var path = nav.FindPath(startCell % nav.Size, startCell / nav.Size, goalCell % nav.Size, goalCell / nav.Size);
Check(path is null || (path[0] == (startCell % nav.Size, startCell / nav.Size) && path.All(p => float.IsFinite(nav.Cost[p.Z * nav.Size + p.X]))), "Paths must only cross walkable cells.");
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

WorldSettings filteredBiomeSettings = TerrainPresetCatalog.Create(TerrainStyle.Natural, 65);
filteredBiomeSettings.CaveDepthCells = 32;
filteredBiomeSettings.LabyrinthSize = 31;
filteredBiomeSettings.RiverThreshold = 70;
filteredBiomeSettings.EnabledBiomes = new[] { Biome.Desert, Biome.Forest, Biome.Snow };
GeneratedWorld filteredBiomeWorld = new WorldGenerator(42, filteredBiomeSettings).Generate();
Check(filteredBiomeWorld.Surface.Biomes.All(filteredBiomeSettings.EnabledBiomes.Contains), "Disabled biomes must not appear in generated maps.");
Check(filteredBiomeSettings.EnabledBiomes.All(filteredBiomeWorld.Surface.Biomes.Contains), "Every enabled terrestrial biome must be represented.");

long[] auditSeeds =
{
    1, 42, 2026, 9876, 173133397,
    -3719345503411677977, 7681369315911520508, -8466692820785526154,
    7924047624999685062, -2448665380361343223, -7438691006823355954
};
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
    Check(report.DominantBiomeRatio <= (style == TerrainStyle.Island ? .85 : .78), $"{style}/{seed} is dominated by one biome ({report.DominantBiomeRatio:P1}).");
    Check(report.DrainageOutlets == 1, $"{style}/{seed} must drain through one principal outlet ({report.DrainageOutlets}).");
    Check(report.ErosionMassRelativeError < 1e-5, $"{style}/{seed} did not conserve erosion mass ({report.ErosionMassRelativeError:E2}).");
    Check(report.BasinCount == report.DrainageOutlets, $"{style}/{seed} basin and outlet counts disagree.");
    Check(report.MaximumStreamOrder >= 1, $"{style}/{seed} did not classify its river hierarchy.");
    int[] mainRiver = Enumerable.Range(0, world.Surface.Height.Length).Where(i => world.Surface.MainRiverChannel[i]).ToArray();
    int mainSpanX = mainRiver.Max(i => i % world.Surface.Size) - mainRiver.Min(i => i % world.Surface.Size);
    int mainSpanZ = mainRiver.Max(i => i / world.Surface.Size) - mainRiver.Min(i => i / world.Surface.Size);
    // Island rivers run from the central peak to the coast instead of across the whole map.
    Check(Math.Max(mainSpanX, mainSpanZ) >= world.Surface.Size * (style == TerrainStyle.Island ? .22 : .70), $"{style}/{seed} principal river does not cross enough of the map ({mainSpanX}x{mainSpanZ}).");
    Check(mainRiver.All(i => world.Surface.DrainageReceiver[i] < 0 || world.Surface.MainRiverChannel[world.Surface.DrainageReceiver[i]] || world.Surface.Ocean[world.Surface.DrainageReceiver[i]]), $"{style}/{seed} principal river is disconnected.");
    Check(mainRiver.Max(i => world.Surface.WaterDepth[i]) >= 3.5f, $"{style}/{seed} principal river is not deep enough.");
    Check(world.Surface.RiverChannel.Count(x => x) <= mainRiver.Length * 4, $"{style}/{seed} produced too many tributary cells.");
    Check(report.CaveComponents == 1, $"{style}/{seed} retained disconnected cave components.");
    Check(world.Caves.Entrances.All(e => world.Surface.CaveOpening[world.Surface.Index(e.X, e.Z)]), $"{style}/{seed} has a cave entrance without a surface opening.");
}

WorldSettings lakeSettings = TerrainPresetCatalog.Create(TerrainStyle.Karst, 65);
lakeSettings.CellSize = 8;
lakeSettings.CaveDepthCells = 32;
lakeSettings.LabyrinthSize = 31;
lakeSettings.RiverThreshold = 70;
lakeSettings.WaterMode = WaterMode.RiversAndLakes;
GeneratedWorld lakeWorld = new WorldGenerator(2026, lakeSettings).Generate();
Check(lakeWorld.Surface.WaterDepth.Where((depth, index) => depth > .25f && !lakeWorld.Surface.RiverChannel[index]).Any(),
    "RiversAndLakes mode must preserve at least one non-river lake cell.");
Check(lakeWorld.Surface.WaterDepth.Where((depth,index) => !lakeWorld.Surface.RiverChannel[index]).DefaultIfEmpty().Max() >= 4,
    "Generated lagoons must include deep water.");
Check(TerrainDiagnostics.Analyze(lakeWorld).ErosionMassRelativeError < 1e-5,
    "Lake preservation must conserve the complete geomorphic pipeline mass.");
GeneratedWorld largeAlpineRegression = Generate(7924047624999685062, TerrainStyle.Alpine, 129);
Check(TerrainDiagnostics.Analyze(largeAlpineRegression).MaximumNeighbourRise <= largeAlpineRegression.Surface.CellSize * 2f,
    "Large alpine maps must not retain hydraulic wall steps.");

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
