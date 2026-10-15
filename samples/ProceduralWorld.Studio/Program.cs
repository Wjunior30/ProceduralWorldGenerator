using ProceduralWorld.Core;
using ProceduralWorld.Studio;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<EngineIntegrationService>();
builder.Services.AddResponseCompression();
var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/presets", () => TerrainPresetCatalog.All.Select(p => new
{
    style = p.Style.ToString(),
    p.DisplayName,
    p.Description,
    p.MountainScale,
    p.RiverScale,
    p.CaveScale
}));

app.MapGet("/api/engines", (EngineIntegrationService integrations) => integrations.Discover());

app.MapPost("/api/integrations/preview", (ProjectIntegrationRequest request, EngineIntegrationService integrations) =>
{
    try { return Results.Ok(integrations.Preview(request)); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});

app.MapPost("/api/integrations/create", (ProjectIntegrationRequest request, EngineIntegrationService integrations) =>
{
    if (!request.Confirmed)
        return Results.BadRequest(new { error = "A criação exige autorização explícita na tela de confirmação." });
    try { return Results.Ok(integrations.Create(request)); }
    catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
    { return Results.BadRequest(new { error = error.Message }); }
});

app.MapPost("/api/generate", (GenerateRequest request) =>
{
    if (!Enum.TryParse(request.Style, true, out TerrainStyle style)) style = TerrainStyle.Natural;
    int size = request.Size is 65 or 129 or 257 ? request.Size : 129;
    WorldSettings settings = TerrainPresetCatalog.Create(style, size);
    settings.MountainHeight = Math.Clamp(request.MountainHeight, 120, 1400);
    settings.ReliefStrength = Math.Clamp(request.ReliefStrength, .2f, 2f);
    settings.ErosionIterations = Math.Clamp(request.ErosionIterations, 0, 100);
    settings.RiverWidthMultiplier = Math.Clamp(request.RiverWidth, .25f, 3f);
    settings.CaveDensity = Math.Clamp(request.CaveDensity, .25f, 3f);
    settings.BiomeRegionScale = Math.Clamp(request.BiomeScale, .4f, 2.5f);
    settings.WaterMode = request.WaterMode?.ToLowerInvariant() switch
    {
        "none" => WaterMode.None,
        "riversandlakes" => WaterMode.RiversAndLakes,
        _ => WaterMode.RiversOnly
    };
    BiomeSelection.Configure(settings, request.EnabledBiomes);

    GeneratedWorld world = new WorldGenerator(request.Seed, settings).Generate();
    TerrainReport report = TerrainDiagnostics.Analyze(world);
    SurfaceMap map = world.Surface;
    return Results.Ok(new
    {
        seed = world.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
        style = style.ToString(),
        world.GenerationVersion,
        fingerprint = world.Fingerprint().ToString("X16"),
        size = map.Size,
        map.CellSize,
        height = map.Height,
        slope = map.Slope,
        waterDepth = map.WaterDepth,
        caveOpening = map.CaveOpening,
        biomes = map.Biomes.Select(x => (int)x).ToArray(),
        report,
        caveEntrances = world.Caves.Entrances.Select(e => new { e.X, e.Z, e.SurfaceY, e.CaveY, e.FacingX, e.FacingZ, e.Radius })
    });
});

app.Run();
