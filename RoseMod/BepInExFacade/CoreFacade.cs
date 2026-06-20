using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using SemRange = SemanticVersioning.Range;
using SemVersion = SemanticVersioning.Version;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version)
        {
            GUID = guid;
            Name = name;
            Version = new SemVersion(version, loose: true);
        }

        public string GUID { get; }
        public string Name { get; }
        public SemVersion Version { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInDependency : Attribute
    {
        public BepInDependency(string dependencyGuid, DependencyFlags flags = DependencyFlags.HardDependency)
        {
            DependencyGUID = dependencyGuid;
            Flags = flags;
            VersionRange = new SemRange("*", loose: true);
        }

        public BepInDependency(string dependencyGuid, string minimumVersion)
        {
            DependencyGUID = dependencyGuid;
            Flags = DependencyFlags.HardDependency;
            VersionRange = new SemRange($">={minimumVersion}", loose: true);
        }

        public string DependencyGUID { get; }
        public DependencyFlags Flags { get; }
        public SemRange VersionRange { get; }

        public enum DependencyFlags
        {
            HardDependency,
            SoftDependency
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInIncompatibility : Attribute
    {
        public BepInIncompatibility(string incompatibilityGuid)
        {
            IncompatibilityGUID = incompatibilityGuid;
        }

        public string IncompatibilityGUID { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInProcess : Attribute
    {
        public BepInProcess(string processName)
        {
            ProcessName = processName;
        }

        public string ProcessName { get; }
    }

    public static class Paths
    {
        public static string GameRootPath { get; internal set; } = Environment.CurrentDirectory;
        public static string BepInExRootPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod");
        public static string PluginPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "BepInExPlugins");
        public static string ConfigPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "Config");
        public static string BepInExConfigPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "Config", "BepInEx.cfg");
        public static string CachePath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "Cache");
        public static string PatcherPluginPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "Patchers");
        public static string ManagedPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "interop");
        public static string GameDataPath { get; internal set; } = Environment.CurrentDirectory;
        public static string ExecutablePath { get; internal set; } = string.Empty;
        public static string ProcessName { get; internal set; } = AppDomain.CurrentDomain.FriendlyName;
        public static string BepInExAssemblyDirectory { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "Core");
        public static string BepInExAssemblyPath { get; internal set; } = Path.Combine(Environment.CurrentDirectory, "RoseMod", "Core", "BepInEx.Core.dll");
        public static string[] DllSearchPaths { get; internal set; } = Array.Empty<string>();
        public static SemVersion BepInExVersion { get; internal set; } = new(6, 0, 0, string.Empty, string.Empty);
        public static SemVersion DisplayBepInExVersion { get; internal set; } = new(6, 0, 0, string.Empty, string.Empty);

        public static void SetExecutablePath(string executablePath, string gameRootPath, string gameDataPath, bool managedPathIsGameDataPath = false, string[]? dllSearchPaths = null)
        {
            ExecutablePath = executablePath;
            GameRootPath = gameRootPath;
            GameDataPath = gameDataPath;
            ProcessName = string.IsNullOrWhiteSpace(executablePath) ? ProcessName : Path.GetFileNameWithoutExtension(executablePath);
            ManagedPath = managedPathIsGameDataPath
                ? gameDataPath
                : Path.Combine(gameDataPath, "Managed");
            DllSearchPaths = dllSearchPaths ?? Array.Empty<string>();
        }
    }

    public sealed class PluginInfo
    {
        public BepInPlugin? Metadata { get; set; }
        public IEnumerable<BepInDependency> Dependencies { get; set; } = Array.Empty<BepInDependency>();
        public IEnumerable<BepInIncompatibility> Incompatibilities { get; set; } = Array.Empty<BepInIncompatibility>();
        public IEnumerable<BepInProcess> Processes { get; set; } = Array.Empty<BepInProcess>();
        public Type? Type { get; set; }
        public string? TypeName { get; set; }
        public string? Location { get; set; }
        public object? Instance { get; set; }

        public override string ToString()
        {
            return Metadata is null ? base.ToString()! : $"{Metadata.Name} {Metadata.Version}";
        }
    }

    public static class Chainloader
    {
        private static readonly Dictionary<string, PluginInfo> plugins = new(StringComparer.OrdinalIgnoreCase);
        internal static Dictionary<string, PluginInfo> PluginInfoDictionary => plugins;
        public static IDictionary<string, PluginInfo> PluginInfos => new ReadOnlyDictionary<string, PluginInfo>(plugins);

        public static void Register(string guid, PluginInfo info)
        {
            plugins[guid] = info;
        }
    }

#if BEPINEX5_MONO
    public abstract class BaseUnityPlugin : UnityEngine.MonoBehaviour
#else
    public abstract class BaseUnityPlugin
