using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
namespace MelonLoader;

public static class EnumExtensions
{
    public static bool HasFlagFast<T>(this T value, T flag)
        where T : Enum
    {
        return value.HasFlag(flag);
    }
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class HarmonyDontPatchAllAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class PatchShield : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class RegisterTypeInIl2Cpp : Attribute
{
    public RegisterTypeInIl2Cpp()
    {
    }

    public RegisterTypeInIl2Cpp(bool logSuccess)
    {
        LogSuccess = logSuccess;
    }

    public bool LogSuccess { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RegisterTypeInIl2CppWithInterfaces : RegisterTypeInIl2Cpp
{
    public RegisterTypeInIl2CppWithInterfaces(params Type[] interfaces)
    {
        Interfaces = interfaces ?? Array.Empty<Type>();
    }

    public Type[] Interfaces { get; }
}

public interface ISupportModule_From
{
}

public interface ISupportModule_To
{
}

public static class Imports
{
    public static string GetCompanyName() => MelonUtils.GameDeveloper;
    public static string GetProductName() => MelonUtils.GameName;
    public static string GetGameDirectory() => MelonUtils.GameDirectory;
    public static string GetGameDataDirectory() => MelonUtils.GetGameDataDirectory();
    public static string GetAssemblyDirectory() => MelonUtils.GetManagedDirectory();
    public static bool IsIl2CppGame() => MelonUtils.IsGameIl2Cpp();
    public static bool IsDebugMode() => MelonDebug.IsEnabled();
    public static void Hook(IntPtr target, IntPtr detour) => MelonUtils.NativeHookAttach(target, detour);
    public static void Unhook(IntPtr target, IntPtr detour) => MelonUtils.NativeHookDetach(target, detour);
}

public sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> values = new(StringComparer.OrdinalIgnoreCase);

    public IniFile(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string ReadValue(string section, string key, string defaultValue = "")
    {
        return values.TryGetValue(section, out var sectionValues) && sectionValues.TryGetValue(key, out var value)
            ? value
            : defaultValue;
    }

    public void WriteValue(string section, string key, string value)
    {
        if (!values.TryGetValue(section, out var sectionValues))
            values[section] = sectionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sectionValues[key] = value;
    }
}

public static class InteropSupport
{
    public interface Interface
    {
    }
}

public readonly struct LemonArraySegment<T> : IEnumerable<T>
{
    public LemonArraySegment(T[] array)
        : this(array, 0, array?.Length ?? 0)
    {
    }

    public LemonArraySegment(T[] array, int offset, int count)
    {
        Array = array ?? System.Array.Empty<T>();
        Offset = offset;
        Count = count;
    }

    public T[] Array { get; }
    public int Offset { get; }
    public int Count { get; }
    public T this[int index] => Array[Offset + index];

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return Array[Offset + i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class LemonEnumerator<T> : IEnumerator<T>
{
    private readonly IList<T> values;
    private int index = -1;

    public LemonEnumerator(IEnumerable<T> values)
    {
        this.values = values?.ToArray() ?? Array.Empty<T>();
    }

    public T Current => values[index];
    object IEnumerator.Current => Current!;
    public bool MoveNext() => ++index < values.Count;
    public void Reset() => index = -1;
    public void Dispose() { }
}

public class LemonTuple<T1> { public LemonTuple(T1 item1) { Item1 = item1; } public T1 Item1 { get; set; } }
public class LemonTuple<T1, T2> : LemonTuple<T1> { public LemonTuple(T1 item1, T2 item2) : base(item1) { Item2 = item2; } public T2 Item2 { get; set; } }
public class LemonTuple<T1, T2, T3> : LemonTuple<T1, T2> { public LemonTuple(T1 item1, T2 item2, T3 item3) : base(item1, item2) { Item3 = item3; } public T3 Item3 { get; set; } }
public class LemonTuple<T1, T2, T3, T4> : LemonTuple<T1, T2, T3> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4) : base(item1, item2, item3) { Item4 = item4; } public T4 Item4 { get; set; } }
public class LemonTuple<T1, T2, T3, T4, T5> : LemonTuple<T1, T2, T3, T4> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) : base(item1, item2, item3, item4) { Item5 = item5; } public T5 Item5 { get; set; } }
public class LemonTuple<T1, T2, T3, T4, T5, T6> : LemonTuple<T1, T2, T3, T4, T5> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) : base(item1, item2, item3, item4, item5) { Item6 = item6; } public T6 Item6 { get; set; } }
public class LemonTuple<T1, T2, T3, T4, T5, T6, T7> : LemonTuple<T1, T2, T3, T4, T5, T6> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) : base(item1, item2, item3, item4, item5, item6) { Item7 = item7; } public T7 Item7 { get; set; } }
public class LemonTuple<T1, T2, T3, T4, T5, T6, T7, T8> : LemonTuple<T1, T2, T3, T4, T5, T6, T7> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8) : base(item1, item2, item3, item4, item5, item6, item7) { Item8 = item8; } public T8 Item8 { get; set; } }
public class LemonTuple<T1, T2, T3, T4, T5, T6, T7, T8, T9> : LemonTuple<T1, T2, T3, T4, T5, T6, T7, T8> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8, T9 item9) : base(item1, item2, item3, item4, item5, item6, item7, item8) { Item9 = item9; } public T9 Item9 { get; set; } }
public class LemonTuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : LemonTuple<T1, T2, T3, T4, T5, T6, T7, T8, T9> { public LemonTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8, T9 item9, T10 item10) : base(item1, item2, item3, item4, item5, item6, item7, item8, item9) { Item10 = item10; } public T10 Item10 { get; set; } }

