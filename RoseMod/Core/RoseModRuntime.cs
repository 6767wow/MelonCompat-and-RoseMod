using System.Reflection;
using System.Threading;

namespace RoseMod;

public static class RoseModRuntime
{
    private static bool started;
    private static int modsLoaded;

    public static RoseModStartupOptions Options { get; private set; } = RoseModStartupOptions.AutoDetect();
    public static RoseModPaths Paths { get; private set; } = RoseModPaths.FromGameRoot(Environment.CurrentDirectory);

    public static void Start(RoseModStartupOptions options)
    {
        if (started)
            return;

        started = true;
        Options = options;
        Paths = RoseModPaths.FromGameRoot(options.GameRoot);
        Paths.Create();

        RoseModConsole.Initialize();
        RoseModLog.Initialize(Paths.LogFile);
        RoseModLog.Banner();
        RoseModLog.Info("Logging initialized.");
        RoseModLog.Info($"Game root: {Paths.GameRoot}");
        RoseModLog.Info($"Backend: {options.Backend}");
        RoseModLog.Info($"RoseMod root: {Paths.Root}");

        ConfigureTrustedPlatformAssemblies(Paths);
        RoseModAssemblyResolver.Install(Paths);
        RoseModIl2CppInteropHost.Initialize(Paths, options);
        RoseModIl2CppFixBridge.Install(Paths, options);
        RoseModPatcherBridge.Load(Paths);
        RoseModSerializationFallback.Install(Paths, options);
        if (options.Backend.Equals("Mono", StringComparison.OrdinalIgnoreCase)
            && RoseModUnityThreadBridge.Enqueue(() => LoadModsOnce(Paths, Options), "Mono mod loading"))
        {
            return;
        }

        LoadModsOnce(Paths, options);
    }

    private static void LoadModsOnce(RoseModPaths paths, RoseModStartupOptions options)
    {
        if (Interlocked.Exchange(ref modsLoaded, 1) != 0)
            return;

        RoseModMelonBridge.Load(paths, options);
        RoseModBepInExBridge.Load(paths);
    }

    private static void ConfigureTrustedPlatformAssemblies(RoseModPaths paths)
    {
        if (!Directory.Exists(paths.Dotnet))
            return;

        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (existing is not null && !string.IsNullOrWhiteSpace(existing))
        {
            foreach (var entry in existing.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(entry))
                    entries.Add(entry);
            }
        }

        foreach (var dll in Directory.EnumerateFiles(paths.Dotnet, "*.dll", SearchOption.TopDirectoryOnly))
            entries.Add(Path.GetFullPath(dll));

        if (entries.Count == 0)
            return;

        AppDomain.CurrentDomain.SetData("TRUSTED_PLATFORM_ASSEMBLIES", string.Join(Path.PathSeparator.ToString(), entries));
        RoseModLog.Info($"Registered {entries.Count} trusted platform assembly path(s) for CoreCLR resolution.");
    }
}
