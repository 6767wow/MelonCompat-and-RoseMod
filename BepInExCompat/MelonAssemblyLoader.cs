using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace MelonLoader.BepInExCompat;

internal static class MelonAssemblyLoader
{
    private static readonly HashSet<string> LoadedLocations = new(StringComparer.OrdinalIgnoreCase);

    public static void LoadFromPluginsDirectory(string pluginsPath)
    {
        if (!Directory.Exists(pluginsPath))
        {
            CompatLog.Warning($"BepInEx plugin directory does not exist: {pluginsPath}");
            return;
        }

        var dlls = Directory.EnumerateFiles(pluginsPath, "*.dll", SearchOption.AllDirectories)
            .Where(ShouldConsiderDll)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CompatLog.Info($"Scanning {dlls.Length} DLL(s) in BepInEx/plugins for MelonLoader mods.");

        var loadedCount = 0;
        foreach (var dll in dlls)
        {
            try
            {
                var count = LoadFromFile(dll);
                loadedCount += count;
            }
            catch (Exception ex)
            {
                CompatLog.Error(ex, $"Failed to scan MelonLoader mod DLL: {dll}");
            }
        }

        CompatLog.Info($"MelonLoader compatibility scan complete. Loaded {loadedCount} melon(s).");
        MelonEventPump.ApplicationStart();
        MelonEventPump.ApplicationLateStart();
    }

