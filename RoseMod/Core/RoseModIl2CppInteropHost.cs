using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace RoseMod;

internal static class RoseModIl2CppInteropHost
{
    private static bool initialized;
    private static string? gameAssemblyPath;
    private static IntPtr gameAssemblyHandle;

    public static void Initialize(RoseModPaths paths, RoseModStartupOptions options)
    {
        if (initialized || !options.Backend.Equals("IL2CPP", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var runtimeType = Type.GetType("Il2CppInterop.Runtime.Startup.Il2CppInteropRuntime, Il2CppInterop.Runtime", throwOnError: false);
            var configurationType = Type.GetType("Il2CppInterop.Runtime.Startup.RuntimeConfiguration, Il2CppInterop.Runtime", throwOnError: false);
            var providerInterface = Type.GetType("Il2CppInterop.Runtime.Injection.IDetourProvider, Il2CppInterop.Runtime", throwOnError: false);
            var detourInterface = Type.GetType("Il2CppInterop.Runtime.Injection.IDetour, Il2CppInterop.Runtime", throwOnError: false);
            if (runtimeType is null || configurationType is null || providerInterface is null || detourInterface is null)
            {
                RoseModLog.Warning("Il2CppInterop runtime assemblies were not found; IL2CPP class injection is disabled.");
                return;
            }

            if (IsRuntimeCreated(runtimeType))
            {
                initialized = true;
                RoseModLog.Info("Il2CppInterop runtime is already initialized.");
                return;
            }

            gameAssemblyPath = Path.Combine(paths.GameRoot, "GameAssembly.dll");
            Environment.SetEnvironmentVariable("IL2CPP_INTEROP_DATABASES_LOCATION", GetInteropDatabasePath(paths));
            TryInstallGameAssemblyResolver();

            var unityVersion = DetectUnityVersion(paths, options);
            var configuration = Activator.CreateInstance(configurationType)
                ?? throw new InvalidOperationException("Could not create Il2CppInterop runtime configuration.");
            configurationType.GetProperty("UnityVersion")?.SetValue(configuration, unityVersion);
            configurationType.GetProperty("DetourProvider")?.SetValue(configuration, CreateDetourProvider(providerInterface, detourInterface));

            var host = runtimeType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new[] { configuration })
                ?? throw new MissingMethodException(runtimeType.FullName, "Create");
            TryAddHarmonySupport(host);
            host.GetType().GetMethod("Start", BindingFlags.Public | BindingFlags.Instance)?.Invoke(host, null);

            initialized = true;
            RoseModLog.Info($"Initialized Il2CppInterop runtime for Unity {unityVersion}.");
        }
        catch (Exception ex)
        {
            RoseModLog.Error(Unwrap(ex), "Failed to initialize Il2CppInterop runtime.");
        }
    }

    private static bool IsRuntimeCreated(Type runtimeType)
    {
        try
        {
            _ = runtimeType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object CreateDetourProvider(Type providerInterface, Type detourInterface)
    {
        var create = typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create), BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(DispatchProxy).FullName, nameof(DispatchProxy.Create));
        var proxy = create.MakeGenericMethod(providerInterface, typeof(RoseModDetourProviderProxy)).Invoke(null, null)
            ?? throw new InvalidOperationException("Could not create Il2CppInterop detour provider proxy.");
        ((RoseModDetourProviderProxy)proxy).DetourInterface = detourInterface;
        return proxy;
    }

    private static string GetInteropDatabasePath(RoseModPaths paths)
    {
        var candidates = new[]
        {
            paths.Interop,
            paths.Il2CppAssemblies
        };

        var existing = candidates.FirstOrDefault(Directory.Exists);
        if (existing is not null)
            return existing;

        Directory.CreateDirectory(paths.Interop);
        return paths.Interop;
    }

