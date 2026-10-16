using Zone = ProceduralWorld.Core.SiteZone;

namespace ProceduralWorld.Core;

// Island settlements: twenty flattened places of different size and role, chosen on suitable ground
// (canyon mines, delta farms, river fishing, coastal ports, high outposts...) and joined by a road network.
public sealed partial class TerrainGenerator
{
    // Placement order matters: big towns first so smaller places fill the gaps between them.
    // 20 cities (5 capitals in distinct regions + 15 towns) for the five military factions, then resource and danger sites.
    /// <summary>The default island: 5 capitals, 15 towns, port, farms, mines, fishing spots, nests and ruins.</summary>
    public static IReadOnlyList<SitePlanEntry> DefaultIslandPlan { get; } = new (SiteKind Kind, Zone Zone, int Radius, int Tier)[]
    {
        (SiteKind.Town, Zone.Lowland, 11, 2), (SiteKind.Town, Zone.High, 11, 2), (SiteKind.Town, Zone.Coast, 11, 2), (SiteKind.Town, Zone.Canyon, 11, 2), (SiteKind.Town, Zone.Delta, 11, 2),
        (SiteKind.Village, Zone.Lowland, 8, 1), (SiteKind.Village, Zone.Lowland, 8, 1), (SiteKind.Village, Zone.Lowland, 8, 1), (SiteKind.Village, Zone.Lowland, 8, 1), (SiteKind.Village, Zone.Lowland, 8, 1),
        (SiteKind.Village, Zone.River, 8, 1), (SiteKind.Village, Zone.River, 8, 1), (SiteKind.Village, Zone.River, 8, 1), (SiteKind.Village, Zone.Coast, 8, 1), (SiteKind.Village, Zone.Coast, 8, 1),
        (SiteKind.Village, Zone.Any, 8, 1), (SiteKind.Village, Zone.High, 8, 1), (SiteKind.Port, Zone.Coast, 8, 1), (SiteKind.Farm, Zone.Delta, 8, 1), (SiteKind.Farm, Zone.Lowland, 8, 1),
        (SiteKind.Mine, Zone.Canyon, 5, 0), (SiteKind.Mine, Zone.High, 5, 0), (SiteKind.Mine, Zone.Any, 5, 0), (SiteKind.Fishing, Zone.River, 4, 0), (SiteKind.Fishing, Zone.Coast, 4, 0),
        (SiteKind.Nest, Zone.Delta, 5, 0), (SiteKind.Nest, Zone.Wild, 5, 0), (SiteKind.Ruins, Zone.High, 5, 0),
    }.Select(p => new SitePlanEntry(p.Kind, p.Zone, p.Radius, p.Tier)).ToArray();

