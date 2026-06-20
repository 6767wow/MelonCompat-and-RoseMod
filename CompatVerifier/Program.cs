using Mono.Cecil;

namespace RoseMod.CompatVerifier;

internal static class Program
{
    private static readonly string[] MelonNamespaces =
    {
        "MelonLoader",
        "MelonLoader.Logging",
        "MelonLoader.Preferences",
        "MelonLoader.Utils",
        "Harmony"
    };

    private static readonly string[] BepInExNamespaces =
    {
        "BepInEx",
        "BepInEx.Configuration",
        "BepInEx.Logging",
        "BepInEx.Unity.IL2CPP",
        "BepInEx.Unity.Mono"
    };

    private static readonly RequiredMember[] RequiredMelonMembers =
    {
        Member("MelonLoader.MelonBase", "Harmony"),
        Member("MelonLoader.MelonBase", "HarmonyInstance"),
        Member("MelonLoader.MelonBase", "harmonyInstance"),
        Member("MelonLoader.MelonBase", "FindIncompatiblities"),
        Member("MelonLoader.MelonBase", "FindIncompatiblitiesFromContext"),
        Member("MelonLoader.MelonBase", "PrintIncompatibilities"),
        Member("MelonLoader.MelonBase/Incompatibility", "Game"),
        Member("MelonLoader.MelonBase/Incompatibility", "ProcessName"),
        Member("MelonLoader.MelonBase/Incompatibility", "Platform"),
        Member("MelonLoader.MelonBase/Incompatibility", "Domain"),
        Member("MelonLoader.MelonBase/Incompatibility", "MLVersion"),
        Member("MelonLoader.MelonBase/Incompatibility", "MLBuild"),
        Member("MelonLoader.MelonBase/Incompatibility", "GameVersion"),
        Member("Harmony.HarmonyInstance", ".ctor"),
        Member("Harmony.HarmonyInstance", "Create"),
        Member("Harmony.HarmonyMethod", ".ctor"),
        Member("Harmony.HarmonyPatch", ".ctor"),
        Member("Harmony.HarmonyPrefix", ".ctor"),
        Member("Harmony.HarmonyPostfix", ".ctor"),
        Member("Harmony.HarmonyTranspiler", ".ctor")
    };

    private static readonly RequiredMember[] RequiredBepInExMembers =
    {
        Member("BepInEx.Paths", "GameRootPath"),
        Member("BepInEx.Paths", "BepInExRootPath"),
        Member("BepInEx.Paths", "PluginPath"),
        Member("BepInEx.Paths", "ConfigPath"),
        Member("BepInEx.Paths", "BepInExConfigPath"),
        Member("BepInEx.Paths", "CachePath"),
        Member("BepInEx.Paths", "ExecutablePath"),
        Member("BepInEx.Paths", "ProcessName"),
        Member("BepInEx.Paths", "SetExecutablePath"),
        Member("BepInEx.Configuration.ConfigFile", "Bind"),
        Member("BepInEx.Configuration.ConfigFile", "AddSetting"),
        Member("BepInEx.Configuration.ConfigFile", "GetSetting"),
        Member("BepInEx.Configuration.ConfigFile", "TryGetEntry"),
        Member("BepInEx.Configuration.ConfigEntry`1", "Value"),
        Member("BepInEx.Configuration.ConfigEntryBase", "BoxedValue"),
        Member("BepInEx.Configuration.ConfigDefinition", ".ctor"),
        Member("BepInEx.Configuration.ConfigDescription", "Empty"),
        Member("BepInEx.Configuration.AcceptableValueBase", "Clamp"),
        Member("BepInEx.Configuration.AcceptableValueList`1", ".ctor"),
        Member("BepInEx.Configuration.AcceptableValueRange`1", ".ctor"),
        Member("BepInEx.Logging.Logger", "CreateLogSource"),
        Member("BepInEx.Logging.ManualLogSource", "LogInfo"),
        Member("BepInEx.Logging.ManualLogSource", "Dispose")
    };

