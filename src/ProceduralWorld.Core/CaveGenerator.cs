namespace ProceduralWorld.Core;

public sealed class CaveGenerator
{
    private readonly ulong _seed;
    private readonly FractalNoise _chambers, _tunnelA, _tunnelB;
    public CaveGenerator(long seed) { _seed = PositionalHash.Mix((ulong)seed); var r = new SplitMix64(_seed); _chambers = new(r.Next(), 3, .052); _tunnelA = new(r.Next(), 2, .072); _tunnelB = new(r.Next(), 2, .072); }

    public CaveVolume Generate(SurfaceMap surface, WorldSettings settings)
    {
        int size = surface.Size, depth = settings.CaveDepthCells; var caves = new CaveVolume(size, depth, size);
        double chamberThreshold = .33 - Math.Min(.09, (settings.CaveDensity - 1) * .08);
        double tunnelThreshold = .042 + Math.Min(.025, (settings.CaveDensity - 1) * .018);
        for (int z = 1; z < size - 1; z++) for (int x = 1; x < size - 1; x++) for (int y = 2; y < depth - 2; y++)
        {
            double chamber = _chambers.Sample(x, y * 1.28, z), spaghetti = Math.Max(Math.Abs(_tunnelA.Sample(x, y, z)), Math.Abs(_tunnelB.Sample(x, y, z)));
            double depthBias = .055 * (1 - y / (double)depth); if (chamber > chamberThreshold - depthBias || spaghetti < tunnelThreshold) caves.SetSolid(x, y, z, false);
        }
        var random = new SplitMix64(PositionalHash.Mix(_seed ^ 0xC0A7EUL)); var chosen = new List<(int X, int Z)>(); int wanted = Math.Max(1, settings.GuaranteedCaveEntrances);
        for (int e = 0, attempts = 0; e < wanted && attempts < wanted * 80; attempts++)
        {
            int x = 5 + random.Next(size - 10), z = 5 + random.Next(size - 10); int i = surface.Index(x, z);
            if (surface.WaterDepth[i] > 0 || chosen.Any(p => (p.X-x)*(p.X-x)+(p.Z-z)*(p.Z-z) < 100)) continue;
            if (surface.Slope[i] < .08f && attempts < wanted * 35) continue;
            (int fx, int fz) = DownhillFacing(surface, x, z); int targetY = 5 + random.Next(Math.Max(1, depth / 2 - 5));
            int surfaceY = (int)Math.Round(surface.HeightAt(x, z) / surface.CellSize); int radius = surface.Geology[i] == GeologicalIdentity.KarstHighland ? 3 : 2;
            CarveRamp(caves, x, z, depth - 2, targetY, fx, fz, radius); CarveChamber(caves, x + fx * 7, targetY, z + fz * 7, radius + 2, Math.Max(2, radius), radius + 2);
            caves.Entrances.Add(new(x, z, surfaceY, targetY, fx, fz, radius)); chosen.Add((x,z)); e++;
        }
        ConnectEntrances(caves); return caves;
    }
    private static (int X,int Z) DownhillFacing(SurfaceMap surface, int x, int z)
    {
        int bx=0,bz=1; float low=surface.HeightAt(x,z);
        for(int dz=-1;dz<=1;dz++) for(int dx=-1;dx<=1;dx++) { if(dx==0&&dz==0)continue; float h=surface.HeightAt(x+dx,z+dz); if(h<low){low=h;bx=dx;bz=dz;} }
        return (bx,bz);
    }
    private static void CarveRamp(CaveVolume caves,int x,int z,int startY,int targetY,int fx,int fz,int radius)
    {
        int vertical=Math.Max(1,startY-targetY), steps=vertical+10;
        for(int s=0;s<=steps;s++) { double t=s/(double)steps; int y=(int)Math.Round(startY+(targetY-startY)*Smooth(t)); int cx=Math.Max(radius+1,Math.Min(caves.Width-radius-2,x+fx*s/2)); int cz=Math.Max(radius+1,Math.Min(caves.Depth-radius-2,z+fz*s/2)); CarveEllipsoid(caves,cx,y,cz,radius+((s<5)?1:0),radius,radius+1); }
    }
    private static void ConnectEntrances(CaveVolume caves)
    {
        for(int i=1;i<caves.Entrances.Count;i++) { var a=caves.Entrances[i-1];var b=caves.Entrances[i];int steps=Math.Max(Math.Abs(b.X-a.X),Math.Max(Math.Abs(b.Z-a.Z),Math.Abs(b.CaveY-a.CaveY))); for(int s=0;s<=steps;s++){double t=steps==0?0:s/(double)steps;CarveEllipsoid(caves,(int)Math.Round(a.X+(b.X-a.X)*t),(int)Math.Round(a.CaveY+(b.CaveY-a.CaveY)*t),(int)Math.Round(a.Z+(b.Z-a.Z)*t),2,2,2);}}
    }
    private static void CarveChamber(CaveVolume c,int x,int y,int z,int rx,int ry,int rz)=>CarveEllipsoid(c,x,y,z,rx,ry,rz);
    private static void CarveEllipsoid(CaveVolume c,int cx,int cy,int cz,int rx,int ry,int rz)
    {
        for(int y=cy-ry;y<=cy+ry;y++) for(int z=cz-rz;z<=cz+rz;z++) for(int x=cx-rx;x<=cx+rx;x++) { if(x<0||y<0||z<0||x>=c.Width||y>=c.Height||z>=c.Depth)continue; double d=(x-cx)*(x-cx)/(double)(rx*rx)+(y-cy)*(y-cy)/(double)(ry*ry)+(z-cz)*(z-cz)/(double)(rz*rz); if(d<=1)c.SetSolid(x,y,z,false); }
    }
    private static double Smooth(double t)=>t*t*(3-2*t);
}
