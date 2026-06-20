namespace RoseMod;

public sealed class RoseModStartupOptions
{
    public RoseModStartupOptions(string gameRoot, string backend, string unityVersion)
    {
        GameRoot = gameRoot;
        Backend = backend;
        UnityVersion = unityVersion;
    }

    public string GameRoot { get; }
    public string Backend { get; }
    public string UnityVersion { get; }

    public static RoseModStartupOptions AutoDetect(string? gameRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(gameRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(gameRoot);

        var backend = File.Exists(Path.Combine(root, "GameAssembly.dll"))
            ? "IL2CPP"
            : Directory.EnumerateDirectories(root, "*_Data", SearchOption.TopDirectoryOnly)
                .Select(data => Path.Combine(data, "Managed"))
                .Any(Directory.Exists)
                    ? "Mono"
                    : "Unknown";

        return new RoseModStartupOptions(root, backend, string.Empty);
    }
}
