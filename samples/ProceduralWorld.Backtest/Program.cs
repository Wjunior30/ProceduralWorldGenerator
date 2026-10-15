using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProceduralWorld.Core;

if (args.Length > 0 && args[0] == "render")
{
    // render <outputDir> [size] [seed]: one PNG per terrain style.
    string renderDir = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("artifacts", "render"));
    int renderSize = args.Length > 2 ? int.Parse(args[2]) : 257;
    long renderSeed = args.Length > 3 ? long.Parse(args[3]) : 2026;
    Directory.CreateDirectory(renderDir);
    Parallel.ForEach(Enum.GetValues<TerrainStyle>(), style =>
        WorldRender.Save(new WorldGenerator(renderSeed, TerrainPresetCatalog.Create(style, renderSize)).Generate().Surface,
            Path.Combine(renderDir, $"{style.ToString().ToLowerInvariant()}.png")));
    Console.WriteLine($"Rendered to {renderDir}");
    return;
}

int[] sizes = args.Length > 0
    ? args[0].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Distinct().OrderBy(x => x).ToArray()
    : new[] { 65, 129 };
string outputRoot = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath(Path.Combine("artifacts", "backtest"));
long[] referenceSeeds = { 1, 7, 42, 1337, 2026, 4096, 9876, 65537, 99991, 173133397 };
int requestedSeedCount = args.Length > 2 && int.TryParse(args[2], out int parsedSeedCount) ? Math.Clamp(parsedSeedCount, 10, 1000) : referenceSeeds.Length;
long[] seeds = referenceSeeds.Concat(Enumerable.Range(0, Math.Max(0, requestedSeedCount - referenceSeeds.Length))
    .Select(i => unchecked((long)(0x9E3779B97F4A7C15UL * (ulong)(i + 11))))).Take(requestedSeedCount).ToArray();
TerrainStyle[] styles = Enum.GetValues<TerrainStyle>();
var cases = sizes.SelectMany(size => styles.SelectMany(style => seeds.Select(seed => (style, seed, size)))).ToArray();
var results = new ConcurrentBag<BacktestResult>();

Console.WriteLine($"Backtest: {cases.Length} worlds ({styles.Length} profiles x {seeds.Length} seeds x {sizes.Length} resolutions)");
var started = DateTimeOffset.UtcNow;
Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6) }, item =>
{
    WorldSettings settings = TerrainPresetCatalog.Create(item.style, item.size);
    GeneratedWorld world = new WorldGenerator(item.seed, settings).Generate();
    BacktestResult result = Analyze(item.style, item.seed, settings, world);
    results.Add(result);
    lock (Console.Out)
        Console.WriteLine($"  {item.style,-15} {item.size,3}² seed {item.seed,10}: {result.Score,5:F1}/100 | relief {result.HeightRange,7:F1} | rivers {result.RiverCoverage,6:P2} | issues {result.Issues.Count}");
});

List<BacktestResult> ordered = results.OrderBy(x => x.Resolution).ThenBy(x => x.Style).ThenBy(x => x.Seed).ToList();
BacktestResult best = ordered.OrderByDescending(x => x.Score).ThenByDescending(x => x.HeightRange).First();
BacktestResult worst = ordered.OrderBy(x => x.Score).ThenByDescending(x => x.Issues.Count).First();
BacktestResult render = ordered.Where(x => x.BrokenRiverCells == 0 && x.UphillRiverCells == 0 && x.InaccessibleEntrances == 0 && x.ReachableCaveRatio >= .9)
    .OrderByDescending(x => x.Score).ThenByDescending(x => x.HeightRange).FirstOrDefault() ?? best;
