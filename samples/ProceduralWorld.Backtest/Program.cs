using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProceduralWorld.Core;

int size = args.Length > 0 && int.TryParse(args[0], out int requestedSize) ? requestedSize : 129;
string outputRoot = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath(Path.Combine("artifacts", "backtest"));
long[] seeds = [1, 7, 42, 1337, 2026, 4096, 9876, 65537, 99991, 173133397];
TerrainStyle[] styles = Enum.GetValues<TerrainStyle>();
var cases = styles.SelectMany(style => seeds.Select(seed => (style, seed))).ToArray();
var results = new ConcurrentBag<BacktestResult>();

Console.WriteLine($"Backtest: {cases.Length} worlds ({styles.Length} profiles x {seeds.Length} seeds), resolution {size}x{size}");
var started = DateTimeOffset.UtcNow;
Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6) }, item =>
{
    WorldSettings settings = TerrainPresetCatalog.Create(item.style, size);
    GeneratedWorld world = new WorldGenerator(item.seed, settings).Generate();
    BacktestResult result = Analyze(item.style, item.seed, settings, world);
    results.Add(result);
    Console.WriteLine($"  {item.style,-14} seed {item.seed,10}: {result.Score,5:F1}/100 | relief {result.HeightRange,7:F1} | rivers {result.RiverCoverage,6:P2} | issues {result.Issues.Count}");
});

List<BacktestResult> ordered = results.OrderBy(x => x.Style).ThenBy(x => x.Seed).ToList();
BacktestResult best = ordered.OrderByDescending(x => x.Score).ThenByDescending(x => x.HeightRange).First();
BacktestResult worst = ordered.OrderBy(x => x.Score).ThenByDescending(x => x.Issues.Count).First();
BacktestResult render = ordered.Where(x => x.BrokenRiverCells == 0 && x.InaccessibleEntrances == 0)
    .OrderByDescending(x => x.Score).ThenByDescending(x => x.HeightRange).ThenByDescending(x => x.RiverCoverage)
    .FirstOrDefault() ?? best;

Directory.CreateDirectory(outputRoot);
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
options.Converters.Add(new JsonStringEnumConverter());
File.WriteAllText(Path.Combine(outputRoot, "backtest.json"), JsonSerializer.Serialize(new BacktestSuite(started, DateTimeOffset.UtcNow, size, seeds, ordered, best, worst, render), options), new UTF8Encoding(false));
WriteCsv(Path.Combine(outputRoot, "backtest.csv"), ordered);
WriteSummary(Path.Combine(outputRoot, "summary.md"), ordered, best, worst, render, started);

Console.WriteLine();
Console.WriteLine($"Best: {best.Style} / {best.Seed} ({best.Score:F1})");
Console.WriteLine($"Worst: {worst.Style} / {worst.Seed} ({worst.Score:F1})");
Console.WriteLine($"3D candidate: {render.Style} / {render.Seed} ({render.Score:F1}, relief {render.HeightRange:F1})");
Console.WriteLine($"Reports: {outputRoot}");