    private static readonly string[] RequiredMelonTypes =
    {
        "MelonLoader.MelonBase",
        "MelonLoader.MelonMod",
        "MelonLoader.MelonPlugin",
        "MelonLoader.MelonAssembly",
        "MelonLoader.MelonHandler",
        "MelonLoader.MelonInfoAttribute",
        "MelonLoader.MelonGameAttribute",
        "MelonLoader.MelonProcessAttribute",
        "MelonLoader.MelonPlatformAttribute",
        "MelonLoader.MelonPlatformDomainAttribute",
        "MelonLoader.MelonPreferences",
        "MelonLoader.MelonPreferences_Category",
        "MelonLoader.MelonPreferences_Entry",
        "MelonLoader.MelonPreferences_Entry`1",
        "MelonLoader.MelonLogger",
        "MelonLoader.MelonLogger/Instance",
        "MelonLoader.Utils.MelonEnvironment",
        "Harmony.HarmonyInstance",
        "Harmony.HarmonyPatch",
        "Harmony.HarmonyPrefix",
        "Harmony.HarmonyPostfix",
        "Harmony.HarmonyTranspiler"
    };

    private static readonly string[] RequiredBepInExTypes =
    {
        "BepInEx.BepInPlugin",
        "BepInEx.BepInDependency",
        "BepInEx.BepInProcess",
        "BepInEx.Paths",
        "BepInEx.Chainloader",
        "BepInEx.PluginInfo",
        "BepInEx.Configuration.ConfigFile",
        "BepInEx.Configuration.ConfigEntryBase",
        "BepInEx.Configuration.ConfigEntry`1",
        "BepInEx.Configuration.ConfigDefinition",
        "BepInEx.Configuration.ConfigDescription",
        "BepInEx.Configuration.AcceptableValueBase",
        "BepInEx.Configuration.AcceptableValueList`1",
        "BepInEx.Configuration.AcceptableValueRange`1",
        "BepInEx.Configuration.SettingChangedEventArgs",
        "BepInEx.Configuration.TomlTypeConverter",
        "BepInEx.Configuration.TypeConverter",
        "BepInEx.Logging.LogLevel",
        "BepInEx.Logging.ManualLogSource",
        "BepInEx.Logging.Logger"
    };

