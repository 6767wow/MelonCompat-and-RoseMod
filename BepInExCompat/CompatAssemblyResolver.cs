using System.Reflection;

namespace MelonLoader.BepInExCompat;

internal static class CompatAssemblyResolver
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> AssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private static bool installed;

    public static void Install(string pluginsPath)
    {
        lock (Gate)
        {
            IndexDirectory(pluginsPath);
            if (installed)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            installed = true;
        }
    }

    public static void IndexDirectory(string directory)
    {
        IndexDirectory(directory, includeProvidedAssemblies: false);
    }

    public static void IndexDirectory(string directory, bool includeProvidedAssemblies)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(name) && (includeProvidedAssemblies || !IsProvidedByBepInExOrGame(name)))
                AssemblyPaths[name] = path;
        }
    }

    private static Assembly? ResolveAssembly(object? sender, ResolveEventArgs args)
    {
        var requestedName = new AssemblyName(args.Name).Name;
        if (requestedName is null)
            return null;

        if (requestedName.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase))
            return typeof(MelonMod).Assembly;

        var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (loadedAssembly is not null)
            return loadedAssembly;

        lock (Gate)
        {
            if (!AssemblyPaths.TryGetValue(requestedName, out var path) || !File.Exists(path))
                return null;

            try
            {
                return Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"Failed to resolve dependency {requestedName} from {path}: {ex.Message}");
                return null;
            }
        }
    }

    private static bool IsProvidedByBepInExOrGame(string assemblyName)
    {
        return assemblyName.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith("Mono.Cecil", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("0Harmony", StringComparison.OrdinalIgnoreCase)
            || assemblyName.Equals("HarmonyX", StringComparison.OrdinalIgnoreCase);
    }
}