public static class LoaderConfig
{
    public static CoreConfig Current { get; } = new();
    public sealed class CoreConfig { public LoaderTheme Theme { get; set; } = LoaderTheme.Normal; public HarmonyLogVerbosity HarmonyLogLevel { get; set; } = HarmonyLogVerbosity.Warning; public enum HarmonyLogVerbosity { None, Error, Warning, Info, Debug, All } public enum LoaderTheme { Normal, Lemon, Lime, Random } }
    public sealed class ConsoleConfig { public bool Enabled { get; set; } = true; }
    public sealed class LogsConfig { public bool Enabled { get; set; } = true; }
    public sealed class MonoDebugServerConfig { public bool Enabled { get; set; } }
    public sealed class UnityEngineConfig { public bool DebugLog { get; set; } = true; }
}

public static class MelonLaunchOptions
{
    public static Core CoreSettings { get; } = new();
    public sealed class Core { public LoadModeEnum LoadMode { get; set; } = LoadModeEnum.NORMAL; public enum LoadModeEnum { NORMAL, DEV, BOTH } }
    public sealed class Console { public DisplayMode Mode { get; set; } = DisplayMode.NORMAL; public enum DisplayMode { NORMAL, HIDE, DISABLE } }
    public sealed class Cpp2IL { public bool Force { get; set; } }
    public sealed class Il2CppAssemblyGenerator { public bool Force { get; set; } }
    public sealed class Logger { public bool Debug { get; set; } }
}

public static class Main
{
    public static bool IsPastInit => true;
    public static bool IsPastStart => true;
    public static string GetGameDirectory() => MelonUtils.GameDirectory;
    public static string GetGameDataDirectory() => MelonUtils.GetGameDataDirectory();
    public static string GetUserDataPath() => MelonUtils.UserDataDirectory;
    public static string GetUnityVersion() => MelonUtils.GetUnityVersion();
}

public static class MelonLoaderBase
{
    public static string UnityVersion => MelonUtils.GetUnityVersion();
}

public class NativeLibrary : IDisposable
{
    public delegate IntPtr StringDelegate();

    public NativeLibrary(IntPtr handle)
    {
        Handle = handle;
    }

    public NativeLibrary(string path)
    {
        Path = path;
    }

    public IntPtr Handle { get; protected set; }
    public string? Path { get; }
    public bool IsInvalid => Handle == IntPtr.Zero && string.IsNullOrEmpty(Path);
    public IntPtr GetExport(string name) => IntPtr.Zero;
    public void Dispose() { }
}

public class NativeLibrary<T> : NativeLibrary
{
    public NativeLibrary(IntPtr handle)
        : base(handle)
    {
    }

    public NativeLibrary(string path)
        : base(path)
    {
    }
}