    /// <summary>Chooses and levels the settlement sites (runs after erosion, skipping the carved river bed).</summary>
    private void FlattenSites(SurfaceMap map)
    {
        int n = map.Size; double extent = (n - 1) * map.CellSize; float sea = _settings.SeaLevel;
        float maximum = map.Height.Max();
        var riverDistance = Distance(map, i => _riverBed[i]);
        var random = new Random((int)(_seed ^ (_seed >> 32)));
        int spacing = Math.Max(7, (int)(n * .068)), stride = Math.Max(2, n / 128);
        var candidates = new List<(int X, int Z, Zone[] Zones, double Flat)>();
        for (int z = 14; z < n - 14; z += stride) for (int x = 14; x < n - 14; x += stride)
        {
            int i = map.Index(x, z); float h = map.Height[i];
            if (_riverBed[i] || h < sea + 4) continue;
            double slope = Math.Max(Math.Abs(map.Height[i + 2] - map.Height[i - 2]), Math.Abs(map.Height[i + 2 * n] - map.Height[i - 2 * n])) / (4 * map.CellSize);
            if (slope > .22) continue;
            double nx = (double)x / (n - 1) - .5, nz = (double)z / (n - 1) - .5, d = Math.Sqrt(nx * nx + nz * nz);
            var zones = new List<Zone> { Zone.Any };
            bool canyon = CanyonMask(nx, nz, d) > .35, delta = DeltaMask(nx, nz, d) > .3, high = h > sea + .45f * (maximum - sea);
            if (canyon) zones.Add(Zone.Canyon);
            if (delta) zones.Add(Zone.Delta);
            if (d > CoastRadius - .07) zones.Add(Zone.Coast);
            if (riverDistance[i] < 9 && riverDistance[i] > 3) zones.Add(Zone.River);
            if (high) zones.Add(Zone.High);
            if (!canyon && !high && d < CoastRadius - .05) zones.Add(Zone.Lowland);
            if (!canyon && d > .15 && riverDistance[i] > 20) zones.Add(Zone.Wild);
            candidates.Add((x, z, zones.ToArray(), 1 - slope * 4));
        }
        map.Sites.Clear();
        foreach (var entry in _settings.SettlementPlan ?? DefaultIslandPlan)
        {
            var (kind, zone, radius, tier) = (entry.Kind, entry.Zone, entry.Radius, entry.Tier);
            // Radii are tuned in 20 m cells; convert to this map, never larger than a fraction of the spacing.
            int scaled = Math.Max(3, Math.Min(spacing / 3, (int)Math.Round(radius * 20.0 / map.CellSize)));
            (int X, int Z)? best = null; double bestScore = double.MinValue;
            foreach (var c in candidates)
            {
                bool clear = map.Sites.All(s => (s.X - c.X) * (s.X - c.X) + (s.Z - c.Z) * (s.Z - c.Z) >= spacing * spacing);
                if (!clear) continue;
                double score = c.Flat + (c.Zones.Contains(zone) ? 3 : 0) + random.NextDouble() * .8;
                if (score > bestScore) { bestScore = score; best = (c.X, c.Z); }
            }
            if (best is null) continue;
            map.Sites.Add(new Site(kind, best.Value.X, best.Value.Z, scaled, tier));
        }

        double massBefore = map.Height.Sum(h => (double)h); var levelled = new bool[map.Height.Length];
        foreach (var site in map.Sites)
        {
            int cx = site.X, cz = site.Z, radius = site.Radius;
            double sum = 0; int count = 0;
            for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
            {
                int i = map.Index(cx + dx, cz + dz);
                if (dx * dx + dz * dz <= radius * radius && !_riverBed[i]) { sum += map.Height[i]; count++; }
            }
            float target = (float)Math.Max(sea + 12, count > 0 ? sum / count : sea + 12); // margin: later hydrology lowers low ground a little
            int outer = (int)Math.Ceiling(radius * 1.6);
            for (int dz = -outer; dz <= outer; dz++) for (int dx = -outer; dx <= outer; dx++)
            {
                int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int i = map.Index(x, z); if (_riverBed[i]) continue;
                double w = Smooth(1.6, .75, Math.Sqrt(dx * dx + dz * dz) / radius);
                map.Height[i] += (float)((target - map.Height[i]) * w);
                if (w > 0) levelled[i] = true;
            }
        }
        // Conserve mass: spread the levelling difference over the ground outside the sites.
        int free = levelled.Count(x => !x);
        float correction = (float)((massBefore - map.Height.Sum(h => (double)h)) / Math.Max(1, free));
        for (int i = 0; i < map.Height.Length; i++) if (!levelled[i]) map.Height[i] += correction;
    }

    /// <summary>Road network: every site joins its nearest connected neighbour (a spanning tree), plus a second link
    /// for short detours so the network has loops. Rivers are bridged, shallow sea gets a causeway, deep sea never.</summary>
    private void BuildRoads(SurfaceMap map)
    {
        Array.Clear(map.Road, 0, map.Road.Length); Array.Clear(map.PavedRoad, 0, map.PavedRoad.Length); map.RoadLinks.Clear();
        int count = map.Sites.Count; if (count < 2) return;
        double Dist(int a, int b) => Math.Sqrt(Math.Pow(map.Sites[a].X - map.Sites[b].X, 2) + Math.Pow(map.Sites[a].Z - map.Sites[b].Z, 2));
        var links = new HashSet<(int, int)>();
        // Highway first: a spanning tree over the settled places (tier 1+), so all towns are joined by asphalt.
        var settled = Enumerable.Range(0, count).Where(i => map.Sites[i].Tier >= 1).ToList();
        var connected = new List<int> { settled.Count > 0 ? settled[0] : 0 };
        foreach (int s in settled.Skip(1).ToList())
        {
            var (from, to) = settled.Where(p => !connected.Contains(p)).SelectMany(p => connected.Select(c => (c, p))).OrderBy(e => Dist(e.c, e.p)).First();
            links.Add((Math.Min(from, to), Math.Max(from, to))); connected.Add(to);
        }
        var pending = Enumerable.Range(0, count).Where(i => !connected.Contains(i)).ToList();
        while (pending.Count > 0)
        {
            var (from, to) = pending.SelectMany(p => connected.Select(c => (c, p))).OrderBy(e => Dist(e.c, e.p)).First();
            links.Add((Math.Min(from, to), Math.Max(from, to))); connected.Add(to); pending.Remove(to);
        }
        for (int a = 0; a < count; a++)
        {
            int b = Enumerable.Range(0, count).Where(x => x != a && !links.Contains((Math.Min(a, x), Math.Max(a, x)))).OrderBy(x => Dist(a, x)).FirstOrDefault();
            if (b != a && Dist(a, b) < map.Size * .28) links.Add((Math.Min(a, b), Math.Max(a, b)));
        }
        var router = new RoadRouter(map);
        foreach (var (a, b) in links.OrderBy(l => l.Item1).ThenBy(l => l.Item2))
        {
            var path = router.Find(map.Index(map.Sites[a].X, map.Sites[a].Z), map.Index(map.Sites[b].X, map.Sites[b].Z));
            if (path is null) continue;
            float length = 0;
            for (int k = 0; k < path.Count; k++)
            {
                map.Road[path[k]] = true;
                if (k > 0) length += map.CellSize * (path[k] % map.Size != path[k - 1] % map.Size && path[k] / map.Size != path[k - 1] / map.Size ? 1.4142f : 1);
            }
            // Highways join settled places (tier 1+: towns, villages, port, farms, outposts); the rest are dirt tracks.
            bool paved = map.Sites[a].Tier >= 1 && map.Sites[b].Tier >= 1;
            if (paved) foreach (int c in path) map.PavedRoad[c] = true;
            map.RoadLinks.Add(new RoadLink(a, b, length, paved, path));
        }
    }