#endif
    {
        protected BaseUnityPlugin()
        {
            Info = CreateInfo(GetType(), this);
            Logger = new Logging.ManualLogSource(Info.Metadata?.Name ?? GetType().Name);
            Log = Logger;
            Config = new Configuration.ConfigFile(Path.Combine(Paths.ConfigPath, GetType().Name + ".cfg"), false, Info.Metadata);
        }

        public Logging.ManualLogSource Logger { get; }
        public Logging.ManualLogSource Log { get; }
        public Configuration.ConfigFile Config { get; }
        public PluginInfo Info { get; }

        private static PluginInfo CreateInfo(Type type, BaseUnityPlugin instance)
        {
            return new PluginInfo
            {
                Metadata = type.GetCustomAttributes(false).OfType<BepInPlugin>().FirstOrDefault(),
                Dependencies = type.GetCustomAttributes(false).OfType<BepInDependency>().ToArray(),
                Incompatibilities = type.GetCustomAttributes(false).OfType<BepInIncompatibility>().ToArray(),
                Processes = type.GetCustomAttributes(false).OfType<BepInProcess>().ToArray(),
                Type = type,
                TypeName = type.FullName,
                Location = type.Assembly.Location,
                Instance = instance
            };
        }
    }

    public static class ConsoleManager
    {
        public enum ConsoleOutRedirectType
        {
            Auto = 0,
            ConsoleOut,
            StandardOut
        }

        public static bool ConsoleEnabled => false;
        public static bool ConsoleActive => false;
        public static TextWriter StandardOutStream => Console.Out;
        public static TextWriter? ConsoleStream => Console.Out;

        public static void Initialize(bool alreadyActive, bool useManagedEncoder)
        {
        }

        public static void CreateConsole()
        {
        }

        public static void DetachConsole()
        {
        }

        public static void SetConsoleTitle(string title)
        {
            try
            {
                Console.Title = title;
            }
            catch
            {
            }
        }

        public static void SetConsoleColor(ConsoleColor color)
        {
            try
            {
                Console.ForegroundColor = color;
            }
            catch
            {
            }
        }
    }

    public static class MetadataHelper
    {
        public static BepInPlugin? GetMetadata(Type pluginType)
        {
            return pluginType.GetCustomAttributes(typeof(BepInPlugin), inherit: false).OfType<BepInPlugin>().FirstOrDefault();
        }

        public static BepInPlugin? GetMetadata(object plugin) => GetMetadata(plugin.GetType());

        public static T[] GetAttributes<T>(Type pluginType)
            where T : Attribute
        {
            return pluginType.GetCustomAttributes(typeof(T), inherit: true).OfType<T>().ToArray();
        }

        public static T[] GetAttributes<T>(Assembly assembly)
            where T : Attribute
        {
            return assembly.GetCustomAttributes(typeof(T)).OfType<T>().ToArray();
        }

        public static IEnumerable<T> GetAttributes<T>(object plugin)
            where T : Attribute
        {
            return GetAttributes<T>(plugin.GetType());
        }

        public static T[] GetAttributes<T>(MemberInfo member)
            where T : Attribute
        {
            return member.GetCustomAttributes(typeof(T), inherit: true).OfType<T>().ToArray();
        }

        public static IEnumerable<BepInDependency> GetDependencies(Type plugin)
        {
            return plugin.GetCustomAttributes(typeof(BepInDependency), inherit: true).Cast<BepInDependency>();
        }
    }

    public static class Utility
    {
        public static bool CLRSupportsDynamicAssemblies => true;
        public static Encoding UTF8NoBom { get; } = new UTF8Encoding(false);

        public static bool TryDo(Action action, out Exception? exception)
        {
            try
            {
                action();
                exception = null;
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        public static string CombinePaths(params string[] parts) => parts.Aggregate(Path.Combine);

        public static string? ParentDirectory(string path, int levels = 1)
        {
            for (var i = 0; i < levels; i++)
                path = Path.GetDirectoryName(path) ?? string.Empty;

            return path;
        }

        public static bool SafeParseBool(string input, bool defaultValue = false) => bool.TryParse(input, out var result) ? result : defaultValue;
        public static string ConvertToWWWFormat(string path) => $"file://{path.Replace('\\', '/')}";
        public static bool IsNullOrWhiteSpace(this string? self) => string.IsNullOrWhiteSpace(self);

        public static IEnumerable<TNode> TopologicalSort<TNode>(IEnumerable<TNode> nodes, Func<TNode, IEnumerable<TNode>> dependencySelector)
        {
            var sorted = new List<TNode>();
            var visited = new HashSet<TNode>();
            var active = new HashSet<TNode>();

            foreach (var node in nodes)
                Visit(node);

            return sorted;

            void Visit(TNode node)
            {
                if (visited.Contains(node))
                    return;
                if (!active.Add(node))
                    throw new Exception("Cyclic Dependency");

                foreach (var dependency in dependencySelector(node))
                    Visit(dependency);

                active.Remove(node);
                visited.Add(node);
                sorted.Add(node);
            }
        }

        public static bool TryResolveDllAssembly<T>(AssemblyName assemblyName, string directory, Func<string, T> loader, out T? assembly)
            where T : class
        {
            assembly = null;
            if (!Directory.Exists(directory))
                return false;

            var searchDirectories = new List<string> { directory };
            searchDirectories.AddRange(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));

            foreach (var searchDirectory in searchDirectories)
            {
                foreach (var extension in new[] { ".dll", ".exe" })
                {
                    var path = Path.Combine(searchDirectory, assemblyName.Name + extension);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        assembly = loader(path);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        public static bool TryResolveDllAssembly(AssemblyName assemblyName, string directory, out Assembly? assembly)
        {
            return TryResolveDllAssembly(assemblyName, directory, Assembly.LoadFrom, out assembly);
        }

        public static bool TryOpenFileStream(string path, FileMode mode, out FileStream? fileStream, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.Read)
        {
            try
            {
                fileStream = new FileStream(path, mode, access, share);
                return true;
            }
            catch (IOException)
            {
                fileStream = null;
                return false;
            }
        }

        public static string HashStream(Stream stream)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return ByteArrayToString(md5.ComputeHash(stream));
        }

        public static string HashStrings(params string[] strings)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return ByteArrayToString(md5.ComputeHash(Encoding.UTF8.GetBytes(string.Concat(strings))));
        }

        public static string ByteArrayToString(byte[] data) => string.Concat(data.Select(value => value.ToString("x2")));

        public static string? GetCommandLineArgValue(string arg)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == arg)
                    return args[i + 1];
            }

            return null;
        }

        public static bool TryParseAssemblyName(string fullName, out AssemblyName? assemblyName)
        {
            try
            {
                assemblyName = new AssemblyName(fullName);
                return true;
            }
            catch
            {
                assemblyName = null;
                return false;
            }
        }

        public static IEnumerable<string> GetUniqueFilesInDirectories(IEnumerable<string> directories, string pattern = "*")
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (var file in Directory.GetFiles(directory, pattern))
                {
                    var fileName = Path.GetFileName(file);
                    if (!result.ContainsKey(fileName))
                        result[fileName] = file;
                }
            }

            return result.Values;
        }
    }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, BepInEx.PluginInfo> PluginInfos => BepInEx.Chainloader.PluginInfoDictionary;
    }
}

