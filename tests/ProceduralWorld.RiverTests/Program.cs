using System.Numerics;
using System.Text.Json;
using ProceduralWorld.Core;
using ProceduralWorld.Studio;

int checks = 0;
void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
RiverSurfaceChecks.Run(Check,args.Length>1?args[1]:null,args.Length>2?args[2]:null);
string output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "procedural-rivers-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(output);
var shallow = WaterPhysics.Sample(.85f, .2f, .4f, Vector2.UnitX);
var deep = WaterPhysics.Sample(0, 1, 2, Vector2.UnitX);
Check(shallow.InWater && !shallow.Swimming && shallow.Submersion < deep.Submersion, "Wading is proportional and distinct from swimming");
Check(deep.Swimming, "Deep water enables swimming");
Check(!WaterPhysics.Sample(0, float.NegativeInfinity, 2, Vector2.One).InWater, "Invalid surface is dry");
Check(!WaterPhysics.Sample(0, 2, 0, Vector2.One).InWater, "Zero depth is dry");
Check(WaterPhysics.HorizontalVelocity(Vector2.Zero, Vector2.Zero, Vector2.UnitX, shallow.Submersion, 1f/60).X > 0, "Shallow water transports the actor");
Check(WaterPhysics.HorizontalVelocity(Vector2.Zero, Vector2.Zero, Vector2.UnitX, 1, 1).X > WaterPhysics.HorizontalVelocity(Vector2.Zero, Vector2.Zero, Vector2.UnitX, .1f, 1).X, "Current coupling increases with submersion");
var threshold = WaterPhysics.Sample(.946f, 1, 2, Vector2.Zero);
Check(WaterPhysics.IsSwimming(threshold, true) && !WaterPhysics.IsSwimming(threshold, false), "Swimming hysteresis preserves the transition");
float y = -1, velocity = 0;
for (int i = 0; i < 600; i++) { var sample = WaterPhysics.Sample(y, 1, 3, Vector2.Zero); velocity = WaterPhysics.VerticalVelocity(velocity, sample.Submersion, 18, false, 1f/60); y += velocity/60; }
Check(Math.Abs(velocity)<.05f && y>0 && y<1, "Buoyancy converges at the waterline");

var service = new EngineIntegrationService();
Check(service.Discover().Count==1 && service.Discover()[0].Id=="unity","Unity is the only project integration");
foreach (string mode in new[] {"riversOnly","none","riversAndLakes"})
{
    var settings = new GenerateRequest(42,"Natural",65,520,1.12f,8,1.15f,1,1,mode,null);
    var request = new ProjectIntegrationRequest("unity",mode,Path.Combine(output,mode),false,false,true,settings);
    var plan = service.Preview(request);
    Check(plan.Files.Contains("Assets/ProceduralWorld/ThirdPersonExplorer.cs") && plan.Files.Contains("Assets/ProceduralWorld/Materials/RiverPreview.shader") && plan.Files.Contains("Assets/Plugins/ProceduralWorld.Core.dll"),"Preview lists river resources: " + mode);
    var result = service.Create(request);
    Check(result.FilesWritten == plan.Files.Count && plan.Files.All(file => File.Exists(Path.Combine(result.ProjectPath,file))),"Every previewed file is exported: " + mode);
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ProjectPath,"ProceduralWorld/world-data.json")));
    var data = json.RootElement;
    int count = data.GetProperty("size").GetInt32(); count *= count;
    Check(new[] {"height","biomes","flow","drainageReceiver","riverChannel"}.All(field => data.GetProperty(field).GetArrayLength()==count),"Complete hydrology reaches the adapter: " + mode);
    string builder = File.ReadAllText(Path.Combine(result.ProjectPath,"Assets/Editor/FrameworkPreviewBuilder.cs"));
    Check(builder.Contains("RiverSurfaceField") && builder.Contains("ThirdPersonSetup.Configure"),"Shared river field and explorer reach Unity: "+mode);
    Check(File.Exists(Path.Combine(result.ProjectPath,"Assets/StreamingAssets/ProceduralWorld/world-config.json")),"Generation settings reach Unity: "+mode);
    Check(plan.Warnings.Any(w=>w.Contains("lagos")) == (mode=="riversAndLakes"),"Lake rendering limitation is explicit: "+mode);
}
Console.WriteLine($"River framework: {checks} checks passed. Exports: {output}");
