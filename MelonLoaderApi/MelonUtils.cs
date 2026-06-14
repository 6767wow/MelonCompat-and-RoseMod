using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace MelonLoader;

internal static class MelonEnvironment
{
    public static void Initialize(string pluginsPath, string gameDataPath, string unityVersion)
    {
        MelonHandler.PluginsDirectory = pluginsPath;
        MelonHandler.ModsDirectory = pluginsPath;
        MelonUtils.BaseDirectory = Directory.GetParent(pluginsPath)?.FullName ?? Environment.CurrentDirectory;
        MelonUtils.GameDirectory = Directory.GetParent(gameDataPath)?.FullName ?? Environment.CurrentDirectory;
        MelonUtils.UserDataDirectory = Path.Combine(MelonUtils.BaseDirectory, "UserData");
        MelonUtils.UserLibsDirectory = Path.Combine(MelonUtils.BaseDirectory, "UserLibs");
        MelonUtils.MelonLoaderDirectory = Path.Combine(MelonUtils.BaseDirectory, "MelonLoader");
        MelonUtils.UnityVersion = unityVersion;

        Directory.CreateDirectory(MelonUtils.UserDataDirectory);
        Directory.CreateDirectory(MelonUtils.UserLibsDirectory);
    }
}

public static class MelonUtils
{
    public static string BaseDirectory { get; internal set; } = Environment.CurrentDirectory;
    public static string GameDirectory { get; internal set; } = Environment.CurrentDirectory;
    public static string UserDataDirectory { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "UserData");
    public static string UserLibsDirectory { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "UserLibs");
    public static string MelonLoaderDirectory { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "MelonLoader");
    public static string UnityVersion { get; internal set; } = string.Empty;
    public static string GameName => Process.GetCurrentProcess().ProcessName;
    public static string GameDeveloper => string.Empty;
    public static string GameVersion => string.Empty;
    public static string HashCode => BuildInfo.Version;
    public static PlatformID GetPlatform => Environment.OSVersion.Platform;
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();
    public static bool IsUnix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    public static MelonGameAttribute CurrentGameAttribute => new(GameDeveloper, GameName);
    public static MelonPlatformAttribute.CompatiblePlatforms CurrentPlatform => IsWindows && Environment.Is64BitProcess
        ? MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X64
        : IsWindows
            ? MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X86
            : IsMac
                ? MelonPlatformAttribute.CompatiblePlatforms.MAC
                : MelonPlatformAttribute.CompatiblePlatforms.LINUX;
    public static MelonPlatformDomainAttribute.CompatibleDomains CurrentDomain =>
#if BEPINEX_MONO
        MelonPlatformDomainAttribute.CompatibleDomains.MONO;
#else
        MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP;
#endif

