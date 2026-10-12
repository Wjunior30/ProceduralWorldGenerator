namespace ProceduralWorld.Core;

public readonly record struct WorldPoint(double X, double Y, double Z)
{
    public double DistanceXZ(WorldPoint p) { double dx = X - p.X, dz = Z - p.Z; return Math.Sqrt(dx * dx + dz * dz); }
}

public sealed class RiverChannel
{
    public const double MaximumHydraulicGrade = .08;
    public RiverChannel(string id, IEnumerable<WorldPoint> points, double baseWidth)
    {
        Id = id; var profile = points.ToArray(); BaseWidth = baseWidth;
        if (profile.Length < 2) throw new ArgumentException("A river needs at least two points.");
        for (int i = 1; i < profile.Length; i++)
            if (profile[i].Y > profile[i - 1].Y + .001) throw new InvalidOperationException("River water must not flow uphill.");
        // Lower upstream shelves until every segment follows a renderable grade.
        // This prevents an engine adapter from joining valid samples with a
        // vertical sheet of water while never raising water above its input.
        for (int i = profile.Length - 2; i >= 0; i--)
        {
            double horizontal = profile[i].DistanceXZ(profile[i + 1]);
            double ceiling = profile[i + 1].Y + horizontal * MaximumHydraulicGrade;
            if (profile[i].Y > ceiling) profile[i] = new WorldPoint(profile[i].X, ceiling, profile[i].Z);
        }
        Points = profile;
    }
    public string Id { get; }
    public IReadOnlyList<WorldPoint> Points { get; }
    public double BaseWidth { get; }
    public double WidthAt(double progress) => BaseWidth * (.65 + .7 * Math.Max(0, Math.Min(1, progress)));
    public double DistanceTo(double x, double z, out double progress)
    {
        double best = double.MaxValue; progress = 0; double total = 0, before = 0;
        var lengths = new double[Points.Count - 1];
        for (int i = 0; i < lengths.Length; i++) { lengths[i] = Points[i].DistanceXZ(Points[i + 1]); total += lengths[i]; }
        for (int i = 0; i < lengths.Length; i++)
        {
            var a = Points[i]; var b = Points[i + 1]; double dx = b.X - a.X, dz = b.Z - a.Z;
            double t = Math.Max(0, Math.Min(1, ((x - a.X) * dx + (z - a.Z) * dz) / Math.Max(.0001, dx * dx + dz * dz)));
            double px = a.X + dx * t, pz = a.Z + dz * t, d = Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            if (d < best) { best = d; progress = total <= 0 ? 0 : (before + lengths[i] * t) / total; }
            before += lengths[i];
        }
        return best;
    }
    public double WaterSurface(double progress)
    {
        progress = Math.Max(0, Math.Min(1, progress)); double scaled = progress * (Points.Count - 1); int i = Math.Min(Points.Count - 2, (int)scaled);
        double t = scaled - i; return Points[i].Y + (Points[i + 1].Y - Points[i].Y) * t;
    }
}

public sealed class RiverNetwork
{
    private readonly List<RiverChannel> _channels = new();
    public IReadOnlyList<RiverChannel> Channels => _channels;
    public void Add(RiverChannel channel)
    {
        _channels.Add(channel);
    }
    public double Carve(double x, double z, double terrainHeight)
    {
        double result = terrainHeight;
        foreach (var river in _channels)
        {
            double d = river.DistanceTo(x, z, out double p), width = river.WidthAt(p);
            if (d >= width * 2) continue;
            double influence = 1 - Math.Min(1, d / (width * 2)); influence = influence * influence * (3 - 2 * influence);
            double bed = river.WaterSurface(p) - Math.Max(1.5, width * .12);
            result = Math.Min(result, terrainHeight + (bed - terrainHeight) * influence);
        }
        return result;
    }
}
