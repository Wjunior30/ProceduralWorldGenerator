using ProceduralWorld.Core;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
GeneratedWorld Generate(long seed) => new WorldGenerator(seed, new WorldSettings { Size = 65, CaveDepthCells = 32, LabyrinthSize = 31, RiverThreshold = 80 }).Generate();

var a = Generate(42); var b = Generate(42); var c = Generate(43);
Check(a.Fingerprint() == b.Fingerprint(), "Same seed must reproduce the same world.");
Check(a.Fingerprint() != c.Fingerprint(), "Different seeds must produce different worlds.");
Check(a.Surface.Height.Max() - a.Surface.Height.Min() > 100, "Terrain must have meaningful vertical relief.");
Check(a.Surface.Biomes.Distinct().Count() >= 3, "World must contain multiple biomes.");
Check(a.Caves.AirCellCount > 0 && a.Caves.Entrances.Count == 4, "Caves must be volumetric and expose guaranteed entrances.");
Check(a.Labyrinth.WalkableCount > a.Labyrinth.Width, "Labyrinth must contain a connected traversable network.");
Console.WriteLine("ProceduralWorld.Tests: 6 checks passed.");