Directory.CreateDirectory(outputRoot);
var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
File.WriteAllText(Path.Combine(outputRoot, "backtest.json"), JsonSerializer.Serialize(new BacktestSuite(started, DateTimeOffset.UtcNow, sizes, seeds, ordered, best, worst, render), options), new UTF8Encoding(false));
WriteCsv(Path.Combine(outputRoot, "backtest.csv"), ordered);
WriteSummary(Path.Combine(outputRoot, "summary.md"), ordered, best, worst, render, started);
WriteRealGeologySummary(Path.Combine(outputRoot, "real-geology-summary.md"), ordered);
WriteRegressionImages(outputRoot, styles, sizes.Max());
Console.WriteLine();
Console.WriteLine($"Best: {best.Style} / {best.Seed} ({best.Score:F1})");
Console.WriteLine($"Worst: {worst.Style} / {worst.Seed} ({worst.Score:F1})");
Console.WriteLine($"3D candidate: {render.Style} / {render.Seed} ({render.Score:F1}, relief {render.HeightRange:F1})");
Console.WriteLine($"Reports: {outputRoot}");

static BacktestResult Analyze(TerrainStyle style, long seed, WorldSettings settings, GeneratedWorld world)
{
    SurfaceMap map = world.Surface;
    TerrainReport report = TerrainDiagnostics.Analyze(world);
    RealGeologyAssessment realGeology = RealGeologyBenchmarks.Assess(style, map, report);
    double mean = map.Height.Average(x => (double)x);
    double stdDev = Math.Sqrt(map.Height.Average(x => (x - mean) * (x - mean)));
    double riverCoverage = map.RiverChannel.Count(x => x) / (double)map.Height.Length;
    double waterCoverage = map.WaterDepth.Count(x => x > .01f) / (double)map.Height.Length;
    var issues = new List<string>();
    double score = 100;
    (double minimumFlat, double maximumSteep) = style switch
    {
        TerrainStyle.RollingPlains => (.18, .14),
        TerrainStyle.Natural => (.07, .42),
        TerrainStyle.Karst => (.07, .34),
        TerrainStyle.Volcanic => (.06, .34),
        TerrainStyle.Alpine => (.025, .68),
        TerrainStyle.Canyonlands => (.04, .64),
        TerrainStyle.Glacial => (.025, .64),
        _ => (.04, .64)
    };

    Penalize(report.BrokenRiverCells > 0, Math.Min(32, 8 + report.BrokenRiverCells * .45), $"{report.BrokenRiverCells} células de rio perdem continuidade hidráulica");
    Penalize(report.UphillDrainageCells > 0, Math.Min(30, 8 + report.UphillDrainageCells * .08), $"{report.UphillDrainageCells} receptores de drenagem sobem no terreno final");
    Penalize(report.UphillRiverCells > 0, Math.Min(30, 10 + report.UphillRiverCells * .8), $"{report.UphillRiverCells} trechos de rio possuem água ascendente");
    Penalize(report.InaccessibleEntrances > 0, Math.Min(35, report.InaccessibleEntrances * 12), $"{report.InaccessibleEntrances} entradas de caverna são inacessíveis");
    Penalize(report.CaveEntrances < settings.GuaranteedCaveEntrances, 12, $"apenas {report.CaveEntrances} entradas de caverna");
    Penalize(report.ReachableCaveRatio < .90, Math.Min(30, (.90 - report.ReachableCaveRatio) * 60), $"somente {report.ReachableCaveRatio:P1} das cavernas são alcançáveis");
    Penalize(report.MaximumNeighbourRise > settings.CellSize * 2, Math.Min(18, (report.MaximumNeighbourRise / settings.CellSize - 2) * 8), $"degrau máximo excessivo ({report.MaximumNeighbourRise:F1})");
    Penalize(report.MaximumLocalApex > settings.CellSize * .86, Math.Min(18, (report.MaximumLocalApex / settings.CellSize - .86) * 12), $"ápice isolado excessivo ({report.MaximumLocalApex:F1})");
    Penalize(report.RepeatedSteepGradeRatio > .035, Math.Min(18, (report.RepeatedSteepGradeRatio - .035) * 180), $"inclinações saturadas em um mesmo valor ({report.RepeatedSteepGradeRatio:P1})");
    Penalize(report.FlatAreaRatio < minimumFlat, Math.Min(15, (minimumFlat - report.FlatAreaRatio) * 80), $"faltam áreas planas ({report.FlatAreaRatio:P1})");
    Penalize(report.SteepAreaRatio > maximumSteep, Math.Min(15, (report.SteepAreaRatio - maximumSteep) * 55), $"excesso de encostas íngremes ({report.SteepAreaRatio:P1})");
    Penalize(report.BiomeCount < 5, (5 - report.BiomeCount) * 5, $"baixa diversidade: {report.BiomeCount} biomas");
    Penalize(report.DominantBiomeRatio > .72, Math.Min(15, (report.DominantBiomeRatio - .72) * 55), $"um bioma domina {report.DominantBiomeRatio:P1} do mapa");
    Penalize(settings.WaterMode != WaterMode.None && riverCoverage < .001, 10, $"rede fluvial quase ausente ({riverCoverage:P2})");
    Penalize(waterCoverage > .24, Math.Min(10, (waterCoverage - .24) * 35), $"água cobre área excessiva ({waterCoverage:P1})");
    Penalize(report.BiomeComponents > map.Height.Length * .055, Math.Min(12, (report.BiomeComponents / (double)map.Height.Length - .055) * 80), $"biomas fragmentados em {report.BiomeComponents} regiões");
    Penalize(report.DrainageOutlets != 1, 8, $"a bacia principal exige uma saída fluvial ({report.DrainageOutlets})");
    Penalize(report.ErosionMassRelativeError >= 1e-5, 20, $"erosão não conservou massa ({report.ErosionMassRelativeError:E2})");
    Penalize(report.BasinCount != report.DrainageOutlets, 10, $"{report.BasinCount} bacias para {report.DrainageOutlets} saídas");
    Penalize(report.CaveComponents != 1, 20, $"{report.CaveComponents} componentes subterrâneos desconectados");

    return new BacktestResult(style, seed, settings.Size, world.Fingerprint().ToString("X16"), Math.Max(0, score), realGeology.Score,
        map.Height.Min(), map.Height.Max(), map.Height.Max() - map.Height.Min(), stdDev,
        report.MaximumNeighbourRise, report.MaximumLocalApex, report.FlatAreaRatio, report.SteepAreaRatio, report.RepeatedSteepGradeRatio,
        report.BiomeCount, report.DominantBiomeRatio, report.DominantGeologyRatio, report.BiomeComponents,
        riverCoverage, waterCoverage, report.RiverCells, report.BrokenRiverCells, report.UphillDrainageCells, report.UphillRiverCells, report.DrainageOutlets,
        report.CaveEntrances, report.InaccessibleEntrances, report.ReachableCaveRatio, world.Caves.AirCellCount, world.Labyrinth.WalkableCount, issues, realGeology.Findings);

    void Penalize(bool condition, double penalty, string issue) { if (!condition) return; score -= penalty; issues.Add(issue); }
}

