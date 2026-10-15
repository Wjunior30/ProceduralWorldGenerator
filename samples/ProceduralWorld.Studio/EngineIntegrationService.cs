using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProceduralWorld.Core;

namespace ProceduralWorld.Studio;

public sealed class EngineIntegrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<EngineDescriptor> Discover()
    {
        string? unity = FindExecutable("UNITY_EDITOR", UnityCandidates());
        return new[] { Describe("unity", "Unity", unity, "Terrain, rios, água animada e exploração em terceira pessoa") };
    }
    public ProjectPlan Preview(ProjectIntegrationRequest request)
    {
        ValidatedRequest valid = Validate(request, inspectExisting: true);
        EngineDescriptor engine = Discover().First(x => x.Id == valid.Engine);
        var warnings = new List<string>();
        if (string.Equals(request.Settings.WaterMode, "riversandlakes", StringComparison.OrdinalIgnoreCase)) warnings.Add("Os dados de lagos são exportados, mas a superfície visual e a natação deste adaptador cobrem apenas rios.");
        if (!engine.Detected) warnings.Add($"{engine.Name} não foi encontrada. O projeto será criado, mas não poderá ser aberto automaticamente.");
        if (Directory.Exists(valid.ProjectPath) && Directory.EnumerateFileSystemEntries(valid.ProjectPath).Any())
            warnings.Add(request.Overwrite ? "Arquivos gerados com o mesmo nome poderão ser substituídos." : "A pasta já contém arquivos; habilite a substituição para continuar.");
        return new ProjectPlan(valid.Engine, valid.ProjectName, valid.ProjectPath, engine.Detected, engine.ExecutablePath, PlannedFiles(valid.Engine, valid.ProjectName), warnings);
    }

    public ProjectCreationResult Create(ProjectIntegrationRequest request)
    {
        ValidatedRequest valid = Validate(request, inspectExisting: true);
        ProjectPlan plan = Preview(request);
        if (Directory.Exists(valid.ProjectPath) && Directory.EnumerateFileSystemEntries(valid.ProjectPath).Any() && !request.Overwrite)
            throw new IOException("A pasta de destino não está vazia. Revise os arquivos e autorize a substituição.");

        WorldSettings settings = BuildSettings(request.Settings);
        GeneratedWorld world = new WorldGenerator(request.Settings.Seed, settings).Generate();
        Directory.CreateDirectory(valid.ProjectPath);
        int files = WriteCommonBundle(valid.ProjectPath, request.Settings, world);
        files += UnityProjectBundle.Write(valid.ProjectPath);

        bool opened = false;
        string message = "Projeto criado com sucesso.";
        EngineDescriptor descriptor = Discover().First(x => x.Id == valid.Engine);
        if (request.OpenAfterCreate && descriptor.Detected && descriptor.ExecutablePath is not null)
        {
            OpenEngine(valid.Engine, descriptor.ExecutablePath, valid.ProjectPath, valid.ProjectName);
            opened = true;
            message = $"Projeto criado e aberto no {descriptor.Name}.";
        }
        else if (request.OpenAfterCreate)
            message = "Projeto criado. A engine não foi encontrada automaticamente; abra a pasta manualmente.";

        return new ProjectCreationResult(valid.Engine, valid.ProjectPath, files, opened, message, world.Fingerprint().ToString("X16"));
    }

    private static WorldSettings BuildSettings(GenerateRequest request)
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
        settings.WaterMode = request.WaterMode?.ToLowerInvariant() switch { "none" => WaterMode.None, "riversandlakes" => WaterMode.RiversAndLakes, _ => WaterMode.RiversOnly };
        BiomeSelection.Configure(settings, request.EnabledBiomes);
        return settings;
    }

    private static int WriteCommonBundle(string root, GenerateRequest settings, GeneratedWorld world)
    {
        string dataRoot = Path.Combine(root, "ProceduralWorld");
        Directory.CreateDirectory(dataRoot);
        TerrainReport report = TerrainDiagnostics.Analyze(world);
        SurfaceMap map = world.Surface;
        WriteText(Path.Combine(dataRoot, "world-config.json"), JsonSerializer.Serialize(new { generator = "ProceduralWorld.Core", world.GenerationVersion, settings, fingerprint = world.Fingerprint().ToString("X16"), report }, JsonOptions));
        WriteText(Path.Combine(dataRoot, "world-data.json"), JsonSerializer.Serialize(new
        {
            world.Seed, style = settings.Style, world.GenerationVersion, size = map.Size, map.CellSize, fingerprint = world.Fingerprint().ToString("X16"),
            minimumHeight = map.Height.Min(), maximumHeight = map.Height.Max(), height = map.Height,
            waterDepth = map.WaterDepth, biomes = map.Biomes.Select(x => (int)x).ToArray(),
            slope = map.Slope, flow = map.Flow, drainageReceiver = map.DrainageReceiver, riverChannel = map.RiverChannel, sediment = map.Sediment, caveOpening = map.CaveOpening,
            caveEntrances = world.Caves.Entrances.Select(e => new { e.X, e.Z, e.SurfaceY, e.CaveY, e.FacingX, e.FacingZ, e.Radius })
        }, JsonOptions));
        WriteRaw16(Path.Combine(dataRoot, "heightmap.r16"), map.Height);
        File.WriteAllBytes(Path.Combine(dataRoot, "biomes.raw8"), map.Biomes.Select(x => (byte)x).ToArray());
        WriteText(Path.Combine(root, "README-PROCEDURAL-WORLD.md"), $"# {settings.Style} procedural world\n\nSeed: `{world.Seed}`  \nFingerprint: `{world.Fingerprint():X16}`  \nResolution: `{map.Size} × {map.Size}`  \n\nProjeto Unity gerado pelo Procedural World Studio. Abra a pasta no Unity Hub ou execute ABRIR-UNITY.cmd. A cena e o personagem são construídos na primeira importação. Em Play: clique no jogo, WASD move, Shift corre, Espaço salta, mouse gira, Esc libera e R retorna. A pasta ProceduralWorld guarda dados e configuração independentes do editor.\n");
        return 5;
    }

    private static void OpenEngine(string engine, string executable, string projectPath, string projectName)
    {
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute=true, ArgumentList={"-force-d3d11","-projectPath",projectPath,"-executeMethod","FrameworkPreviewLauncher.OpenPreview"} });
    }
    private static ValidatedRequest Validate(ProjectIntegrationRequest request, bool inspectExisting)
    {
        string engine = request.Engine?.Trim().ToLowerInvariant() ?? "";
        if (engine != "unity") throw new ArgumentException("A integração disponível é Unity.");
        string name = Regex.Replace(request.ProjectName?.Trim() ?? "", "[^A-Za-z0-9 _-]", "");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Informe um nome de projeto válido.");
        if (string.IsNullOrWhiteSpace(request.Destination) || !Path.IsPathFullyQualified(request.Destination)) throw new ArgumentException("Informe uma pasta de destino absoluta.");
        string path = Path.GetFullPath(request.Destination);
        string? root = Path.GetPathRoot(path);
        if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Não é permitido criar o projeto diretamente na raiz de um disco.");
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if ((!string.IsNullOrEmpty(windows) && path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(programFiles) && path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Escolha uma pasta de projetos fora das pastas protegidas do sistema.");
        return new(engine, name, path);
    }

    private static IReadOnlyList<string> PlannedFiles(string engine, string projectName)
    {
        var files = new List<string> { "ProceduralWorld/world-config.json", "ProceduralWorld/world-data.json", "ProceduralWorld/heightmap.r16", "ProceduralWorld/biomes.raw8", "README-PROCEDURAL-WORLD.md" };
        files.AddRange(UnityProjectBundle.Files);
        return files;
    }

    private static EngineDescriptor Describe(string id, string name, string? path, string capability) => new(id, name, path is not null, path, capability, path is null ? "Não detectada" : "Instalada");
    private static string? FindExecutable(string variable, IEnumerable<string> candidates)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        return candidates.Where(File.Exists).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }
    private static IEnumerable<string> UnityCandidates()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
        if (Directory.Exists(root)) foreach (string file in Directory.EnumerateFiles(root, "Unity.exe", SearchOption.AllDirectories)) yield return file;
    }
    private static void WriteText(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false)); }
    private static void WriteRaw16(string path, float[] values)
    {
        float min = values.Min(), span = Math.Max(.0001f, values.Max() - min); byte[] bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) { ushort value = (ushort)Math.Clamp((int)MathF.Round((values[i] - min) / span * ushort.MaxValue), 0, ushort.MaxValue); bytes[i * 2] = (byte)(value & 255); bytes[i * 2 + 1] = (byte)(value >> 8); }
        File.WriteAllBytes(path, bytes);
    }

    private sealed record ValidatedRequest(string Engine, string ProjectName, string ProjectPath);

}
