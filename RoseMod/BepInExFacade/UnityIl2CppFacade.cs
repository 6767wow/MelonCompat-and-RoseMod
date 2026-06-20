using BepInEx.Configuration;
using BepInEx.Logging;

namespace BepInEx.Unity.IL2CPP;

public abstract class BasePlugin
{
    protected BasePlugin()
    {
        Info = CreateInfo(GetType(), this);
        Log = new ManualLogSource(Info.Metadata?.Name ?? GetType().Name);
        Config = new ConfigFile(Path.Combine(BepInEx.Paths.ConfigPath, GetType().Name + ".cfg"), false, Info.Metadata);
    }

    public ManualLogSource Log { get; }
    public ConfigFile Config { get; }
    public global::BepInEx.PluginInfo Info { get; }
    public virtual void Load()
    {
    }

    public virtual bool Unload() => false;

    public virtual T? AddComponent<T>()
        where T : class
    {
        return null;
    }

    private static global::BepInEx.PluginInfo CreateInfo(Type type, object instance)
    {
        return new global::BepInEx.PluginInfo
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
