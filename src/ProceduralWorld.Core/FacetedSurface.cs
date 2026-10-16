using System.Numerics;

namespace ProceduralWorld.Core;

// Faceted (low-poly) surface style: the same generated world shown as large flat-shaded triangles, one solid colour per
// face from a biome palette (Valheim-like). It is a presentation style over the SurfaceMap; generation does not change.
// Height(x, z) returns the exact faceted plane, so physics and AI can stand on what is drawn.
// Coordinates are metres from the map origin (x = column * CellSize), like TerrainMeshBuilder.

public enum TerrainSurfaceStyle { Smooth, Faceted }

public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Hex(string hex)
    {
        int v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new Rgb(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }
    public Rgb Lerp(Rgb o, float t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
    public Rgb Scale(float s) => new(Math.Min(1, R * s), Math.Min(1, G * s), Math.Min(1, B * s));
}

/// <summary>Colours of the faceted style; Default() is a sober, earthy palette (moss, olive, cold stone).</summary>
public sealed class FacetedPalette
{
    public Dictionary<Biome, Rgb> Ground { get; set; } = new();
    public Rgb Rock { get; set; } = Rgb.Hex("6b6660");
    public Rgb Snow { get; set; } = Rgb.Hex("e3e8ea");
    public Rgb RiverBed { get; set; } = Rgb.Hex("5a5245");
    public Rgb SeaFloor { get; set; } = Rgb.Hex("4a5c5a");
    /// <summary>Random brightness change per face (0.07 = ±7%) so large areas do not look flat.</summary>
    public float Variation { get; set; } = .07f;
    /// <summary>Slope (rise/run) where faces turn to rock, and where the change starts.</summary>
    public float RockSlope { get; set; } = .75f;
    public float RockSlopeStart { get; set; } = .45f;
    /// <summary>Height above sea where snow fully covers flat faces (0 = no snow).</summary>
    public float SnowLine { get; set; } = 780;

    public static FacetedPalette Default() => new()
    {
        Ground = new Dictionary<Biome, Rgb>
        {
            [Biome.Forest] = Rgb.Hex("4f5b3a"), [Biome.Jungle] = Rgb.Hex("3f5a35"), [Biome.Taiga] = Rgb.Hex("4a5640"),
            [Biome.Grassland] = Rgb.Hex("6b7445"), [Biome.Savanna] = Rgb.Hex("767048"), [Biome.Swamp] = Rgb.Hex("4d4f38"),
            [Biome.Desert] = Rgb.Hex("857158"), [Biome.Beach] = Rgb.Hex("a89c80"), [Biome.Tundra] = Rgb.Hex("7a7d6e"),
            [Biome.Rock] = Rgb.Hex("6b6660"), [Biome.Snow] = Rgb.Hex("e3e8ea"), [Biome.River] = Rgb.Hex("5a5245"), [Biome.Lake] = Rgb.Hex("5a5245"),
        },
    };
}

/// <summary>Flat-shaded triangle list: each triangle has its own 3 vertices (positions, normals, colours as xyz / rgb triplets).</summary>
public sealed class FacetedMesh
{
    public float[] Positions { get; init; } = Array.Empty<float>();
    public float[] Normals { get; init; } = Array.Empty<float>();
    public float[] Colors { get; init; } = Array.Empty<float>();
    public int[] Indices { get; init; } = Array.Empty<int>();
    public int Triangles => Indices.Length / 3;
}

public sealed class FacetedSurface
{
    readonly SurfaceMap _map; readonly float _sea; readonly ulong _seed;
    public float FacetSize { get; }
    /// <summary>Vertical jitter of facet corners in metres (organic low-poly look); 0 keeps the generated relief exactly.</summary>
    public float Roughness { get; }
    public FacetedPalette Palette { get; }

    public FacetedSurface(SurfaceMap map, float facetSize = 8, FacetedPalette? palette = null, float seaLevel = 0, ulong seed = 0, float roughness = .6f)
    {
        if (facetSize <= 0) throw new ArgumentOutOfRangeException(nameof(facetSize));
        _map = map; _sea = seaLevel; _seed = seed; FacetSize = facetSize; Roughness = roughness; Palette = palette ?? FacetedPalette.Default();
    }

    float Extent => (_map.Size - 1) * _map.CellSize;

    /// <summary>Height of a facet corner (lattice point i, j): smooth relief plus a small deterministic jitter.</summary>
    public float Corner(int i, int j)
    {
        float x = Math.Min(Extent, i * FacetSize), z = Math.Min(Extent, j * FacetSize);
        float h = Smooth(x, z);
        if (Roughness <= 0) return h;
        ulong k = (ulong)(uint)i * 0x9E3779B1UL ^ ((ulong)(uint)j << 32) ^ _seed;
        k ^= k >> 33; k *= 0xff51afd7ed558ccdUL; k ^= k >> 33;
        return h + ((k & 0xFFFF) / 65535f - .5f) * 2 * Roughness;
    }

    /// <summary>Exact height of the faceted surface (the plane of the triangle under the point).</summary>
    public float Height(float x, float z)
    {
        float fx = Math.Clamp(x, 0, Extent) / FacetSize, fz = Math.Clamp(z, 0, Extent) / FacetSize;
        int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz); float u = fx - i, v = fz - j;
        float h00 = Corner(i, j), h10 = Corner(i + 1, j), h01 = Corner(i, j + 1), h11 = Corner(i + 1, j + 1);
        if (((i + j) & 1) == 0) // diagonal 00-11
            return u >= v ? h00 + (h10 - h00) * u + (h11 - h10) * v : h00 + (h11 - h01) * u + (h01 - h00) * v;
        // diagonal 10-01
        return u + v <= 1 ? h00 + (h10 - h00) * u + (h01 - h00) * v : h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v);
    }

    /// <summary>Triangles covering [x0, x0+size] x [z0, z0+size] (metres), at this surface's facet size times the step.</summary>
    public FacetedMesh BuildChunk(float x0, float z0, float size, int step = 1)
    {
        float f = FacetSize; int i0 = (int)Math.Round(x0 / f), j0 = (int)Math.Round(z0 / f), n = Math.Max(1, (int)Math.Round(size / f / step));
        var pos = new List<float>(n * n * 18); var nor = new List<float>(n * n * 18); var col = new List<float>(n * n * 18); var idx = new List<int>(n * n * 6);
        for (int b = 0; b < n; b++) for (int a = 0; a < n; a++)
        {
            int i = i0 + a * step, j = j0 + b * step, s = step;
            var p00 = P(i, j); var p10 = P(i + s, j); var p01 = P(i, j + s); var p11 = P(i + s, j + s);
            if ((((i / s) + (j / s)) & 1) == 0) { Tri(p00, p11, p10); Tri(p00, p01, p11); }
            else { Tri(p00, p01, p10); Tri(p10, p01, p11); }
        }
        return new FacetedMesh { Positions = pos.ToArray(), Normals = nor.ToArray(), Colors = col.ToArray(), Indices = idx.ToArray() };

        Vector3 P(int i, int j) => new(i * f, Corner(i, j), j * f);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a)); if (normal.Y < 0) { (b, c) = (c, b); normal = -normal; }
            var color = FaceColor((a + b + c) / 3, normal);
            foreach (var v in new[] { a, b, c })
            {
                idx.Add(pos.Count / 3);
                pos.Add(v.X); pos.Add(v.Y); pos.Add(v.Z); nor.Add(normal.X); nor.Add(normal.Y); nor.Add(normal.Z); col.Add(color.R); col.Add(color.G); col.Add(color.B);
            }
        }
    }

    /// <summary>Colour of a face: biome ground, rock on steep faces, snow high up, river bed and sea floor; a little per-face variation.</summary>
    public Rgb FaceColor(Vector3 center, Vector3 normal)
    {
        int cx = Math.Clamp((int)Math.Round(center.X / _map.CellSize), 0, _map.Size - 1), cz = Math.Clamp((int)Math.Round(center.Z / _map.CellSize), 0, _map.Size - 1);
        int cell = _map.Index(cx, cz); var biome = _map.Biomes[cell];
        Rgb color;
        if (_map.Ocean[cell]) color = Palette.SeaFloor;
        else if (_map.WaterDepth[cell] > .2f) color = Palette.RiverBed;
        else
        {
            color = Palette.Ground.TryGetValue(biome, out var g) ? g : Palette.Rock;
            float slope = MathF.Sqrt(Math.Max(0, 1 - normal.Y * normal.Y)) / Math.Max(.05f, normal.Y);
            float rock = Math.Clamp((slope - Palette.RockSlopeStart) / Math.Max(.01f, Palette.RockSlope - Palette.RockSlopeStart), 0, 1);
            color = color.Lerp(Palette.Rock, rock);
            if (Palette.SnowLine > 0) { float snow = Math.Clamp((center.Y - _sea - Palette.SnowLine * .8f) / (Palette.SnowLine * .2f), 0, 1) * (1 - rock * .7f); color = color.Lerp(Palette.Snow, snow); }
        }
        ulong k = (ulong)(int)(center.X * 7.3f) * 0x9E3779B97F4A7C15UL ^ (ulong)(int)(center.Z * 3.1f) ^ _seed;
        k ^= k >> 31; k *= 0xbf58476d1ce4e5b9UL; k ^= k >> 29;
        return color.Scale(1 + ((k & 0xFFFF) / 65535f - .5f) * 2 * Palette.Variation);
    }

    // Bilinear sample of the generated relief.
    float Smooth(float x, float z)
    {
        float fx = Math.Clamp(x / _map.CellSize, 0, _map.Size - 1.001f), fz = Math.Clamp(z / _map.CellSize, 0, _map.Size - 1.001f);
        int ix = (int)fx, iz = (int)fz; float u = fx - ix, v = fz - iz;
        float a = _map.Height[_map.Index(ix, iz)], b = _map.Height[_map.Index(ix + 1, iz)], c = _map.Height[_map.Index(ix, iz + 1)], d = _map.Height[_map.Index(ix + 1, iz + 1)];
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    /// <summary>Faceted water: a flat grid at the given level whose corners an engine can raise with Wave(x, z, time).</summary>
    public static FacetedMesh WaterPlane(float x0, float z0, float size, float facet, float level)
    {
        int n = Math.Max(1, (int)Math.Round(size / facet)); var pos = new List<float>(); var idx = new List<int>();
        for (int b = 0; b < n; b++) for (int a = 0; a < n; a++)
        {
            float x = x0 + a * facet, z = z0 + b * facet;
            var quad = ((a + b) & 1) == 0
                ? new[] { (x, z), (x, z + facet), (x + facet, z + facet), (x, z), (x + facet, z + facet), (x + facet, z) }
                : new[] { (x, z), (x, z + facet), (x + facet, z), (x + facet, z), (x, z + facet), (x + facet, z + facet) };
            foreach (var (px, pz) in quad) { idx.Add(pos.Count / 3); pos.Add(px); pos.Add(level); pos.Add(pz); }
        }
        return new FacetedMesh { Positions = pos.ToArray(), Indices = idx.ToArray() };
    }

    /// <summary>Gentle low-poly swell (metres) for water corners at time t (seconds).</summary>
    public static float Wave(float x, float z, float t) => (MathF.Sin(x * .045f + t * .9f) + MathF.Sin(z * .06f - t * .7f) + MathF.Sin((x + z) * .03f + t * .5f)) * .18f;
}