static void WriteCsv(string path, IReadOnlyList<BacktestResult> results)
{
    var csv = new StringBuilder("style,seed,resolution,fingerprint,score,min,max,range,stddev,maxRise,maxApex,flatRatio,steepRatio,repeatedGrade,biomes,dominantBiome,dominantGeology,components,riverCoverage,waterCoverage,riverCells,brokenRivers,uphillDrainage,uphillRivers,outlets,caveEntrances,inaccessibleCaves,reachableCaves,caveAir,labyrinth,issues\n");
    foreach (BacktestResult x in results)
        csv.AppendLine(string.Join(',', x.Style, x.Seed, x.Resolution, x.Fingerprint, F(x.Score), F(x.MinimumHeight), F(x.MaximumHeight), F(x.HeightRange), F(x.HeightStdDev), F(x.MaximumNeighbourRise), F(x.MaximumLocalApex), F(x.FlatAreaRatio), F(x.SteepAreaRatio), F(x.RepeatedSteepGradeRatio), x.BiomeCount, F(x.DominantBiomeRatio), F(x.DominantGeologyRatio), x.BiomeComponents, F(x.RiverCoverage), F(x.WaterCoverage), x.RiverCells, x.BrokenRiverCells, x.UphillDrainageCells, x.UphillRiverCells, x.DrainageOutlets, x.CaveEntrances, x.InaccessibleEntrances, F(x.ReachableCaveRatio), x.CaveAirCells, x.LabyrinthWalkableCells, Quote(string.Join("; ", x.Issues))));
    File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
    static string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';
}