public static class ResolvedMelons
{
    public static readonly List<MelonAssembly> Assemblies = new();
}

public sealed class TomlMapper
{
    public object? ToToml<T>(T value) => value;
    public T? FromToml<T>(object value) => value is T typed ? typed : default;
    public object[] WriteArray<T>(T[] value) => value?.Cast<object>().ToArray() ?? Array.Empty<object>();
    public T[] ReadArray<T>(object value) => value as T[] ?? Array.Empty<T>();
    public List<T> ReadList<T>(object value) => value as List<T> ?? new List<T>();
    public object[] WriteList<T>(List<T> value) => value?.Cast<object>().ToArray() ?? Array.Empty<object>();
}

public static class bHaptics
{
    public static bool WasError => false;
    public static bool IsPlaying() => false;
    public static bool IsPlaying(string key) => false;
    public static bool IsDeviceConnected(DeviceType type, bool isLeft = true) => false;
    public static bool IsDeviceConnected(PositionType type) => false;
    public static bool IsFeedbackRegistered(string key) => false;
    public static void RegisterFeedback(string key, string tactFileStr) { }
    public static void RegisterFeedbackFromTactFile(string key, string tactFileStr) { }
    public static void RegisterFeedbackFromTactFileReflected(string key, string tactFileStr) { }
    public static void SubmitRegistered(string key) { }
    public static void SubmitRegistered(string key, int startTimeMillis) { }
    public static void SubmitRegistered(string key, string altKey, ScaleOption option) { }
    public static void SubmitRegistered(string key, string altKey, ScaleOption scaleOption, RotationOption rotationOption) { }
    public static void TurnOff() { }
    public static void TurnOff(string key) { }
    public static void Submit(string key, DeviceType type, bool isLeft, byte[] bytes, int durationMillis) { }
    public static void Submit(string key, PositionType position, byte[] bytes, int durationMillis) { }
    public static void Submit(string key, DeviceType type, bool isLeft, List<DotPoint> points, int durationMillis) { }
    public static void Submit(string key, PositionType position, List<DotPoint> points, int durationMillis) { }
    public static void Submit(string key, DeviceType type, bool isLeft, List<PathPoint> points, int durationMillis) { }
    public static void Submit(string key, PositionType position, List<PathPoint> points, int durationMillis) { }
    public static FeedbackStatus GetCurrentFeedbackStatus(DeviceType type, bool isLeft = true) => new() { values = Array.Empty<int>() };
    public static FeedbackStatus GetCurrentFeedbackStatus(PositionType pos) => new() { values = Array.Empty<int>() };
    public static PositionType DeviceTypeToPositionType(DeviceType pos, bool isLeft = true) => PositionType.Head;
    public enum DeviceType { None = 0, Tactal = 1, TactSuit = 2, Tactosy_arms = 3, Tactosy_hands = 4, Tactosy_feet = 5 }
    public enum PositionType { All = 0, Left = 1, Right = 2, Vest = 3, Head = 4, Racket = 5, HandL = 6, HandR = 7, FootL = 8, FootR = 9, ForearmL = 10, ForearmR = 11, VestFront = 201, VestBack = 202, GloveLeft = 203, GloveRight = 204, Custom1 = 251, Custom2 = 252, Custom3 = 253, Custom4 = 254 }
    public sealed class RotationOption { public RotationOption(float offsetX, float offsetY) { OffsetX = offsetX; OffsetY = offsetY; } public float OffsetX; public float OffsetY; }
    public sealed class ScaleOption { public ScaleOption(float intensity = 1f, float duration = 1f) { Intensity = intensity; Duration = duration; } public float Intensity; public float Duration; }
    public sealed class DotPoint { public DotPoint(int index, int intensity = 50) { Index = index; Intensity = intensity; } public int Index; public int Intensity; }
    [StructLayout(LayoutKind.Sequential)] public struct PathPoint { public PathPoint(float x, float y, int intensity = 50, int motorCount = 3) { X = x; Y = y; Intensity = intensity; MotorCount = motorCount; } public float X; public float Y; public int Intensity; public int MotorCount; }
    [StructLayout(LayoutKind.Sequential)] public struct FeedbackStatus { public int[] values; }
}
