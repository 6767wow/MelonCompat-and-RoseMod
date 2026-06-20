namespace RoseMod;

public sealed class RoseModPaths
{
    public const string RootFolderName = "RoseMod";

    private RoseModPaths(string gameRoot)
    {
        GameRoot = Path.GetFullPath(gameRoot);
        Root = ResolveRoot(GameRoot);
        Core = Path.Combine(Root, "Core");
        MelonMods = Path.Combine(Root, "MelonMods");
        BepInExPlugins = Path.Combine(Root, "BepInExPlugins");
        Patchers = Path.Combine(Root, "Patchers");
        LegacyBepInExPatchers = Path.Combine(GameRoot, "BepInEx", "patchers");
        Interop = Path.Combine(Root, "interop");
        Il2CppAssemblies = Path.Combine(Root, "Il2CppAssemblies");
        UserData = Path.Combine(Root, "UserData");
        UserLibs = Path.Combine(Root, "UserLibs");
        Logs = Path.Combine(Root, "Logs");
        LogFile = Path.Combine(Logs, "RoseMod.log");
        Dotnet = Path.Combine(GameRoot, "dotnet");
        GameData = Directory.EnumerateDirectories(GameRoot, "*_Data", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path).Length)
            .FirstOrDefault() ?? GameRoot;
        GameManaged = Path.Combine(GameData, "Managed");
    }

    public string GameRoot { get; }
    public string GameData { get; }
    public string Root { get; }
    public string Core { get; }
    public string MelonMods { get; }
    public string BepInExPlugins { get; }
    public string Patchers { get; }
    public string LegacyBepInExPatchers { get; }
    public string Interop { get; }
    public string Il2CppAssemblies { get; }
    public string UserData { get; }
    public string UserLibs { get; }
    public string Logs { get; }
    public string LogFile { get; }
    public string Dotnet { get; }
    public string GameManaged { get; }

    public static RoseModPaths FromGameRoot(string gameRoot) => new(gameRoot);

    private static string ResolveRoot(string gameRoot)
    {
        var assemblyRoot = ResolveRootFromAssembly(gameRoot);
        return assemblyRoot ?? Path.Combine(gameRoot, RootFolderName);
    }

    private static string? ResolveRootFromAssembly(string gameRoot)
    {
        try
        {
            var coreDirectory = Path.GetDirectoryName(typeof(RoseModRuntime).Assembly.Location);
            if (string.IsNullOrWhiteSpace(coreDirectory))
                return null;

            var rootDirectory = Directory.GetParent(coreDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(rootDirectory))
                return null;

            if (!Path.GetFileName(rootDirectory).Equals(RootFolderName, StringComparison.OrdinalIgnoreCase))
                return null;

            var fullRoot = Path.GetFullPath(rootDirectory);
            var fullGameRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullRoot.StartsWith(fullGameRoot, StringComparison.OrdinalIgnoreCase) ? fullRoot : null;
        }
        catch
        {
            return null;
        }
    }

    public void Create()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Core);
        Directory.CreateDirectory(MelonMods);
        Directory.CreateDirectory(BepInExPlugins);
        Directory.CreateDirectory(Patchers);
        EnsureLegacyPatcherView();
        Directory.CreateDirectory(Interop);
        Directory.CreateDirectory(Il2CppAssemblies);
        Directory.CreateDirectory(UserData);
        Directory.CreateDirectory(UserLibs);
        Directory.CreateDirectory(Logs);
    }

    private void EnsureLegacyPatcherView()
    {
        try
        {
            var legacyBepInExRoot = Path.Combine(GameRoot, "BepInEx");
            var legacyCore = Path.Combine(legacyBepInExRoot, "core");
            if (Directory.Exists(legacyCore))
                return;

            Directory.CreateDirectory(legacyBepInExRoot);
            if (Directory.Exists(LegacyBepInExPatchers))
                return;

            if (!TryCreateDirectoryJunction(LegacyBepInExPatchers, Patchers))
                Directory.CreateDirectory(LegacyBepInExPatchers);
        }
        catch
        {
            try
            {
                Directory.CreateDirectory(LegacyBepInExPatchers);
            }
            catch
            {
            }
        }
    }

    private static bool TryCreateDirectoryJunction(string linkPath, string targetPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }
}
