using ProceduralWorld.Core;

long seed = args.Length > 0 && long.TryParse(args[0], out long parsedSeed) ? parsedSeed : 2026;
TerrainStyle style = args.Length > 1 && Enum.TryParse<TerrainStyle>(args[1], true, out TerrainStyle parsedStyle) ? parsedStyle : TerrainStyle.Natural;
WorldSettings settings = TerrainPresetCatalog.Create(style, 129);
GeneratedWorld world = new WorldGenerator(seed, settings).Generate();
TerrainReport report = TerrainDiagnostics.Analyze(world);
TerrainMeshData nearMesh = TerrainMeshBuilder.Build(world.Surface, sampleStep: 1, includeSkirts: true);
TerrainMeshData farMesh = TerrainMeshBuilder.Build(world.Surface, sampleStep: 4, includeSkirts: true);
string biomes = string.Join(", ", world.Surface.Biomes.GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => $"{x.Key}:{x.Count() * 100d / world.Surface.Biomes.Length:F1}%"));
string geology = string.Join(", ", world.Surface.Geology.GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => $"{x.Key}:{x.Count() * 100d / world.Surface.Geology.Length:F1}%"));

Console.WriteLine($"seed={world.Seed} version={world.GenerationVersion} style={style} fingerprint={world.Fingerprint():X16}");
Console.WriteLine($"height={report.MinimumHeight:0.0}..{report.MaximumHeight:0.0} max-rise={report.MaximumNeighbourRise:0.0} flat={report.FlatAreaRatio:P1} steep={report.SteepAreaRatio:P1}");
Console.WriteLine($"rivers={report.RiverCells} broken={report.BrokenRiverCells} uphill-drainage={report.UphillDrainageCells} uphill-rivers={report.UphillRiverCells} outlets={report.DrainageOutlets}");
Console.WriteLine($"cave-air={world.Caves.AirCellCount} entrances={report.CaveEntrances} inaccessible={report.InaccessibleEntrances} reachable={report.ReachableCaveRatio:P1}");
Console.WriteLine($"biomes={biomes}");
Console.WriteLine($"geology={geology}");
Console.WriteLine($"mesh-near={nearMesh.Vertices.Count} vertices ({nearMesh.SkirtVertexCount} skirt) mesh-far={farMesh.Vertices.Count} vertices ({farMesh.SkirtVertexCount} skirt) labyrinth={world.Labyrinth.WalkableCount}");
