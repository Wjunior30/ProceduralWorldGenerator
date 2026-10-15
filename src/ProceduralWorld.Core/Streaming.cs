namespace ProceduralWorld.Core;

public enum ChunkDetail { Collision, High, Medium, Low, Horizon }
public readonly record struct ChunkCoordinate(int X, int Z);
public sealed record ChunkRequest(ChunkCoordinate Coordinate, ChunkDetail Detail, int SampleStep, bool NeedsCollision);

public sealed class StreamingSettings
{
    public int ChunkSize { get; set; } = 256;
    public int LoadRadius { get; set; } = 3;
    public int HighDetailRadius { get; set; } = 2;
    public int CollisionRadius { get; set; } = 1;
    /// <summary>Rings beyond LoadRadius up to this radius are planned as Horizon impostors; at or below LoadRadius disables them.</summary>
    public int HorizonRadius { get; set; }
    public int MediumStep { get; set; } = 8;
    public int LowStep { get; set; } = 16;
    public int HorizonStep { get; set; } = 64;
}

public sealed class StreamingPlanner
{
    public IReadOnlyList<ChunkRequest> Plan(double worldX, double worldZ, StreamingSettings settings)
    {
        int cx = (int)Math.Floor(worldX / settings.ChunkSize), cz = (int)Math.Floor(worldZ / settings.ChunkSize);
        var requests = new List<ChunkRequest>();
        int radius = Math.Max(settings.LoadRadius, settings.HorizonRadius);
        for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
        {
            int ring = Math.Max(Math.Abs(dx), Math.Abs(dz));
            ChunkDetail detail = ring <= settings.CollisionRadius ? ChunkDetail.Collision : ring <= settings.HighDetailRadius ? ChunkDetail.High
                : ring < settings.LoadRadius ? ChunkDetail.Medium : ring == settings.LoadRadius ? ChunkDetail.Low : ChunkDetail.Horizon;
            int step = detail switch
            {
                ChunkDetail.Collision or ChunkDetail.High => 1,
                ChunkDetail.Medium => settings.MediumStep,
                ChunkDetail.Low => settings.LowStep,
                _ => settings.HorizonStep,
            };
            requests.Add(new(new(cx + dx, cz + dz), detail, step, ring <= settings.CollisionRadius));
        }
        return requests.OrderBy(x => Math.Max(Math.Abs(x.Coordinate.X - cx), Math.Abs(x.Coordinate.Z - cz))).ThenBy(x => x.Coordinate.Z).ThenBy(x => x.Coordinate.X).ToArray();
    }
}

public interface IHeightSource { double HeightAt(double x, double z); }
public sealed class FixedHeightSnapshot : IHeightSource
{
    private readonly float[] _height;
    public FixedHeightSnapshot(int size, double cellSize, IEnumerable<float> height) { Size = size; CellSize = cellSize; _height = height.ToArray(); if (_height.Length != size * size) throw new ArgumentException(); }
    public int Size { get; }
    public double CellSize { get; }
    public double HeightAt(double x, double z)
    {
        double gx = Math.Max(0, Math.Min(Size - 1.001, x / CellSize)), gz = Math.Max(0, Math.Min(Size - 1.001, z / CellSize)); int x0 = (int)gx, z0 = (int)gz, x1 = Math.Min(Size - 1, x0 + 1), z1 = Math.Min(Size - 1, z0 + 1); double tx = gx - x0, tz = gz - z0;
        double a = _height[z0 * Size + x0] + (_height[z0 * Size + x1] - _height[z0 * Size + x0]) * tx;
        double b = _height[z1 * Size + x0] + (_height[z1 * Size + x1] - _height[z1 * Size + x0]) * tx; return a + (b - a) * tz;
    }
}
