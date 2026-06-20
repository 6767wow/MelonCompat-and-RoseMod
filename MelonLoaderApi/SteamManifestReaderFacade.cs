namespace MelonLoader.Utils;

public sealed class SteamManifestReader
{
    public SteamManifestReader(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? this[string key] => Values.TryGetValue(key, out var value) ? value : null;
}