    public static int Main(string[] args)
    {
        var options = VerifierOptions.Parse(args);
        var failures = new List<string>();

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        VerifyApiSurface(
            "MelonLoader",
            options.ReferenceMelon,
            options.FacadeMelon,
            MelonNamespaces,
            RequiredMelonTypes,
            RequiredMelonMembers,
            options.MelonCoverage,
            failures);

        VerifyApiSurface(
            "BepInEx.Core",
            options.ReferenceBepInEx,
            options.FacadeBepInEx,
            BepInExNamespaces,
            RequiredBepInExTypes,
            RequiredBepInExMembers,
            options.BepInExCoverage,
            failures);

        foreach (var modPath in options.ModPaths)
            CheckModReferences(modPath, options, failures);

        if (failures.Count == 0)
        {
            Console.WriteLine("Compatibility verifier passed.");
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Compatibility verifier failed:");
        foreach (var failure in failures)
            Console.Error.WriteLine("  - " + failure);

        return 1;
    }

    private static void VerifyApiSurface(
        string name,
        string referencePath,
        string facadePath,
        IReadOnlyCollection<string> namespaces,
        IReadOnlyCollection<string> requiredTypes,
        IReadOnlyCollection<RequiredMember> requiredMembers,
        double minimumCoverage,
        List<string> failures)
    {
        RequireFile(referencePath, name + " reference");
        RequireFile(facadePath, name + " facade");

        using var reference = ModuleDefinition.ReadModule(referencePath);
        using var facade = ModuleDefinition.ReadModule(facadePath);

        var referenceTypes = PublicTypes(reference)
            .Where(type => namespaces.Contains(EffectiveNamespace(type), StringComparer.Ordinal))
            .ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var facadeTypes = PublicTypes(facade)
            .Where(type => namespaces.Contains(EffectiveNamespace(type), StringComparer.Ordinal))
            .ToDictionary(type => type.FullName, StringComparer.Ordinal);

        var coveredTypes = referenceTypes.Keys.Count(facadeTypes.ContainsKey);
        var coverage = referenceTypes.Count == 0 ? 100 : coveredTypes * 100.0 / referenceTypes.Count;
        Console.WriteLine($"{name}: {coveredTypes}/{referenceTypes.Count} public types covered ({coverage:0.0}%).");

        if (VerifierOptions.CurrentListMissing)
        {
            foreach (var missing in referenceTypes.Keys.Where(type => !facadeTypes.ContainsKey(type)).OrderBy(type => type, StringComparer.Ordinal))
                Console.WriteLine($"  missing type: {missing}");
        }

        if (coverage < minimumCoverage)
            failures.Add($"{name} public type coverage is {coverage:0.0}% but minimum is {minimumCoverage:0.0}%.");

        foreach (var typeName in requiredTypes)
        {
            if (!facadeTypes.ContainsKey(typeName))
                failures.Add($"{name} facade is missing required type {typeName}.");
        }

        foreach (var member in requiredMembers)
        {
            if (!facadeTypes.TryGetValue(member.TypeName, out var type))
            {
                failures.Add($"{name} facade is missing type {member.TypeName} for required member {member.MemberName}.");
                continue;
            }

            if (!HasPublicMember(type, member.MemberName))
                failures.Add($"{name} facade is missing required member {member.TypeName}.{member.MemberName}.");
        }
    }

    private static IEnumerable<TypeDefinition> PublicTypes(ModuleDefinition module)
    {
        return module.Types.SelectMany(Flatten).Where(type => type.IsPublic || type.IsNestedPublic);
    }

    private static string EffectiveNamespace(TypeDefinition type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (!string.IsNullOrWhiteSpace(current.Namespace))
                return current.Namespace;
        }

        return string.Empty;
    }

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(Flatten))
            yield return nested;
    }

    private static bool HasPublicMember(TypeDefinition type, string name)
    {
        return type.Fields.Any(field => field.IsPublic && field.Name == name)
            || type.Properties.Any(property => property.Name == name && (property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true))
            || type.Methods.Any(method => method.IsPublic && method.Name == name);
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{label} assembly was not found.", path);
    }

    private static RequiredMember Member(string typeName, string memberName) => new(typeName, memberName);

    private static void PrintUsage()
    {
        Console.WriteLine("RoseMod.CompatVerifier");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project CompatVerifier -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --reference-melon <path>      Real MelonLoader.dll to compare against.");
        Console.WriteLine("  --facade-melon <path>         Built RoseMod MelonLoader.dll facade.");
        Console.WriteLine("  --reference-bepinex <path>    Real BepInEx.Core.dll to compare against.");
        Console.WriteLine("  --facade-bepinex <path>       Built RoseMod BepInEx.Core.dll facade.");
        Console.WriteLine("  --melon-coverage <number>     Minimum public type coverage percentage.");
        Console.WriteLine("  --bepinex-coverage <number>   Minimum public type coverage percentage.");
        Console.WriteLine("  --check-mod <path>            Verify a mod DLL's facade member references.");
        Console.WriteLine("  --list-missing                Print missing public facade types.");
    }

    private static void CheckModReferences(string modPath, VerifierOptions options, List<string> failures)
    {
        RequireFile(modPath, "mod");

        var facadePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MelonLoader"] = options.FacadeMelon,
            ["BepInEx.Core"] = options.FacadeBepInEx,
            ["BepInEx.Unity.IL2CPP"] = options.FacadeBepInExUnityIl2Cpp,
            ["BepInEx.Unity.Mono"] = options.FacadeBepInExUnityMono
        };

        using var mod = ModuleDefinition.ReadModule(modPath);
        var facades = facadePaths
            .Where(pair => File.Exists(pair.Value))
            .ToDictionary(
                pair => pair.Key,
                pair => ModuleDefinition.ReadModule(pair.Value),
                StringComparer.OrdinalIgnoreCase);

        try
        {
            var missingTypes = new SortedSet<string>(StringComparer.Ordinal);
            var missingMembers = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var typeRef in mod.GetTypeReferences())
            {
                if (!TryGetFacade(typeRef.Scope, facades, out var facade))
                    continue;

                if (FindType(facade, typeRef.FullName) is null)
                    missingTypes.Add($"{AssemblyName(typeRef.Scope)}::{typeRef.FullName}");
            }

            foreach (var memberRef in mod.GetMemberReferences())
            {
                if (!TryGetFacade(memberRef.DeclaringType.Scope, facades, out var facade))
                    continue;

                var declaringType = FindType(facade, memberRef.DeclaringType.FullName);
                if (declaringType is null)
                {
                    missingTypes.Add($"{AssemblyName(memberRef.DeclaringType.Scope)}::{memberRef.DeclaringType.FullName}");
                    continue;
                }

                if (!HasCompatibleMember(declaringType, memberRef))
                    missingMembers.Add($"{AssemblyName(memberRef.DeclaringType.Scope)}::{memberRef.FullName}");
            }

            Console.WriteLine($"{Path.GetFileName(modPath)} facade reference check: {missingTypes.Count} missing type(s), {missingMembers.Count} missing member(s).");
            foreach (var missingType in missingTypes.Take(30))
                failures.Add($"{Path.GetFileName(modPath)} references missing facade type {missingType}.");
            foreach (var missingMember in missingMembers.Take(50))
                failures.Add($"{Path.GetFileName(modPath)} references missing facade member {missingMember}.");
            if (missingTypes.Count > 30)
                failures.Add($"{Path.GetFileName(modPath)} has {missingTypes.Count - 30} additional missing facade type reference(s).");
            if (missingMembers.Count > 50)
                failures.Add($"{Path.GetFileName(modPath)} has {missingMembers.Count - 50} additional missing facade member reference(s).");
        }
        finally
        {
            foreach (var facade in facades.Values)
                facade.Dispose();
        }
    }

    private static bool TryGetFacade(IMetadataScope scope, Dictionary<string, ModuleDefinition> facades, out ModuleDefinition facade)
    {
        return facades.TryGetValue(AssemblyName(scope), out facade!);
    }

    private static string AssemblyName(IMetadataScope scope)
    {
        return scope switch
        {
            AssemblyNameReference assembly => assembly.Name,
            ModuleDefinition module => module.Assembly?.Name.Name ?? module.Name,
            ModuleReference module => module.Name,
            _ => scope.Name
        };
    }

    private static TypeDefinition? FindType(ModuleDefinition module, string fullName)
    {
        return PublicAndNonPublicTypes(module).FirstOrDefault(type => type.FullName == fullName);
    }

    private static IEnumerable<TypeDefinition> PublicAndNonPublicTypes(ModuleDefinition module)
    {
        return module.Types.SelectMany(Flatten);
    }

    private static bool HasCompatibleMember(TypeDefinition declaringType, MemberReference memberRef)
    {
        return memberRef switch
        {
            MethodReference methodRef => declaringType.Methods.Any(method => MethodMatches(method, methodRef)),
            FieldReference fieldRef => declaringType.Fields.Any(field => field.Name == fieldRef.Name),
            _ => true
        };
    }

    private static bool MethodMatches(MethodDefinition candidate, MethodReference expected)
    {
        if (candidate.Name != expected.Name)
            return false;

        if (candidate.Parameters.Count != expected.Parameters.Count)
            return false;

        for (var i = 0; i < candidate.Parameters.Count; i++)
        {
            if (!SameReferenceName(candidate.Parameters[i].ParameterType, expected.Parameters[i].ParameterType))
                return false;
        }

        return true;
    }

    private static bool SameReferenceName(TypeReference left, TypeReference right)
    {
        if (left.FullName == right.FullName)
            return true;

        if (left is GenericParameter && right is GenericParameter)
            return true;

        if (left is GenericInstanceType leftGeneric && right is GenericInstanceType rightGeneric)
            return leftGeneric.ElementType.FullName == rightGeneric.ElementType.FullName
                && leftGeneric.GenericArguments.Count == rightGeneric.GenericArguments.Count;

        return false;
    }
}

