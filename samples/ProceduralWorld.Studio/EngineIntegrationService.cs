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
        string? godot = FindExecutable("GODOT_BIN", GodotCandidates());
        string? unity = FindExecutable("UNITY_EDITOR", UnityCandidates());
        string? unreal = FindExecutable("UNREAL_EDITOR", UnrealCandidates());
        return new[]
        {
            Describe("godot", "Godot", godot, "Projeto completo com terreno, colisão, câmera e iluminação"),
            Describe("unity", "Unity", unity, "Projeto completo com Terrain, câmera e iluminação"),
            Describe("unreal", "Unreal Engine", unreal, "Projeto, heightmap RAW16 e pacote de importação"),
            new EngineDescriptor("generic", "Exportação genérica", true, null, "JSON, RAW16, biomas e dados de cavernas", "Sempre disponível")
        };
    }

    public ProjectPlan Preview(ProjectIntegrationRequest request)
    {
        ValidatedRequest valid = Validate(request, inspectExisting: true);
        EngineDescriptor engine = Discover().First(x => x.Id == valid.Engine);
        var warnings = new List<string>();
        if (!engine.Detected && valid.Engine != "generic") warnings.Add($"{engine.Name} não foi encontrada. O projeto será criado, mas não poderá ser aberto automaticamente.");
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
        files += valid.Engine switch
        {
            "godot" => WriteGodotProject(valid.ProjectPath, valid.ProjectName),
            "unity" => WriteUnityProject(valid.ProjectPath),
            "unreal" => WriteUnrealProject(valid.ProjectPath, valid.ProjectName),
            _ => 0
        };

        bool opened = false;
        string message = "Projeto criado com sucesso.";
        EngineDescriptor descriptor = Discover().First(x => x.Id == valid.Engine);
        if (request.OpenAfterCreate && descriptor.Detected && descriptor.ExecutablePath is not null)
        {
            OpenEngine(valid.Engine, descriptor.ExecutablePath, valid.ProjectPath, valid.ProjectName);
            opened = true;
            message = $"Projeto criado e aberto no {descriptor.Name}.";
        }
        else if (request.OpenAfterCreate && valid.Engine != "generic")
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
            world.Seed, style = settings.Style, world.GenerationVersion, size = map.Size, map.CellSize,
            minimumHeight = map.Height.Min(), maximumHeight = map.Height.Max(), height = map.Height,
            waterDepth = map.WaterDepth, biomes = map.Biomes.Select(x => (int)x).ToArray(),
            caveEntrances = world.Caves.Entrances.Select(e => new { e.X, e.Z, e.SurfaceY, e.CaveY, e.FacingX, e.FacingZ, e.Radius })
        }, JsonOptions));
        WriteRaw16(Path.Combine(dataRoot, "heightmap.r16"), map.Height);
        File.WriteAllBytes(Path.Combine(dataRoot, "biomes.raw8"), map.Biomes.Select(x => (byte)x).ToArray());
        WriteText(Path.Combine(root, "README-PROCEDURAL-WORLD.md"), $"# {settings.Style} procedural world\n\nSeed: `{world.Seed}`  \nFingerprint: `{world.Fingerprint():X16}`  \nResolution: `{map.Size} × {map.Size}`  \n\nGenerated by Procedural World Studio. The `ProceduralWorld` folder is engine-neutral and can be reimported without the Studio.\n");
        return 5;
    }

    private static int WriteGodotProject(string root, string projectName)
    {
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        WriteText(Path.Combine(root, "project.godot"), GodotProject(projectName));
        WriteText(Path.Combine(root, "main.tscn"), GodotScene);
        WriteText(Path.Combine(root, "scripts", "generated_world.gd"), GodotScript);
        return 3;
    }

    private static int WriteUnityProject(string root)
    {
        string assets = Path.Combine(root, "Assets", "ProceduralWorld");
        string streaming = Path.Combine(root, "Assets", "StreamingAssets", "ProceduralWorld");
        Directory.CreateDirectory(assets); Directory.CreateDirectory(streaming);
        File.Copy(Path.Combine(root, "ProceduralWorld", "world-data.json"), Path.Combine(streaming, "world-data.json"), true);
        WriteText(Path.Combine(assets, "GeneratedWorld.cs"), UnityScript);
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        WriteText(Path.Combine(root, "Packages", "manifest.json"), "{\n  \"dependencies\": {\n    \"com.unity.modules.terrain\": \"1.0.0\",\n    \"com.unity.modules.jsonserialize\": \"1.0.0\"\n  }\n}\n");
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        WriteText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1\n");
        return 4;
    }

    private static int WriteUnrealProject(string root, string projectName)
    {
        string safeName = Regex.Replace(projectName, "[^A-Za-z0-9_]", "");
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "ProceduralWorld";
        WriteText(Path.Combine(root, safeName + ".uproject"), "{\n  \"FileVersion\": 3,\n  \"EngineAssociation\": \"5.0\",\n  \"Category\": \"World Generation\",\n  \"Description\": \"Generated by Procedural World Studio\",\n  \"Plugins\": []\n}\n");
        WriteText(Path.Combine(root, "UNREAL-IMPORT.md"), "# Unreal import\n\nOpen the project, create a Landscape and import `ProceduralWorld/heightmap.r16`. Use the exact resolution stored in `world-config.json`. `biomes.raw8` can be imported as a weight map for material layers.\n");
        return 2;
    }

    private static void OpenEngine(string engine, string executable, string projectPath, string projectName)
    {
        ProcessStartInfo start = engine switch
        {
            "godot" => new(executable) { UseShellExecute = true, ArgumentList = { "--editor", "--path", projectPath } },
            "unity" => new(executable) { UseShellExecute = true, ArgumentList = { "-projectPath", projectPath } },
            "unreal" => new(executable) { UseShellExecute = true, ArgumentList = { Path.Combine(projectPath, Regex.Replace(projectName, "[^A-Za-z0-9_]", "") + ".uproject") } },
            _ => throw new ArgumentException("A exportação genérica não possui uma engine para abrir.")
        };
        Process.Start(start);
    }

    private static ValidatedRequest Validate(ProjectIntegrationRequest request, bool inspectExisting)
    {
        string engine = request.Engine?.Trim().ToLowerInvariant() ?? "";
        if (engine is not ("godot" or "unity" or "unreal" or "generic")) throw new ArgumentException("Selecione uma engine válida.");
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
        if (engine == "godot") files.AddRange(new[] { "project.godot", "main.tscn", "scripts/generated_world.gd" });
        if (engine == "unity") files.AddRange(new[] { "Assets/ProceduralWorld/GeneratedWorld.cs", "Assets/StreamingAssets/ProceduralWorld/world-data.json", "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt" });
        if (engine == "unreal") files.AddRange(new[] { Regex.Replace(projectName, "[^A-Za-z0-9_]", "") + ".uproject", "UNREAL-IMPORT.md" });
        return files;
    }

    private static EngineDescriptor Describe(string id, string name, string? path, string capability) => new(id, name, path is not null, path, capability, path is null ? "Não detectada" : "Instalada");
    private static string? FindExecutable(string variable, IEnumerable<string> candidates)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        return candidates.Where(File.Exists).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }
    private static IEnumerable<string> GodotCandidates()
    {
        foreach (string root in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Godot"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Godot") })
            if (Directory.Exists(root)) foreach (string file in Directory.EnumerateFiles(root, "Godot*.exe", SearchOption.TopDirectoryOnly).Where(x => !x.Contains("console", StringComparison.OrdinalIgnoreCase))) yield return file;
    }
    private static IEnumerable<string> UnityCandidates()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
        if (Directory.Exists(root)) foreach (string file in Directory.EnumerateFiles(root, "Unity.exe", SearchOption.AllDirectories)) yield return file;
    }
    private static IEnumerable<string> UnrealCandidates()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
        if (Directory.Exists(root)) foreach (string file in Directory.EnumerateFiles(root, "UnrealEditor.exe", SearchOption.AllDirectories)) yield return file;
    }

    private static void WriteText(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false)); }
    private static void WriteRaw16(string path, float[] values)
    {
        float min = values.Min(), span = Math.Max(.0001f, values.Max() - min); byte[] bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) { ushort value = (ushort)Math.Clamp((int)MathF.Round((values[i] - min) / span * ushort.MaxValue), 0, ushort.MaxValue); bytes[i * 2] = (byte)(value & 255); bytes[i * 2 + 1] = (byte)(value >> 8); }
        File.WriteAllBytes(path, bytes);
    }

    private sealed record ValidatedRequest(string Engine, string ProjectName, string ProjectPath);

    private static string GodotProject(string name) => $"; Generated by Procedural World Studio\nconfig_version=5\n\n[application]\nconfig/name=\"{name.Replace("\"", "")}\"\nrun/main_scene=\"res://main.tscn\"\n\n[display]\nwindow/size/viewport_width=1600\nwindow/size/viewport_height=900\nwindow/size/window_width_override=1600\nwindow/size/window_height_override=900\n\n[rendering]\nrenderer/rendering_method=\"gl_compatibility\"\nrenderer/rendering_method.mobile=\"gl_compatibility\"\n";
    private const string GodotScene = "[gd_scene load_steps=2 format=3]\n\n[ext_resource path=\"res://scripts/generated_world.gd\" type=\"Script\" id=\"1\"]\n\n[node name=\"GeneratedWorld\" type=\"Node3D\"]\nscript = ExtResource(\"1\")\n";
    private const string GodotScript = """
extends Node3D

func _ready() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string("res://ProceduralWorld/world-data.json"))
	if data == null: push_error("Invalid world-data.json"); return
	var size: int = int(data.size)
	var cell: float = float(data.cellSize)
	var heights: Array = data.height
	var biomes: Array = data.biomes
	var water: Array = data.waterDepth
	var vertices := PackedVector3Array(); var normals := PackedVector3Array(); var colors := PackedColorArray(); var uvs := PackedVector2Array(); var indices := PackedInt32Array()
	var palette := [Color("326d7b"),Color("315a78"),Color("d7c58e"),Color("b99769"),Color("a4a765"),Color("6d9a64"),Color("3f7657"),Color("2e6550"),Color("536d57"),Color("648075"),Color("a6aaa0"),Color("77746d"),Color("e2e6e4")]
	for z in size:
		for x in size:
			var i := z * size + x; var l: float = float(heights[z*size+maxi(0,x-1)]); var r: float = float(heights[z*size+mini(size-1,x+1)]); var d: float = float(heights[maxi(0,z-1)*size+x]); var u: float = float(heights[mini(size-1,z+1)*size+x])
			var altitude: float = inverse_lerp(float(data.minimumHeight),float(data.maximumHeight),float(heights[i])); var color: Color = palette[clampi(int(biomes[i]),0,palette.size()-1)].lerp(Color.WHITE,altitude*.12)
			if float(water[i]) > .01: color = Color("287a91").lerp(Color("6db9c7"),clampf(float(water[i])*.8,0.0,1.0))
			vertices.append(Vector3((x-(size-1)*.5)*cell,float(heights[i]),(z-(size-1)*.5)*cell)); normals.append(Vector3(l-r,cell*2.0,d-u).normalized()); colors.append(color); uvs.append(Vector2(float(x)/(size-1),float(z)/(size-1)))
	for z in size-1:
		for x in size-1:
			var i := z*size+x; indices.append_array(PackedInt32Array([i,i+size,i+1,i+1,i+size,i+size+1]))
	var arrays := []; arrays.resize(Mesh.ARRAY_MAX); arrays[Mesh.ARRAY_VERTEX]=vertices; arrays[Mesh.ARRAY_NORMAL]=normals; arrays[Mesh.ARRAY_COLOR]=colors; arrays[Mesh.ARRAY_TEX_UV]=uvs; arrays[Mesh.ARRAY_INDEX]=indices
	var mesh := ArrayMesh.new(); mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
	var material := StandardMaterial3D.new(); material.vertex_color_use_as_albedo=true; material.roughness=.92; material.cull_mode=BaseMaterial3D.CULL_DISABLED; mesh.surface_set_material(0,material)
	var instance := MeshInstance3D.new(); instance.mesh=mesh; add_child(instance)
	var body := StaticBody3D.new(); var collision := CollisionShape3D.new(); collision.shape=mesh.create_trimesh_shape(); body.add_child(collision); add_child(body)
	var light := DirectionalLight3D.new(); light.rotation_degrees=Vector3(-48,-34,0); light.shadow_enabled=true; light.light_energy=1.35; light.light_color=Color("ffe1b4"); add_child(light)
	var fill := DirectionalLight3D.new(); fill.rotation_degrees=Vector3(-22,145,0); fill.light_energy=.28; fill.light_color=Color("8ab9d5"); fill.shadow_enabled=false; add_child(fill)
	var extent: float = (size-1)*cell; var relief: float = float(data.maximumHeight)-float(data.minimumHeight)
	var camera := Camera3D.new(); camera.projection=Camera3D.PROJECTION_ORTHOGONAL; camera.size=extent*1.50; camera.far=10000.0; camera.position=Vector3(-extent*.72,float(data.maximumHeight)+extent*1.10,extent*.82); camera.look_at_from_position(camera.position,Vector3(0,float(data.minimumHeight)+relief*.31,0)); add_child(camera)
	var environment := WorldEnvironment.new(); var env := Environment.new(); env.background_mode=Environment.BG_COLOR; env.background_color=Color("263943"); env.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR; env.ambient_light_color=Color("a8c0c5"); env.ambient_light_energy=.62; env.tonemap_mode=Environment.TONE_MAPPER_FILMIC; environment.environment=env; add_child(environment)
	if "--capture" in OS.get_cmdline_user_args(): call_deferred("capture_preview")

func capture_preview() -> void:
	await get_tree().process_frame
	await get_tree().process_frame
	await RenderingServer.frame_post_draw
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://artifacts"))
	var image := get_viewport().get_texture().get_image()
	var error := image.save_png("res://artifacts/world-3d.png")
	print("CAPTURE_SAVED res://artifacts/world-3d.png error=", error)
	get_tree().quit()
""";
    private const string UnityScript = """
using System;
using System.IO;
using UnityEngine;

[Serializable] public class ProceduralWorldData { public int size; public float cellSize; public float minimumHeight; public float maximumHeight; public float[] height; }
public static class GeneratedWorldBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Build()
    {
        string path=Path.Combine(Application.streamingAssetsPath,"ProceduralWorld/world-data.json"); if(!File.Exists(path)) return;
        var data=JsonUtility.FromJson<ProceduralWorldData>(File.ReadAllText(path)); var terrainData=new TerrainData(); terrainData.heightmapResolution=data.size;
        float range=Mathf.Max(.01f,data.maximumHeight-data.minimumHeight); var heights=new float[data.size,data.size];
        for(int z=0;z<data.size;z++) for(int x=0;x<data.size;x++) heights[z,x]=(data.height[z*data.size+x]-data.minimumHeight)/range;
        terrainData.size=new Vector3((data.size-1)*data.cellSize,range,(data.size-1)*data.cellSize); terrainData.SetHeights(0,0,heights);
        var terrain=Terrain.CreateTerrainGameObject(terrainData); terrain.name="Procedural World"; terrain.transform.position=new Vector3(-terrainData.size.x*.5f,data.minimumHeight,-terrainData.size.z*.5f);
        if(Camera.main==null){var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera"; camera.transform.position=new Vector3(0,data.maximumHeight+terrainData.size.x*.6f,-terrainData.size.z*.65f); camera.transform.LookAt(Vector3.up*(data.minimumHeight+range*.35f));}
        if(UnityEngine.Object.FindFirstObjectByType<Light>()==null){var light=new GameObject("Sun").AddComponent<Light>(); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(50,-30,0);}
    }
}
""";
}