static void WriteSummary(string path, IReadOnlyList<BacktestResult> results, BacktestResult best, BacktestResult worst, BacktestResult render, DateTimeOffset started)
{
    var md = new StringBuilder();
    md.AppendLine("# Procedural World geological backtest").AppendLine();
    md.AppendLine($"Generated {results.Count} worlds in {(DateTimeOffset.UtcNow - started).TotalSeconds:F1}s. Scores validate topology and geological distributions; they are not artistic ratings.").AppendLine();
    md.AppendLine("| Profile | Resolution | Avg | Worst | Flat | Steep | Rivers | Reachable caves | Failures |");
    md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (var group in results.GroupBy(x => (x.Style, x.Resolution)).OrderBy(x => x.Key.Resolution).ThenBy(x => x.Key.Style))
        md.AppendLine($"| {group.Key.Style} | {group.Key.Resolution} | {group.Average(x => x.Score):F1} | {group.Min(x => x.Score):F1} | {group.Average(x => x.FlatAreaRatio):P1} | {group.Average(x => x.SteepAreaRatio):P1} | {group.Average(x => x.RiverCoverage):P2} | {group.Average(x => x.ReachableCaveRatio):P1} | {group.Count(x => x.Issues.Count > 0)} / {group.Count()} |");
    md.AppendLine().AppendLine($"**Best:** {best.Style}, seed `{best.Seed}`, score {best.Score:F1}.");
    md.AppendLine($"**Worst:** {worst.Style}, seed `{worst.Seed}`, score {worst.Score:F1}.");
    md.AppendLine($"**3D render candidate:** {render.Style}, seed `{render.Seed}`, score {render.Score:F1}, relief {render.HeightRange:F1}.").AppendLine();
    var findings = results.SelectMany(x => x.Issues).GroupBy(x => x).OrderByDescending(x => x.Count()).Take(20).ToArray();
    md.AppendLine("## Findings").AppendLine();
    if (findings.Length == 0) md.AppendLine("- No structural failures.");
    foreach (var finding in findings) md.AppendLine($"- {finding.Key}: {finding.Count()} cases");
    md.AppendLine().AppendLine("## Worst cases").AppendLine();
    foreach (BacktestResult item in results.OrderBy(x => x.Score).Take(10))
        md.AppendLine($"- {item.Style} `{item.Seed}` — {item.Score:F1}: {(item.Issues.Count == 0 ? "no structural findings" : string.Join("; ", item.Issues))}");
    File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
}

