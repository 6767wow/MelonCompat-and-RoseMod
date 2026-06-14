using System.Runtime.Serialization;

namespace Semver;

public sealed class SemVersion : IComparable, IComparable<SemVersion>, ISerializable
{
    public SemVersion(int major, int minor, int patch)
        : this(major, minor, patch, null, null)
    {
    }

    public SemVersion(int major, int minor, int patch, string? prerelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease ?? string.Empty;
        Build = build ?? string.Empty;
    }

    public SemVersion(Version version)
        : this(version.Major, version.Minor, Math.Max(0, version.Build))
    {
    }

    private SemVersion(SerializationInfo info, StreamingContext context)
        : this(info.GetInt32(nameof(Major)), info.GetInt32(nameof(Minor)), info.GetInt32(nameof(Patch)), info.GetString(nameof(Prerelease)), info.GetString(nameof(Build)))
    {
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }
    public string Build { get; }

    public static SemVersion Parse(string version, bool strict = false)
    {
        if (!TryParse(version, out var parsed, strict))
            throw new FormatException($"Invalid semantic version: {version}");

        return parsed;
    }

    public static bool TryParse(string version, out SemVersion result, bool strict = false)
    {
        result = new SemVersion(0, 0, 0);
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var buildSplit = version.Split('+', 2);
        var prereleaseSplit = buildSplit[0].Split('-', 2);
        var parts = prereleaseSplit[0].Split('.');
        if (parts.Length < 1 || parts.Length > 3)
            return false;

        if (!int.TryParse(parts[0], out var major))
            return false;

        var minor = 0;
        var patch = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], out minor))
            return false;
        if (parts.Length > 2 && !int.TryParse(parts[2], out patch))
            return false;
        if (strict && parts.Length != 3)
            return false;

        result = new SemVersion(
            major,
            minor,
            patch,
            prereleaseSplit.Length > 1 ? prereleaseSplit[1] : null,
            buildSplit.Length > 1 ? buildSplit[1] : null);
        return true;
    }

    public SemVersion Change(int? major = null, int? minor = null, int? patch = null, string? prerelease = null, string? build = null)
    {
        return new SemVersion(major ?? Major, minor ?? Minor, patch ?? Patch, prerelease ?? Prerelease, build ?? Build);
    }

    public int CompareTo(object? obj) => obj is SemVersion other ? CompareTo(other) : 1;

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
            return 1;

        var major = Major.CompareTo(other.Major);
        if (major != 0)
            return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
            return minor;

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0)
            return patch;

        if (string.IsNullOrEmpty(Prerelease) && !string.IsNullOrEmpty(other.Prerelease))
            return 1;
        if (!string.IsNullOrEmpty(Prerelease) && string.IsNullOrEmpty(other.Prerelease))
            return -1;

        return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
    }

    public int CompareByPrecedence(SemVersion other) => CompareTo(other);

    public bool PrecedenceMatches(SemVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public static int Compare(SemVersion left, SemVersion right) => left.CompareTo(right);

    public static bool Equals(SemVersion left, SemVersion right) => left == right;

    public override bool Equals(object? obj) => obj is SemVersion other && CompareTo(other) == 0 && string.Equals(Build, other.Build, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease.ToLowerInvariant(), Build.ToLowerInvariant());

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue(nameof(Major), Major);
        info.AddValue(nameof(Minor), Minor);
        info.AddValue(nameof(Patch), Patch);
        info.AddValue(nameof(Prerelease), Prerelease);
        info.AddValue(nameof(Build), Build);
    }

    public override string ToString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrWhiteSpace(Prerelease))
            version += "-" + Prerelease;
        if (!string.IsNullOrWhiteSpace(Build))
            version += "+" + Build;
        return version;
    }

    public static implicit operator SemVersion(string version) => Parse(version, false);

    public static bool operator ==(SemVersion? left, SemVersion? right) => ReferenceEquals(left, right) || left is not null && right is not null && left.Equals(right);
    public static bool operator !=(SemVersion? left, SemVersion? right) => !(left == right);
    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;
}
