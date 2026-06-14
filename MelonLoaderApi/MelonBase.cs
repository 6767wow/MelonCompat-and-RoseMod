using System.Collections.ObjectModel;
using System.Reflection;
using HarmonyLib;
using MelonLoader.Logging;

namespace MelonLoader;

public abstract class MelonBase
{
    private static readonly List<MelonBase> AllRegisteredMelons = new();

    protected MelonBase()
    {
        Info = new MelonInfoAttribute(GetType(), GetType().Name, "0.0.0", "Unknown", null);
        LoggerInstance = new MelonLogger.Instance(GetType().Name);
        HarmonyInstance = new Harmony(BuildHarmonyId(GetType().FullName ?? GetType().Name));
    }

    public static readonly MelonEvent<MelonBase> OnMelonRegistered = new();
    public static readonly MelonEvent<MelonBase> OnMelonUnregistered = new();
    public static readonly MelonEvent<MelonBase> OnMelonInitializing = new();

    public readonly MelonEvent OnRegister = new();
    public readonly MelonEvent OnUnregister = new();

    public MelonAssembly? MelonAssembly { get; private set; }
    public Assembly? Assembly { get; private set; }
    public string Location { get; private set; } = string.Empty;
    public string Hash { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public ColorARGB ConsoleColor { get; private set; } = MelonLogger.DefaultMelonColor;
    public ColorARGB AuthorConsoleColor { get; private set; } = MelonLogger.DefaultTextColor;
    public MelonInfoAttribute Info { get; private set; }
    public MelonAdditionalCreditsAttribute? AdditionalCredits { get; private set; }
    public MelonProcessAttribute[] SupportedProcesses { get; private set; } = Array.Empty<MelonProcessAttribute>();
    public MelonGameAttribute[] Games { get; private set; } = Array.Empty<MelonGameAttribute>();
    public MelonGameVersionAttribute[] SupportedGameVersions { get; private set; } = Array.Empty<MelonGameVersionAttribute>();
    public MelonOptionalDependenciesAttribute? OptionalDependencies { get; private set; }
    public MelonPlatformAttribute SupportedPlatforms { get; private set; } = new(MelonPlatformAttribute.CompatiblePlatforms.UNIVERSAL);
    public MelonPlatformDomainAttribute SupportedDomain { get; private set; } = new(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP);
    public VerifyLoaderVersionAttribute? SupportedMLVersion { get; private set; }
    public VerifyLoaderBuildAttribute? SupportedMLBuild { get; private set; }
    public Harmony HarmonyInstance { get; private set; }
    public Harmony harmonyInstance => HarmonyInstance;
    public MelonLogger.Instance LoggerInstance { get; private set; }
    public string ID { get; private set; } = string.Empty;
    public bool Registered { get; private set; }
    public bool HarmonyDontPatchAll { get; protected set; }
    public virtual string MelonTypeName => "Melon";
    public static ReadOnlyCollection<MelonBase> RegisteredMelons => AllRegisteredMelons.AsReadOnly();

    public virtual void OnPreSupportModule()
    {
    }

    public virtual void OnUpdate()
    {
    }

    public virtual void OnFixedUpdate()
    {
    }

    public virtual void OnLateUpdate()
    {
    }

    public virtual void OnGUI()
    {
    }

    public virtual void OnApplicationQuit()
    {
    }

    public virtual void OnApplicationStart()
    {
    }

    public virtual void OnApplicationLateStart()
    {
    }

    public virtual void OnPreferencesSaved()
    {
    }

    public virtual void OnPreferencesSaved(string filepath)
    {
    }

    public virtual void OnPreferencesLoaded()
    {
    }

    public virtual void OnPreferencesLoaded(string filepath)
    {
    }

    public virtual void OnEarlyInitializeMelon()
    {
    }

    public virtual void OnInitializeMelon()
    {
    }

    public virtual void OnLateInitializeMelon()
    {
    }

    public virtual void OnDeinitializeMelon()
    {
    }

    public virtual void OnModSettingsApplied()
    {
    }

    public virtual object? SendMessage(string name, params object?[] args)
    {
        var method = GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return method?.Invoke(this, args);
    }

    public virtual bool Register()
    {
        if (Registered)
            return true;

        Registered = true;
        AllRegisteredMelons.Add(this);

        if (this is MelonMod mod)
            MelonHandler.AddMod(mod);
        if (this is MelonPlugin plugin)
            MelonHandler.AddPlugin(plugin);

        if (!InvokeSafe(nameof(OnEarlyInitializeMelon), OnEarlyInitializeMelon))
            return FailRegistration();

        OnMelonInitializing.Invoke(this);
        OnRegister.Invoke();

        if (!InvokeSafe(nameof(OnInitializeMelon), OnInitializeMelon))
            return FailRegistration();

        OnMelonRegistered.Invoke(this);
        return true;
    }

    public virtual void Unregister(string reason = "", bool silent = false)
    {
        if (!Registered)
            return;

        Registered = false;
        InvokeSafe(nameof(OnDeinitializeMelon), OnDeinitializeMelon);
        AllRegisteredMelons.Remove(this);
        MelonHandler.Remove(this);
        OnUnregister.Invoke();
        OnMelonUnregistered.Invoke(this);

        if (!silent)
            LoggerInstance.Msg($"Unregistered{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason}")}");
    }