static void WriteRealGeologySummary(string path, IReadOnlyList<BacktestResult> results)
{
    var md = new StringBuilder("# Comparação com arquétipos geológicos reais\n\n");
    md.AppendLine("Comparação geomorfológica por métricas e formas diagnósticas; não substitui validação contra um DEM real.\n");
    md.AppendLine("| Perfil | Resolução | Nota média | Pior | Casos com achados |");
    md.AppendLine("|---|---:|---:|---:|---:|");
    foreach (var group in results.GroupBy(x => (x.Style, x.Resolution)).OrderBy(x => x.Key.Resolution).ThenBy(x => x.Key.Style))
        md.AppendLine($"| {group.Key.Style} | {group.Key.Resolution} | {group.Average(x => x.RealGeologyScore):F1} | {group.Min(x => x.RealGeologyScore):F1} | {group.Count(x => x.RealGeologyFindings.Count > 0)} / {group.Count()} |");
    md.AppendLine("\n## Achados mais frequentes\n");
    var findings = results.SelectMany(x => x.RealGeologyFindings).GroupBy(x => x).OrderByDescending(x => x.Count()).Take(30).ToArray();
    if (findings.Length == 0) md.AppendLine("- Nenhuma incompatibilidade nas faixas adotadas.");
    foreach (var finding in findings) md.AppendLine($"- {finding.Key}: {finding.Count()} casos");
    md.AppendLine("\n## Referências conceituais\n");
    md.AppendLine("- USGS, Geology of Canyonlands National Park: mesas, estratos resistentes e cânions fluviais encaixados.");
    md.AppendLine("- USGS, Geology of Mammoth Cave: calcário, dolinas, rios sumidouros e drenagem subterrânea.");
    md.AppendLine("- USGS, Geology of Glacier National Park: vales em U, circos, arestas, lagos e morainas.");
    md.AppendLine("- USGS, Principal Types of Volcanoes: cones, escudos, calderas e derrames de lava.");
    File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
}

static void WriteRegressionImages(string outputRoot, IReadOnlyList<TerrainStyle> styles, int size)
{
    string directory = Path.Combine(outputRoot, "regression");
    Directory.CreateDirectory(directory);
    foreach (TerrainStyle style in styles)
    {
        GeneratedWorld world = new WorldGenerator(2026, TerrainPresetCatalog.Create(style, size)).Generate();
        SurfaceMap map = world.Surface;
        float minimum = map.Height.Min(), range = Math.Max(.001f, map.Height.Max() - minimum);
        var ppm = new StringBuilder($"P3\n{map.Size} {map.Size}\n255\n");
        for (int z = 0; z < map.Size; z++)
        {
            for (int x = 0; x < map.Size; x++)
            {
                int i = map.Index(x, z); float normalized = (map.Height[i] - minimum) / range;
                int r = (int)(35 + normalized * 190), g = (int)(55 + normalized * 175), b = (int)(45 + normalized * 145);
                if (map.WaterDepth[i] > 0) { r = 25; g = 95; b = 190; }
                else if (map.CaveOpening[i]) { r = 18; g = 18; b = 18; }
                ppm.Append(r).Append(' ').Append(g).Append(' ').Append(b).Append(' ');
            }
            ppm.AppendLine();
        }
        File.WriteAllText(Path.Combine(directory, $"{style.ToString().ToLowerInvariant()}-2026-{size}.ppm"), ppm.ToString(), new UTF8Encoding(false));
    }
}

sealed record BacktestSuite(DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int[] Resolutions, long[] Seeds, IReadOnlyList<BacktestResult> Results, BacktestResult Best, BacktestResult Worst, BacktestResult RenderCandidate);
sealed record BacktestResult(TerrainStyle Style, long Seed, int Resolution, string Fingerprint, double Score, double RealGeologyScore,
    double MinimumHeight, double MaximumHeight, double HeightRange, double HeightStdDev,
    double MaximumNeighbourRise, double MaximumLocalApex, double FlatAreaRatio, double SteepAreaRatio, double RepeatedSteepGradeRatio,
    int BiomeCount, double DominantBiomeRatio, double DominantGeologyRatio, int BiomeComponents,
    double RiverCoverage, double WaterCoverage, int RiverCells, int BrokenRiverCells, int UphillDrainageCells, int UphillRiverCells, int DrainageOutlets,
    int CaveEntrances, int InaccessibleEntrances, double ReachableCaveRatio, long CaveAirCells, int LabyrinthWalkableCells,
    IReadOnlyList<string> Issues, IReadOnlyList<string> RealGeologyFindings);