    private static void TryAddHarmonySupport(object host)
    {
        try
        {
            var harmonySupport = Type.GetType("Il2CppInterop.HarmonySupport.HarmonySupport, Il2CppInterop.HarmonySupport", throwOnError: false);
            var method = harmonySupport?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name == "AddHarmonySupport" && candidate.IsGenericMethodDefinition);
            method?.MakeGenericMethod(host.GetType()).Invoke(null, new[] { host });
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"Il2CppInterop Harmony support was not enabled: {Unwrap(ex).Message}");
        }
    }

    private static void TryInstallGameAssemblyResolver()
    {
        try
        {
            var nativeLibrary = Type.GetType("System.Runtime.InteropServices.NativeLibrary", throwOnError: false);
            var resolverType = Type.GetType("System.Runtime.InteropServices.DllImportResolver", throwOnError: false);
            var il2cppType = Type.GetType("Il2CppInterop.Runtime.IL2CPP, Il2CppInterop.Runtime", throwOnError: false);
            if (nativeLibrary is null || resolverType is null || il2cppType is null)
                return;

            var resolverMethod = typeof(RoseModIl2CppInteropHost).GetMethod(nameof(ResolveDllImport), BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(typeof(RoseModIl2CppInteropHost).FullName, nameof(ResolveDllImport));
            var resolver = Delegate.CreateDelegate(resolverType, resolverMethod);
            var setResolver = nativeLibrary.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "SetDllImportResolver" && method.GetParameters().Length == 2);
            setResolver?.Invoke(null, new object[] { il2cppType.Assembly, resolver });
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"GameAssembly import resolver was not installed: {Unwrap(ex).Message}");
        }
    }

    private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals("GameAssembly", StringComparison.OrdinalIgnoreCase)
            && !libraryName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        if (gameAssemblyHandle != IntPtr.Zero)
            return gameAssemblyHandle;

        if (string.IsNullOrWhiteSpace(gameAssemblyPath) || !File.Exists(gameAssemblyPath))
            return IntPtr.Zero;

        gameAssemblyHandle = LoadLibrary(gameAssemblyPath!);
        return gameAssemblyHandle;
    }

    private static Version DetectUnityVersion(RoseModPaths paths, RoseModStartupOptions options)
    {
        if (TryParseUnityVersion(options.UnityVersion, out var version))
            return version;

        foreach (var candidate in new[]
        {
            (Path.Combine(paths.GameData, "globalgamemanagers"), new[] { 20, 48 }),
            (Path.Combine(paths.GameData, "data.unity3d"), new[] { 18 }),
            (Path.Combine(paths.GameData, "mainData"), new[] { 20 })
        })
        {
            foreach (var offset in candidate.Item2)
            {
                if (TryReadUnityVersion(candidate.Item1, offset, out version))
                    return version;
            }
        }

        var executable = FindGameExecutable(paths);
        if (executable is not null && TryParseUnityVersion(FileVersionInfo.GetVersionInfo(executable).FileVersion, out version))
            return version;

        RoseModLog.Warning("Unity version could not be detected; defaulting Il2CppInterop to Unity 2019.4.0.");
        return new Version(2019, 4, 0);
    }

    private static string? FindGameExecutable(RoseModPaths paths)
    {
        return Directory.EnumerateFiles(paths.GameRoot, "*.exe", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Directory.Exists(Path.Combine(paths.GameRoot, Path.GetFileNameWithoutExtension(path) + "_Data")));
    }

    private static bool TryReadUnityVersion(string path, int offset, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length <= offset)
                return false;

            stream.Position = offset;
            var bytes = new List<byte>();
            for (var next = stream.ReadByte(); next > 0 && bytes.Count < 64; next = stream.ReadByte())
                bytes.Add((byte)next);

            return TryParseUnityVersion(System.Text.Encoding.ASCII.GetString(bytes.ToArray()), out version);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseUnityVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = Regex.Match(value, @"(?<major>\d+)\.(?<minor>\d+)\.(?<build>\d+)");
        if (!match.Success)
            return false;

        version = new Version(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["build"].Value));
        return true;
    }

    private static Exception Unwrap(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}