    // Array-based A* reused across the network; existing roads are cheap so links merge into shared trunks.
    private sealed class RoadRouter
    {
        private readonly SurfaceMap _map; private readonly float[] _g; private readonly int[] _parent, _stamp, _closed; private int _search;
        public RoadRouter(SurfaceMap map) { _map = map; int c = map.Height.Length; _g = new float[c]; _parent = new int[c]; _stamp = new int[c]; _closed = new int[c]; }

        private float Cost(int i) => _map.Ocean[i] ? (_map.WaterDepth[i] < 2.5f ? 60 : float.PositiveInfinity) // shallow sea: causeway
            : _map.Biomes[i] == Biome.Snow ? 40
            : _map.WaterDepth[i] > 0 ? 20 // bridge
            : _map.Road[i] ? .6f
            : 1 + _map.Slope[i] * 12;

        public List<int>? Find(int start, int goal)
        {
            int n = _map.Size, stamp = ++_search;
            // Binary heap on parallel arrays: no boxing comparers or tree nodes (slow under Unity's Mono runtime).
            var heap = new List<(float F, int Cell)>();
            void Push(float f, int c) { heap.Add((f, c)); int i = heap.Count - 1; while (i > 0) { int p = (i - 1) / 2; if (heap[p].F <= heap[i].F) break; (heap[p], heap[i]) = (heap[i], heap[p]); i = p; } }
            int Pop() { int top = heap[0].Cell; heap[0] = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1); int i = 0; while (true) { int l = i * 2 + 1, r = l + 1, m = i; if (l < heap.Count && heap[l].F < heap[m].F) m = l; if (r < heap.Count && heap[r].F < heap[m].F) m = r; if (m == i) break; (heap[m], heap[i]) = (heap[i], heap[m]); i = m; } return top; }
            float H(int i) { int dx = Math.Abs(i % n - goal % n), dz = Math.Abs(i / n - goal / n); return (Math.Max(dx, dz) + .4142f * Math.Min(dx, dz)) * .6f; }
            _stamp[start] = stamp; _g[start] = 0; _parent[start] = -1; Push(H(start), start);
            while (heap.Count > 0)
            {
                int cell = Pop();
                if (_closed[cell] == stamp) continue; _closed[cell] = stamp;
                if (cell == goal) { var path = new List<int>(); for (int c = goal; c >= 0; c = _parent[c]) path.Add(c); path.Reverse(); return path; }
                int x = cell % n, z = cell / n;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz; if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int next = nz * n + nx; if (_closed[next] == stamp) continue;
                    float c = Cost(next); if (float.IsInfinity(c)) continue;
                    float candidate = _g[cell] + c * (dx != 0 && dz != 0 ? 1.4142f : 1);
                    if (_stamp[next] == stamp && _g[next] <= candidate) continue;
                    _stamp[next] = stamp; _g[next] = candidate; _parent[next] = cell; Push(candidate + H(next), next);
                }
            }
            return null;
        }
    }
}
