using System.Collections;
using System.Reflection;
using Mono.Cecil;

namespace RoseMod;

internal static class RoseModPatcherBridge
{
    private const string PatcherInfoAttributeName = "BepInEx.Preloader.Core.Patching.PatcherPluginInfoAttribute";
    private const string BasePatcherName = "BepInEx.Preloader.Core.Patching.BasePatcher";
    private const string TargetAssemblyAttributeName = "BepInEx.Preloader.Core.Patching.TargetAssemblyAttribute";
    private const string TargetTypeAttributeName = "BepInEx.Preloader.Core.Patching.TargetTypeAttribute";
    private const string AllAssemblies = "_all";

    public static void Load(RoseModPaths paths)
    {
        if (!Directory.Exists(paths.Patchers))
            return;

        RoseModBepInExBridge.InitializeFacadePaths(paths);
        RoseModAssemblyResolver.Index(paths.Patchers);

        var allDlls = Directory.EnumerateFiles(paths.Patchers, "*.dll", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var managedDlls = allDlls.Where(IsManagedAssembly).ToArray();

        if (managedDlls.Length != allDlls.Length)
            RoseModLog.Info($"Skipped {allDlls.Length - managedDlls.Length} native/non-managed DLL(s) in {Path.GetFileName(paths.Root)}/Patchers.");

        RoseModLog.Info($"Scanning {managedDlls.Length} managed DLL(s) in {Path.GetFileName(paths.Root)}/Patchers for BepInEx patchers.");
        if (managedDlls.Length == 0)
            return;

        var context = new PatcherExecutionContext(paths);
        context.LoadAvailableAssemblies();

        foreach (var dll in managedDlls)
            context.LoadPatchersFromAssembly(dll);

        if (context.Patchers.Count == 0)
        {
            RoseModLog.Info("No BepInEx patchers were found.");
            context.Dispose();
            return;
        }

        context.Execute();
        context.Dispose();
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

    private sealed class PatcherExecutionContext
    {
        private readonly RoseModPaths paths;
        private readonly DefaultAssemblyResolver cecilResolver = new();
        private readonly ReaderParameters readerParameters;
        private readonly Dictionary<string, AssemblyDefinition> availableAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> availableAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Assembly> loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PatchDefinition> patchDefinitions = new();
        private object? facadeContext;

        public PatcherExecutionContext(RoseModPaths paths)
        {
            this.paths = paths;
            foreach (var directory in GetAssemblySearchDirectories(paths))
            {
                if (Directory.Exists(directory))
                    cecilResolver.AddSearchDirectory(directory);
            }

            readerParameters = new ReaderParameters
            {
                AssemblyResolver = cecilResolver,
                ReadSymbols = false,
                ReadingMode = ReadingMode.Immediate
            };
        }

        public List<PatcherInstance> Patchers { get; } = new();

        public void LoadAvailableAssemblies()
        {
            foreach (var directory in GetTargetAssemblyDirectories(paths))
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(file);
                    if (availableAssemblies.ContainsKey(fileName))
                        continue;

                    try
                    {
                        var assembly = AssemblyDefinition.ReadAssembly(file, readerParameters);
                        if (assembly.Name.Name is "mscorlib" or "System")
                        {
                            assembly.Dispose();
                            continue;
                        }

                        availableAssemblies[fileName] = assembly;
                        availableAssemblyPaths[fileName] = file;
                    }
                    catch (BadImageFormatException)
                    {
                    }
                    catch (Exception ex)
                    {
                        RoseModLog.Warning($"Could not index patch target {fileName}: {ex.Message}");
                    }
                }
            }

            RoseModLog.Info($"Indexed {availableAssemblies.Count} patch target assembl{(availableAssemblies.Count == 1 ? "y" : "ies")}.");
        }

        public void LoadPatchersFromAssembly(string path)
        {
            try
            {
                var assembly = Assembly.LoadFrom(path);
                var count = 0;
                var types = GetLoadableTypes(assembly);
                foreach (var type in types.Where(IsPatcherType))
                {
                    var patcher = Activator.CreateInstance(type);
                    if (patcher is null)
                        continue;

                    var metadata = ReadMetadata(type);
                    var instance = new PatcherInstance(type, patcher, metadata.Name, metadata.Guid);
                    Patchers.Add(instance);
                    EnsureFacadeContext(patcher);
                    count++;
                    AddPatchDefinitions(instance);
                    RoseModLog.Info($"Loaded BepInEx patcher: {metadata.Name} {metadata.Version} ({metadata.Guid})");
                }

                foreach (var type in types.Where(IsLegacyStaticPatcherType))
                {
                    var instance = PatcherInstance.LegacyStatic(type);
                    Patchers.Add(instance);
                    count++;
                    AddLegacyPatchDefinitions(instance);
                    RoseModLog.Info($"Loaded BepInEx 5 static patcher: {type.FullName}");
                }

                if (count == 0)
                    RoseModLog.Warning($"No BepInEx patcher types found in {Path.GetFileName(path)}.");
            }
            catch (Exception ex)
            {
                RoseModLog.Error(ex, $"Failed to load BepInEx patcher assembly: {path}");
            }
        }

        public void Execute()
        {
            foreach (var patcher in Patchers)
                InvokeLifecycle(patcher, "Initialize");

            var patched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in patchDefinitions)
            {
                var targets = ResolveTargets(definition).ToArray();
                if (targets.Length == 0)
                {
                    RoseModLog.Warning($"Patch target not found for {definition.DisplayName}; skipping.");
                    continue;
                }

                foreach (var target in targets)
                {
                    if (RunPatch(definition, target))
                        patched.Add(target.FileName);
                }
            }

            DumpPatchedAssemblies(patched);

            foreach (var patcher in Patchers)
                InvokeLifecycle(patcher, "Finalizer");

            RoseModLog.Info($"BepInEx patcher scan complete. Loaded {Patchers.Count} patcher(s), applied {patched.Count} assembly patch target(s).");
        }

