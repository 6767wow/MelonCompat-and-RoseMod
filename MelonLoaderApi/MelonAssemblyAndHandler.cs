using System.Collections.ObjectModel;
using System.Reflection;

namespace MelonLoader;

public sealed class MelonAssembly
{
    private static readonly List<MelonAssembly> Assemblies = new();

    private MelonAssembly(Assembly assembly, string location)
    {
        Assembly = assembly;
        Location = location;
        Hash = MelonUtils.ComputeSimpleSHA256Hash(location);
    }

    public static readonly MelonEvent<Assembly> OnAssemblyResolving = new();

    public readonly MelonEvent OnUnregister = new();

    public static ReadOnlyCollection<MelonAssembly> LoadedAssemblies => Assemblies.AsReadOnly();
    public Assembly Assembly { get; }
    public string Location { get; }
    public string Hash { get; }
    public bool HarmonyDontPatchAll { get; set; }
    public ReadOnlyCollection<MelonBase> LoadedMelons => MelonHandler.AllMelons.Where(m => m.Assembly == Assembly).ToList().AsReadOnly();
    public ReadOnlyCollection<RottenMelon> RottenMelons => Array.Empty<RottenMelon>().ToList().AsReadOnly();

    public static MelonAssembly LoadMelonAssembly(string path, bool loadMelons = true)
    {
        var assembly = Assembly.LoadFrom(path);
        var melonAssembly = GetOrCreate(assembly, path);
        if (loadMelons)
            BepInExCompat.MelonAssemblyLoader.LoadFromAssembly(assembly, path);
        return melonAssembly;
    }

    public static MelonAssembly LoadMelonAssembly(string path, Assembly assembly, bool loadMelons = true)
    {
        var melonAssembly = GetOrCreate(assembly, path);
        if (loadMelons)
            BepInExCompat.MelonAssemblyLoader.LoadFromAssembly(assembly, path);
        return melonAssembly;
    }

    public static MelonAssembly LoadRawMelonAssembly(string path, byte[] data, byte[]? symbols = null, bool loadMelons = true)
    {
        var assembly = Assembly.Load(data, symbols);
        var melonAssembly = GetOrCreate(assembly, path);
        if (loadMelons)
            BepInExCompat.MelonAssemblyLoader.LoadFromAssembly(assembly, path);
        return melonAssembly;
    }

    public void LoadMelons() => BepInExCompat.MelonAssemblyLoader.LoadFromAssembly(Assembly, Location);

    public void UnregisterMelons(string reason = "", bool silent = false)
    {
        foreach (var melon in LoadedMelons.ToArray())
            melon.Unregister(reason, silent);
        OnUnregister.Invoke();
    }

    public static T? FindMelonInstance<T>()
        where T : MelonBase
    {
        return MelonHandler.GetRegistered<T>().FirstOrDefault();
    }

    public static MelonAssembly? GetMelonAssemblyOfMember(MemberInfo member, object? instance = null)
    {
        return Assemblies.FirstOrDefault(assembly => assembly.Assembly == member.Module.Assembly);
    }

    internal static MelonAssembly GetOrCreate(Assembly assembly, string location)
    {
        var existing = Assemblies.FirstOrDefault(candidate => candidate.Assembly == assembly);
        if (existing is not null)
            return existing;

        var melonAssembly = new MelonAssembly(assembly, location);
        Assemblies.Add(melonAssembly);
        return melonAssembly;
    }
}

public static class MelonHandler
{
    private static readonly List<MelonMod> mods = new();
    private static readonly List<MelonPlugin> plugins = new();

    public static string ModsDirectory { get; internal set; } = string.Empty;
    public static string PluginsDirectory { get; internal set; } = string.Empty;
    public static List<MelonMod> Mods => mods;
    public static List<MelonPlugin> Plugins => plugins;
    internal static IEnumerable<MelonBase> AllMelons => mods.Cast<MelonBase>().Concat(plugins);

    public static void LoadFromFile(string path, bool loadMelons = true)
    {
        if (loadMelons)
            BepInExCompat.MelonAssemblyLoader.LoadFromFile(path);
        else
            Assembly.LoadFrom(path);
    }

    public static void LoadFromFile(string path, string _)
    {
        LoadFromFile(path, true);
    }

    public static void LoadFromAssembly(Assembly assembly, string location, bool loadMelons = true)
    {
        if (loadMelons)
            BepInExCompat.MelonAssemblyLoader.LoadFromAssembly(assembly, location);
    }

    public static void LoadFromAssembly(Assembly assembly, string location)
    {
        LoadFromAssembly(assembly, location, true);
    }

    public static void LoadFromByteArray(byte[] data, string location)
    {
        LoadFromByteArray(data, Array.Empty<byte>(), location, true);
    }

    public static void LoadFromByteArray(byte[] data, string location, bool loadMelons)
    {
        LoadFromByteArray(data, Array.Empty<byte>(), location, loadMelons);
    }

    public static void LoadFromByteArray(byte[] data, byte[] symbols, string location)
    {
        LoadFromByteArray(data, symbols, location, true);
    }

    public static void LoadFromByteArray(byte[] data, byte[]? symbols, string location, bool loadMelons)
    {
        var assembly = Assembly.Load(data, symbols);
        if (loadMelons)
            BepInExCompat.MelonAssemblyLoader.LoadFromAssembly(assembly, location);
    }

    public static bool IsMelonAlreadyLoaded(string name) => AllMelons.Any(m => string.Equals(m.Info.Name, name, StringComparison.OrdinalIgnoreCase));
    public static bool IsModAlreadyLoaded(string name) => mods.Any(m => string.Equals(m.Info.Name, name, StringComparison.OrdinalIgnoreCase));
    public static bool IsPluginAlreadyLoaded(string name) => plugins.Any(m => string.Equals(m.Info.Name, name, StringComparison.OrdinalIgnoreCase));
    public static string GetMelonHash(MelonBase melon) => melon.Hash;

    internal static void AddMod(MelonMod mod)
    {
        if (!mods.Contains(mod))
            mods.Add(mod);
    }

    internal static void AddPlugin(MelonPlugin plugin)
    {
        if (!plugins.Contains(plugin))
            plugins.Add(plugin);
    }

    internal static void Remove(MelonBase melon)
    {
        if (melon is MelonMod mod)
            mods.Remove(mod);
        if (melon is MelonPlugin plugin)
            plugins.Remove(plugin);
    }

    internal static List<T> GetRegistered<T>()
        where T : MelonBase
    {
        return AllMelons.OfType<T>().ToList();
    }
}

public sealed class RottenMelon
{
    public RottenMelon(string name, string reason)
    {
        Name = name;
        Reason = reason;
    }

    public string Name { get; }
    public string Reason { get; }
}