static BacktestResult Analyze(TerrainStyle style, long seed, WorldSettings settings, GeneratedWorld world)
{
    SurfaceMap map = world.Surface;
    TerrainReport diagnostic = TerrainDiagnostics.Analyze(world);
    double sum = 0, sumSq = 0, riseSum = 0, edgeStepSum = 0;
    long neighbours = 0, flatNeighbours = 0, cliffNeighbours = 0, edgeSamples = 0;
    int water = 0, rivers = 0;
    var biomeCounts = new Dictionary<Biome, int>();

    for (int z = 0; z < map.Size; z++)
    for (int x = 0; x < map.Size; x++)
    {
        int i = map.Index(x, z);
        double h = map.Height[i];
        sum += h; sumSq += h * h;
        if (map.WaterDepth[i] > .01f) water++;
        if (map.RiverChannel[i]) rivers++;
        biomeCounts[map.Biomes[i]] = biomeCounts.GetValueOrDefault(map.Biomes[i]) + 1;
        if (x + 1 < map.Size) AddNeighbour(i, map.Index(x + 1, z));
        if (z + 1 < map.Size) AddNeighbour(i, map.Index(x, z + 1));
        if (x == 0) AddEdge(i, map.Index(1, z));
        if (x == map.Size - 1) AddEdge(i, map.Index(map.Size - 2, z));
        if (z == 0) AddEdge(i, map.Index(x, 1));
        if (z == map.Size - 1) AddEdge(i, map.Index(x, map.Size - 2));
    }

    int count = map.Height.Length;
    double mean = sum / count;
    double stdDev = Math.Sqrt(Math.Max(0, sumSq / count - mean * mean));
    double dominant = biomeCounts.Values.Max() / (double)count;
    double riverCoverage = rivers / (double)count;
    double waterCoverage = water / (double)count;
    double flatRatio = flatNeighbours / (double)Math.Max(1, neighbours);
    double cliffRatio = cliffNeighbours / (double)Math.Max(1, neighbours);
    double roughness = riseSum / Math.Max(1, neighbours);
    double boundaryStep = edgeStepSum / Math.Max(1, edgeSamples);
    var issues = new List<string>();
    double score = 100;

    Penalize(diagnostic.BrokenRiverCells > 0, Math.Min(35, 10 + diagnostic.BrokenRiverCells * .4), $"{diagnostic.BrokenRiverCells} células de rio perdem continuidade");
    Penalize(diagnostic.InaccessibleEntrances > 0, Math.Min(35, diagnostic.InaccessibleEntrances * 12), $"{diagnostic.InaccessibleEntrances} entradas de caverna são inacessíveis");
    Penalize(diagnostic.CaveEntrances < settings.GuaranteedCaveEntrances, 12, $"apenas {diagnostic.CaveEntrances} entradas de caverna");
    Penalize(diagnostic.MaximumNeighbourRise > settings.CellSize * 6, Math.Min(18, (diagnostic.MaximumNeighbourRise / settings.CellSize - 6) * 2), $"degrau máximo excessivo ({diagnostic.MaximumNeighbourRise:F1})");
    Penalize(cliffRatio > .12, Math.Min(12, (cliffRatio - .12) * 80), $"excesso de encostas abruptas ({cliffRatio:P1})");
    Penalize(flatRatio > .72 && style != TerrainStyle.RollingPlains, Math.Min(12, (flatRatio - .72) * 45), $"relevo excessivamente plano ({flatRatio:P1})");
    Penalize(diagnostic.BiomeCount < 5, (5 - diagnostic.BiomeCount) * 5, $"baixa diversidade: {diagnostic.BiomeCount} biomas");
    Penalize(dominant > .72, Math.Min(15, (dominant - .72) * 55), $"um bioma domina {dominant:P1} do mapa");
    Penalize(settings.WaterMode != WaterMode.None && riverCoverage < .001, 10, $"rede fluvial quase ausente ({riverCoverage:P2})");
    Penalize(waterCoverage > .22, Math.Min(10, (waterCoverage - .22) * 35), $"água cobre área excessiva ({waterCoverage:P1})");
    Penalize(diagnostic.BiomeComponents > count * .045, Math.Min(12, (diagnostic.BiomeComponents / (double)count - .045) * 90), $"biomas fragmentados em {diagnostic.BiomeComponents} regiões");
    Penalize(boundaryStep > settings.CellSize * 2.25, Math.Min(10, (boundaryStep / settings.CellSize - 2.25) * 2), $"bordas têm variação média alta ({boundaryStep:F1})");

    return new BacktestResult(style, seed, world.Fingerprint().ToString("X16"), Math.Max(0, score),
        diagnostic.MinimumHeight, diagnostic.MaximumHeight, diagnostic.MaximumHeight - diagnostic.MinimumHeight,
        mean, stdDev, diagnostic.MaximumNeighbourRise, roughness, flatRatio, cliffRatio, boundaryStep,
        diagnostic.BiomeCount, dominant, diagnostic.BiomeComponents, riverCoverage, waterCoverage,
        diagnostic.RiverCells, diagnostic.BrokenRiverCells, diagnostic.CaveEntrances, diagnostic.InaccessibleEntrances,
        world.Caves.AirCellCount, world.Labyrinth.WalkableCount, issues);

    void AddNeighbour(int a, int b)
    {
        double rise = Math.Abs(map.Height[a] - map.Height[b]);
        riseSum += rise; neighbours++;
        double grade = rise / map.CellSize;
        if (grade < .08) flatNeighbours++;
        if (grade > 1.25) cliffNeighbours++;
    }
    void AddEdge(int a, int b) { edgeStepSum += Math.Abs(map.Height[a] - map.Height[b]); edgeSamples++; }
    void Penalize(bool condition, double penalty, string issue) { if (!condition) return; score -= penalty; issues.Add(issue); }
}

