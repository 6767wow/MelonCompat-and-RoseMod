using System.Reflection;

namespace RoseMod;

internal static class RoseModBepInExBridge
{
    public static void Load(RoseModPaths paths)
    {
        if (!Directory.Exists(paths.BepInExPlugins))
            return;

        InitializeFacadePaths(paths);

        var dlls = Directory.EnumerateFiles(paths.BepInExPlugins, "*.dll", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var managedDlls = dlls.Where(IsManagedAssembly).ToArray();

        if (managedDlls.Length != dlls.Length)
            RoseModLog.Info($"Skipped {dlls.Length - managedDlls.Length} native/non-managed DLL(s) in {Path.GetFileName(paths.Root)}/BepInExPlugins.");

        RoseModLog.Info($"Scanning {managedDlls.Length} managed DLL(s) in {Path.GetFileName(paths.Root)}/BepInExPlugins.");
        var candidates = DiscoverPluginCandidates(managedDlls);
        foreach (var candidate in OrderPluginCandidates(candidates))
        {
            try
            {
                LoadPlugin(candidate.Type, candidate.Path);
            }
            catch (Exception ex)
            {
                RoseModLog.Error(ex, $"Failed to load BepInEx-style plugin: {candidate.Type.FullName} -> {Path.GetFileName(candidate.Path)}");
            }
        }
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static List<PluginCandidate> DiscoverPluginCandidates(string[] paths)
    {
        var candidates = new List<PluginCandidate>();
        foreach (var path in paths)
        {
            try
            {
                var assembly = Assembly.LoadFrom(path);
                foreach (var type in GetLoadableTypes(assembly).Where(IsBepInExPluginType))
                {
                    var metadata = GetPluginMetadata(type);
                    if (metadata.Guid is null)
                    {
                        RoseModLog.Warning($"Skipped BepInEx-style plugin type without a GUID: {type.FullName}");
                        continue;
                    }

                    candidates.Add(new PluginCandidate(type, path, metadata.Guid, metadata.Name, GetPluginDependencies(type)));
                }
            }
            catch (Exception ex)
            {
                RoseModLog.Error(ex, $"Failed to inspect BepInEx-style plugin assembly: {path}");
            }
        }

        return candidates;
    }

    private static IReadOnlyList<PluginCandidate> OrderPluginCandidates(List<PluginCandidate> candidates)
    {
        var byGuid = new Dictionary<string, PluginCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (byGuid.ContainsKey(candidate.Guid))
            {
                RoseModLog.Warning($"Duplicate BepInEx plugin GUID {candidate.Guid}; keeping {Path.GetFileName(byGuid[candidate.Guid].Path)} before {Path.GetFileName(candidate.Path)}.");
                continue;
            }

            byGuid[candidate.Guid] = candidate;
        }

        var ordered = new List<PluginCandidate>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in byGuid.Values.OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase))
            Visit(candidate);

        return ordered;

