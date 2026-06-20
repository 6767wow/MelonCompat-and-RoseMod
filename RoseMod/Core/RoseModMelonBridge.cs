using System.Reflection;

namespace RoseMod;

internal static class RoseModMelonBridge
{
    public static void Load(RoseModPaths paths, RoseModStartupOptions options)
    {
        var melonLoaderPath = Path.Combine(paths.Core, "MelonLoader.dll");
        if (!File.Exists(melonLoaderPath))
        {
            RoseModLog.Warning("MelonLoader facade is missing; MelonLoader mod loading is disabled.");
            return;
        }

        try
        {
            var assembly = Assembly.LoadFrom(melonLoaderPath);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatLog", "Initialize", paths.LogFile);
            Invoke(assembly, "MelonLoader.MelonEnvironment", "Initialize", paths.MelonMods, paths.GameData, options.UnityVersion);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatAssemblyResolver", "Install", paths.MelonMods);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatAssemblyResolver", "IndexDirectory", paths.UserLibs);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatAssemblyResolver", "IndexDirectory", paths.Core);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatAssemblyResolver", "IndexDirectory", paths.Dotnet, true);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatAssemblyResolver", "IndexDirectory", paths.Interop);
            Invoke(assembly, "MelonLoader.BepInExCompat.CompatAssemblyResolver", "IndexDirectory", paths.Il2CppAssemblies);
            if (options.Backend.Equals("IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                Invoke(assembly, "MelonLoader.BepInExCompat.InteropNamespaceRewriter", "Initialize", paths.Root, GetFixupResolverDirectories(paths));
                Invoke(assembly, "MelonLoader.BepInExCompat.ClassInjectorCompatibilityPatches", "Install");
            }

            Invoke(assembly, "MelonLoader.BepInExCompat.MelonAssemblyLoader", "LoadFromPluginsDirectory", paths.MelonMods);
            Invoke(assembly, "MelonLoader.BepInExCompat.MelonEventPumpBehaviour", "InstallStandalone");
            Invoke(assembly, "MelonLoader.BepInExCompat.MelonEventPumpBehaviour", "PublishCurrentScene");
        }
        catch (Exception ex)
        {
            RoseModLog.Error(ex, "Failed to load MelonLoader-compatible mods.");
        }
    }

    private static string[] GetFixupResolverDirectories(RoseModPaths paths)
    {
        return new[]
        {
            paths.Core,
            paths.Interop,
            paths.Il2CppAssemblies,
            paths.Dotnet,
            paths.GameManaged,
            paths.UserLibs,
            paths.MelonMods
        };
    }

    private static void Invoke(Assembly assembly, string typeName, string methodName, params object[] args)
    {
        var type = assembly.GetType(typeName, throwOnError: true)!;
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(candidate => candidate.Name == methodName && ArgumentsMatch(candidate, args))
            ?? throw new MissingMethodException(typeName, methodName);
        method.Invoke(null, args);
    }

    private static bool ArgumentsMatch(MethodInfo method, object[] args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != args.Length)
            return false;

        for (var i = 0; i < parameters.Length; i++)
        {
            var arg = args[i];
            if (arg is null)
            {
                if (parameters[i].ParameterType.IsValueType)
                    return false;

                continue;
            }

            if (!parameters[i].ParameterType.IsInstanceOfType(arg))
                return false;
        }

        return true;
    }
}
