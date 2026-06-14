namespace MelonCompatInstaller;

internal sealed record MelonLoaderInstall(bool Exists, IReadOnlyList<string> Items, IReadOnlyList<string> ModDlls)
{
    public static MelonLoaderInstall Detect(string gameRoot)
    {
        var melonLoaderDirectory = Path.Combine(gameRoot, "MelonLoader");
        var doorstopConfig = Path.Combine(gameRoot, "doorstop_config.ini");
        var hasMelonLoaderDirectory = Directory.Exists(melonLoaderDirectory);
        var doorstopReferencesMelonLoader = File.Exists(doorstopConfig)
            && File.ReadAllText(doorstopConfig).Contains("MelonLoader", StringComparison.OrdinalIgnoreCase);

        if (!hasMelonLoaderDirectory && !doorstopReferencesMelonLoader)
            return new MelonLoaderInstall(false, Array.Empty<string>(), Array.Empty<string>());

        var items = new List<string>();
        foreach (var directoryName in new[] { "MelonLoader", "Mods", "Plugins", "UserData", "UserLibs" })
        {
            var directory = Path.Combine(gameRoot, directoryName);
            if (Directory.Exists(directory))
                items.Add(directory);
        }

        foreach (var fileName in new[] { "version.dll", "dobby.dll", "Latest.log", "MelonLoader.dll" })
        {
            var file = Path.Combine(gameRoot, fileName);
            if (File.Exists(file))
                items.Add(file);
        }

        if (doorstopReferencesMelonLoader)
            items.Add(doorstopConfig);

        var modsDirectory = Path.Combine(gameRoot, "Mods");
        var modDlls = Directory.Exists(modsDirectory)
            ? Directory.EnumerateFiles(modsDirectory, "*.dll", SearchOption.AllDirectories).ToArray()
            : Array.Empty<string>();

        return new MelonLoaderInstall(true, items.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), modDlls);
    }

    public void Remove(IProgress<string>? progress)
    {
        foreach (var item in Items.OrderByDescending(path => path.Length))
        {
            if (Directory.Exists(item))
            {
                Directory.Delete(item, recursive: true);
                progress?.Report("Removed MelonLoader directory: " + Path.GetFileName(item));
            }
            else if (File.Exists(item))
            {
                File.Delete(item);
                progress?.Report("Removed MelonLoader file: " + Path.GetFileName(item));
            }
        }
    }
}