        public void Dispose()
        {
            foreach (var assembly in availableAssemblies.Values)
                assembly.Dispose();
            availableAssemblies.Clear();
            availableAssemblyPaths.Clear();
        }

        private static IEnumerable<string> GetAssemblySearchDirectories(RoseModPaths paths)
        {
            yield return paths.Patchers;
            yield return paths.Core;
            yield return paths.UserLibs;
            yield return paths.GameManaged;
            yield return paths.Interop;
            yield return paths.Il2CppAssemblies;
            yield return paths.Dotnet;
        }

        private static IEnumerable<string> GetTargetAssemblyDirectories(RoseModPaths paths)
        {
            yield return paths.GameManaged;
            yield return paths.Interop;
            yield return paths.Il2CppAssemblies;
        }

        private void EnsureFacadeContext(object patcher)
        {
            var contextProperty = patcher.GetType().GetProperty("Context", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (contextProperty is null)
                return;

            facadeContext ??= Activator.CreateInstance(contextProperty.PropertyType);
            if (facadeContext is null)
                return;

            SetDictionary(facadeContext, "AvailableAssemblies", availableAssemblies);
            SetDictionary(facadeContext, "AvailableAssembliesPaths", availableAssemblyPaths);
            SetDictionary(facadeContext, "LoadedAssemblies", loadedAssemblies);
            SetList(facadeContext, "PatcherPlugins", Patchers.Where(patcherInstance => patcherInstance.Instance is not null).Select(patcherInstance => patcherInstance.Instance!));
            SetPropertyIfExists(facadeContext, "DumpedAssembliesPath", Path.Combine(paths.Root, "PatchedAssemblies"));
            contextProperty.SetValue(patcher, facadeContext);
        }

        private void AddPatchDefinitions(PatcherInstance patcher)
        {
            var methods = patcher.Type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.DeclaringType != typeof(object));

            foreach (var method in methods)
            {
                var targetAssemblies = method.GetCustomAttributes(false)
                    .Where(attribute => attribute.GetType().FullName == TargetAssemblyAttributeName)
                    .Select(attribute => ReadTargetAssembly(attribute))
                    .Where(target => !string.IsNullOrWhiteSpace(target))
                    .ToArray();
                var targetTypes = method.GetCustomAttributes(false)
                    .Where(attribute => attribute.GetType().FullName == TargetTypeAttributeName)
                    .Select(ReadTargetType)
                    .Where(target => target is not null)
                    .Cast<TargetTypeSpec>()
                    .ToArray();

                foreach (var targetAssembly in targetAssemblies)
                    patchDefinitions.Add(PatchDefinition.ForAssembly(patcher, method, targetAssembly!));

                foreach (var targetType in targetTypes)
                    patchDefinitions.Add(PatchDefinition.ForType(patcher, method, targetType));
            }
        }

        private void AddLegacyPatchDefinitions(PatcherInstance patcher)
        {
            var patchMethod = patcher.Type.GetMethod("Patch", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(AssemblyDefinition) }, null);
            if (patchMethod is null)
                return;

