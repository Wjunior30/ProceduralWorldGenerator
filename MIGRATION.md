# Migration matrix

| Source | Migrated capability | Destination |
|---|---|---|
| ProceduralMap | positional hash, coherent/ridged noise, continuous terrain, climate, biomes, hydrology, erosion | existing core + `Geology.cs` |
| ProceduralMap | Minecraft-like block chunks and volumetric cheese/spaghetti caves | `VoxelWorld.cs` |
| SimulationLife `large_world_height.gd` | multi-scale relief and engine-neutral height queries | `TerrainGenerator.cs`, `Geology.cs`, `Streaming.cs` |
| SimulationLife `procedural_biomes.gd` | geological identities, massif/peak/mesa/talus/fissure shaping | `Geology.cs` |
| SimulationLife `procedural_rivers.gd` | continuous monotonic channels, variable width and terrain carving | `RiverNetwork.cs` |
| SimulationLife `procedural_cave_network.gd` | routes, chambers, profiles, formations and SDF cave queries | `ImplicitCaves.cs` |
| SimulationLife `large_world_streamer.gd` | chunk rings, LOD sample steps and collision planning | `Streaming.cs` |
| SimulationLife fixed snapshots | bilinear fixed-height sampling | `Streaming.cs` |
| Labyrinthitis | corridors, glades, arenas, niches, dungeons, drains and wall layers | `LabyrinthWorld.cs`, `LabyrinthGenerator.cs` |
| Project FiberBlade | deterministic 2D plains/caves/foundations and collision motor | `PlatformLayout.cs`, `PlatformMotor.cs` |

Meshes, materials, rendering nodes and engine physics are intentionally adapters: the library emits deterministic data for Godot, Unity, Unreal or a custom engine.