    public static void RegisterSorted<T>(IEnumerable<T> melons)
        where T : MelonBase
    {
        foreach (var melon in melons.OrderBy(static melon => melon.Priority))
            melon.Register();
    }

    public static T CreateWrapper<T>(
        string name,
        string version,
        string author,
        MelonGameAttribute[] games,
        MelonProcessAttribute[] processes,
        int priority,
        ColorARGB? melonColor,
        ColorARGB? authorColor,
        string id)
        where T : MelonBase, new()
    {
        var melon = new T();
        melon.AttachCompatMetadata(
            typeof(T).Assembly,
            typeof(T).Assembly.Location,
            new MelonInfoAttribute(typeof(T), name, version, author, null),
            priority,
            null,
            null,
            processes,
            games,
            null,
            null,
            null,
            null,
            melonColor,
            authorColor,
            id);
        return melon;
    }

    public static MelonBase? FindMelon(string name, string author)
    {
        return AllRegisteredMelons.FirstOrDefault(melon =>
            string.Equals(melon.Info.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(melon.Info.Author, author, StringComparison.OrdinalIgnoreCase));
    }

    public static void SendMessageAll(string name, params object?[] args)
    {
        foreach (var melon in AllRegisteredMelons.ToArray())
            melon.SendMessage(name, args);
    }

    public static void ExecuteAll(LemonAction<MelonBase> action, bool unregisterOnFail = false, string source = "")
    {
        ExecuteList(action, AllRegisteredMelons.ToList(), unregisterOnFail, source);
    }

    public static void ExecuteList<T>(LemonAction<T> action, List<T> list, bool unregisterOnFail = false, string source = "")
        where T : MelonBase
    {
        foreach (var melon in list.ToArray())
        {
            try
            {
                action(melon);
            }
            catch (Exception ex)
            {
                melon.LoggerInstance.Error($"{source} callback failed", ex);
                if (unregisterOnFail)
                    melon.Unregister(ex.Message, true);
            }
        }
    }

    internal void AttachCompatMetadata(
        Assembly assembly,
        string location,
        MelonInfoAttribute info,
        int priority,
        MelonOptionalDependenciesAttribute? optionalDependencies,
        MelonAdditionalCreditsAttribute? additionalCredits,
        MelonProcessAttribute[] supportedProcesses,
        MelonGameAttribute[] games,
        MelonPlatformAttribute? supportedPlatforms,
        MelonPlatformDomainAttribute? supportedDomain,
        VerifyLoaderVersionAttribute? supportedMLVersion,
        VerifyLoaderBuildAttribute? supportedMLBuild,
        ColorARGB? melonColor,
        ColorARGB? authorColor,
        string? id)
    {
        Assembly = assembly;
        Location = location;
        Hash = MelonUtils.ComputeSimpleSHA256Hash(location);
        Info = info;
        Priority = priority;
        OptionalDependencies = optionalDependencies;
        AdditionalCredits = additionalCredits;
        SupportedProcesses = supportedProcesses;
        Games = games;
        SupportedPlatforms = supportedPlatforms ?? new MelonPlatformAttribute(MelonPlatformAttribute.CompatiblePlatforms.UNIVERSAL);
        SupportedDomain = supportedDomain ?? new MelonPlatformDomainAttribute(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP);
        SupportedMLVersion = supportedMLVersion;
        SupportedMLBuild = supportedMLBuild;
        ConsoleColor = melonColor ?? MelonLogger.DefaultMelonColor;
        AuthorConsoleColor = authorColor ?? MelonLogger.DefaultTextColor;
        ID = string.IsNullOrWhiteSpace(id) ? BuildHarmonyId($"{info.Author}.{info.Name}") : id!;
        LoggerInstance = new MelonLogger.Instance(info.Name, ConsoleColor);
        HarmonyInstance = new Harmony(ID);
        MelonAssembly = MelonAssembly.GetOrCreate(assembly, location);
    }

    internal bool InvokeSafe(string callbackName, Action callback)
    {
        try
        {
            callback();
            return true;
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"{callbackName} failed{BepInExCompat.CompatExceptionDiagnostics.Describe(ex)}", ex);
            return false;
        }
    }

