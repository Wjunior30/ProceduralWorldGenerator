using ProceduralWorld.Core.Platforming;

namespace ProceduralWorld.Core.Layout2D;

public enum PlainsBlock : byte { Air, Grass, Dirt, Stone }

public sealed class PlainsLayout : IPlatformMap
{
    public const int CellsPerUnit = 2;
    public const float CellSize = .5f;
    public PlainsLayout(int widthUnits, int heightUnits, int seed)
    {
        Columns = widthUnits * CellsPerUnit; Rows = heightUnits * CellsPerUnit;
        Surface = new int[Columns]; Blocks = new PlainsBlock[Columns, Rows]; CaveMouth = new bool[Columns];
        var terrain = new FractalNoise(PositionalHash.Mix((ulong)seed), 4, .014);
        var detail = new FractalNoise(PositionalHash.Mix((ulong)seed ^ 77), 3, .055);
        for (int x = 0; x < Columns; x++)
        {
            int ground = Rows / 2 + (int)Math.Round((terrain.Sample(x, 0) * 4 + detail.Sample(x, 0) * .8) * CellsPerUnit);
            if (x > 0) ground = Math.Max(Surface[x - 1] - 1, Math.Min(Surface[x - 1] + 1, ground)); Surface[x] = ground;
            for (int y = 0; y <= ground; y++) Blocks[x, y] = y == ground ? PlainsBlock.Grass : y > ground - 14 ? PlainsBlock.Dirt : PlainsBlock.Stone;
        }
        int spacing = 65 * CellsPerUnit;
        for (int entrance = Columns / 2 + 40; entrance < Columns - 36; entrance += spacing) CarveEntrance(entrance, seed);
        for (int x = 0; x < Columns; x++) CaveMouth[x] = Blocks[x, Surface[x]] == PlainsBlock.Air;
    }
    public int Columns { get; }
    public int Rows { get; }
    public float TileSize => CellSize;
    public int[] Surface { get; }
    public PlainsBlock[,] Blocks { get; }
    public bool[] CaveMouth { get; }
    public bool Solid(int x, int y) => x < 0 || y < 0 || x >= Columns || y >= Rows || Blocks[x, y] != PlainsBlock.Air;
    public int Column(float worldX) => Math.Max(0, Math.Min(Columns - 1, (int)Math.Floor(worldX * CellsPerUnit)));
    public float Ground(float worldX) => (Surface[Column(worldX)] + 1) * CellSize;
    public bool TryFoundation(float left, float right, out int first, out int last, out int row)
    {
        first = Column(left); last = Column(right); row = Surface[first]; int low = row;
        if (left < CellSize || right > Columns * CellSize - CellSize) return false;
        for (int x = first; x <= last; x++) { if (CaveMouth[x]) return false; low = Math.Min(low, Surface[x]); row = Math.Max(row, Surface[x]); }
        if (row - low > 1) return false;
        for (int x = first; x <= last; x++) { for (int y = Surface[x]; y < row; y++) Blocks[x, y] = PlainsBlock.Dirt; Surface[x] = row; Blocks[x, row] = PlainsBlock.Grass; }
        return true;
    }
    public ulong Fingerprint() { ulong hash = 14695981039346656037UL; unchecked { foreach (var b in Blocks) hash = (hash ^ (byte)b) * 1099511628211UL; } return hash; }
    private void CarveEntrance(int start, int seed)
    {
        double top = Surface[start] + 1;
        for (int i = 0; i <= 48; i++)
        {
            double t = i / 48d, x = start + t * 38, y = top - t * 28 - Math.Sin(t * Math.PI) * 4; double rx = 2.5 + t * 1.5, ry = 2.2 + t;
            for (int cx = (int)(x - rx); cx <= x + rx; cx++) for (int cy = (int)(y - ry); cy <= y + ry; cy++)
                if (cx > 1 && cx < Columns - 2 && cy > 1 && cy < Rows - 1 && (cx - x) * (cx - x) / (rx * rx) + (cy - y) * (cy - y) / (ry * ry) < 1) Blocks[cx, cy] = PlainsBlock.Air;
        }
    }
}
