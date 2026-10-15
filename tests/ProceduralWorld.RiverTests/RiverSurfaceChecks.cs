using System.Text.Json;
using ProceduralWorld.Core;

internal static class RiverSurfaceChecks
{
    public static void Run(Action<bool,string> check,string? dataPath,string? referencePath)
    {
        var dry=new SurfaceMap(17,8);Array.Fill(dry.Height,20);
        var empty=new RiverSurfaceField(dry);
        check(empty.Sample(0,0).Depth==0 && !empty.RefinedCells.Any(),"Empty network remains dry without refining the map");
        var map=new SurfaceMap(17,8);Array.Fill(map.Height,20);
        for(int z=0;z<17;z++){int i=z*17+8;map.RiverChannel[i]=true;map.Flow[i]=z+1;map.DrainageReceiver[i]=z<16?i+17:-1;}
        var field=new RiverSurfaceField(map);var center=field.Sample(0,0);
        check(center.Depth>.4f && center.Velocity.Y>0,"River corridor has a carved bed and downstream current");
        check(field.Sample(field.HalfExtent+1,0).Depth==0,"Queries outside the world are dry");
        check(map.Height.All(x=>x==20),"River field leaves generator heights untouched");
        check(field.Sample(.7f,.4f)==field.Sample(.7f,.4f),"Repeated refined queries are deterministic");
        check(field.RefinedCells.Count()<16*16,"Refinement remains local to the corridor");
        var lakeMap=new SurfaceMap(17,8);Array.Fill(lakeMap.Height,20);lakeMap.Height[lakeMap.Index(8,8)]=14;lakeMap.WaterDepth[lakeMap.Index(8,8)]=6;
        var lakeField=new RiverSurfaceField(lakeMap);var lake=lakeField.Sample(0,0);
        check(lake.Depth>5.9f && lake.Velocity.LengthSquared()==0,"Deep lagoons render as still water in the shared field");
        if(dataPath==null || referencePath==null)return;
        using var input=JsonDocument.Parse(File.ReadAllText(dataPath));var data=input.RootElement;
        var source=new SurfaceMap(data.GetProperty("size").GetInt32(),data.GetProperty("cellSize").GetSingle());
        data.GetProperty("height").EnumerateArray().Select(x=>x.GetSingle()).ToArray().CopyTo(source.Height,0);
        data.GetProperty("flow").EnumerateArray().Select(x=>x.GetSingle()).ToArray().CopyTo(source.Flow,0);
        data.GetProperty("riverChannel").EnumerateArray().Select(x=>x.GetBoolean()).ToArray().CopyTo(source.RiverChannel,0);
        data.GetProperty("drainageReceiver").EnumerateArray().Select(x=>x.GetInt32()).ToArray().CopyTo(source.DrainageReceiver,0);
        var approved=new RiverSurfaceField(source);
        using var references=JsonDocument.Parse(File.ReadAllText(referencePath));
        float maxHeight=0,maxVelocity=0;int count=0;
        foreach(var sample in references.RootElement.EnumerateArray()){
            var actual=approved.Sample(sample.GetProperty("x").GetSingle(),sample.GetProperty("z").GetSingle());
            maxHeight=Math.Max(maxHeight,Math.Abs(actual.Bed-sample.GetProperty("bed").GetSingle()));
            maxHeight=Math.Max(maxHeight,Math.Abs(actual.Surface-sample.GetProperty("surface").GetSingle()));
            var velocity=actual.Depth>.025f?actual.Velocity/WaterPhysics.CurrentSpeed:System.Numerics.Vector2.Zero;
            maxVelocity=Math.Max(maxVelocity,System.Numerics.Vector2.Distance(velocity,new(sample.GetProperty("currentX").GetSingle(),sample.GetProperty("currentZ").GetSingle())));count++;
        }
        check(count>100 && maxHeight<.003f && maxVelocity<.001f,$"C# corridor matches approved reference: {count} samples, height {maxHeight:F6}m, flow {maxVelocity:F7}");
    }
}
