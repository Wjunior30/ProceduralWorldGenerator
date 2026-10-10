namespace ProceduralWorld.Core;

public readonly record struct Rect2D(double X, double Y, double Width, double Height);
public readonly record struct Segment2D(double X1, double Y1, double X2, double Y2);
public sealed class LabyrinthWorldData
{
    public double CellSize { get; init; } = 1;
    public int Width { get; init; }
    public int Height { get; init; }
    public double WallHeight { get; init; } = 4;
    public List<Segment2D> Corridors { get; } = new();
    public List<Rect2D> Glades { get; } = new();
    public List<Rect2D> Arenas { get; } = new();
    public List<Rect2D> Niches { get; } = new();
    public List<Rect2D> Dungeons { get; } = new();
    public List<Segment2D> Drains { get; } = new();
    public List<Segment2D> MacroWalls { get; } = new();
    public List<Segment2D> DayWalls { get; } = new();
    public List<string> BiomeNames { get; } = new();
}
