using System.Numerics;

namespace ProceduralWorld.Core;

public readonly record struct TerrainVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, Biome Biome, GeologicalIdentity Geology);

public sealed class TerrainMeshData
{
    public TerrainMeshData(IReadOnlyList<TerrainVertex> vertices, IReadOnlyList<int> indices, int sampleStep, int skirtVertexCount = 0, int gridWidth = 0, int gridHeight = 0)
    { Vertices = vertices; Indices = indices; SampleStep = sampleStep; SkirtVertexCount = skirtVertexCount; GridWidth = gridWidth; GridHeight = gridHeight; }
    public IReadOnlyList<TerrainVertex> Vertices { get; }
    public IReadOnlyList<int> Indices { get; }
    public int SampleStep { get; }
    public int SkirtVertexCount { get; }
    public int GridWidth { get; }
    public int GridHeight { get; }
    public IReadOnlyList<TerrainVertex> Edge(TerrainEdge edge)
    {
        if (GridWidth < 2 || GridHeight < 2) throw new InvalidOperationException("Mesh grid dimensions are unavailable.");
        return edge switch
        {
            TerrainEdge.North => Enumerable.Range(0, GridWidth).Select(i => Vertices[i]).ToArray(),
            TerrainEdge.East => Enumerable.Range(0, GridHeight).Select(i => Vertices[i * GridWidth + GridWidth - 1]).ToArray(),
            TerrainEdge.South => Enumerable.Range(0, GridWidth).Reverse().Select(i => Vertices[(GridHeight - 1) * GridWidth + i]).ToArray(),
            _ => Enumerable.Range(0, GridHeight).Reverse().Select(i => Vertices[i * GridWidth]).ToArray()
        };
    }
}

public enum TerrainEdge { North, East, South, West }

public static class TerrainMeshBuilder
{
    public static TerrainMeshData Build(SurfaceMap map, int originX = 0, int originZ = 0, int size = 0, int sampleStep = 1, bool includeSkirts = false, float skirtDepth = 24)
    {
        if (sampleStep < 1) throw new ArgumentOutOfRangeException(nameof(sampleStep));
        if (skirtDepth <= 0) throw new ArgumentOutOfRangeException(nameof(skirtDepth));
        if (size == 0) size = map.Size - Math.Max(originX, originZ);
        if (originX < 0 || originZ < 0 || size < 2 || originX + size > map.Size || originZ + size > map.Size)
            throw new ArgumentOutOfRangeException(nameof(size));

        List<int> xs = SampleAxis(originX, size, sampleStep);
        List<int> zs = SampleAxis(originZ, size, sampleStep);
        var vertices = new List<TerrainVertex>(xs.Count * zs.Count + (includeSkirts ? (xs.Count + zs.Count) * 2 : 0));
        var indices = new List<int>((xs.Count - 1) * (zs.Count - 1) * 6 + (includeSkirts ? (xs.Count + zs.Count) * 12 : 0));
        foreach (int z in zs) foreach (int x in xs)
        {
            int i = map.Index(x, z);
            vertices.Add(new TerrainVertex(
                new Vector3(x * map.CellSize, map.Height[i], z * map.CellSize),
                NormalAt(map, x, z),
                new Vector2(x / (float)(map.Size - 1), z / (float)(map.Size - 1)),
                map.Biomes[i], map.Geology[i]));
        }
        int width = xs.Count;
        for (int z = 0; z < zs.Count - 1; z++) for (int x = 0; x < xs.Count - 1; x++)
        {
            int a = z * width + x, b = a + 1, c = a + width, d = c + 1;
            float diagonalAd = Math.Abs(vertices[a].Position.Y - vertices[d].Position.Y);
            float diagonalBc = Math.Abs(vertices[b].Position.Y - vertices[c].Position.Y);
            if (diagonalAd <= diagonalBc) { indices.Add(a); indices.Add(c); indices.Add(d); indices.Add(a); indices.Add(d); indices.Add(b); }
            else { indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d); }
        }
        int skirtStart = vertices.Count;
        if (includeSkirts)
        {
            AddSkirt(Enumerable.Range(0, width).ToArray());
            AddSkirt(Enumerable.Range(0, zs.Count).Select(row => row * width + width - 1).ToArray());
            AddSkirt(Enumerable.Range(0, width).Reverse().Select(column => (zs.Count - 1) * width + column).ToArray());
            AddSkirt(Enumerable.Range(0, zs.Count).Reverse().Select(row => row * width).ToArray());
        }
        return new TerrainMeshData(vertices, indices, sampleStep, vertices.Count - skirtStart, width, zs.Count);

