using ProceduralWorld.Core;

long seed = args.Length > 0 && long.TryParse(args[0], out long parsedSeed) ? parsedSeed : 2026;
TerrainStyle style = args.Length > 1 && Enum.TryParse<TerrainStyle>(args[1], true, out var parsedStyle) ? parsedStyle : TerrainStyle.Natural;
var settings = TerrainPresetCatalog.Create(style, 129);
var world = new WorldGenerator(seed, settings).Generate();
var report = TerrainDiagnostics.Analyze(world);
var nearMesh = TerrainMeshBuilder.Build(world.Surface, sampleStep: 1);
var farMesh = TerrainMeshBuilder.Build(world.Surface, sampleStep: 4);

Console.WriteLine($"seed={world.Seed} version={world.GenerationVersion} style={style} fingerprint={world.Fingerprint():X16}");
Console.WriteLine($"height={report.MinimumHeight:0.0}..{report.MaximumHeight:0.0} max-rise={report.MaximumNeighbourRise:0.0}");
Console.WriteLine($"biomes={report.BiomeCount} components={report.BiomeComponents} rivers={report.RiverCells} broken-rivers={report.BrokenRiverCells}");
Console.WriteLine($"cave-air={world.Caves.AirCellCount} entrances={report.CaveEntrances} inaccessible={report.InaccessibleEntrances}");
Console.WriteLine($"mesh-near={nearMesh.Vertices.Count} vertices mesh-far={farMesh.Vertices.Count} vertices labyrinth={world.Labyrinth.WalkableCount}");
