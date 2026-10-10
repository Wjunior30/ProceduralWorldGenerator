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

