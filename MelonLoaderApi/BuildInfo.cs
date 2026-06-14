using Semver;

namespace MelonLoader
{
    public static class BuildInfo
    {
        public static string Name => "MelonLoader";
        public static string Description => "MelonLoader 0.5.7-0.7.3 compatibility shim for BepInEx 6";
        public static string Author => "Lava Gang compatibility facade";
        public static string Company => "BepInEx";
        public static string Version => "0.7.3";
        public static SemVersion VersionNumber => new(0, 7, 3);
    }
}

namespace MelonLoader.Properties
{
    public static class BuildInfo
    {
        public static string Name = MelonLoader.BuildInfo.Name;
        public static string Description = MelonLoader.BuildInfo.Description;
        public static string Author = MelonLoader.BuildInfo.Author;
        public static string Company = MelonLoader.BuildInfo.Company;
        public static string Version => MelonLoader.BuildInfo.Version;
        public static SemVersion VersionNumber => MelonLoader.BuildInfo.VersionNumber;
    }
}
