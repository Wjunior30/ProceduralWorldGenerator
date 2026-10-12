namespace ProceduralWorld.Core;

public enum GeologicalIdentity { AlluvialBasin, ErodedMassif, KarstHighland, BadlandsMesa, GlacialRange, VolcanicPlateau }

public sealed record GeologicalProfile(
    GeologicalIdentity Identity, float MacroRelief, float RidgeSharpness, float Erosion, float Strata,
    float Fissures, float Talus, float CaveDensity, float RiverAffinity, float Hardness);

public static class GeologicalProfiles
{
    private static readonly IReadOnlyDictionary<GeologicalIdentity, GeologicalProfile> Profiles =
        new Dictionary<GeologicalIdentity, GeologicalProfile>
        {
            [GeologicalIdentity.AlluvialBasin] = new(GeologicalIdentity.AlluvialBasin, .20f, .04f, .88f, .38f, .04f, .12f, .20f, 1f, .24f),
            [GeologicalIdentity.ErodedMassif] = new(GeologicalIdentity.ErodedMassif, .74f, .42f, .82f, .48f, .32f, .62f, .62f, .86f, .66f),
            [GeologicalIdentity.KarstHighland] = new(GeologicalIdentity.KarstHighland, .72f, .38f, .74f, .74f, .58f, .55f, 1f, .68f, .58f),
            [GeologicalIdentity.BadlandsMesa] = new(GeologicalIdentity.BadlandsMesa, .58f, .28f, .48f, 1f, .46f, .78f, .34f, .22f, .52f),
            [GeologicalIdentity.GlacialRange] = new(GeologicalIdentity.GlacialRange, 1f, .72f, .70f, .24f, .38f, .88f, .38f, .74f, .82f),
            [GeologicalIdentity.VolcanicPlateau] = new(GeologicalIdentity.VolcanicPlateau, .68f, .62f, .34f, .62f, .72f, .64f, .76f, .35f, .88f)
        };

    public static GeologicalProfile For(GeologicalIdentity identity) => Profiles[identity];

    // Compatibility helper for callers that only have a biome. Terrain generation
    // assigns geology first and does not use this mapping.
    public static GeologicalProfile For(Biome biome) => biome switch
    {
        Biome.River or Biome.Lake or Biome.Swamp or Biome.Beach => For(GeologicalIdentity.AlluvialBasin),
        Biome.Desert => For(GeologicalIdentity.BadlandsMesa),
        Biome.Snow or Biome.Tundra => For(GeologicalIdentity.GlacialRange),
        Biome.Jungle or Biome.Forest => For(GeologicalIdentity.ErodedMassif),
        Biome.Rock => For(GeologicalIdentity.ErodedMassif),
        _ => For(GeologicalIdentity.AlluvialBasin)
    };
}

public static class GeologicalShapes
{
    public static double ErodedMassif(double baseNoise, double ridge, double erosion)
        => Math.Pow(Math.Max(0, baseNoise), .75) * (1 + Math.Max(0, ridge) * .65) * (1 - Math.Max(0, erosion) * .45);
    public static double AngularPeak(double ridge, double detail)
        => Math.Pow(Math.Max(0, ridge), .62) + detail * .10;
    public static double Mesa(double baseNoise, double strata)
    {
        double level = Math.Floor(Math.Max(0, baseNoise) * 6);
        double local = Math.Max(0, baseNoise) * 6 - level;
        double softened = local * local * (3 - 2 * local);
        return (level + softened) / 6d + strata * .028;
    }
    public static double Talus(double slope, double debris) => Math.Max(0, slope - .35) * (.35 + debris * .65);
    public static double Fissure(double signedNoise, double width) => 1 - Smooth(0, Math.Max(.0001, width), Math.Abs(signedNoise));
    private static double Smooth(double a, double b, double x) { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
}

public readonly record struct Stratum(float MinimumHeight, float MaximumHeight, string Material, float Hardness);

public sealed class GeologicalColumn
{
    public GeologicalColumn(Biome biome, float elevation, float slope)
        : this(GeologicalProfiles.For(biome).Identity, biome, elevation, slope, 0) { }

    public GeologicalColumn(GeologicalIdentity identity, Biome biome, float elevation, float slope, float strata)
    {
        Identity = identity; Biome = biome; Elevation = elevation; Slope = slope; StrataPhase = strata;
        Profile = GeologicalProfiles.For(identity); Strata = BuildStrata(Profile, elevation, strata);
    }
    public GeologicalIdentity Identity { get; }
    public Biome Biome { get; }
    public float Elevation { get; }
    public float Slope { get; }
    public float StrataPhase { get; }
    public GeologicalProfile Profile { get; }
    public IReadOnlyList<Stratum> Strata { get; }

    private static IReadOnlyList<Stratum> BuildStrata(GeologicalProfile profile, float top, float phase)
    {
        float regolith = 2.2f + (1 - profile.Hardness) * 3.5f;
        float weathered = 15 + profile.Strata * 15 + phase * 3;
        string middle = profile.Identity switch
        {
            GeologicalIdentity.BadlandsMesa => "sandstone",
            GeologicalIdentity.KarstHighland => "weathered-limestone",
            GeologicalIdentity.GlacialRange => "fractured-granite",
            GeologicalIdentity.VolcanicPlateau => "weathered-basalt",
            GeologicalIdentity.AlluvialBasin => "compacted-sediment",
            _ => "weathered-rock"
        };
        string baseMaterial = profile.Identity switch
        {
            GeologicalIdentity.KarstHighland => "limestone",
            GeologicalIdentity.VolcanicPlateau => "basalt",
            GeologicalIdentity.GlacialRange => "granite",
            GeologicalIdentity.BadlandsMesa => "cemented-sandstone",
            GeologicalIdentity.AlluvialBasin => "sedimentary-bedrock",
            _ => "bedrock"
        };
        return new[]
        {
            new Stratum(top - regolith, top, "regolith", .18f + profile.Hardness * .12f),
            new Stratum(top - weathered, top - regolith, middle, Math.Min(.9f, profile.Hardness * .72f)),
            new Stratum(float.MinValue, top - weathered, baseMaterial, profile.Hardness)
        };
    }
}
