using MelonLoader.Logging;
using Semver;

namespace MelonLoader;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class MelonInfoAttribute : Attribute
{
    public MelonInfoAttribute(Type systemType, string name, string version, string author, string? downloadLink = null)
    {
        SystemType = systemType;
        Name = name;
        Version = version;
        SemanticVersion = SemVersion.TryParse(version, out var semver, false) ? semver : new SemVersion(0, 0, 0);
        Author = author;
        DownloadLink = downloadLink ?? string.Empty;
    }

    public MelonInfoAttribute(Type systemType, string name, int major, int minor, int patch, string author, string downloadLink, string prerelease)
        : this(systemType, name, new SemVersion(major, minor, patch, prerelease, null).ToString(), author, downloadLink)
    {
    }

    public MelonInfoAttribute(Type systemType, string name, int major, int minor, int patch, string author, string downloadLink)
        : this(systemType, name, $"{major}.{minor}.{patch}", author, downloadLink)
    {
    }

    public Type SystemType { get; }
    public string Name { get; }
    public string Version { get; }
    public SemVersion SemanticVersion { get; }
    public string Author { get; }
    public string DownloadLink { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MelonModInfoAttribute : MelonInfoAttribute
{
    public MelonModInfoAttribute(Type systemType, string name, string version, string author, string? downloadLink = null)
        : base(systemType, name, version, author, downloadLink)
    {
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MelonPluginInfoAttribute : MelonInfoAttribute
{
    public MelonPluginInfoAttribute(Type systemType, string name, string version, string author, string? downloadLink = null)
        : base(systemType, name, version, author, downloadLink)
    {
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class MelonGameAttribute : Attribute
{
    public MelonGameAttribute(string developer, string name)
    {
        Developer = developer ?? string.Empty;
        Name = name ?? string.Empty;
        Universal = IsUniversalValue(Developer) || IsUniversalValue(Name);
    }

    public string Developer { get; }
    public string Name { get; }
    public bool Universal { get; }

    public bool IsCompatible(string developer, string name)
    {
        return Universal
            || (string.Equals(Developer, developer, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsCompatible(MelonGameAttribute attribute) => attribute.Universal || IsCompatible(attribute.Developer, attribute.Name);

    public bool IsCompatible(MelonModGameAttribute attribute) => IsCompatible(attribute.Developer, attribute.GameName);

    public bool IsCompatible(MelonPluginGameAttribute attribute) => IsCompatible(attribute.Developer, attribute.GameName);

    public bool IsCompatibleBecauseUniversal(MelonGameAttribute attribute) => Universal || attribute.Universal;

    public bool IsCompatibleBecauseUniversal(MelonModGameAttribute attribute) => Universal || IsUniversalValue(attribute.GameName);

    public bool IsCompatibleBecauseUniversal(MelonPluginGameAttribute attribute) => Universal || IsUniversalValue(attribute.GameName);

    private static bool IsUniversalValue(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Equals("Universal", StringComparison.OrdinalIgnoreCase)
            || value.Equals("*", StringComparison.OrdinalIgnoreCase);
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MelonModGameAttribute : MelonGameAttribute
{
    public MelonModGameAttribute(string developer, string gameName)
        : base(developer, gameName)
    {
        GameName = gameName;
    }

    public string GameName { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MelonPluginGameAttribute : MelonGameAttribute
{
    public MelonPluginGameAttribute(string developer, string gameName)
        : base(developer, gameName)
    {
        GameName = gameName;
    }

    public string GameName { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MelonGameVersionAttribute : Attribute
{
    public MelonGameVersionAttribute(string version)
    {
        Version = version ?? string.Empty;
        Universal = string.IsNullOrWhiteSpace(Version) || Version == "*";
    }

    public string Version { get; }
    public bool Universal { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MelonProcessAttribute : Attribute
{
    public MelonProcessAttribute(string exeName)
    {
        EXE_Name = exeName ?? string.Empty;
        Universal = string.IsNullOrWhiteSpace(EXE_Name) || EXE_Name == "*";
    }

    public string EXE_Name { get; }
    public bool Universal { get; }

    public bool IsCompatible(string exeName)
    {
        var expected = Path.GetFileNameWithoutExtension(EXE_Name);
        var actual = Path.GetFileNameWithoutExtension(exeName);
        return Universal || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonPriorityAttribute : Attribute
{
    public MelonPriorityAttribute(int priority)
    {
        Priority = priority;
    }

    public int Priority;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonOptionalDependenciesAttribute : Attribute
{
    public MelonOptionalDependenciesAttribute(params string[] assemblyNames)
    {
        AssemblyNames = assemblyNames ?? Array.Empty<string>();
    }

    public string[] AssemblyNames { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonAdditionalDependenciesAttribute : Attribute
{
    public MelonAdditionalDependenciesAttribute(params string[] assemblyNames)
    {
        AssemblyNames = assemblyNames ?? Array.Empty<string>();
    }

    public string[] AssemblyNames { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonIncompatibleAssembliesAttribute : Attribute
{
    public MelonIncompatibleAssembliesAttribute(params string[] assemblyNames)
    {
        AssemblyNames = assemblyNames ?? Array.Empty<string>();
    }

    public string[] AssemblyNames { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonAdditionalCreditsAttribute : Attribute
{
    public MelonAdditionalCreditsAttribute(string credits)
    {
        Credits = credits ?? string.Empty;
    }

    public string Credits { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonIDAttribute : Attribute
{
    public MelonIDAttribute(string id)
    {
        ID = id ?? string.Empty;
    }

    public MelonIDAttribute(int id)
        : this(id.ToString())
    {
    }

    public string ID { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class MelonColorAttribute : Attribute
{
    public MelonColorAttribute()
        : this(ConsoleColor.Magenta)
    {
    }

    public MelonColorAttribute(ConsoleColor color)
    {
        Color = color;
        DrawingColor = ColorARGB.FromConsoleColor(color);
    }

    public MelonColorAttribute(int alpha, int red, int green, int blue)
    {
        DrawingColor = ColorARGB.FromArgb((byte)alpha, (byte)red, (byte)green, (byte)blue);
        Color = ConsoleColor.White;
    }

    public ConsoleColor Color { get; }
    public ColorARGB DrawingColor { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonAuthorColorAttribute : MelonColorAttribute
{
    public MelonAuthorColorAttribute()
    {
    }

    public MelonAuthorColorAttribute(ConsoleColor color)
        : base(color)
    {
    }

    public MelonAuthorColorAttribute(int alpha, int red, int green, int blue)
        : base(alpha, red, green, blue)
    {
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonPlatformAttribute : Attribute
{
    public MelonPlatformAttribute(params CompatiblePlatforms[] platforms)
    {
        Platforms = platforms is { Length: > 0 } ? platforms : new[] { CompatiblePlatforms.UNIVERSAL };
    }

    public enum CompatiblePlatforms
    {
        UNIVERSAL,
        WINDOWS_X86,
        WINDOWS_X64,
        LINUX,
        MAC,
        ANDROID
    }

    public CompatiblePlatforms[] Platforms { get; }

    public bool IsCompatible(CompatiblePlatforms platform)
    {
        return Platforms.Contains(CompatiblePlatforms.UNIVERSAL) || Platforms.Contains(platform);
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MelonPlatformDomainAttribute : Attribute
{
    public MelonPlatformDomainAttribute(CompatibleDomains domain)
    {
        Domain = domain;
    }

    public enum CompatibleDomains
    {
        UNIVERSAL,
        MONO,
        IL2CPP
    }

    public CompatibleDomains Domain { get; }

    public bool IsCompatible(CompatibleDomains domain) => Domain == CompatibleDomains.UNIVERSAL || Domain == domain;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class VerifyLoaderBuildAttribute : Attribute
{
    public VerifyLoaderBuildAttribute(string hashCode)
    {
        HashCode = hashCode ?? string.Empty;
    }

    public string HashCode { get; }

    public bool IsCompatible(string hashCode) => string.IsNullOrWhiteSpace(HashCode) || string.Equals(HashCode, hashCode, StringComparison.OrdinalIgnoreCase);
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class VerifyLoaderVersionAttribute : Attribute
{
    public VerifyLoaderVersionAttribute(string version)
        : this(version, false)
    {
    }

    public VerifyLoaderVersionAttribute(string version, bool isMinimum)
        : this(SemVersion.TryParse(version, out var parsed, false) ? parsed : new SemVersion(0, 0, 0), isMinimum)
    {
    }

    public VerifyLoaderVersionAttribute(int major, int minor, int patch)
        : this(new SemVersion(major, minor, patch), false)
    {
    }

    public VerifyLoaderVersionAttribute(int major, int minor, int patch, bool isMinimum)
        : this(new SemVersion(major, minor, patch), isMinimum)
    {
    }

    public VerifyLoaderVersionAttribute(int major, int minor, int patch, string prerelease, bool isMinimum)
        : this(new SemVersion(major, minor, patch, prerelease, null), isMinimum)
    {
    }

    public VerifyLoaderVersionAttribute(SemVersion semVer, bool isMinimum)
    {
        SemVer = semVer;
        Major = semVer.Major;
        Minor = semVer.Minor;
        Patch = semVer.Patch;
        Prerelease = semVer.Prerelease;
        IsMinimum = isMinimum;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }
    public SemVersion SemVer { get; }
    public bool IsMinimum { get; }

    public bool IsCompatible(string version) => IsCompatible(SemVersion.Parse(version, false));

    public bool IsCompatible(SemVersion version) => IsMinimum ? version >= SemVer : version == SemVer;
}
