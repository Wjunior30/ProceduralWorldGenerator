using ProceduralWorld.Core;

internal sealed record RealGeologyAssessment(double Score, IReadOnlyList<string> Findings);

internal static class RealGeologyBenchmarks
{
    public static RealGeologyAssessment Assess(TerrainStyle style, SurfaceMap map, TerrainReport report)
    {
        var findings = new List<string>();
        double score = 100;
        double reliefRatio = (report.MaximumHeight - report.MinimumHeight) / Math.Max(1, map.CellSize * (map.Size - 1));
        double riverRatio = report.RiverCells / (double)map.Height.Length;
        int landformDiversity = map.Landforms.Distinct().Count();

        void Require(bool condition, double penalty, string finding)
        {
            if (condition) return;
            score -= penalty;
            findings.Add(finding);
        }
        double Ratio(params LandformKind[] kinds) => map.Landforms.Count(kinds.Contains) / (double)map.Landforms.Length;

        switch (style)
        {
            case TerrainStyle.Natural:
                Require(landformDiversity >= 5, 18, $"paisagem natural possui apenas {landformDiversity} classes de forma");
                Require(reliefRatio is >= .10 and <= .70, 15, $"relevo relativo natural fora da faixa ({reliefRatio:F2})");
                Require(report.FlatAreaRatio >= .08 && report.SteepAreaRatio >= .03, 15, "não equilibra áreas planas e encostas");
                break;
            case TerrainStyle.Alpine:
                Require(reliefRatio >= .28, 22, $"relevo alpino insuficiente ({reliefRatio:F2})");
                Require(Ratio(LandformKind.Ridge, LandformKind.Cirque, LandformKind.Massif) >= .12, 22, "faltam cristas, circos e maciços alpinos");
                Require(report.SteepAreaRatio >= .16, 18, $"poucas encostas alpinas íngremes ({report.SteepAreaRatio:P1})");
                break;
            case TerrainStyle.Canyonlands:
                Require(Ratio(LandformKind.Mesa, LandformKind.Escarpment) >= .18, 24, "faltam mesas e escarpas sedimentares");
                Require(report.SteepAreaRatio >= .15, 18, $"cânions pouco encaixados ({report.SteepAreaRatio:P1} íngreme)");
                Require(riverRatio >= .008, 12, "drenagem superficial insuficiente para incisão");
                break;
            case TerrainStyle.Karst:
                Require(Ratio(LandformKind.Sinkhole, LandformKind.DryValley) >= .015, 25, "faltam dolinas e vales secos");
                Require(report.ReachableCaveRatio >= .90 && report.CaveEntrances >= 3, 20, "rede subterrânea cárstica insuficiente");
                Require(riverRatio <= .045, 12, $"drenagem superficial excessiva para carste ({riverRatio:P1})");
                break;
            case TerrainStyle.RollingPlains:
                Require(reliefRatio <= .18, 22, $"planície com relevo relativo excessivo ({reliefRatio:F2})");
                Require(report.FlatAreaRatio >= .32, 22, $"área plana insuficiente ({report.FlatAreaRatio:P1})");
                Require(Ratio(LandformKind.Floodplain, LandformKind.Plain, LandformKind.Terrace) >= .10, 18, "faltam várzeas, planícies e terraços");
                break;
            case TerrainStyle.Glacial:
                Require(reliefRatio >= .22, 20, $"relevo glacial insuficiente ({reliefRatio:F2})");
                Require(Ratio(LandformKind.Cirque, LandformKind.Moraine, LandformKind.Ridge) >= .06, 25, "faltam circos, morainas e cristas glaciais");
                Require(report.FlatAreaRatio >= .08, 12, "faltam fundos largos de vales glaciais");
                break;
            case TerrainStyle.Volcanic:
                Require(Ratio(LandformKind.VolcanicCone, LandformKind.Caldera, LandformKind.LavaField) >= .08, 25, "faltam cones, calderas e campos de lava");
                Require(reliefRatio is >= .06 and <= .55, 15, $"perfil vulcânico incompatível ({reliefRatio:F2})");
                Require(map.Geology.Count(x => x == GeologicalIdentity.VolcanicPlateau) >= map.Geology.Length * .45, 18, "substrato vulcânico não domina o edifício");
                break;
        }
        return new(Math.Max(0, score), findings);
    }
}
