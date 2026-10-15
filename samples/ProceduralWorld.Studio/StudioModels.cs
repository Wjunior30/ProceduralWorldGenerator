using System.Text.Json.Serialization;

namespace ProceduralWorld.Studio;

public sealed record GenerateRequest(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Seed,
    string Style,
    int Size,
    float MountainHeight,
    float ReliefStrength,
    int ErosionIterations,
    float RiverWidth,
    float CaveDensity,
    float BiomeScale,
    string WaterMode,
    string[]? EnabledBiomes);

public sealed record EngineDescriptor(
    string Id,
    string Name,
    bool Detected,
    string? ExecutablePath,
    string Capability,
    string Status);

public sealed record ProjectIntegrationRequest(
    string Engine,
    string ProjectName,
    string Destination,
    bool OpenAfterCreate,
    bool Overwrite,
    bool Confirmed,
    GenerateRequest Settings);

public sealed record ProjectPlan(
    string Engine,
    string ProjectName,
    string ProjectPath,
    bool EngineDetected,
    string? ExecutablePath,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Warnings);

public sealed record ProjectCreationResult(
    string Engine,
    string ProjectPath,
    int FilesWritten,
    bool Opened,
    string Message,
    string Fingerprint);
