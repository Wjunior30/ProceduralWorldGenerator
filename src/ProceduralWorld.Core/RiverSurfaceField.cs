using System.Numerics;

namespace ProceduralWorld.Core;

public readonly record struct RiverSurfaceSample(float Bed, float Surface, Vector2 Velocity, float WetBank, float Rapids)
{
    public float Depth => Math.Max(0, Surface - Bed);
    internal static RiverSurfaceSample Blend(RiverSurfaceSample a, RiverSurfaceSample b, RiverSurfaceSample c, Vector3 w)
        => new(a.Bed*w.X+b.Bed*w.Y+c.Bed*w.Z,a.Surface*w.X+b.Surface*w.Y+c.Surface*w.Z,a.Velocity*w.X+b.Velocity*w.Y+c.Velocity*w.Z,a.WetBank*w.X+b.WetBank*w.Y+c.WetBank*w.Z,a.Rapids*w.X+b.Rapids*w.Y+c.Rapids*w.Z);
}
public readonly record struct RiverSurfaceSegment(Vector2 A, Vector2 B, float LevelA, float LevelB, float Width, float Depth, float Speed, float Rapids);

/// <summary>Engine-neutral port of the approved SimulationLife river corridor. Coordinates are centred metres; velocity is m/s. Create one instance per world/worker.</summary>
public sealed class RiverSurfaceField
{
    private const float Bucket=32, Bank=12;
    private readonly float[] source;
    private readonly List<RiverSurfaceSegment> segments=new();
    private readonly Dictionary<(int,int),List<int>> buckets=new();
    private readonly HashSet<(int,int)> refined=new();
    private readonly Dictionary<(int,int),RiverSurfaceSample> cache=new();
    public int Size { get; }
    public float CellSize { get; }
    public float HalfExtent { get; }
    public float Step => CellSize/4;
    public IReadOnlyList<RiverSurfaceSegment> Segments => segments;
    public IEnumerable<(int X,int Z)> RefinedCells => refined;

    public RiverSurfaceField(SurfaceMap map)
    {
        if(map.Size<2||!float.IsFinite(map.CellSize)||map.CellSize<=0)throw new ArgumentException("Invalid surface map",nameof(map));
        Size=map.Size;CellSize=map.CellSize;HalfExtent=(Size-1)*CellSize*.5f;source=(float[])map.Height.Clone();
        var levels=(float[])source.Clone();float maximum=Math.Max(1,map.Flow.Max());
        float Amount(int i)=>(float)(Math.Log(1+Math.Max(0,map.Flow[i]))/Math.Log(1+maximum));
        var nodes=new Dictionary<int,Vector2>();var incoming=new Dictionary<int,List<int>>();
        for(int i=0;i<source.Length;i++) if(map.RiverChannel[i]) {
            nodes[i]=new Vector2(i%Size*CellSize-HalfExtent,i/Size*CellSize-HalfExtent);
            levels[i]-=Lerp(.12f,.85f,Amount(i));int r=map.DrainageReceiver[i];
            if(r>=source.Length||r< -1)throw new ArgumentException("Invalid drainage receiver",nameof(map));
            if(r>=0){if(!incoming.TryGetValue(r,out var list)) incoming[r]=list=new List<int>();list.Add(i);}
        }
        var ordered=nodes.Keys.OrderBy(i=>map.Flow[i]).ToArray();
        for(int pass=0;pass<16;pass++) {
            bool changed=false;
            foreach(int i in ordered){int r=map.DrainageReceiver[i];if(!nodes.ContainsKey(r))continue;
                float drop=Math.Max(.0005f,Vector2.Distance(nodes[i],nodes[r])*.00005f);
                if(levels[r]>levels[i]-drop){levels[r]=levels[i]-drop;changed=true;}}
            if(!changed)break;
        }
        var smooth=new Dictionary<int,Vector2>(nodes);
        foreach(var node in nodes) {
            int i=node.Key,r=map.DrainageReceiver[i];
            if(nodes.ContainsKey(r)&&incoming.TryGetValue(i,out var previous)&&previous.Count==1){
                Vector2 tangent=Normalize(nodes[r]-nodes[previous[0]]),side=new(-tangent.Y,tangent.X),p=node.Value;
                smooth[i]=p*.5f+(nodes[previous[0]]+nodes[r])*.25f+side*(float)(Math.Sin(p.X*.021+p.Y*.013)*2.2+Math.Sin(p.Y*.037-p.X*.014)*.8);
            }
            smooth[i]=Vector2.Clamp(smooth[i],new Vector2(-HalfExtent),new Vector2(HalfExtent));
        }
        foreach(int i in nodes.Keys) {
            int r=map.DrainageReceiver[i];if(!nodes.ContainsKey(r))continue;
            Vector2 a=smooth[i],b=smooth[r];float amount=Amount(i);
            float width=Lerp(4,11,(float)Math.Pow(amount,.8)),depth=Lerp(.5f,2.7f,(float)Math.Pow(amount,.72));
            float slope=Math.Max(0,levels[i]-levels[r])/Math.Max(Vector2.Distance(a,b),1);
            var segment=new RiverSurfaceSegment(a,b,levels[i],levels[r],width,depth,Clamp(.65f+amount*1.3f+(float)Math.Sqrt(slope)*2,.5f,3.2f),Smooth(.04f,.4f,slope));
            int id=segments.Count;segments.Add(segment);
            float radius=width+Bank+CellSize*2;var lo=Vector2.Min(a,b)-new Vector2(radius);var hi=Vector2.Max(a,b)+new Vector2(radius);
            for(int z=Floor(lo.Y/Bucket);z<=Floor(hi.Y/Bucket);z++)for(int x=Floor(lo.X/Bucket);x<=Floor(hi.X/Bucket);x++){
                if(!buckets.TryGetValue((x,z),out var list))buckets[(x,z)]=list=new List<int>();list.Add(id);}
            for(int z=Math.Max(0,Floor((lo.Y+HalfExtent)/CellSize));z<Math.Min(Size-1,(int)Math.Ceiling((hi.Y+HalfExtent)/CellSize));z++)
                for(int x=Math.Max(0,Floor((lo.X+HalfExtent)/CellSize));x<Math.Min(Size-1,(int)Math.Ceiling((hi.X+HalfExtent)/CellSize));x++)refined.Add((x,z));
        }
    }