    public static int LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!LoadedLocations.Add(fullPath))
            return 0;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(SafeLocation(a), fullPath, StringComparison.OrdinalIgnoreCase))
            ?? InteropNamespaceRewriter.LoadAssemblyWithFixups(fullPath);

        return LoadFromAssembly(assembly, fullPath);
    }

    public static int LoadFromAssembly(Assembly assembly, string location)
    {
        if (assembly == typeof(MelonMod).Assembly)
            return 0;

        var infos = PullMelonInfos(assembly);
        if (infos.Length == 0)
            return 0;

        var processAttributes = PullAttributes<MelonProcessAttribute>(assembly);
        if (!IsProcessCompatible(processAttributes))
        {
            CompatLog.Warning($"Skipping {Path.GetFileName(location)} because its MelonProcess attribute does not match {Process.GetCurrentProcess().ProcessName}.");
            return 0;
        }

        var gameAttributes = PullAttributes<MelonGameAttribute>(assembly);
        var priority = PullAttributes<MelonPriorityAttribute>(assembly).FirstOrDefault()?.Priority ?? 0;
        var optionalDependencies = PullAttributes<MelonOptionalDependenciesAttribute>(assembly).FirstOrDefault();
        var additionalCredits = PullAttributes<MelonAdditionalCreditsAttribute>(assembly).FirstOrDefault();
        var platform = PullAttributes<MelonPlatformAttribute>(assembly).FirstOrDefault();
        var domain = PullAttributes<MelonPlatformDomainAttribute>(assembly).FirstOrDefault();
        var verifyVersion = PullAttributes<VerifyLoaderVersionAttribute>(assembly).FirstOrDefault();
        var verifyBuild = PullAttributes<VerifyLoaderBuildAttribute>(assembly).FirstOrDefault();
        var melonColor = PullAttributes<MelonColorAttribute>(assembly).FirstOrDefault()?.DrawingColor;
        var authorColor = PullAttributes<MelonAuthorColorAttribute>(assembly).FirstOrDefault()?.DrawingColor;
        var id = PullAttributes<MelonIDAttribute>(assembly).FirstOrDefault()?.ID;

        if (platform is not null && !platform.IsCompatible(MelonUtils.CurrentPlatform))
        {
            CompatLog.Warning($"Skipping {Path.GetFileName(location)} because its MelonPlatform attribute does not match {MelonUtils.CurrentPlatform}.");
            return 0;
        }

        if (domain is not null && !domain.IsCompatible(MelonUtils.CurrentDomain))
        {
            CompatLog.Warning($"Skipping {Path.GetFileName(location)} because its MelonPlatformDomain attribute does not match {MelonUtils.CurrentDomain}.");
            return 0;
        }

        var count = 0;
        foreach (var info in infos)
        {
            if (info.SystemType is null)
            {
                CompatLog.Warning($"Skipping melon metadata in {assembly.GetName().Name}: SystemType was null.");
                continue;
            }

            if (!typeof(MelonBase).IsAssignableFrom(info.SystemType))
            {
                CompatLog.Warning($"Skipping {info.Name}: {info.SystemType.FullName} does not inherit MelonBase.");
                continue;
            }

            try
            {
                var melon = (MelonBase?)Activator.CreateInstance(info.SystemType);
                if (melon is null)
                {
                    CompatLog.Warning($"Skipping {info.Name}: Activator returned null.");
                    continue;
                }

                melon.AttachCompatMetadata(
                    assembly,
                    location,
                    info,
                    priority,
                    optionalDependencies,
                    additionalCredits,
                    processAttributes,
                    gameAttributes,
                    platform,
                    domain,
                    verifyVersion,
                    verifyBuild,
                    melonColor,
                    authorColor,
                    id);

                if (!melon.Register())
                {
                    CompatLog.Warning($"Skipped {info.Name} from {Path.GetFileName(location)} because melon initialization failed.");
                    continue;
                }

                AutoPatchWithHarmony(melon);
                LogLoadedMelon(melon, location);
                count++;
            }
            catch (Exception ex)
            {
                CompatLog.Error(ex, $"Failed to instantiate Melon {info.Name} from {assembly.GetName().Name}.");
            }
        }

        return count;
    }

    private static bool ShouldConsiderDll(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("MelonLoader.dll", StringComparison.OrdinalIgnoreCase))
            return false;

        var assemblyName = Path.GetFileNameWithoutExtension(path);
        return !assemblyName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Mono.Cecil", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("MonoMod", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("0Harmony", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("HarmonyX", StringComparison.OrdinalIgnoreCase);
    }

    private static MelonInfoAttribute[] PullMelonInfos(Assembly assembly)
    {
        try
        {
            var infos = assembly.GetCustomAttributes<MelonInfoAttribute>().ToArray();
            if (infos.Length > 0)
                return infos;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to read MelonInfo attributes from {assembly.GetName().Name}: {ex.Message}");
        }

        return TypeFallbackInfos(assembly).ToArray();
    }

    private static IEnumerable<MelonInfoAttribute> TypeFallbackInfos(Assembly assembly)
    {
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || !typeof(MelonBase).IsAssignableFrom(type))
                continue;

            yield return new MelonInfoAttribute(type, type.Name, assembly.GetName().Version?.ToString() ?? "0.0.0", assembly.GetName().Name ?? "Unknown", null);
        }
    }

    private static T[] PullAttributes<T>(Assembly assembly)
        where T : Attribute
    {
        try
        {
            return assembly.GetCustomAttributes<T>().ToArray();
        }
        catch
        {
            return Array.Empty<T>();
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static t => t is not null).Cast<Type>().ToArray();
        }
    }

    private static bool IsProcessCompatible(MelonProcessAttribute[] processAttributes)
    {
        if (processAttributes.Length == 0)
            return true;

        var exeName = Process.GetCurrentProcess().ProcessName;
        return processAttributes.Any(attribute => attribute.Universal || attribute.IsCompatible(exeName));
    }

    private static void AutoPatchWithHarmony(MelonBase melon)
    {
        if (melon.HarmonyDontPatchAll || melon.Assembly is null)
            return;

        try
        {
            melon.HarmonyInstance.PatchAll(melon.Assembly);
        }
        catch (Exception ex)
        {
            melon.LoggerInstance.Warning($"Harmony PatchAll failed: {ex.Message}");
        }
    }

    private static void LogLoadedMelon(MelonBase melon, string location)
    {
        var kind = melon is MelonPlugin ? "MelonPlugin" : "MelonMod";
        var info = melon.Info;
        CompatLog.Info($"Loaded {kind}: {info.Name} v{info.Version} by {info.Author} -> {Path.GetFileName(location)}");
    }

    private static string? SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch
        {
            return null;
        }
    }
}
