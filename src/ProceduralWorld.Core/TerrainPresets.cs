namespace ProceduralWorld.Core;

public sealed record TerrainPreset(TerrainStyle Style, string DisplayName, string Description, float MountainScale, float RidgePower, float PlateauStrength, float ValleyStrength, float ErosionScale, float RiverScale, float CaveScale);

public static class TerrainPresetCatalog
{
    private static readonly TerrainPreset[] Items =
    {
        new(TerrainStyle.Natural, "Natural", "Balanced continents, valleys, mountain chains and plains.", 1f, 1.25f, .12f, .7f, 1f, 1f, 1f),
        new(TerrainStyle.Alpine, "Alpine", "Tall connected ranges, deep valleys and strong glacial erosion.", 1.32f, .82f, .04f, 1f, 1.3f, 1.15f, .65f),
        new(TerrainStyle.Canyonlands, "Canyonlands", "Broad plateaus cut by rivers, mesas and exposed strata.", .72f, 1.4f, .78f, 1.25f, .85f, 1.4f, .55f),
        new(TerrainStyle.Karst, "Karst", "Limestone highlands, sinkholes and dense cave systems.", .88f, 1.15f, .2f, .9f, 1.15f, 1.05f, 1.75f),
        new(TerrainStyle.RollingPlains, "Rolling plains", "Large navigable plains with smooth hills and wide rivers.", .35f, 1.8f, .04f, .45f, 1.45f, 1.5f, .5f),
        new(TerrainStyle.Glacial, "Glacial", "U-shaped valleys, high ridges and broad cold basins.", 1.18f, .9f, .08f, 1.2f, 1.55f, 1.25f, .7f),
        new(TerrainStyle.Volcanic, "Volcanic", "Plateaus, isolated cones, fractures and lava-tube caves.", 1.05f, 1.05f, .42f, .65f, .55f, .7f, 1.35f),
        new(TerrainStyle.Island, "Island", "Sea-bound island: snowy peak, forests, a river-cut canyon, swamp delta and beaches.", 1f, 1.25f, .12f, .7f, 1f, 1f, 1f)
    };
    public static IReadOnlyList<TerrainPreset> All => Items;
    public static TerrainPreset Get(TerrainStyle style) => Items.First(x => x.Style == style);
    public static WorldSettings Create(TerrainStyle style, int size = 129) => WithIslandDefaults(CreateBase(style, size));

    private static WorldSettings WithIslandDefaults(WorldSettings settings)
    {
        if (settings.TerrainStyle != TerrainStyle.Island) return settings;
        settings.SeaLevel = 40; settings.SingleRiver = true; settings.MainRiverWidth = 48; settings.MainRiverDepth = 4.5f;
        return settings;
    }

    private static WorldSettings CreateBase(TerrainStyle style, int size)
    {
        TerrainPreset p = Get(style);
        return new WorldSettings { Size = size, TerrainStyle = style, ReliefStrength = 1f, ErosionStrength = .18f * p.ErosionScale, ErosionIterations = (int)Math.Round(24 * p.ErosionScale), RiverWidthMultiplier = p.RiverScale, CaveDensity = p.CaveScale };
    }
}