    public RiverSurfaceSample Sample(float x,float z)
    {
        var p=new Vector2(x,z);
        if(!float.IsFinite(x)||!float.IsFinite(z))throw new ArgumentOutOfRangeException(nameof(x));
        if(Math.Abs(x)>HalfExtent||Math.Abs(z)>HalfExtent)return Dry(Base(p));
        float gx=(x+HalfExtent)/CellSize,gz=(z+HalfExtent)/CellSize;
        int cx=Math.Max(0,Math.Min(Size-2,Floor(gx))),cz=Math.Max(0,Math.Min(Size-2,Floor(gz)));
        if(!refined.Contains((cx,cz))){float tx=Clamp(gx-cx,0,1),tz=Clamp(gz-cz,0,1);
            float a=source[cz*Size+cx],b=source[cz*Size+cx+1],c=source[(cz+1)*Size+cx],d=source[(cz+1)*Size+cx+1];
            return Dry(tx+tz<=1?a*(1-tx-tz)+b*tx+c*tz:d*(tx+tz-1)+c*(1-tx)+b*(1-tz));}
        float px=x/Step,pz=z/Step;int ix=Floor(px),iz=Floor(pz);float u=px-ix,v=pz-iz;
        if(u==0&&v==0)return Lattice(ix,iz);
        return u+v<=1?RiverSurfaceSample.Blend(Lattice(ix,iz),Lattice(ix+1,iz),Lattice(ix,iz+1),new(1-u-v,u,v))
            :RiverSurfaceSample.Blend(Lattice(ix+1,iz+1),Lattice(ix,iz+1),Lattice(ix+1,iz),new(u+v-1,1-u,1-v));
    }
    private RiverSurfaceSample Lattice(int x,int z){if(!cache.TryGetValue((x,z),out var value)){value=Raw(new Vector2(x*Step,z*Step));cache[(x,z)]=value;}return value;}
    private RiverSurfaceSample Raw(Vector2 point)
    {
        float ground=Base(point);
        if(!buckets.TryGetValue((Floor(point.X/Bucket),Floor(point.Y/Bucket)),out var ids))return Dry(ground);
        float nearest=float.PositiveInfinity,width=0,weightSum=0,level=0,depth=0,rapid=0;var velocity=Vector2.Zero;
        foreach(int id in ids){var s=segments[id];var ab=s.B-s.A;float t=Clamp(Vector2.Dot(point-s.A,ab)/Math.Max(ab.LengthSquared(),.01f),0,1);
            float distance=Vector2.Distance(point,s.A+ab*t);if(distance>s.Width+Bank)continue;
            float ratio=distance/s.Width;if(ratio<nearest){nearest=ratio;width=s.Width;}
            float weight=(float)Math.Pow(Math.Max(0,1-distance/(s.Width+Bank)),4);
            level+=Lerp(s.LevelA,s.LevelB,t)*weight;depth+=s.Depth*weight;velocity+=Normalize(ab)*s.Speed*weight;rapid+=s.Rapids*weight;weightSum+=weight;
        }
        if(weightSum<.00001f)return Dry(ground);
        level/=weightSum;depth/=weightSum;velocity/=weightSum;rapid/=weightSum;float bed,wet;
        if(nearest<1){float section=1-Smooth(.12f,1,nearest);bed=level-depth*section;wet=1;velocity*=Lerp(.18f,1,(float)Math.Sqrt(section));}
        else{float distance=(nearest-1)*width;bed=Lerp(level,ground,Smooth(0,Bank,distance));wet=1-Smooth(0,3,distance);level=bed;velocity=Vector2.Zero;}
        return new(bed,level,velocity,wet,rapid);
    }
    private float Base(Vector2 p){float gx=(p.X+HalfExtent)/CellSize,gz=(p.Y+HalfExtent)/CellSize;int x=Math.Max(0,Math.Min(Size-2,Floor(gx))),z=Math.Max(0,Math.Min(Size-2,Floor(gz)));float u=Clamp(gx-x,0,1),v=Clamp(gz-z,0,1);return Lerp(Lerp(source[z*Size+x],source[z*Size+x+1],u),Lerp(source[(z+1)*Size+x],source[(z+1)*Size+x+1],u),v);}
    private static RiverSurfaceSample Dry(float height)=>new(height,height,Vector2.Zero,0,0);
    private static int Floor(float value)=>(int)Math.Floor(value);
    private static Vector2 Normalize(Vector2 value)=>value.LengthSquared()<.0000001f?Vector2.Zero:Vector2.Normalize(value);
    private static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v));
    private static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    private static float Smooth(float a,float b,float value){float t=Clamp((value-a)/(b-a),0,1);return t*t*(3-2*t);}
}