    public static string GetApplicationPath() => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    public static string GetGameDataDirectory() => Path.Combine(GameDirectory, $"{GameName}_Data");
    public static string GetManagedDirectory() => Path.Combine(GetGameDataDirectory(), "Managed");
    public static string GetUnityVersion() => UnityVersion;
    public static bool IsGameIl2Cpp() =>
#if BEPINEX_MONO
        false;
#else
        true;
#endif
    public static bool IsGame32Bit() => !Environment.Is64BitProcess;
    public static bool IsOldMono() => false;
    public static bool IsUnderWineOrSteamProton() => false;
    public static bool ContainsExtension(string path) => !string.IsNullOrWhiteSpace(Path.GetExtension(path));
    public static string GetPathAncestor(string path, int ancestor) => Enumerable.Range(0, ancestor).Aggregate(path, static (current, _) => Directory.GetParent(current)?.FullName ?? current);
    public static string GetFileProductName(string path) => FileVersionInfo.GetVersionInfo(path).ProductName ?? Path.GetFileNameWithoutExtension(path);
    public static string MakePlural(string text, int count) => count == 1 ? text : text + "s";
    public static T Clamp<T>(T value, T min, T max)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0)
            return min;
        return value.CompareTo(max) > 0 ? max : value;
    }

    public static string ComputeSimpleSHA256Hash(string path) => ComputeHash(path, SHA256.Create());

    public static string ComputeSimpleSHA512Hash(string path) => ComputeHash(path, SHA512.Create());

    public static string ToString(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
    public static string ToString(byte[] bytes, string format) => string.Join(string.Empty, bytes.Select(b => b.ToString(format)));
    public static string ToString(byte[] bytes, IFormatProvider provider) => string.Join(string.Empty, bytes.Select(b => b.ToString(provider)));
    public static string ToString(byte[] bytes, string format, IFormatProvider provider) => string.Join(string.Empty, bytes.Select(b => b.ToString(format, provider)));

    public static T? PullAttributeFromAssembly<T>(Assembly assembly, bool inherit = false)
        where T : Attribute
    {
        return assembly.GetCustomAttributes<T>().FirstOrDefault();
    }

    public static T[] PullAttributesFromAssembly<T>(Assembly assembly, bool inherit = false)
        where T : Attribute
    {
        return assembly.GetCustomAttributes<T>().ToArray();
    }

    public static IEnumerable<Type> GetValidTypes(Assembly assembly) => GetValidTypes(assembly, _ => true);

    public static IEnumerable<Type> GetValidTypes(Assembly assembly, LemonFunc<Type, bool> predicate)
    {
        try
        {
            return assembly.GetTypes().Where(predicate.Invoke).ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>().Where(predicate.Invoke).ToArray();
        }
    }

    public static Type? GetValidType(Assembly assembly, string typeName) => GetValidType(assembly, typeName, _ => true);

    public static Type? GetValidType(Assembly assembly, string typeName, LemonFunc<Type, bool> predicate)
    {
        return GetValidTypes(assembly, predicate).FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
    }

    public static bool IsTypeEqualToFullName(Type type, string fullName) => type.FullName == fullName;
    public static bool IsTypeEqualToName(Type type, string name) => type.Name == name;
    public static bool IsManagedDLL(string path) => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);
    public static bool IsNotImplemented(MethodBase method) => false;
    public static MelonBase? GetMelonFromStackTrace() => null;
    public static MelonBase? GetMelonFromStackTrace(StackTrace stackTrace, bool includeSelf = false) => null;
    public static void SetConsoleTitle(string title) => MelonConsole.SetTitle(title);
    public static void AddNativeDLLDirectory(string path) { }
    public static IntPtr GetFunctionPointer(Delegate del) => System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(del);
    public static Delegate GetDelegate(IntPtr pointer, Type type) => System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(pointer, type);
    public static T GetDelegate<T>(IntPtr pointer) where T : Delegate => System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(pointer);
    public static void GetDelegate<T>(IntPtr pointer, out T del) where T : Delegate => del = GetDelegate<T>(pointer);
    public static IntPtr GetNativeLibraryExport(IntPtr library, string name) => IntPtr.Zero;
    public static void NativeHookAttach(IntPtr target, IntPtr detour) { }
    public static void NativeHookDetach(IntPtr target, IntPtr detour) { }
    public static void TryPatchAll(Harmony harmony, Assembly assembly) => harmony.PatchAll(assembly);
    public static List<MethodInfo> TryPatchAll(Harmony harmony, Assembly assembly, bool log) { harmony.PatchAll(assembly); return new List<MethodInfo>(); }
    public static void TryPatchAll(Harmony harmony, Type type) => harmony.PatchAll(type);
    public static List<MethodInfo> TryPatchAll(Harmony harmony, Type type, bool log) { harmony.PatchAll(type); return new List<MethodInfo>(); }
    public static HarmonyMethod ToNewHarmonyMethod(MethodInfo method) => new(method);

    private static string ComputeHash(string path, HashAlgorithm algorithm)
    {
        using (algorithm)
        {
            if (!File.Exists(path))
                return string.Empty;

            using var stream = File.OpenRead(path);
            return ToString(algorithm.ComputeHash(stream));
        }
    }
}

public static class MelonCoroutines
{
    public static object? Start(System.Collections.IEnumerator routine) => BepInExCompat.CompatUnityDriver.StartManagedCoroutine(routine);

    public static void Stop(object coroutine) => BepInExCompat.CompatUnityDriver.StopManagedCoroutine(coroutine);
}

public sealed class MelonConsole
{
    public static void SetTitle(string title)
    {
        try
        {
            Console.Title = title;
        }
        catch
        {
        }
    }
}

public sealed class MelonCompatibilityLayer
{
}
