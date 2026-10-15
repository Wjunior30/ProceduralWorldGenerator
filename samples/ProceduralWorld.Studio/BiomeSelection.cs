using ProceduralWorld.Core;

namespace ProceduralWorld.Studio;

internal static class BiomeSelection
{
    public static void Configure(WorldSettings settings, string[]? names)
    {
        if (names is null) return;
        Biome[] parsed = names
            .Select(name => Enum.TryParse(name, true, out Biome biome) ? (Biome?)biome : null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        if (parsed.Length == 0 || !parsed.Any(x => x is not Biome.River and not Biome.Lake))
            throw new ArgumentException("Selecione pelo menos um bioma terrestre.");
        settings.EnabledBiomes = parsed;
    }
}