    private bool FailRegistration()
    {
        Registered = false;
        AllRegisteredMelons.Remove(this);
        MelonHandler.Remove(this);
        return false;
    }

    private static string BuildHarmonyId(string value)
    {
        var safe = new string(value.Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '.').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    public sealed class Incompatibility
    {
        public Incompatibility(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}

public abstract class MelonTypeBase<T> : MelonBase
    where T : MelonBase
{
    public static new ReadOnlyCollection<T> RegisteredMelons => MelonHandler.GetRegistered<T>().AsReadOnly();

    public virtual string TypeName => typeof(T).Name;

    public override string MelonTypeName => TypeName;

    public static void ExecuteAll(LemonAction<T> action, bool unregisterOnFail = false, string source = "")
    {
        ExecuteList(action, MelonHandler.GetRegistered<T>(), unregisterOnFail, source);
    }
}

public class MelonMod : MelonTypeBase<MelonMod>
{
    public override string MelonTypeName => "Mod";
    public MelonModInfoAttribute? InfoAttribute => Info as MelonModInfoAttribute;
    public MelonModGameAttribute[] GameAttributes => Games.OfType<MelonModGameAttribute>().ToArray();

    public virtual void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
    }

    public virtual void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
    }

    public virtual void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
    }

    public virtual void OnLevelWasLoaded(int level)
    {
    }

    public virtual void OnLevelWasInitialized(int level)
    {
    }
}

public class MelonPlugin : MelonTypeBase<MelonPlugin>
{
    public override string MelonTypeName => "Plugin";
    public MelonPluginInfoAttribute? InfoAttribute => Info as MelonPluginInfoAttribute;
    public MelonPluginGameAttribute[] GameAttributes => Games.OfType<MelonPluginGameAttribute>().ToArray();

    public virtual void OnPreInitialization()
    {
    }

    public virtual void OnApplicationEarlyStart()
    {
    }

    public virtual void OnPreModsLoaded()
    {
    }

    public virtual void OnApplicationStarted()
    {
    }
}

public static class Melon<T>
    where T : MelonBase
{
    public static T? Instance => MelonHandler.GetRegistered<T>().FirstOrDefault();
    public static MelonLogger.Instance Logger => Instance?.LoggerInstance ?? new MelonLogger.Instance(typeof(T).Name);
}