        void AddSkirt(IReadOnlyList<int> edge)
        {
            int lowerStart = vertices.Count;
            foreach (int source in edge)
            {
                TerrainVertex vertex = vertices[source];
                vertices.Add(vertex with { Position = new Vector3(vertex.Position.X, vertex.Position.Y - skirtDepth, vertex.Position.Z) });
            }
            for (int i = 0; i + 1 < edge.Count; i++)
            {
                int topA = edge[i], topB = edge[i + 1], lowA = lowerStart + i, lowB = lowerStart + i + 1;
                indices.Add(topA); indices.Add(lowA); indices.Add(lowB);
                indices.Add(topA); indices.Add(lowB); indices.Add(topB);
            }
        }
    }

    public static int RecommendedSampleStep(float distanceInCells)
        => distanceInCells < 48 ? 1 : distanceInCells < 112 ? 2 : distanceInCells < 240 ? 4 : 8;

    private static List<int> SampleAxis(int origin, int size, int step)
    {
        var result = new List<int>(); int end = origin + size - 1;
        for (int value = origin; value < end; value += step) result.Add(value);
        if (result.Count == 0 || result[^1] != end) result.Add(end);
        return result;
    }

    private static Vector3 NormalAt(SurfaceMap map, int x, int z)
    {
        int x0 = Math.Max(0, x - 1), x1 = Math.Min(map.Size - 1, x + 1);
        int z0 = Math.Max(0, z - 1), z1 = Math.Min(map.Size - 1, z + 1);
        float dx = (map.HeightAt(x1, z) - map.HeightAt(x0, z)) / Math.Max(map.CellSize, (x1 - x0) * map.CellSize);
        float dz = (map.HeightAt(x, z1) - map.HeightAt(x, z0)) / Math.Max(map.CellSize, (z1 - z0) * map.CellSize);
        return Vector3.Normalize(new Vector3(-dx, 1, -dz));
    }
}

public static class TerrainSeamBuilder
{
    public static TerrainMeshData Stitch(IReadOnlyList<TerrainVertex> edgeA, IReadOnlyList<TerrainVertex> edgeB)
    {
        if (edgeA.Count < 2 || edgeB.Count < 2) throw new ArgumentException("Both edges need at least two vertices.");
        var vertices = edgeA.Concat(edgeB).ToArray();
        var indices = new List<int>((edgeA.Count + edgeB.Count - 2) * 3);
        int a = 0, b = 0, offset = edgeA.Count;
        while (a + 1 < edgeA.Count || b + 1 < edgeB.Count)
        {
            double nextA = a + 1 < edgeA.Count ? (a + 1d) / (edgeA.Count - 1) : double.PositiveInfinity;
            double nextB = b + 1 < edgeB.Count ? (b + 1d) / (edgeB.Count - 1) : double.PositiveInfinity;
            if (nextA <= nextB)
            {
                indices.Add(a); indices.Add(offset + b); indices.Add(++a);
            }
            else
            {
                indices.Add(a); indices.Add(offset + b); indices.Add(offset + ++b);
            }
        }
        return new TerrainMeshData(vertices, indices, Math.Min(edgeA.Count, edgeB.Count), 0, 0, 0);
    }
}
