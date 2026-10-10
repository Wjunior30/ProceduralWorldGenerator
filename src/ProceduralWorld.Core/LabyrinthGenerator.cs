namespace ProceduralWorld.Core;

public sealed class LabyrinthGenerator
{
    private readonly ulong _seed;
    public LabyrinthGenerator(long seed) => _seed = PositionalHash.Mix((ulong)seed ^ 0x1AB71A7UL);

    public LabyrinthPlan Generate(int requestedSize)
    {
        int size = Math.Max(9, requestedSize | 1); var plan = new LabyrinthPlan(size, size); var random = new SplitMix64(_seed);
        var stack = new Stack<(int X, int Y)>(); plan[1, 1] = LabyrinthCell.Corridor; stack.Push((1, 1));
        var directions = new[] { (X: 2, Y: 0), (X: -2, Y: 0), (X: 0, Y: 2), (X: 0, Y: -2) };
        while (stack.Count > 0)
        {
            var current = stack.Peek(); var options = new List<(int X, int Y)>();
            foreach (var d in directions)
            {
                int nx = current.X + d.X, ny = current.Y + d.Y;
                if (nx > 0 && ny > 0 && nx < size - 1 && ny < size - 1 && plan[nx, ny] == LabyrinthCell.Wall) options.Add((nx, ny));
            }
            if (options.Count == 0) { stack.Pop(); continue; }
            var next = options[random.Next(options.Count)]; plan[(current.X + next.X) / 2, (current.Y + next.Y) / 2] = LabyrinthCell.Corridor;
            plan[next.X, next.Y] = LabyrinthCell.Corridor; stack.Push(next);
        }
        CarveRoom(plan, size / 2, size / 2, 5, 5, LabyrinthCell.Arena);
        int roomCount = Math.Max(2, size / 12);
        for (int i = 0; i < roomCount; i++)
        {
            int x = 3 + random.Next(size - 6), y = 3 + random.Next(size - 6); CarveRoom(plan, x, y, 3, 3, LabyrinthCell.Room);
        }
        for (int y = 2; y < size - 2; y++) for (int x = 2; x < size - 2; x++)
            if (plan[x, y] == LabyrinthCell.Wall && AdjacentWalkable(plan, x, y) == 1 && (PositionalHash.Of(_seed, x, y) % 23 == 0)) plan[x, y] = LabyrinthCell.Niche;
        return plan;
    }

    private static void CarveRoom(LabyrinthPlan plan, int centerX, int centerY, int width, int height, LabyrinthCell cell)
    {
        for (int y = centerY - height / 2; y <= centerY + height / 2; y++) for (int x = centerX - width / 2; x <= centerX + width / 2; x++)
            if (x > 0 && y > 0 && x < plan.Width - 1 && y < plan.Height - 1) plan[x, y] = cell;
    }
    private static int AdjacentWalkable(LabyrinthPlan plan, int x, int y)
    {
        int count = 0; if (plan[x - 1, y] != LabyrinthCell.Wall) count++; if (plan[x + 1, y] != LabyrinthCell.Wall) count++;
        if (plan[x, y - 1] != LabyrinthCell.Wall) count++; if (plan[x, y + 1] != LabyrinthCell.Wall) count++; return count;
    }
}

