using System.Reflection;
using System.Threading;

namespace RoseMod;

internal static class RoseModSerializationFallback
{
    private static readonly HashSet<string> PluginAssemblyRoots = new(StringComparer.OrdinalIgnoreCase);
    private static int installed;
    private static int copyFailuresLogged;

    public static void Install(RoseModPaths paths, RoseModStartupOptions options)
    {
        if (!options.Backend.Equals("Mono", StringComparison.OrdinalIgnoreCase))
            return;

        if (Interlocked.CompareExchange(ref installed, 1, 0) != 0)
            return;

        AddPluginRoot(paths.BepInExPlugins);
        AddPluginRoot(paths.MelonMods);

        if (PluginAssemblyRoots.Count == 0)
            return;

        try
        {
            var unityObjectType = FindUnityType("UnityEngine.Object");
            if (unityObjectType is null)
            {
                RoseModLog.Warning("UnityEngine.Object was unavailable; plugin serialization fallback was not installed.");
                return;
            }

            var postfix = typeof(RoseModSerializationFallback).GetMethod(nameof(ClonePostfix), BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(typeof(RoseModSerializationFallback).FullName, nameof(ClonePostfix));
            var patched = PatchCloneMethods(unityObjectType, postfix);
            if (patched == 0)
            {
                RoseModLog.Warning("No Unity clone methods were available for plugin serialization fallback.");
                return;
            }

            RoseModLog.Info($"Installed RoseMod plugin serialization fallback on {patched} Unity clone method(s).");
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"Failed to install plugin serialization fallback: {Unwrap(ex).Message}");
        }
    }

    private static void AddPluginRoot(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            PluginAssemblyRoots.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        }
        catch
        {
        }
    }

    private static int PatchCloneMethods(Type unityObjectType, MethodInfo postfix)
    {
        var harmonyAssembly = LoadHarmonyAssembly();
        var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
        var harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
        var harmony = Activator.CreateInstance(harmonyType, "dev.jayde.rosemod.plugin-serialization-fallback");
        var harmonyPostfix = Activator.CreateInstance(harmonyMethodType, postfix);
        var patchMethod = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "Patch")
            .Select(method => new { Method = method, Parameters = method.GetParameters() })
            .FirstOrDefault(candidate => candidate.Parameters.Length >= 3
                && typeof(MethodBase).IsAssignableFrom(candidate.Parameters[0].ParameterType)
                && candidate.Parameters.Any(parameter => parameter.Name == "postfix"))
            ?.Method;
        if (patchMethod is null)
            return 0;

        var patched = 0;
        foreach (var method in GetCloneMethods(unityObjectType))
        {
            try
            {
                var parameters = patchMethod.GetParameters();
                var args = new object?[parameters.Length];
                args[0] = method;
                for (var i = 1; i < parameters.Length; i++)
                {
                    if (parameters[i].Name == "postfix")
                        args[i] = harmonyPostfix;
                }

                patchMethod.Invoke(harmony, args);
                patched++;
            }
            catch (Exception ex)
            {
                RoseModLog.Warning($"Failed to patch Unity clone method {method.Name}: {Unwrap(ex).Message}");
            }
        }

        return patched;
    }

    private static IEnumerable<MethodInfo> GetCloneMethods(Type unityObjectType)
    {
        return unityObjectType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => !method.ContainsGenericParameters)
            .Where(method => method.ReturnType != typeof(void) && unityObjectType.IsAssignableFrom(method.ReturnType))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || !unityObjectType.IsAssignableFrom(parameters[0].ParameterType))
                    return false;

                return method.Name.Equals("Instantiate", StringComparison.Ordinal)
                    || method.Name.StartsWith("Internal_Clone", StringComparison.Ordinal)
                    || method.Name.StartsWith("Internal_Instantiate", StringComparison.Ordinal);
            })
            .Distinct()
            .ToArray();
    }

    private static void ClonePostfix(object[] __args, object __result)
    {
        if (__result is null || __args.Length == 0 || __args[0] is null)
            return;

        try
        {
            CopySerializedPluginState(__args[0], __result);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref copyFailuresLogged, 1) == 0)
                RoseModLog.Warning($"Plugin serialization fallback copy failed: {Unwrap(ex).Message}");
        }
    }

    private static void CopySerializedPluginState(object original, object clone)
    {
        if (ReferenceEquals(original, clone))
            return;

        var gameObjectType = FindUnityType("UnityEngine.GameObject");
        var componentType = FindUnityType("UnityEngine.Component");
        if (gameObjectType is null || componentType is null)
            return;

        if (componentType.IsInstanceOfType(original) && componentType.IsInstanceOfType(clone))
        {
            CopyComponentFields(original, clone, componentType);
            return;
        }

        if (gameObjectType.IsInstanceOfType(original) && gameObjectType.IsInstanceOfType(clone))
            CopyGameObjectComponents(original, clone, componentType);
    }

    private static void CopyGameObjectComponents(object originalGameObject, object cloneGameObject, Type componentType)
    {
        var getComponents = originalGameObject.GetType().GetMethod("GetComponents", new[] { typeof(Type) });
        if (getComponents is null)
            return;

        var originalComponents = getComponents.Invoke(originalGameObject, new object[] { componentType }) as Array;
        var cloneComponents = getComponents.Invoke(cloneGameObject, new object[] { componentType }) as Array;
        if (originalComponents is null || cloneComponents is null)
            return;

        var length = Math.Min(originalComponents.Length, cloneComponents.Length);
        for (var i = 0; i < length; i++)
        {
            var originalComponent = originalComponents.GetValue(i);
            var cloneComponent = cloneComponents.GetValue(i);
            if (originalComponent is null || cloneComponent is null)
                continue;

            if (originalComponent.GetType() == cloneComponent.GetType())
                CopyComponentFields(originalComponent, cloneComponent, componentType);
        }
    }

    private static void CopyComponentFields(object originalComponent, object cloneComponent, Type componentBaseType)
    {
        var componentType = originalComponent.GetType();
        if (componentType != cloneComponent.GetType() || !IsPluginType(componentType))
            return;

        for (var current = componentType; current is not null && current != componentBaseType; current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!ShouldCopyField(field))
                    continue;

                try
                {
                    field.SetValue(cloneComponent, field.GetValue(originalComponent));
                }
                catch
                {
                }
            }
        }
    }

    private static bool ShouldCopyField(FieldInfo field)
    {
        if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
            return false;

        if (field.FieldType.IsPointer)
            return false;

        if (field.IsPublic)
            return true;

        return field.GetCustomAttributes(false)
            .Any(attribute => attribute.GetType().FullName == "UnityEngine.SerializeField");
    }

    private static bool IsPluginType(Type type)
    {
        try
        {
            var location = type.Assembly.Location;
            if (string.IsNullOrWhiteSpace(location))
                return false;

            var fullPath = Path.GetFullPath(location);
            return PluginAssemblyRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static Type? FindUnityType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                    return type;
            }
            catch
            {
            }
        }

        return Type.GetType(fullName + ", UnityEngine.CoreModule", throwOnError: false);
    }

    private static Assembly LoadHarmonyAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name?.Equals("0Harmony", StringComparison.OrdinalIgnoreCase) == true)
            ?? Assembly.Load("0Harmony");
    }

    private static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null }
            ? exception.InnerException
            : exception;
    }
}