internal class RoseModDetourProviderProxy : System.Reflection.DispatchProxy
{
    public Type? DetourInterface { get; set; }

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        if (targetMethod.Name == "Create" && args is { Length: 2 } && args[1] is Delegate detourTarget)
            return RoseModDetourProxy.Create(DetourInterface ?? throw new InvalidOperationException("Detour interface was not configured."), (IntPtr)args[0]!, detourTarget);

        throw new NotSupportedException("Unsupported Il2CppInterop detour provider call: " + targetMethod.Name);
    }
}

internal class RoseModDetourProxy : System.Reflection.DispatchProxy
{
    private static readonly object ActiveDetoursGate = new();
    private static readonly List<object> ActiveDetours = new();
    private readonly List<Delegate> cachedDelegates = new();
    private RoseModNativeDetour? nativeDetour;
    private Type? targetDelegateType;
    private string targetDescription = "unknown";
    private IntPtr original;
    private IntPtr detour;
    private IntPtr trampoline;
    private bool skipApply;

    public static object Create(Type detourInterface, IntPtr original, Delegate target)
    {
        var create = typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create), BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(DispatchProxy).FullName, nameof(DispatchProxy.Create));
        var proxy = create.MakeGenericMethod(detourInterface, typeof(RoseModDetourProxy)).Invoke(null, null)
            ?? throw new InvalidOperationException("Could not create Il2CppInterop detour proxy.");
        ((RoseModDetourProxy)proxy).Initialize(original, target);
        lock (ActiveDetoursGate)
            ActiveDetours.Add(proxy);
        return proxy;
    }

    private void Initialize(IntPtr originalPointer, Delegate target)
    {
        var targetPointer = Marshal.GetFunctionPointerForDelegate(target);
        nativeDetour = new RoseModNativeDetour(originalPointer, targetPointer);
        targetDelegateType = target.GetType();
        targetDescription = target.Method.DeclaringType is null
            ? target.Method.Name
            : $"{target.Method.DeclaringType.FullName}.{target.Method.Name}";
        skipApply = ShouldSkipDetour(targetDescription);
        original = originalPointer;
        detour = targetPointer;
        cachedDelegates.Add(target);
        RoseModLog.Info($"Created Il2CppInterop detour for {targetDescription} at 0x{original.ToInt64():X}.");
    }

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        if (nativeDetour is null)
            throw new ObjectDisposedException(nameof(RoseModDetourProxy));

        switch (targetMethod.Name)
        {
            case "get_Target":
                return original;
            case "get_Detour":
                return detour;
            case "get_OriginalTrampoline":
                return trampoline;
            case "Apply":
                if (skipApply)
                {
                    RoseModLog.Info($"Disabled Il2CppInterop detour for {targetDescription} for Unity 6 stability.");
                    return null!;
                }

                RoseModLog.Info($"Applying Il2CppInterop detour for {targetDescription}.");
                nativeDetour.Apply();
                return null!;
            case "Dispose":
                nativeDetour.Dispose();
                cachedDelegates.Clear();
                nativeDetour = null;
                lock (ActiveDetoursGate)
                    ActiveDetours.Remove(this);
                return null!;
            case "GenerateTrampoline":
                var genericArgument = targetMethod.GetGenericArguments().SingleOrDefault();
                if (genericArgument is null || genericArgument.ContainsGenericParameters)
                    genericArgument = targetDelegateType;
                if (genericArgument is null)
                    throw new InvalidOperationException("Could not determine Il2CppInterop trampoline delegate type.");

                var generated = nativeDetour.GenerateTrampoline(genericArgument);
                cachedDelegates.Add(generated);
                trampoline = nativeDetour.Trampoline;
                RoseModLog.Info($"Generated Il2CppInterop trampoline for {targetDescription} at 0x{trampoline.ToInt64():X}.");
                return generated;
            default:
                throw new NotSupportedException("Unsupported Il2CppInterop detour call: " + targetMethod.Name);
        }
    }

    private static bool ShouldSkipDetour(string description)
    {
        return description.IndexOf("Class_FromName_Hook.Hook", StringComparison.Ordinal) >= 0
            || description.IndexOf("GarbageCollector_RunFinalizer_Patch.Hook", StringComparison.Ordinal) >= 0;
    }
}