namespace BepInEx.Logging
{
    [Flags]
    public enum LogLevel
    {
        None = 0,
        Fatal = 1,
        Error = 2,
        Warning = 4,
        Message = 8,
        Info = 16,
        Debug = 32,
        All = Fatal | Error | Warning | Message | Info | Debug
    }

    public interface ILogSource
    {
        string SourceName { get; }
    }

    public interface ILogListener : IDisposable
    {
        void LogEvent(object sender, LogEventArgs eventArgs);
    }

    public sealed class LogEventArgs : EventArgs
    {
        public LogEventArgs(object data, LogLevel level, ILogSource source)
        {
            Data = data;
            Level = level;
            Source = source;
        }

        public object Data { get; }
        public LogLevel Level { get; }
        public ILogSource Source { get; }

        public override string ToString() => $"[{Level}:{Source.SourceName}] {Data}";
        public string ToStringLine() => ToString();
    }

    public sealed class ManualLogSource : ILogSource, IDisposable
    {
        public ManualLogSource(string sourceName)
        {
            SourceName = sourceName;
            Logger.AddLogSource(this);
        }

        public string SourceName { get; }
        public event EventHandler<LogEventArgs>? LogEvent;

        public void Log(LogLevel level, object data)
        {
            var eventArgs = new LogEventArgs(data, level, this);
            LogEvent?.Invoke(this, eventArgs);
            Logger.Dispatch(this, eventArgs);
            RoseModFacadeLog.Write(level.ToString(), SourceName, data?.ToString() ?? string.Empty);
        }

        public void LogFatal(object data) => Log(LogLevel.Fatal, data);
        public void LogError(object data) => Log(LogLevel.Error, data);
        public void LogWarning(object data) => Log(LogLevel.Warning, data);
        public void LogMessage(object data) => Log(LogLevel.Message, data);
        public void LogInfo(object data) => Log(LogLevel.Info, data);
        public void LogDebug(object data) => Log(LogLevel.Debug, data);

        public void Dispose()
        {
            Logger.RemoveLogSource(this);
        }
    }

    public static class Logger
    {
        private static readonly List<ILogSource> sources = new();
        private static readonly List<ILogListener> listeners = new();

        public static ICollection<ILogSource> Sources => sources;
        public static ICollection<ILogListener> Listeners => listeners;
        public static LogLevel ListenedLogLevels => listeners.Count == 0
            ? LogLevel.All
            : listeners.Aggregate(LogLevel.None, (current, listener) => current | GetLogLevelFilter(listener));

