using ProceduralWorld.Core;

var world = new WorldGenerator(2026, new WorldSettings { Size = 129, WaterMode = WaterMode.RiversOnly }).Generate();
Console.WriteLine($"seed={world.Seed} fingerprint={world.Fingerprint():X16}");
Console.WriteLine($"height={world.Surface.Height.Min():0.0}..{world.Surface.Height.Max():0.0} rivers={world.Surface.Biomes.Count(x => x == Biome.River)}");
Console.WriteLine($"cave-air={world.Caves.AirCellCount} entrances={world.Caves.Entrances.Count} labyrinth={world.Labyrinth.WalkableCount}");