            foreach (var targetAssembly in ReadLegacyTargetAssemblies(patcher.Type))
                patchDefinitions.Add(PatchDefinition.ForAssembly(patcher, patchMethod, targetAssembly));
        }

        private IEnumerable<PatchTarget> ResolveTargets(PatchDefinition definition)
        {
            if (definition.TargetAssembly is not null)
            {
                if (definition.TargetAssembly.Equals(AllAssemblies, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var pair in availableAssemblies)
                        yield return new PatchTarget(pair.Key, pair.Value, null);
                    yield break;
                }

                if (availableAssemblies.TryGetValue(definition.TargetAssembly, out var assembly))
                    yield return new PatchTarget(definition.TargetAssembly, assembly, null);
                yield break;
            }

            if (definition.TargetType is null)
                yield break;

            if (!availableAssemblies.TryGetValue(definition.TargetType.Assembly, out var targetAssembly))
                yield break;

            var targetType = FindType(targetAssembly.MainModule.Types, definition.TargetType.TypeName);
            if (targetType is not null)
                yield return new PatchTarget(definition.TargetType.Assembly, targetAssembly, targetType);
        }

        private bool RunPatch(PatchDefinition definition, PatchTarget target)
        {
            try
            {
                var args = BuildArguments(definition, target);
                if (args is null)
                {
                    RoseModLog.Warning($"Patch method has unsupported signature: {definition.DisplayName}");
                    return false;
                }

                var result = definition.Method.Invoke(definition.Patcher.Instance, args);
                var applied = definition.Method.ReturnType != typeof(bool) || result is true;
                if (!applied)
                    return false;

                if (args.Length > 0
                    && definition.Method.GetParameters()[0].ParameterType.IsByRef
                    && args[0] is AssemblyDefinition updatedAssembly)
                {
                    availableAssemblies[target.FileName] = updatedAssembly;
                }

                RoseModLog.Info($"Applied BepInEx patcher {definition.DisplayName}.");
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                RoseModLog.Error(ex.InnerException, $"BepInEx patcher failed: {definition.DisplayName}");
                return false;
            }
            catch (Exception ex)
            {
                RoseModLog.Error(ex, $"BepInEx patcher failed: {definition.DisplayName}");
                return false;
            }
        }

        private object[]? BuildArguments(PatchDefinition definition, PatchTarget target)
        {
            var parameters = definition.Method.GetParameters();
            if (parameters.Length == 0)
                return Array.Empty<object>();

            if (parameters.Length > 2)
                return null;

            var arguments = new object?[parameters.Length];
            var firstType = parameters[0].ParameterType.IsByRef
                ? parameters[0].ParameterType.GetElementType()
                : parameters[0].ParameterType;

            if (firstType == typeof(AssemblyDefinition))
            {
                arguments[0] = target.Assembly;
            }
            else if (firstType == typeof(TypeDefinition))
            {
                if (target.Type is null)
                    return null;
                arguments[0] = target.Type;
            }
            else if (firstType == typeof(Assembly))
            {
                var assembly = FindLoadedAssembly(Path.GetFileNameWithoutExtension(target.FileName));
                if (assembly is null)
                {
                    RoseModLog.Warning($"{definition.DisplayName} requested a runtime Assembly for {target.FileName}, but it is not loaded yet.");
                    return null;
                }

                arguments[0] = assembly;
            }
            else if (firstType == typeof(Type))
            {
                if (definition.TargetType is null)
                    return null;

                var runtimeType = FindRuntimeType(definition.TargetType.TypeName);
                if (runtimeType is null)
                {
                    RoseModLog.Warning($"{definition.DisplayName} requested runtime type {definition.TargetType.TypeName}, but it is not loaded yet.");
                    return null;
                }

                arguments[0] = runtimeType;
            }
            else
            {
                return null;
            }

            if (parameters.Length == 2)
            {
                if (parameters[1].ParameterType != typeof(string))
                    return null;
                arguments[1] = target.FileName;
            }

            return arguments!;
        }

        private void DumpPatchedAssemblies(HashSet<string> patched)
        {
            if (patched.Count == 0)
                return;

            var outputDirectory = Path.Combine(paths.Root, "PatchedAssemblies");
            Directory.CreateDirectory(outputDirectory);

            foreach (var fileName in patched.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (!availableAssemblies.TryGetValue(fileName, out var assembly))
                    continue;

                var destination = Path.Combine(outputDirectory, fileName);
                try
                {
                    assembly.Write(destination);
                    var assemblyName = Path.GetFileNameWithoutExtension(fileName);
                    if (FindLoadedAssembly(assemblyName) is not null)
                    {
                        RoseModLog.Warning($"Patched {fileName} was written to RoseMod/PatchedAssemblies, but {assemblyName} is already loaded. A true preloader is required for this patch to replace the live assembly.");
                        continue;
                    }

                    var bytes = File.ReadAllBytes(destination);
                    var loaded = Assembly.Load(bytes);
                    loadedAssemblies[fileName] = loaded;
                    SetDictionary(facadeContext, "LoadedAssemblies", loadedAssemblies);
                    RoseModLog.Info($"Loaded patched assembly into memory: {fileName}");
                }
                catch (Exception ex)
                {
                    RoseModLog.Warning($"Failed to write or load patched assembly {fileName}: {ex.Message}");
                }
            }
        }

        private static void InvokeLifecycle(PatcherInstance patcher, string methodName)
        {
            try
            {
                var flags = patcher.IsLegacyStatic
                    ? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                    : BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var method = patcher.Type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                if (method is null)
                    return;

                if (!patcher.IsLegacyStatic
                    && method.DeclaringType == method.GetBaseDefinition().DeclaringType
                    && method.DeclaringType?.FullName == BasePatcherName)
                    return;

                method.Invoke(patcher.Instance, Array.Empty<object>());
                RoseModLog.Info($"Ran BepInEx patcher {methodName}: {patcher.DisplayName}");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                RoseModLog.Error(ex.InnerException, $"BepInEx patcher {methodName} failed: {patcher.DisplayName}");
            }
            catch (Exception ex)
            {
                RoseModLog.Error(ex, $"BepInEx patcher {methodName} failed: {patcher.DisplayName}");
            }
        }

        private static bool IsPatcherType(Type type)
        {
            if (type.IsAbstract)
                return false;

            if (!type.GetCustomAttributes(false).Any(attribute => attribute.GetType().FullName == PatcherInfoAttributeName))
                return false;

            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.FullName == BasePatcherName)
                    return true;
            }

            return false;
        }

        private static bool IsLegacyStaticPatcherType(Type type)
        {
            if (!type.IsClass || !type.IsAbstract || !type.IsSealed)
                return false;

            var targetDlls = type.GetProperty("TargetDLLs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var initialize = type.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var patch = type.GetMethod("Patch", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(AssemblyDefinition) }, null);

            return targetDlls is not null && (initialize is not null || patch is not null);
        }

        private static PatcherMetadata ReadMetadata(Type type)
        {
            var attribute = type.GetCustomAttributes(false)
                .FirstOrDefault(candidate => candidate.GetType().FullName == PatcherInfoAttributeName);
            if (attribute is null)
                return new PatcherMetadata(type.FullName ?? type.Name, type.Name, "0.0.0");

            return new PatcherMetadata(
                attribute.GetType().GetProperty("GUID")?.GetValue(attribute)?.ToString() ?? type.FullName ?? type.Name,
                attribute.GetType().GetProperty("Name")?.GetValue(attribute)?.ToString() ?? type.Name,
                attribute.GetType().GetProperty("Version")?.GetValue(attribute)?.ToString() ?? "0.0.0");
        }

        private static string? ReadTargetAssembly(object attribute)
        {
            return attribute.GetType().GetProperty("TargetAssembly")?.GetValue(attribute)?.ToString();
        }

        private static TargetTypeSpec? ReadTargetType(object attribute)
        {
            var targetAssembly = attribute.GetType().GetProperty("TargetAssembly")?.GetValue(attribute)?.ToString();
            var targetType = attribute.GetType().GetProperty("TargetType")?.GetValue(attribute)?.ToString();
            return string.IsNullOrWhiteSpace(targetAssembly) || string.IsNullOrWhiteSpace(targetType)
                ? null
                : new TargetTypeSpec(targetAssembly!, targetType!);
        }

        private static IEnumerable<string> ReadLegacyTargetAssemblies(Type type)
        {
            var property = type.GetProperty("TargetDLLs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is null)
                yield break;

            object? value;
            try
            {
                value = property.GetValue(null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                RoseModLog.Warning($"Failed to read TargetDLLs from BepInEx 5 patcher {type.FullName}: {ex.InnerException.Message}");
                yield break;
            }
            catch (Exception ex)
            {
                RoseModLog.Warning($"Failed to read TargetDLLs from BepInEx 5 patcher {type.FullName}: {ex.Message}");
                yield break;
            }

            if (value is not IEnumerable enumerable)
                yield break;

            foreach (var item in enumerable)
            {
                var target = item?.ToString();
                if (!string.IsNullOrWhiteSpace(target))
                    yield return target!;
            }
        }

        private static TypeDefinition? FindType(IEnumerable<TypeDefinition> types, string fullName)
        {
            foreach (var type in types)
            {
                if (type.FullName == fullName)
                    return type;

                var nested = FindType(type.NestedTypes, fullName);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static Assembly? FindLoadedAssembly(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                        return assembly;
                }
                catch
                {
                }
            }

            return null;
        }

        private static Type? FindRuntimeType(string fullName)
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

            return null;
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (var loaderException in ex.LoaderExceptions.Where(loaderException => loaderException is not null).Take(5))
                    RoseModLog.Warning($"Patcher type load issue in {assembly.GetName().Name}: {loaderException!.Message}");
                return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }
        }

        private static void SetDictionary<TValue>(object? context, string propertyName, Dictionary<string, TValue> values)
        {
            if (context is null)
                return;

            var property = context.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(context) is not IDictionary target)
                return;

            target.Clear();
            foreach (var pair in values)
                target[pair.Key] = pair.Value!;
        }

        private static void SetList(object? context, string propertyName, IEnumerable<object> values)
        {
            if (context is null)
                return;

            var property = context.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(context) is not IList target)
                return;

            target.Clear();
            foreach (var value in values)
                target.Add(value);
        }

        private static void SetPropertyIfExists(object? target, string propertyName, object value)
        {
            if (target is null)
                return;

            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.CanWrite == true)
                property.SetValue(target, value);
        }
    }

    private sealed class PatcherInstance
    {
        private PatcherInstance(Type type, object? instance, string name, string guid, bool isLegacyStatic)
        {
            Type = type;
            Instance = instance;
            Name = name;
            Guid = guid;
            IsLegacyStatic = isLegacyStatic;
        }

        public PatcherInstance(Type type, object instance, string name, string guid)
            : this(type, instance, name, guid, false)
        {
        }

        public Type Type { get; }
        public object? Instance { get; }
        public string Name { get; }
        public string Guid { get; }
        public bool IsLegacyStatic { get; }
        public string DisplayName => $"{Name} ({Guid})";

        public static PatcherInstance LegacyStatic(Type type)
        {
            return new PatcherInstance(type, null, type.Name, type.FullName ?? type.Name, true);
        }
    }

    private sealed class PatchDefinition
    {
        private PatchDefinition(PatcherInstance patcher, MethodInfo method, string? targetAssembly, TargetTypeSpec? targetType)
        {
            Patcher = patcher;
            Method = method;
            TargetAssembly = targetAssembly;
            TargetType = targetType;
        }

        public PatcherInstance Patcher { get; }
        public MethodInfo Method { get; }
        public string? TargetAssembly { get; }
        public TargetTypeSpec? TargetType { get; }
        public string DisplayName => TargetType is null
            ? $"{Patcher.DisplayName}/{Method.Name} -> {TargetAssembly}"
            : $"{Patcher.DisplayName}/{Method.Name} -> {TargetType.Assembly}/{TargetType.TypeName}";

        public static PatchDefinition ForAssembly(PatcherInstance patcher, MethodInfo method, string targetAssembly) =>
            new(patcher, method, targetAssembly, null);

        public static PatchDefinition ForType(PatcherInstance patcher, MethodInfo method, TargetTypeSpec targetType) =>
            new(patcher, method, null, targetType);
    }

    private sealed class PatchTarget
    {
        public PatchTarget(string fileName, AssemblyDefinition assembly, TypeDefinition? type)
        {
            FileName = fileName;
            Assembly = assembly;
            Type = type;
        }

        public string FileName { get; }
        public AssemblyDefinition Assembly { get; }
        public TypeDefinition? Type { get; }
    }

    private sealed class TargetTypeSpec
    {
        public TargetTypeSpec(string assembly, string typeName)
        {
            Assembly = assembly;
            TypeName = typeName;
        }

        public string Assembly { get; }
        public string TypeName { get; }
    }

    private sealed class PatcherMetadata
    {
        public PatcherMetadata(string guid, string name, string version)
        {
            Guid = guid;
            Name = name;
            Version = version;
        }

        public string Guid { get; }
        public string Name { get; }
        public string Version { get; }
    }
}