internal sealed record RequiredMember(string TypeName, string MemberName);

internal sealed record VerifierOptions(
    string ReferenceMelon,
    string FacadeMelon,
    string ReferenceBepInEx,
    string FacadeBepInEx,
    string FacadeBepInExUnityIl2Cpp,
    string FacadeBepInExUnityMono,
    List<string> ModPaths,
    double MelonCoverage,
    double BepInExCoverage,
    bool ShowHelp)
{
    public static bool CurrentListMissing { get; private set; }

    public static VerifierOptions Parse(string[] args)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = FindRepositoryRoot();
        var options = new VerifierOptions(
            Path.Combine(userProfile, ".nuget", "packages", "lavagang.melonloader", "0.7.3", "lib", "net6.0", "MelonLoader.dll"),
            Path.Combine(root, "bin", "RoseMod", "MelonLoader", "Release", "netstandard2.0", "MelonLoader.dll"),
            Path.Combine(userProfile, ".nuget", "packages", "bepinex.core", "6.0.0-be.764", "lib", "netstandard2.0", "BepInEx.Core.dll"),
            Path.Combine(root, "bin", "RoseMod", "BepInEx.Core", "Release", "netstandard2.0", "BepInEx.Core.dll"),
            Path.Combine(root, "bin", "RoseMod", "BepInEx.Unity.IL2CPP", "Release", "netstandard2.0", "BepInEx.Unity.IL2CPP.dll"),
            Path.Combine(root, "bin", "RoseMod", "BepInEx.Unity.Mono", "Release", "netstandard2.0", "BepInEx.Unity.Mono.dll"),
            new List<string>(),
            45,
            55,
            false);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--reference-melon":
                    options = options with { ReferenceMelon = RequireValue(args, ref i, arg) };
                    break;
                case "--facade-melon":
                    options = options with { FacadeMelon = RequireValue(args, ref i, arg) };
                    break;
                case "--reference-bepinex":
                    options = options with { ReferenceBepInEx = RequireValue(args, ref i, arg) };
                    break;
                case "--facade-bepinex":
                    options = options with { FacadeBepInEx = RequireValue(args, ref i, arg) };
                    break;
                case "--facade-bepinex-il2cpp":
                    options = options with { FacadeBepInExUnityIl2Cpp = RequireValue(args, ref i, arg) };
                    break;
                case "--facade-bepinex-mono":
                    options = options with { FacadeBepInExUnityMono = RequireValue(args, ref i, arg) };
                    break;
                case "--check-mod":
                    options.ModPaths.Add(RequireValue(args, ref i, arg));
                    break;
                case "--list-missing":
                    CurrentListMissing = true;
                    break;
                case "--melon-coverage":
                    options = options with { MelonCoverage = double.Parse(RequireValue(args, ref i, arg)) };
                    break;
                case "--bepinex-coverage":
                    options = options with { BepInExCoverage = double.Parse(RequireValue(args, ref i, arg)) };
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options = options with { ShowHelp = true };
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + arg);
            }
        }

        return options;
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "RoseMod.MelonLoader.csproj")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent is null)
                break;

            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException(name + " requires a value.");

        index++;
        return args[index];
    }
}
