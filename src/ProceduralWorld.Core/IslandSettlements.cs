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
        // Only the main landmass (reachable from the centre over land or wadeable shallows) may hold places: an islet across deep sea gets no road.
        var mainland = new bool[map.Height.Length]; var fill = new Queue<int>(); int centre = map.Index(n / 2, n / 2);
        mainland[centre] = true; fill.Enqueue(centre);
        while (fill.Count > 0)
        {
            int c = fill.Dequeue(), cx = c % n, cz = c / n;
            for (int k = 0; k < 4; k++)
            {
                int x = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), z = cz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int j = map.Index(x, z); if (mainland[j] || map.Height[j] < sea + 1) continue;
                mainland[j] = true; fill.Enqueue(j);
            }
        }
        var candidates = new List<(int X, int Z, Zone[] Zones, double Flat)>();
        for (int z = 14; z < n - 14; z += stride) for (int x = 14; x < n - 14; x += stride)
        {
            int i = map.Index(x, z); float h = map.Height[i];
            if (_riverBed[i] || h < sea + 4 || !mainland[i]) continue;
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

        // Military posts: for each settled place, random flat candidates 350-800 m away, spread around it, clear of other places.
        map.Outposts.Clear();
        int postRadius = Math.Max(2, (int)Math.Round(60 / map.CellSize)), near = (int)(350 / map.CellSize), far = (int)(800 / map.CellSize);
        for (int s = 0; s < map.Sites.Count; s++)
        {
            var home = map.Sites[s]; if (home.Tier < 1) continue;
            var chosen = new List<(int X, int Z)>();
            for (int k = 0; k < _settings.OutpostsPerCity; k++)
            {
                (int X, int Z)? best = null; double bestScore = double.MinValue;
                foreach (var c in candidates)
                {
                    double d = Math.Sqrt((c.X - home.X) * (c.X - home.X) + (c.Z - home.Z) * (c.Z - home.Z));
                    if (d < near || d > far) continue;
                    if (map.Sites.Any(o => (o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z) < (o.Radius + postRadius + 6) * (o.Radius + postRadius + 6))) continue;
                    if (map.Outposts.Concat(chosen.Select(p => new OutpostSite(s, p.X, p.Z, postRadius))).Any(o => (o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z) < 15 * 15)) continue;
                    double angle = Math.Atan2(c.Z - home.Z, c.X - home.X), spread = chosen.Count == 0 ? Math.PI : chosen.Min(p => Math.Abs(Math.IEEERemainder(angle - Math.Atan2(p.Z - home.Z, p.X - home.X), 2 * Math.PI)));
                    double score = c.Flat * 2 + spread + random.NextDouble() * 1.5; // flat, spread around the town, and different every world
                    if (score > bestScore) { bestScore = score; best = (c.X, c.Z); }
                }
                if (best is null) break;
                chosen.Add(best.Value);
            }
            foreach (var p in chosen) map.Outposts.Add(new OutpostSite(s, p.X, p.Z, postRadius));
        }

        double massBefore = map.Height.Sum(h => (double)h); var levelled = new bool[map.Height.Length];
        foreach (var (cx, cz, radius) in map.Sites.Select(x => (x.X, x.Z, x.Radius)).Concat(map.Outposts.Select(o => (o.X, o.Z, o.Radius))).ToList())
        {
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
                // With side rivers keep a lip along every valley: a site levelled below the bed would breach its wall.
                if (_valleyAt.Length > 0 && riverDistance[i] <= 2 && target < map.Height[i]) continue;
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

    /// <summary>Final levelling after hydrology (and the valley re-carving it triggers): every site and military post
    /// becomes a flat plateau at the median of its own dry ground, easing out to the land around it. Channel and
    /// water cells keep their bed, so a river through a town still flows. Without this, later carving left towns
    /// with tens of metres of relief.</summary>
    private void SettleSites(SurfaceMap map)
    {
        int n = map.Size;
        foreach (var (cx, cz, radius) in map.Sites.Select(s => (s.X, s.Z, s.Radius)).Concat(map.Outposts.Select(o => (o.X, o.Z, o.Radius))).ToList())
        {
            var ground = new List<float>();
            for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= n || z >= n || dx * dx + dz * dz > radius * radius) continue;
                int i = map.Index(x, z); if (map.RiverChannel[i] || map.WaterDepth[i] > 0 || map.Ocean[i]) continue;
                ground.Add(map.Height[i]);
            }
            if (ground.Count == 0) continue;
            ground.Sort(); float target = Math.Max(ground[ground.Count / 2], _settings.SeaLevel + 1.5f);
            int outer = (int)Math.Ceiling(radius * 1.5);
            for (int dz = -outer; dz <= outer; dz++) for (int dx = -outer; dx <= outer; dx++)
            {
                int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int i = map.Index(x, z); if (map.RiverChannel[i] || map.WaterDepth[i] > 0 || map.Ocean[i]) continue;
                double w = Smooth(1.5, 1.0, Math.Sqrt(dx * dx + dz * dz) / radius); // flat out to the radius, eased beyond
                map.Height[i] += (float)((target - map.Height[i]) * w);
            }
        }
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
        var router = new RoadRouter(map) { Bank = _valleyAt.Length > 0 ? RiverBanks(map) : null };
        var reached = new HashSet<int>(); var ordered = links.OrderBy(l => l.Item1).ThenBy(l => l.Item2).ToList();
        for (int li = 0; li < ordered.Count; li++)
        {
            var (a, b) = ordered[li];
            var path = router.Find(map.Index(map.Sites[a].X, map.Sites[a].Z), map.Index(map.Sites[b].X, map.Sites[b].Z));
            if (path is null)
            {
                // Blocked: try the next nearest place for whichever end still has no road (each place keeps at least one link).
                foreach (int lonely in new[] { a, b }.Where(s => !reached.Contains(s) && !ordered.Skip(li + 1).Any(l => l.Item1 == s || l.Item2 == s)))
                {
                    int other = Enumerable.Range(0, count).Where(x => x != lonely && !links.Contains((Math.Min(lonely, x), Math.Max(lonely, x)))).OrderBy(x => Dist(lonely, x)).Take(4).FirstOrDefault(x => x != lonely);
                    if (other != lonely && links.Add((Math.Min(lonely, other), Math.Max(lonely, other)))) ordered.Add((Math.Min(lonely, other), Math.Max(lonely, other)));
                }
                continue;
            }
            reached.Add(a); reached.Add(b);
            float length = 0;
            for (int k = 0; k < path.Count; k++)
            {
                if (_settings.Roads) map.Road[path[k]] = true;
                if (k > 0) length += map.CellSize * (path[k] % map.Size != path[k - 1] % map.Size && path[k] / map.Size != path[k - 1] / map.Size ? 1.4142f : 1);
            }
            // Highways join settled places (tier 1+: towns, villages, port, farms, outposts); the rest are dirt tracks.
            bool paved = _settings.Roads && map.Sites[a].Tier >= 1 && map.Sites[b].Tier >= 1;
            if (paved) foreach (int c in path) map.PavedRoad[c] = true;
            map.RoadLinks.Add(new RoadLink(a, b, length, paved, path));
        }
        // Each military post gets a dirt track to its town.
        foreach (var post in map.Outposts)
        {
            var home = map.Sites[post.Site];
            var track = router.Find(map.Index(post.X, post.Z), map.Index(home.X, home.Z));
            if (track != null && _settings.Roads) foreach (int c in track) map.Road[c] = true;
        }
        // Any place still without a road gets a long bridge to the nearest connected place, so every site is reachable.
        router.BridgeDeepWater = true;
        foreach (int lonely in Enumerable.Range(0, count).Where(s => !reached.Contains(s)).ToList())
        {
            int other = reached.OrderBy(x => Dist(lonely, x)).DefaultIfEmpty(-1).First(); if (other < 0) break;
            var path = router.Find(map.Index(map.Sites[lonely].X, map.Sites[lonely].Z), map.Index(map.Sites[other].X, map.Sites[other].Z));
            if (path is null) continue;
            float length = 0;
            for (int k = 0; k < path.Count; k++) { if (_settings.Roads) map.Road[path[k]] = true; if (k > 0) length += map.CellSize * (path[k] % map.Size != path[k - 1] % map.Size && path[k] / map.Size != path[k - 1] / map.Size ? 1.4142f : 1); }
            map.RoadLinks.Add(new RoadLink(Math.Min(lonely, other), Math.Max(lonely, other), length, false, path)); reached.Add(lonely);
        }
    }

    // Dry land cells touching inland river water.
    private static bool[] RiverBanks(SurfaceMap map)
    {
        int n = map.Size; var bank = new bool[map.Height.Length];
        for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++)
        {
            int i = map.Index(x, z); if (map.WaterDepth[i] > 0 || map.Ocean[i]) continue;
            for (int dz = -1; dz <= 1 && !bank[i]; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int j = map.Index(x + dx, z + dz);
                if (map.WaterDepth[j] > 0 && !map.Ocean[j]) { bank[i] = true; break; }
            }
        }
        return bank;
    }

    // Array-based A* reused across the network; existing roads are cheap so links merge into shared trunks.
    private sealed class RoadRouter
    {
        private readonly SurfaceMap _map; private readonly float[] _g; private readonly int[] _parent, _stamp, _closed; private int _search;
        public RoadRouter(SurfaceMap map) { _map = map; int c = map.Height.Length; _g = new float[c]; _parent = new int[c]; _stamp = new int[c]; _closed = new int[c]; }

        /// <summary>Last resort for a place cut off by deep water: allow a long, very expensive bridge.</summary>
        public bool BridgeDeepWater;
        /// <summary>Dry cells beside a river (its rendered water reaches them): costly, so roads cross the river instead of hugging it.</summary>
        public bool[]? Bank;
        private float Cost(int i) => _map.Ocean[i] ? (_map.WaterDepth[i] < 2.5f ? 60 : BridgeDeepWater ? 150 : float.PositiveInfinity) // shallow sea: causeway
            : _map.Biomes[i] == Biome.Snow ? 40
            : _map.WaterDepth[i] > 0 ? 20 // bridge
            : Bank?[i] == true ? 12
            : _map.Road[i] ? .6f
            : _map.Slope[i] > .9f ? 400 // cliffs only as a last resort: a road there would be a ramp nobody can walk
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