        public static ManualLogSource CreateLogSource(string sourceName) => new(sourceName);

        internal static void AddLogSource(ILogSource source)
        {
            if (!sources.Contains(source))
                sources.Add(source);
        }

        internal static void RemoveLogSource(ILogSource source)
        {
            sources.Remove(source);
        }

        internal static void Dispatch(object sender, LogEventArgs eventArgs)
        {
            foreach (var listener in listeners.ToArray())
            {
                if ((GetLogLevelFilter(listener) & eventArgs.Level) != 0)
                    listener.LogEvent(sender, eventArgs);
            }
        }

        private static LogLevel GetLogLevelFilter(ILogListener listener)
        {
            try
            {
                var value = listener.GetType().GetProperty("LogLevelFilter")?.GetValue(listener);
                return value is LogLevel level ? level : LogLevel.All;
            }
            catch
            {
                return LogLevel.All;
            }
        }
    }

    public class ConsoleLogListener : ILogListener
    {
        public LogLevel LogLevelFilter { get; set; } = LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            ConsoleManager.SetConsoleColor(eventArgs.Level.GetConsoleColor());
            Console.WriteLine(eventArgs.ToString());
            ConsoleManager.SetConsoleColor(ConsoleColor.Gray);
        }

        public void Dispose()
        {
        }
    }

    public class DiskLogListener : ILogListener
    {
        public static HashSet<string> BlacklistedSources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DiskLogListener(string localPath, LogLevel displayedLogLevel = LogLevel.Info, bool appendLog = false, bool delayedFlushing = true, int fileLimit = 5)
        {
            LogLevelFilter = displayedLogLevel;
            var path = Path.IsPathRooted(localPath) ? localPath : Path.Combine(Paths.BepInExRootPath, localPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            LogWriter = TextWriter.Synchronized(new StreamWriter(path, appendLog, Utility.UTF8NoBom));
            InstantFlushing = !delayedFlushing;
        }

        public TextWriter? LogWriter { get; protected set; }
        public LogLevel LogLevelFilter { get; set; }
        private bool InstantFlushing { get; }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (LogWriter is null || BlacklistedSources.Contains(eventArgs.Source.SourceName))
                return;

            LogWriter.WriteLine(eventArgs.ToString());
            if (InstantFlushing)
                LogWriter.Flush();
        }

        public void Dispose()
        {
            LogWriter?.Flush();
            LogWriter?.Dispose();
            LogWriter = null;
        }
    }

    public class HarmonyLogSource : ILogSource, IDisposable
    {
        public string SourceName { get; } = "HarmonyX";
        public event EventHandler<LogEventArgs>? LogEvent;

        public void Write(LogLevel level, object data)
        {
            LogEvent?.Invoke(this, new LogEventArgs(data, level, this));
        }

        public void Dispose()
        {
        }
    }

    public class TraceLogSource : TraceListener
    {
        private static TraceLogSource? traceListener;

        protected TraceLogSource()
        {
            LogSource = new ManualLogSource("Trace");
        }

        public static bool IsListening { get; private set; }
        protected ManualLogSource LogSource { get; }

        public static ILogSource CreateSource()
        {
            if (traceListener is null)
            {
                traceListener = new TraceLogSource();
                Trace.Listeners.Add(traceListener);
                IsListening = true;
            }

            return traceListener.LogSource;
        }

        public override void Write(string? message) => LogSource.Log(LogLevel.Info, message ?? string.Empty);
        public override void WriteLine(string? message) => LogSource.Log(LogLevel.Info, message ?? string.Empty);

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            var level = eventType switch
            {
                TraceEventType.Critical => LogLevel.Fatal,
                TraceEventType.Error => LogLevel.Error,
                TraceEventType.Warning => LogLevel.Warning,
                TraceEventType.Information => LogLevel.Info,
                _ => LogLevel.Debug
            };
            LogSource.Log(level, (message ?? string.Empty).Trim());
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            TraceEvent(eventCache, source, eventType, id, args is null || args.Length == 0 ? format : string.Format(format ?? string.Empty, args));
        }
    }

    public static class LogLevelExtensions
    {
        public static ConsoleColor GetConsoleColor(this LogLevel level)
        {
            return level switch
            {
                LogLevel.Fatal or LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Debug => ConsoleColor.DarkGray,
                LogLevel.Info => ConsoleColor.Cyan,
                LogLevel.Message => ConsoleColor.White,
                _ => ConsoleColor.Gray
            };
        }

        public static LogLevel GetHighestLevel(this LogLevel level)
        {
            foreach (var candidate in new[] { LogLevel.Fatal, LogLevel.Error, LogLevel.Warning, LogLevel.Message, LogLevel.Info, LogLevel.Debug })
            {
                if ((level & candidate) != 0)
                    return candidate;
            }

            return LogLevel.None;
        }
    }

    internal static class RoseModFacadeLog
    {
        private static readonly object Gate = new();
        private const string Reset = "\u001b[0m";
        private static readonly string Gray = Color(211, 211, 211);
        private static readonly string Timestamp = Color(0, 255, 0);
        private static readonly string Theme = Color(80, 220, 140);
        private static readonly string InfoColor = Color(0, 255, 255);
        private static readonly string WarningColor = Color(255, 255, 0);
        private static readonly string ErrorColor = Color(205, 92, 92);
        private static readonly string DebugColor = Color(130, 130, 130);

        // Console format adapted from Simple Log Utility by Fibles.
        public static void Write(string level, string sourceName, string message)
        {
            var color = ColorForLevel(level);
            lock (Gate)
            {
                foreach (var part in message.Replace("\r\n", "\n").Split('\n'))
                {
                    var cleanText = part.Replace(Reset, color);
                    var time = DateTime.Now;
                    Console.WriteLine($"{Gray}[{Timestamp}{time:HH:mm:ss.fff}{Gray}] [{Theme}{sourceName}{Gray}] [{color}{level}{Gray}]{color} {cleanText}{Reset}");
                    AppendLogFile($"[{time:HH:mm:ss.fff}] [{sourceName}] [{level}] {part}");
                }
            }
        }

        private static void AppendLogFile(string line)
        {
            try
            {
                var logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "Logs", "RoseMod.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string ColorForLevel(string level)
        {
            return level.ToLowerInvariant() switch
            {
                "fatal" => ErrorColor,
                "error" => ErrorColor,
                "warning" => WarningColor,
                "debug" => DebugColor,
                "info" => InfoColor,
                "message" => Theme,
                _ => Theme
            };
        }

        private static string Color(byte red, byte green, byte blue) => $"\u001b[38;2;{red};{green};{blue}m";
    }
}

namespace BepInEx.Configuration
{
    public abstract class AcceptableValueBase
    {
        protected AcceptableValueBase(Type valueType)
        {
            ValueType = valueType;
        }

        public Type ValueType { get; }
        public abstract object Clamp(object value);
        public abstract bool IsValid(object value);
        public abstract string ToDescriptionString();
    }

    public sealed class AcceptableValueList<T> : AcceptableValueBase
    {
        public AcceptableValueList(params T[] acceptableValues)
            : base(typeof(T))
        {
            AcceptableValues = acceptableValues;
        }

        public T[] AcceptableValues { get; }

        public override object Clamp(object value) => IsValid(value) ? value : AcceptableValues.FirstOrDefault()!;
        public override bool IsValid(object value) => value is T typed && AcceptableValues.Contains(typed);
        public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", AcceptableValues.Select(value => value?.ToString()));
    }

    public sealed class AcceptableValueRange<T> : AcceptableValueBase
        where T : IComparable
    {
        public AcceptableValueRange(T minValue, T maxValue)
            : base(typeof(T))
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T MinValue { get; }
        public T MaxValue { get; }

        public override object Clamp(object value)
        {
            if (value is not T typed)
                return MinValue;

            if (typed.CompareTo(MinValue) < 0)
                return MinValue;

            return typed.CompareTo(MaxValue) > 0 ? MaxValue : typed;
        }

        public override bool IsValid(object value) => value is T typed && typed.CompareTo(MinValue) >= 0 && typed.CompareTo(MaxValue) <= 0;
        public override string ToDescriptionString() => $"# Acceptable value range: From {MinValue} to {MaxValue}";
    }

    public sealed class ConfigDefinition : IEquatable<ConfigDefinition>
    {
        public ConfigDefinition(string section, string key)
            : this(section, key, string.Empty)
        {
        }

        public ConfigDefinition(string section, string key, string? description)
        {
            Section = section;
            Key = key;
        }

        public string Section { get; }
        public string Key { get; }

        public bool Equals(ConfigDefinition? other)
        {
            return other is not null
                && string.Equals(Section, other.Section, StringComparison.Ordinal)
                && string.Equals(Key, other.Key, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as ConfigDefinition);
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Section?.GetHashCode() ?? 0) * 397) ^ (Key?.GetHashCode() ?? 0);
            }
        }
        public override string ToString() => $"{Section}.{Key}";
        public static bool operator ==(ConfigDefinition? left, ConfigDefinition? right) => Equals(left, right);
        public static bool operator !=(ConfigDefinition? left, ConfigDefinition? right) => !Equals(left, right);
    }

    public sealed class ConfigDescription
    {
        public ConfigDescription(string? description, AcceptableValueBase? acceptableValues = null, params object[]? tags)
        {
            Description = description ?? string.Empty;
            AcceptableValues = acceptableValues;
            Tags = tags ?? Array.Empty<object>();
        }

        public string Description { get; }
        public AcceptableValueBase? AcceptableValues { get; }
        public object[] Tags { get; }
        public static ConfigDescription Empty { get; } = new(string.Empty);
    }

    public abstract class ConfigEntryBase
    {
        protected ConfigEntryBase(ConfigFile configFile, ConfigDefinition definition, Type settingType, object defaultValue, ConfigDescription description)
        {
            ConfigFile = configFile;
            Definition = definition;
            SettingType = settingType;
            DefaultValue = defaultValue;
            Description = description;
        }

        public ConfigFile ConfigFile { get; }
        public ConfigDefinition Definition { get; }
        public Type SettingType { get; }
        public object DefaultValue { get; }
        public ConfigDescription Description { get; }
        public abstract object BoxedValue { get; set; }

        public virtual string GetSerializedValue() => TomlTypeConverter.ConvertToString(BoxedValue, SettingType);
        public virtual void SetSerializedValue(string value) => BoxedValue = TomlTypeConverter.ConvertToValue(value, SettingType);

        public virtual void WriteDescription(StreamWriter writer)
        {
            if (!string.IsNullOrWhiteSpace(Description.Description))
                writer.WriteLine("# " + Description.Description);
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        internal ConfigEntry(ConfigFile configFile, ConfigDefinition definition, T value, ConfigDescription description)
            : base(configFile, definition, typeof(T), value!, description)
        {
            Value = value;
        }

        public T Value { get; set; }

        public override object BoxedValue
        {
            get => Value!;
            set => Value = value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
        }
    }

    public sealed class ConfigFile : IDictionary<ConfigDefinition, ConfigEntryBase>
    {
        private readonly Dictionary<ConfigDefinition, ConfigEntryBase> entries = new();

        public ConfigFile(string configPath, bool saveOnInit)
            : this(configPath, saveOnInit, null)
        {
        }

        public ConfigFile(string configPath, bool saveOnInit, BepInEx.BepInPlugin? ownerMetadata)
        {
            ConfigFilePath = configPath;
            SaveOnConfigSet = saveOnInit;
        }

        public string ConfigFilePath { get; }
        public bool SaveOnConfigSet { get; set; }
        public bool GenerateSettingDescriptions { get; set; } = true;
        public static ConfigFile CoreConfig { get; } = new(Path.Combine(BepInEx.Paths.ConfigPath, "BepInEx.cfg"), false);
        public ReadOnlyCollection<ConfigDefinition> ConfigDefinitions => entries.Keys.ToList().AsReadOnly();
        public ICollection<ConfigDefinition> Keys => entries.Keys;
        public ICollection<ConfigEntryBase> Values => entries.Values;
        public int Count => entries.Count;
        public bool IsReadOnly => false;

        public ConfigEntryBase this[ConfigDefinition key]
        {
            get => entries[key];
            set => entries[key] = value;
        }

        public ConfigEntryBase this[string section, string key] => entries[new ConfigDefinition(section, key)];

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string? description = null)
        {
            return Bind(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            return Bind(new ConfigDefinition(section, key), defaultValue, description);
        }

        public ConfigEntry<T> Bind<T>(ConfigDefinition definition, T defaultValue, ConfigDescription description)
        {
            if (!entries.TryGetValue(definition, out var existing))
            {
                existing = new ConfigEntry<T>(this, definition, defaultValue, description);
                entries[definition] = existing;
            }

            return (ConfigEntry<T>)existing;
        }

        public ConfigEntry<T> AddSetting<T>(string section, string key, T defaultValue, string? description = null)
        {
            return AddSetting(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> AddSetting<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            return AddSetting(new ConfigDefinition(section, key), defaultValue, description);
        }

        public ConfigEntry<T> AddSetting<T>(ConfigDefinition definition, T defaultValue, ConfigDescription description)
        {
            var entry = new ConfigEntry<T>(this, definition, defaultValue, description);
            entries[definition] = entry;
            return entry;
        }

        public ConfigEntry<T> GetSetting<T>(string section, string key) => GetSetting<T>(new ConfigDefinition(section, key));
        public ConfigEntry<T> GetSetting<T>(ConfigDefinition definition) => (ConfigEntry<T>)entries[definition];
        public ConfigEntryBase[] GetConfigEntries() => entries.Values.ToArray();

        public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T>? entry)
        {
            return TryGetEntry(new ConfigDefinition(section, key), out entry);
        }

        public bool TryGetEntry<T>(ConfigDefinition definition, out ConfigEntry<T>? entry)
        {
            if (entries.TryGetValue(definition, out var existing) && existing is ConfigEntry<T> typed)
            {
                entry = typed;
                return true;
            }

            entry = null;
            return false;
        }

        public ConfigWrapper<T> Wrap<T>(string section, string key, string description, T defaultValue)
        {
            return new ConfigWrapper<T>(this, new ConfigDefinition(section, key), defaultValue);
        }

        public ConfigWrapper<T> Wrap<T>(ConfigDefinition definition, T defaultValue)
        {
            return new ConfigWrapper<T>(this, definition, defaultValue);
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
            using var writer = new StreamWriter(ConfigFilePath);
            foreach (var entry in entries.Values)
                writer.WriteLine($"{entry.Definition} = {entry.GetSerializedValue()}");
        }

        public void Reload()
        {
        }

        public void Add(ConfigDefinition key, ConfigEntryBase value) => entries.Add(key, value);
        public bool ContainsKey(ConfigDefinition key) => entries.ContainsKey(key);
        public bool Remove(ConfigDefinition key) => entries.Remove(key);
        public bool TryGetValue(ConfigDefinition key, out ConfigEntryBase value) => entries.TryGetValue(key, out value!);
        public void Add(KeyValuePair<ConfigDefinition, ConfigEntryBase> item) => entries.Add(item.Key, item.Value);
        public void Clear() => entries.Clear();
        public bool Contains(KeyValuePair<ConfigDefinition, ConfigEntryBase> item) => ((IDictionary<ConfigDefinition, ConfigEntryBase>)entries).Contains(item);
        public void CopyTo(KeyValuePair<ConfigDefinition, ConfigEntryBase>[] array, int arrayIndex) => ((IDictionary<ConfigDefinition, ConfigEntryBase>)entries).CopyTo(array, arrayIndex);
        public bool Remove(KeyValuePair<ConfigDefinition, ConfigEntryBase> item) => ((IDictionary<ConfigDefinition, ConfigEntryBase>)entries).Remove(item);
        public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator() => entries.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ConfigWrapper<T>
    {
        public ConfigWrapper(ConfigFile configFile, ConfigDefinition definition, T defaultValue)
        {
            ConfigFile = configFile;
            Definition = definition;
            ConfigEntry = configFile.Bind(definition, defaultValue, ConfigDescription.Empty);
        }

        public ConfigFile ConfigFile { get; }
        public ConfigDefinition Definition { get; }
        public ConfigEntry<T> ConfigEntry { get; }
        public T Value
        {
            get => ConfigEntry.Value;
            set => ConfigEntry.Value = value;
        }
    }

    public sealed class SettingChangedEventArgs : EventArgs
    {
        public SettingChangedEventArgs(ConfigEntryBase changedSetting)
        {
            ChangedSetting = changedSetting;
        }

        public ConfigEntryBase ChangedSetting { get; }
    }

    public sealed class TypeConverter
    {
        public Func<string, Type, object>? ConvertToObject { get; set; }
        public Func<object, Type, string>? ConvertToString { get; set; }
    }

    public static class TomlTypeConverter
    {
        private static readonly Dictionary<Type, TypeConverter> converters = new();

        public static bool AddConverter(Type type, TypeConverter converter)
        {
            converters[type] = converter;
            return true;
        }

        public static bool CanConvert(Type type) => type.IsEnum || converters.ContainsKey(type) || type == typeof(string) || type.IsPrimitive || type == typeof(decimal);
        public static IEnumerable<Type> GetSupportedTypes() => converters.Keys;
        public static TypeConverter GetConverter(Type type) => converters[type];
        public static string ConvertToString(object value, Type type) => converters.TryGetValue(type, out var converter) && converter.ConvertToString is not null
            ? converter.ConvertToString(value, type)
            : value?.ToString() ?? string.Empty;

        public static T ConvertToValue<T>(string value) => (T)ConvertToValue(value, typeof(T));

        public static object ConvertToValue(string value, Type type)
        {
            if (converters.TryGetValue(type, out var converter) && converter.ConvertToObject is not null)
                return converter.ConvertToObject(value, type);

            if (type == typeof(string))
                return value;

            if (type.IsEnum)
                return Enum.Parse(type, value, ignoreCase: true);

            return Convert.ChangeType(value, type);
        }
    }
}

namespace BepInEx.Preloader.Core.Patching
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PatcherPluginInfoAttribute : Attribute
    {
        public PatcherPluginInfoAttribute(string GUID, string Name, string Version)
        {
            this.GUID = GUID;
            this.Name = Name;
            this.Version = TryParseVersion(Version);
        }

        public string GUID { get; protected set; }
        public string Name { get; protected set; }
        public SemanticVersioning.Version Version { get; protected set; }

        private static SemanticVersioning.Version TryParseVersion(string version)
        {
            if (SemanticVersioning.Version.TryParse(version, out var parsed))
                return parsed;

            try
            {
                var systemVersion = new Version(version);
                return new SemanticVersioning.Version(
                    systemVersion.Major,
                    systemVersion.Minor,
                    systemVersion.Build >= 0 ? systemVersion.Build : 0);
            }
            catch
            {
                return new SemanticVersioning.Version(0, 0, 0);
            }
        }

        public static PatcherPluginInfoAttribute? FromType(Type type)
        {
            return type.GetCustomAttributes(typeof(PatcherPluginInfoAttribute), inherit: false)
                .OfType<PatcherPluginInfoAttribute>()
                .FirstOrDefault();
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TargetAssemblyAttribute : Attribute
    {
        public const string AllAssemblies = "_all";

        public TargetAssemblyAttribute(string targetAssembly)
        {
            TargetAssembly = targetAssembly;
        }

        public string TargetAssembly { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TargetTypeAttribute : Attribute
    {
        public TargetTypeAttribute(string targetAssembly, string targetType)
        {
            TargetAssembly = targetAssembly;
            TargetType = targetType;
        }

        public string TargetAssembly { get; }
        public string TargetType { get; }
    }

    public abstract class BasePatcher
    {
        protected BasePatcher()
        {
            Info = PatcherPluginInfoAttribute.FromType(GetType())
                ?? new PatcherPluginInfoAttribute(GetType().FullName ?? GetType().Name, GetType().Name, "0.0.0");
            Log = BepInEx.Logging.Logger.CreateLogSource(Info.Name);
            Config = new BepInEx.Configuration.ConfigFile(
                BepInEx.Utility.CombinePaths(BepInEx.Paths.ConfigPath, Info.GUID + ".cfg"),
                false,
                new BepInEx.BepInPlugin(Info.GUID, Info.Name, Info.Version.ToString()));
        }

        public BepInEx.Logging.ManualLogSource Log { get; }
        public BepInEx.Configuration.ConfigFile Config { get; }
        public PatcherPluginInfoAttribute Info { get; }
        public PatcherContext Context { get; set; } = new();

        public virtual void Initialize()
        {
        }

        public virtual void Finalizer()
        {
        }
    }

    public class PatchDefinition
    {
        public PatchDefinition(TargetAssemblyAttribute targetAssembly, BasePatcher instance, MethodInfo methodInfo)
        {
            TargetAssembly = targetAssembly;
            Instance = instance;
            MethodInfo = methodInfo;
            FullName = $"{methodInfo.DeclaringType?.FullName}/{methodInfo.Name} -> {targetAssembly.TargetAssembly}";
        }

        public PatchDefinition(TargetTypeAttribute targetType, BasePatcher instance, MethodInfo methodInfo)
        {
            TargetType = targetType;
            Instance = instance;
            MethodInfo = methodInfo;
            FullName = $"{methodInfo.DeclaringType?.FullName}/{methodInfo.Name} -> {targetType.TargetAssembly}/{targetType.TargetType}";
        }

        public TargetAssemblyAttribute? TargetAssembly { get; }
        public TargetTypeAttribute? TargetType { get; }
        public BasePatcher Instance { get; }
        public MethodInfo MethodInfo { get; }
        public string FullName { get; }
    }

    public class PatcherContext
    {
        public Dictionary<string, Mono.Cecil.AssemblyDefinition> AvailableAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AvailableAssembliesPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Assembly> LoadedAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<BasePatcher> PatcherPlugins { get; } = new();
        public List<PatchDefinition> PatchDefinitions { get; } = new();
        public string DumpedAssembliesPath { get; set; } = BepInEx.Utility.CombinePaths(BepInEx.Paths.BepInExRootPath, "PatchedAssemblies", BepInEx.Paths.ProcessName);
    }

    public sealed class AssemblyPatcher : IDisposable
    {
        private readonly Func<byte[], string, Assembly> assemblyLoader;

        public AssemblyPatcher(Func<byte[], string, Assembly> assemblyLoader)
        {
            this.assemblyLoader = assemblyLoader;
        }

        public PatcherContext PatcherContext { get; } = new();

        public void AddPatchersFromDirectory(string directory)
        {
            RoseMod.ReflectionPatcherShim.AddPatchersFromDirectory(directory, PatcherContext);
        }

        public void LoadAssemblyDirectories(params string[] directories)
        {
            RoseMod.ReflectionPatcherShim.LoadAssemblyDirectories(PatcherContext, directories);
        }

        public void PatchAndLoad()
        {
            RoseMod.ReflectionPatcherShim.PatchAndLoad(PatcherContext, assemblyLoader);
        }

        public void Dispose()
        {
            foreach (var assembly in PatcherContext.AvailableAssemblies.Values)
                assembly.Dispose();
            PatcherContext.AvailableAssemblies.Clear();
            PatcherContext.AvailableAssembliesPaths.Clear();
            PatcherContext.PatcherPlugins.Clear();
            PatcherContext.PatchDefinitions.Clear();
        }
    }
}

namespace RoseMod
{
    internal static class ReflectionPatcherShim
    {
        public static void AddPatchersFromDirectory(string directory, BepInEx.Preloader.Core.Patching.PatcherContext context)
        {
        }

        public static void LoadAssemblyDirectories(BepInEx.Preloader.Core.Patching.PatcherContext context, params string[] directories)
        {
        }

        public static void PatchAndLoad(BepInEx.Preloader.Core.Patching.PatcherContext context, Func<byte[], string, Assembly> assemblyLoader)
        {
        }
    }
}
