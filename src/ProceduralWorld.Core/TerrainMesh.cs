using System.Numerics;

namespace ProceduralWorld.Core;

public readonly record struct TerrainVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, Biome Biome, GeologicalIdentity Geology);

public sealed class TerrainMeshData
{
    public TerrainMeshData(IReadOnlyList<TerrainVertex> vertices, IReadOnlyList<int> indices, int sampleStep)
    { Vertices = vertices; Indices = indices; SampleStep = sampleStep; }
    public IReadOnlyList<TerrainVertex> Vertices { get; }
    public IReadOnlyList<int> Indices { get; }
    public int SampleStep { get; }
}

public static class TerrainMeshBuilder
{
    public static TerrainMeshData Build(SurfaceMap map, int originX = 0, int originZ = 0, int size = 0, int sampleStep = 1)
    {
        if (sampleStep < 1) throw new ArgumentOutOfRangeException(nameof(sampleStep));
        if (size == 0) size = map.Size - Math.Max(originX, originZ);
        if (originX < 0 || originZ < 0 || size < 2 || originX + size > map.Size || originZ + size > map.Size)
            throw new ArgumentOutOfRangeException(nameof(size));

        var xs = SampleAxis(originX, size, sampleStep);
        var zs = SampleAxis(originZ, size, sampleStep);
        var vertices = new List<TerrainVertex>(xs.Count * zs.Count);
        var indices = new List<int>((xs.Count - 1) * (zs.Count - 1) * 6);
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
        return new TerrainMeshData(vertices, indices, sampleStep);
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
