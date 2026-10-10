namespace ProceduralWorld.Core;

public enum CaveProfile { Karst, Fracture, Dissolution, VerticalShaft, SlotCanyon, LavaTube }
public enum CaveFormationKind { Stalactite, Stalagmite, Column, Flowstone, Breakdown, Skylight }
public sealed record CaveFormation(CaveFormationKind Kind, WorldPoint Position, double Scale);
public sealed record CaveChamber(WorldPoint Center, WorldPoint Radius, CaveProfile Profile);

public sealed class CaveRoute
{
    public CaveRoute(IEnumerable<WorldPoint> points, double radius, CaveProfile profile) { Points = points.ToArray(); Radius = radius; Profile = profile; if (Points.Count < 2) throw new ArgumentException(); }
    public IReadOnlyList<WorldPoint> Points { get; }
    public double Radius { get; }
    public CaveProfile Profile { get; }
}

public sealed class ImplicitCaveSystem
{
    private readonly List<CaveRoute> _routes = new();
    private readonly List<CaveChamber> _chambers = new();
    private readonly List<CaveFormation> _formations = new();
    public IReadOnlyList<CaveRoute> Routes => _routes;
    public IReadOnlyList<CaveChamber> Chambers => _chambers;
    public IReadOnlyList<CaveFormation> Formations => _formations;
    public void AddRoute(CaveRoute route) => _routes.Add(route);
    public void AddChamber(CaveChamber chamber) => _chambers.Add(chamber);
    public void AddFormation(CaveFormation formation) => _formations.Add(formation);
    public double SignedDistance(WorldPoint p)
    {
        double d = double.MaxValue;
        foreach (var route in _routes) for (int i = 0; i + 1 < route.Points.Count; i++) d = Math.Min(d, Capsule(p, route.Points[i], route.Points[i + 1]) - route.Radius);
        foreach (var c in _chambers)
        {
            double dx = (p.X - c.Center.X) / Math.Max(.001, c.Radius.X), dy = (p.Y - c.Center.Y) / Math.Max(.001, c.Radius.Y), dz = (p.Z - c.Center.Z) / Math.Max(.001, c.Radius.Z);
            d = Math.Min(d, (Math.Sqrt(dx * dx + dy * dy + dz * dz) - 1) * Math.Min(c.Radius.X, Math.Min(c.Radius.Y, c.Radius.Z)));
        }
        return d;
    }
    public bool IsInside(WorldPoint p) => SignedDistance(p) <= 0;
    public bool HasNavigableEntrance(double minimumRadius) => _routes.Any(x => x.Radius >= minimumRadius && x.Points[0].Y > x.Points.Skip(1).Min(p => p.Y));
    private static double Capsule(WorldPoint p, WorldPoint a, WorldPoint b)
    {
        double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
        double t = ((p.X - a.X) * x + (p.Y - a.Y) * y + (p.Z - a.Z) * z) / Math.Max(.000001, x * x + y * y + z * z); t = Math.Max(0, Math.Min(1, t));
        double dx = p.X - (a.X + x * t), dy = p.Y - (a.Y + y * t), dz = p.Z - (a.Z + z * t); return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public static class CaveRouteFactory
{
    public static CaveRoute DescendingEntrance(long seed, WorldPoint surface, double length, double radius, CaveProfile profile)
    {
        var random = new SplitMix64(PositionalHash.Mix((ulong)seed)); var points = new List<WorldPoint> { surface };
        for (int i = 1; i <= 8; i++)
        {
            double t = i / 8d, bend = (random.NextDouble() - .5) * radius * 1.4;
            points.Add(new(surface.X + length * t + bend, surface.Y - length * (.25 + .55 * t) * t, surface.Z + Math.Sin(t * Math.PI * 2) * radius + bend * .5));
        }
        return new(points, radius, profile);
    }
}

