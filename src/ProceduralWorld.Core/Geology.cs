namespace ProceduralWorld.Core;

public enum GeologicalIdentity { AlluvialBasin, ErodedMassif, KarstHighland, BadlandsMesa, GlacialRange, VolcanicPlateau }

public sealed record GeologicalProfile(
    GeologicalIdentity Identity, float MacroRelief, float RidgeSharpness, float Erosion, float Strata,
    float Fissures, float Talus, float CaveDensity, float RiverAffinity);

public static class GeologicalProfiles
{
    public static GeologicalProfile For(Biome biome) => biome switch
    {
        Biome.River or Biome.Lake or Biome.Swamp => new(GeologicalIdentity.AlluvialBasin, .25f, .05f, .85f, .45f, .05f, .15f, .2f, 1f),
        Biome.Rock => new(GeologicalIdentity.KarstHighland, .9f, .55f, .75f, .7f, .65f, .7f, 1f, .65f),
        Biome.Desert => new(GeologicalIdentity.BadlandsMesa, .65f, .3f, .55f, 1f, .55f, .8f, .45f, .12f),
        Biome.Snow or Biome.Tundra => new(GeologicalIdentity.GlacialRange, 1f, .7f, .8f, .3f, .4f, .85f, .4f, .75f),
        Biome.Jungle or Biome.Forest => new(GeologicalIdentity.ErodedMassif, .75f, .4f, 1f, .5f, .35f, .6f, .75f, .9f),
        _ => new(GeologicalIdentity.VolcanicPlateau, .55f, .5f, .4f, .65f, .45f, .55f, .55f, .45f)
    };
}

public static class GeologicalShapes
{
    public static double ErodedMassif(double baseNoise, double ridge, double erosion)
        => Math.Pow(Math.Max(0, baseNoise), .75) * (1 + Math.Max(0, ridge) * .65) * (1 - Math.Max(0, erosion) * .45);
    public static double AngularPeak(double ridge, double detail)
        => Math.Pow(Math.Max(0, ridge), .48) + detail * .12;
    public static double Mesa(double baseNoise, double strata)
    {
        double terrace = Math.Round(Math.Max(0, baseNoise) * 7) / 7d;
        return terrace + strata * .035;
    }
    public static double Talus(double slope, double debris) => Math.Max(0, slope - .35) * (.35 + debris * .65);
    public static double Fissure(double signedNoise, double width) => 1 - Smooth(0, Math.Max(.0001, width), Math.Abs(signedNoise));
    private static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
}

public readonly record struct Stratum(float MinimumHeight, float MaximumHeight, string Material, float Hardness);

public sealed class GeologicalColumn
{
    public GeologicalColumn(Biome biome, float elevation, float slope)
    {
        Biome = biome; Elevation = elevation; Slope = slope; Profile = GeologicalProfiles.For(biome);
        Strata = BuildStrata(Profile, elevation);
    }
    public Biome Biome { get; }
    public float Elevation { get; }
    public float Slope { get; }
    public GeologicalProfile Profile { get; }
    public IReadOnlyList<Stratum> Strata { get; }
    private static IReadOnlyList<Stratum> BuildStrata(GeologicalProfile profile, float top) => new[]
    {
        new Stratum(top - 3, top, "regolith", .2f),
        new Stratum(top - 22, top - 3, profile.Identity == GeologicalIdentity.BadlandsMesa ? "sandstone" : "weathered-rock", .55f),
        new Stratum(float.MinValue, top - 22, profile.Identity == GeologicalIdentity.KarstHighland ? "limestone" : "bedrock", 1f)
    };
}
