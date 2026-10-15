using System.Reflection;
using ProceduralWorld.Core;

namespace ProceduralWorld.Studio;

/// <summary>Unity runtime and editor tools embedded in build and published Studio.</summary>
internal static class UnityProjectBundle
{
    private const string Prefix = "UnityTemplate/";
    private static readonly Assembly Source = typeof(UnityProjectBundle).Assembly;
    private static IEnumerable<string> Resources() => Source.GetManifestResourceNames().Where(x => x.StartsWith(Prefix,StringComparison.Ordinal)).OrderBy(x=>x,StringComparer.Ordinal);
    public static IReadOnlyList<string> Files => Resources().Select(x=>x[Prefix.Length..].Replace('\\','/')).Append("Assets/Plugins/ProceduralWorld.Core.dll").Append("Assets/StreamingAssets/ProceduralWorld/world-data.json").Append("Assets/StreamingAssets/ProceduralWorld/world-config.json").ToArray();
    public static int Write(string root)
    {
        foreach(string name in Resources()) {
            string path=Path.Combine(root,name[Prefix.Length..].Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using Stream input=Source.GetManifestResourceStream(name)!;
            using FileStream output=File.Create(path);input.CopyTo(output);
        }
        string plugins=Path.Combine(root,"Assets","Plugins");Directory.CreateDirectory(plugins);
        File.Copy(typeof(WorldGenerator).Assembly.Location,Path.Combine(plugins,"ProceduralWorld.Core.dll"),true);
        string data=Path.Combine(root,"Assets","StreamingAssets","ProceduralWorld");Directory.CreateDirectory(data);
        foreach(string file in new[]{"world-data.json","world-config.json"})File.Copy(Path.Combine(root,"ProceduralWorld",file),Path.Combine(data,file),true);
        return Files.Count;
    }
}
