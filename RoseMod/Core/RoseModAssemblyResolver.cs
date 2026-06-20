using System.Reflection;

namespace RoseMod;

internal static class RoseModAssemblyResolver
{
    private static readonly Dictionary<string, string> PathsByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static bool installed;
    private static bool preferMonoHarmony;

    public static void Install(RoseModPaths paths)
    {
        preferMonoHarmony = !File.Exists(Path.Combine(paths.GameRoot, "GameAssembly.dll"));

        foreach (var directory in GetAssemblyDirectories(paths))
            Index(directory);

        PreferBackendSpecificAssemblies(paths);

        if (installed)
            return;

        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        installed = true;
            RoseModLog.Info($"Indexed {PathsByName.Count} managed assembl{(PathsByName.Count == 1 ? "y" : "ies")} for RoseMod resolution.");
    }

    private static void PreferBackendSpecificAssemblies(RoseModPaths paths)
    {
        if (!preferMonoHarmony)
            return;

        var monoLib = Path.Combine(paths.Core, "lib", "mono");
        PreferAssemblyPath("BepInEx", Path.Combine(monoLib, "BepInEx.dll"));
        PreferAssemblyPath("0Harmony", Path.Combine(monoLib, "0Harmony.dll"));
        PreferAssemblyPath("MonoMod.Utils", Path.Combine(monoLib, "MonoMod.Utils.dll"));
        PreferAssemblyPath("MonoMod.RuntimeDetour", Path.Combine(monoLib, "MonoMod.RuntimeDetour.dll"));
    }

    private static void PreferAssemblyPath(string assemblyName, string path)
    {
        if (File.Exists(path))
            PathsByName[assemblyName] = path;
    }

    public static void Index(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!PathsByName.ContainsKey(name))
                PathsByName[name] = dll;

            if (name.Equals("BepInEx.Core", StringComparison.OrdinalIgnoreCase) && !PathsByName.ContainsKey("BepInEx"))
                PathsByName["BepInEx"] = dll;
            if (name.Equals("BepInEx.Core", StringComparison.OrdinalIgnoreCase) && !PathsByName.ContainsKey("BepInEx.Preloader.Core"))
                PathsByName["BepInEx.Preloader.Core"] = dll;
        }
    }

    private static IEnumerable<string> GetAssemblyDirectories(RoseModPaths paths)
    {
        yield return paths.Core;
        yield return paths.Dotnet;
        yield return paths.GameManaged;
        yield return Path.Combine(paths.GameData, "Managed", "UnityEngine");
        yield return paths.Interop;
        yield return paths.Il2CppAssemblies;
        yield return paths.MelonMods;
        yield return paths.BepInExPlugins;
        yield return paths.Patchers;
        yield return paths.UserLibs;
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (!PathsByName.TryGetValue(name!, out var path)
            && IsBepInExFacadeAlias(name!)
            && PathsByName.TryGetValue("BepInEx.Core", out var bepinexCorePath))
        {
            path = bepinexCorePath;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            WarnMissingInteropAssembly(name!);
            return null;
        }

        try
        {
            return Assembly.LoadFrom(path);
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"Failed to resolve {name} from {path}: {ex.Message}");
            return null;
        }
    }

    private static void WarnMissingInteropAssembly(string name)
    {
        if (!MissingWarnings.Add(name))
            return;

        if (name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
        {
            RoseModLog.Warning($"Could not resolve {name}. For IL2CPP mods, place generated interop assemblies in RoseMod/interop or RoseMod/Il2CppAssemblies.");
        }
    }

    private static bool IsBepInExFacadeAlias(string name)
    {
        return name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BepInEx.Preloader.Core", StringComparison.OrdinalIgnoreCase);
    }
}
