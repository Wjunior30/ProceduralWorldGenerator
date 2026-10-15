using System.IO.Compression;
using ProceduralWorld.Core;

// Renders a world to PNG: top-down hillshaded map (left) and oblique voxel-space view (right).
static class WorldRender
{
    static readonly Dictionary<Biome, (int R, int G, int B)> Colors = new()
    {
        [Biome.River] = (38, 104, 178), [Biome.Lake] = (30, 88, 160), [Biome.Beach] = (214, 200, 150), [Biome.Desert] = (212, 184, 124),
        [Biome.Savanna] = (176, 170, 96), [Biome.Grassland] = (122, 160, 82), [Biome.Forest] = (58, 110, 58), [Biome.Jungle] = (34, 96, 46),
        [Biome.Swamp] = (72, 96, 70), [Biome.Taiga] = (64, 98, 82), [Biome.Tundra] = (150, 156, 140), [Biome.Rock] = (128, 120, 112), [Biome.Snow] = (236, 240, 244),
    };

    public static void Save(SurfaceMap map, string path)
    {
        int n = map.Size, mapPx = 512, viewW = 900, h = 512, w = mapPx + viewW;
        var rgb = new byte[w * h * 3];
        var shade = new float[n * n]; var color = new (float R, float G, float B)[n * n];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = map.Index(x, z);
            float dx = map.Height[map.Index(Math.Min(n - 1, x + 1), z)] - map.Height[map.Index(Math.Max(0, x - 1), z)];
            float dz = map.Height[map.Index(x, Math.Min(n - 1, z + 1))] - map.Height[map.Index(x, Math.Max(0, z - 1))];
            var normal = System.Numerics.Vector3.Normalize(new(-dx, 4 * map.CellSize, -dz));
            shade[i] = Math.Clamp(.35f + .8f * System.Numerics.Vector3.Dot(normal, System.Numerics.Vector3.Normalize(new(-1, 1.2f, -1))), .15f, 1.25f);
            var c = Colors[map.Biomes[i]];
            if (map.WaterDepth[i] > 0) { float d = Math.Min(1, map.WaterDepth[i] / 4); c = ((int)(46 - 20 * d), (int)(120 - 40 * d), (int)(196 - 30 * d)); shade[i] = 1; }
            if (map.Road[i]) { c = (160, 122, 78); shade[i] = 1; }
            foreach (var site in map.Sites) if (Math.Abs(x - site.X) <= 1 && Math.Abs(z - site.Z) <= 1) { c = (220, 40, 40); shade[i] = 1; }
            color[i] = (c.R, c.G, c.B);
        }
        (float R, float G, float B) Pixel(float fx, float fz)
        {
            int x = Math.Clamp((int)fx, 0, n - 1), z = Math.Clamp((int)fz, 0, n - 1), i = map.Index(x, z);
            return (color[i].R * shade[i], color[i].G * shade[i], color[i].B * shade[i]);
        }
        void Put(int px, int py, (float R, float G, float B) c)
        {
            int o = (py * w + px) * 3; rgb[o] = (byte)Math.Clamp(c.R, 0, 255); rgb[o + 1] = (byte)Math.Clamp(c.G, 0, 255); rgb[o + 2] = (byte)Math.Clamp(c.B, 0, 255);
        }
        for (int py = 0; py < h; py++) for (int px = 0; px < mapPx; px++) Put(px, py, Pixel(px * (n - 1f) / mapPx, py * (n - 1f) / h));

        // Oblique view: camera south of the map looking north, painter by column with y-buffer.
        float min = map.Height.Min(), range = Math.Max(1, map.Height.Max() - min), extent = (n - 1) * map.CellSize;
        for (int py = 0; py < h; py++) for (int px = mapPx; px < w; px++) { float t = py / (float)h; Put(px, py, (200 - 60 * t, 214 - 50 * t, 230 - 30 * t)); }
        float camX = n * .5f, camZ = n * 1.25f, camH = range * 1.25f + min, horizon = h * .18f, scale = h * 1.1f * map.CellSize / range * .5f;
        for (int col = 0; col < viewW; col++)
        {
            float yBuffer = h, angle = (col / (float)viewW - .5f) * 1.1f;
            float dirX = (float)Math.Sin(angle), dirZ = -(float)Math.Cos(angle);
            for (float dist = n * .3f; dist < n * 2.2f; dist += Math.Max(.25f, dist * .004f))
            {
                float sx = camX + dirX * dist, sz = camZ + dirZ * dist;
                if (sx < 0 || sz < 0 || sx > n - 1 || sz > n - 1) continue;
                int i = map.Index((int)sx, (int)sz);
                float height = map.Height[i] + (map.WaterDepth[i] > 0 ? map.WaterDepth[i] * .2f : 0);
                float screen = horizon + (camH - height) / (dist * map.CellSize) * scale * n;
                if (screen >= yBuffer) continue;
                var c = Pixel(sx, sz); float fog = Math.Min(1, (dist - n * .3f) / (n * 1.9f));
                c = (c.R + (205 - c.R) * fog * .7f, c.G + (215 - c.G) * fog * .7f, c.B + (228 - c.B) * fog * .7f);
                for (int py = Math.Max(0, (int)screen); py < (int)yBuffer; py++) Put(mapPx + col, py, c);
                yBuffer = Math.Max(0, screen);
            }
        }
        WritePng(path, w, h, rgb);
    }

    static void WritePng(string path, int w, int h, byte[] rgb)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            file.Write(BigEndian((uint)data.Length)); file.Write(typeBytes); file.Write(data);
            file.Write(BigEndian(Crc(typeBytes.Concat(data).ToArray())));
        }
        var header = new byte[13]; BigEndian((uint)w).CopyTo(header, 0); BigEndian((uint)h).CopyTo(header, 4); header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgb, y * w * 3, w * 3); }
        Chunk("IDAT", raw.ToArray()); Chunk("IEND", Array.Empty<byte>());
    }

    static byte[] BigEndian(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
    static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
        return ~c;
    }
}