static void WriteCsv(string path, IReadOnlyList<BacktestResult> results)
{
    var csv = new StringBuilder("style,seed,fingerprint,score,min_height,max_height,height_range,stddev,max_neighbour_rise,roughness,flat_ratio,cliff_ratio,boundary_step,biomes,dominant_biome_ratio,biome_components,river_coverage,water_coverage,river_cells,broken_river_cells,cave_entrances,inaccessible_entrances,cave_air_cells,labyrinth_walkable,issues\n");
    foreach (BacktestResult x in results)
        csv.AppendLine(string.Join(',', x.Style, x.Seed, x.Fingerprint, F(x.Score), F(x.MinimumHeight), F(x.MaximumHeight), F(x.HeightRange), F(x.HeightStdDev), F(x.MaximumNeighbourRise), F(x.MeanNeighbourRise), F(x.FlatNeighbourRatio), F(x.CliffNeighbourRatio), F(x.MeanBoundaryStep), x.BiomeCount, F(x.DominantBiomeRatio), x.BiomeComponents, F(x.RiverCoverage), F(x.WaterCoverage), x.RiverCells, x.BrokenRiverCells, x.CaveEntrances, x.InaccessibleEntrances, x.CaveAirCells, x.LabyrinthWalkableCells, Quote(string.Join("; ", x.Issues))));
    File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
    static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';
}

static void WriteSummary(string path, IReadOnlyList<BacktestResult> results, BacktestResult best, BacktestResult worst, BacktestResult render, DateTimeOffset started)
{
    var md = new StringBuilder();
    md.AppendLine("# Procedural World backtest").AppendLine();
    md.AppendLine($"Generated {results.Count} worlds in {(DateTimeOffset.UtcNow - started).TotalSeconds:F1}s. Scores are diagnostic indicators, not artistic ratings.").AppendLine();
    md.AppendLine("| Profile | Avg score | Worst | Avg relief | Avg river | Avg cliffs | Failures |");
    md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
    foreach (IGrouping<TerrainStyle, BacktestResult> group in results.GroupBy(x => x.Style).OrderBy(x => x.Key))
        md.AppendLine($"| {group.Key} | {group.Average(x => x.Score):F1} | {group.Min(x => x.Score):F1} | {group.Average(x => x.HeightRange):F1} | {group.Average(x => x.RiverCoverage):P2} | {group.Average(x => x.CliffNeighbourRatio):P1} | {group.Count(x => x.Issues.Count > 0)} / {group.Count()} |");
    md.AppendLine().AppendLine($"**Best:** {best.Style}, seed `{best.Seed}`, score {best.Score:F1}.  ");
    md.AppendLine($"**Worst:** {worst.Style}, seed `{worst.Seed}`, score {worst.Score:F1}.  ");
    md.AppendLine($"**3D render candidate:** {render.Style}, seed `{render.Seed}`, score {render.Score:F1}, relief {render.HeightRange:F1}.").AppendLine();
    md.AppendLine("## Most frequent findings").AppendLine();
    foreach (var issue in results.SelectMany(x => x.Issues).GroupBy(NormalizeIssue).OrderByDescending(x => x.Count()).Take(12))
        md.AppendLine($"- {issue.Key}: {issue.Count()} cases");
    md.AppendLine().AppendLine("## Worst cases").AppendLine();
    foreach (BacktestResult item in results.OrderBy(x => x.Score).Take(10))
        md.AppendLine($"- {item.Style} `{item.Seed}` — {item.Score:F1}: {(item.Issues.Count == 0 ? "no rule violations" : string.Join("; ", item.Issues))}");
    File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));

    static string NormalizeIssue(string issue)
    {
        if (issue.Contains("rio")) return "River continuity or coverage";
        if (issue.Contains("caverna")) return "Cave accessibility or count";
        if (issue.Contains("bioma")) return "Biome diversity or dominance";
        if (issue.Contains("fragment")) return "Biome fragmentation";
        if (issue.Contains("degrau") || issue.Contains("encostas")) return "Abrupt slopes";
        if (issue.Contains("plano")) return "Excess flatness";
        if (issue.Contains("bordas")) return "Boundary discontinuity";
        if (issue.Contains("água")) return "Water coverage";
        return issue;
    }
}

sealed record BacktestSuite(DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int Resolution, long[] Seeds, IReadOnlyList<BacktestResult> Results, BacktestResult Best, BacktestResult Worst, BacktestResult RenderCandidate);
sealed record BacktestResult(TerrainStyle Style, long Seed, string Fingerprint, double Score,
    double MinimumHeight, double MaximumHeight, double HeightRange, double MeanHeight, double HeightStdDev,
    double MaximumNeighbourRise, double MeanNeighbourRise, double FlatNeighbourRatio, double CliffNeighbourRatio, double MeanBoundaryStep,
    int BiomeCount, double DominantBiomeRatio, int BiomeComponents, double RiverCoverage, double WaterCoverage,
    int RiverCells, int BrokenRiverCells, int CaveEntrances, int InaccessibleEntrances, long CaveAirCells, int LabyrinthWalkableCells,
    IReadOnlyList<string> Issues);