        void Visit(PluginCandidate candidate)
        {
            if (visited.Contains(candidate.Guid))
                return;

            if (!visiting.Add(candidate.Guid))
            {
                RoseModLog.Warning($"Cyclic BepInEx plugin dependency detected at {candidate.DisplayName}; loading remaining plugins in discovered order.");
                return;
            }

            foreach (var dependency in candidate.Dependencies)
            {
                if (byGuid.TryGetValue(dependency.Guid, out var dependencyCandidate))
                {
                    Visit(dependencyCandidate);
                    continue;
                }

                if (dependency.Required)
                    RoseModLog.Warning($"{candidate.DisplayName} declares missing hard dependency {dependency.Guid}; RoseMod will still try to load it.");
            }

            visiting.Remove(candidate.Guid);
            if (visited.Add(candidate.Guid))
                ordered.Add(candidate);
        }
    }

    private static PluginMetadata GetPluginMetadata(Type type)
    {
        var metadata = type.GetCustomAttributes(false)
            .FirstOrDefault(attribute => attribute.GetType().FullName == "BepInEx.BepInPlugin");
        if (metadata is null)
            return new PluginMetadata(null, null);

        return new PluginMetadata(
            metadata.GetType().GetProperty("GUID")?.GetValue(metadata) as string,
            metadata.GetType().GetProperty("Name")?.GetValue(metadata)?.ToString());
    }

    private static PluginDependency[] GetPluginDependencies(Type type)
    {
        return type.GetCustomAttributes(false)
            .Where(attribute => attribute.GetType().FullName == "BepInEx.BepInDependency")
            .Select(attribute =>
            {
                var dependencyGuid = attribute.GetType().GetProperty("DependencyGUID")?.GetValue(attribute) as string;
                var flags = attribute.GetType().GetProperty("Flags")?.GetValue(attribute)?.ToString() ?? string.Empty;
                return string.IsNullOrWhiteSpace(dependencyGuid)
                    ? null
                    : new PluginDependency(dependencyGuid!, flags.IndexOf("SoftDependency", StringComparison.OrdinalIgnoreCase) < 0);
            })
            .Where(dependency => dependency is not null)
            .Cast<PluginDependency>()
            .ToArray();
    }

    private static void LoadPlugin(Type type, string path)
    {
        var instance = CreatePluginInstance(type, out var unityComponent);
        if (instance is null)
            return;

        RegisterPlugin(type, instance);
        if (unityComponent)
        {
            RoseModLog.Info($"Activating BepInEx-style Unity plugin: {type.FullName} -> {Path.GetFileName(path)}");
            ActivateUnityComponent(instance);
        }
        else
        {
            InvokeIfExists(type, instance, "Load");
            InvokeIfExists(type, instance, "Awake");
            InvokeIfExists(type, instance, "OnEnable");
            InvokeIfExists(type, instance, "Start");
        }

        RoseModLog.Info($"Loaded BepInEx-style plugin: {type.FullName} -> {Path.GetFileName(path)}");
    }

    private static object? CreatePluginInstance(Type type, out bool unityComponent)
    {
        unityComponent = false;
        var monoBehaviourType = FindUnityType("UnityEngine.MonoBehaviour");
        if (monoBehaviourType is null || !monoBehaviourType.IsAssignableFrom(type))
            return Activator.CreateInstance(type);

        var gameObjectType = FindUnityType("UnityEngine.GameObject")
            ?? throw new TypeLoadException("UnityEngine.GameObject");
        var gameObject = Activator.CreateInstance(gameObjectType, $"RoseMod BepInEx Plugin - {type.FullName}")
            ?? throw new InvalidOperationException("Unity did not create a plugin GameObject.");

        gameObjectType.GetMethod("SetActive", new[] { typeof(bool) })?.Invoke(gameObject, new object[] { false });
        TryDontDestroyOnLoad(gameObject);

        var addComponent = gameObjectType.GetMethod("AddComponent", new[] { typeof(Type) })
            ?? throw new MissingMethodException(gameObjectType.FullName, "AddComponent");
        var instance = addComponent.Invoke(gameObject, new object[] { type });
        unityComponent = instance is not null;
        return instance;
    }

    private static void ActivateUnityComponent(object component)
    {
        try
        {
            var gameObject = component.GetType().GetProperty("gameObject")?.GetValue(component);
            gameObject?.GetType().GetMethod("SetActive", new[] { typeof(bool) })?.Invoke(gameObject, new object[] { true });
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"Failed to activate BepInEx Unity component {component.GetType().FullName}: {ex.Message}");
        }
    }

    private static void TryDontDestroyOnLoad(object gameObject)
    {
        try
        {
            var unityObjectType = FindUnityType("UnityEngine.Object");
            unityObjectType?.GetMethod("DontDestroyOnLoad", BindingFlags.Public | BindingFlags.Static, null, new[] { unityObjectType }, null)
                ?.Invoke(null, new[] { gameObject });
        }
        catch
        {
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

    internal static void InitializeFacadePaths(RoseModPaths paths)
    {
        foreach (var facade in LoadBepInExFacadeAssemblies(paths))
            InitializeFacadePaths(paths, facade);
    }

    private static void InitializeFacadePaths(RoseModPaths paths, Assembly facade)
    {
        var pathsType = facade.GetType("BepInEx.Paths", throwOnError: false);
        if (pathsType is null)
            return;

        SetStaticProperty(pathsType, "GameRootPath", paths.GameRoot);
        SetStaticProperty(pathsType, "BepInExRootPath", paths.Root);
        SetStaticProperty(pathsType, "PluginPath", paths.BepInExPlugins);
        SetStaticProperty(pathsType, "ConfigPath", Path.Combine(paths.Root, "Config"));
        SetStaticProperty(pathsType, "BepInExConfigPath", Path.Combine(paths.Root, "Config", "BepInEx.cfg"));
        SetStaticProperty(pathsType, "CachePath", Path.Combine(paths.Root, "Cache"));
        SetStaticProperty(pathsType, "PatcherPluginPath", paths.Patchers);
        SetStaticProperty(pathsType, "ManagedPath", Directory.Exists(paths.GameManaged) ? paths.GameManaged : paths.Interop);
        SetStaticProperty(pathsType, "GameDataPath", paths.GameData);
        SetStaticProperty(pathsType, "ExecutablePath", ResolveExecutablePath(paths.GameRoot));
        SetStaticProperty(pathsType, "ProcessName", Path.GetFileNameWithoutExtension(ResolveExecutablePath(paths.GameRoot)));
        SetStaticProperty(pathsType, "BepInExAssemblyDirectory", paths.Core);
        SetStaticProperty(pathsType, "BepInExAssemblyPath", Path.Combine(paths.Core, "BepInEx.Core.dll"));
    }

    private static IEnumerable<Assembly> LoadBepInExFacadeAssemblies(RoseModPaths paths)
    {
        foreach (var name in new[] { "BepInEx.Core", "BepInEx" })
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
            if (loaded is not null)
            {
                yield return loaded;
                continue;
            }

            var facadePath = name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(paths.Core, "lib", "mono", "BepInEx.dll")
                : Path.Combine(paths.Core, "BepInEx.Core.dll");
            if (File.Exists(facadePath))
                yield return Assembly.LoadFrom(facadePath);
        }
    }

    private static void SetStaticProperty(Type type, string name, string value)
    {
        var setter = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true);
        setter?.Invoke(null, new object[] { value });
    }

    private static void RegisterPlugin(Type type, object instance)
    {
        var metadata = type.GetCustomAttributes(false)
            .FirstOrDefault(attribute => attribute.GetType().FullName == "BepInEx.BepInPlugin");
        if (metadata is null)
            return;

        var guid = metadata.GetType().GetProperty("GUID")?.GetValue(metadata) as string;
        if (string.IsNullOrWhiteSpace(guid))
            return;

        var facade = metadata.GetType().Assembly;
        var pluginInfoType = facade.GetType("BepInEx.PluginInfo", throwOnError: false);
        var chainloaderType = facade.GetType("BepInEx.Chainloader", throwOnError: false);
        if (pluginInfoType is null || chainloaderType is null)
            return;

        var pluginInfo = type.GetProperty("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        if (pluginInfo is not null && !pluginInfoType.IsInstanceOfType(pluginInfo))
            pluginInfo = null;

        pluginInfo ??= Activator.CreateInstance(pluginInfoType);
        if (pluginInfo is null)
            return;

        pluginInfoType.GetProperty("Metadata")?.SetValue(pluginInfo, metadata);
        pluginInfoType.GetProperty("Type")?.SetValue(pluginInfo, type);
        pluginInfoType.GetProperty("TypeName")?.SetValue(pluginInfo, type.FullName);
        pluginInfoType.GetProperty("Location")?.SetValue(pluginInfo, type.Assembly.Location);
        var instanceProperty = pluginInfoType.GetProperty("Instance");
        if (instanceProperty is not null && instanceProperty.PropertyType.IsInstanceOfType(instance))
            instanceProperty.SetValue(pluginInfo, instance);
        SetEnumerableProperty(pluginInfoType, pluginInfo, "Dependencies", type, "BepInEx.BepInDependency");
        SetEnumerableProperty(pluginInfoType, pluginInfo, "Incompatibilities", type, "BepInEx.BepInIncompatibility");
        SetEnumerableProperty(pluginInfoType, pluginInfo, "Processes", type, "BepInEx.BepInProcess");
        chainloaderType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, new[] { guid, pluginInfo });
    }

    private static void SetEnumerableProperty(Type pluginInfoType, object pluginInfo, string propertyName, Type pluginType, string attributeFullName)
    {
        var property = pluginInfoType.GetProperty(propertyName);
        if (property is null)
            return;

        var values = pluginType.GetCustomAttributes(false)
            .Where(attribute => attribute.GetType().FullName == attributeFullName)
            .ToArray();
        property.SetValue(pluginInfo, CreateTypedAttributeCollection(property.PropertyType, values));
    }

    private static object CreateTypedAttributeCollection(Type propertyType, object[] values)
    {
        var elementType = GetEnumerableElementType(propertyType) ?? typeof(object);
        var array = Array.CreateInstance(elementType, values.Length);
        for (var i = 0; i < values.Length; i++)
            array.SetValue(values[i], i);

        return array;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        return type.GetInterfaces()
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static bool IsBepInExPluginType(Type type)
    {
        if (type.IsAbstract)
            return false;

        var hasPluginAttribute = type.GetCustomAttributes(false)
            .Any(attribute => attribute.GetType().FullName == "BepInEx.BepInPlugin");
        if (!hasPluginAttribute)
            return false;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName is "BepInEx.Unity.IL2CPP.BasePlugin" or "BepInEx.Unity.Mono.BaseUnityPlugin" or "BepInEx.BaseUnityPlugin")
                return true;
        }

        return false;
    }

    private static void InvokeIfExists(Type type, object instance, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method?.Invoke(instance, Array.Empty<object>());
    }

    private static string ResolveExecutablePath(string gameRoot)
    {
        return Directory.EnumerateFiles(gameRoot, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(path => Directory.Exists(Path.Combine(gameRoot, Path.GetFileNameWithoutExtension(path) + "_Data")))
            .OrderBy(path => Path.GetFileName(path).Length)
            .FirstOrDefault()
            ?? string.Empty;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            LogTypeLoadFailure(assembly, ex);
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static void LogTypeLoadFailure(Assembly assembly, ReflectionTypeLoadException exception)
    {
        var messages = exception.LoaderExceptions
            .Where(loaderException => loaderException is not null)
            .Select(loaderException => loaderException!.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        if (messages.Length == 0)
        {
            RoseModLog.Warning($"Some types in {assembly.GetName().Name} could not be loaded.");
            return;
        }

        RoseModLog.Warning($"Some types in {assembly.GetName().Name} could not be loaded: {string.Join(" | ", messages)}");
    }

    private sealed class PluginCandidate
    {
        public PluginCandidate(Type type, string path, string guid, string? name, PluginDependency[] dependencies)
        {
            Type = type;
            Path = path;
            Guid = guid;
            Name = name;
            Dependencies = dependencies;
        }

        public Type Type { get; }
        public string Path { get; }
        public string Guid { get; }
        public string? Name { get; }
        public PluginDependency[] Dependencies { get; }
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Guid : $"{Name} ({Guid})";
    }

    private sealed class PluginDependency
    {
        public PluginDependency(string guid, bool required)
        {
            Guid = guid;
            Required = required;
        }

        public string Guid { get; }
        public bool Required { get; }
    }

    private sealed class PluginMetadata
    {
        public PluginMetadata(string? guid, string? name)
        {
            Guid = guid;
            Name = name;
        }

        public string? Guid { get; }
        public string? Name { get; }
    }
}
