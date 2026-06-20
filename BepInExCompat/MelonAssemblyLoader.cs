using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace MelonLoader.BepInExCompat;

internal static class MelonAssemblyLoader
{
    private static readonly HashSet<string> LoadedLocations = new(StringComparer.OrdinalIgnoreCase);

    public static void LoadFromPluginsDirectory(string pluginsPath)
    {
        if (!Directory.Exists(pluginsPath))
        {
            CompatLog.Warning($"BepInEx plugin directory does not exist: {pluginsPath}");
            return;
        }

        var dlls = Directory.EnumerateFiles(pluginsPath, "*.dll", SearchOption.AllDirectories)
            .Where(ShouldConsiderDll)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CompatLog.Info($"Scanning {dlls.Length} DLL(s) in {FriendlyDirectoryName(pluginsPath)} for MelonLoader mods.");

        var loadedCount = 0;
        foreach (var dll in dlls)
        {
            try
            {
                var count = LoadFromFile(dll);
                loadedCount += count;
            }
            catch (Exception ex)
            {
                CompatLog.Error(ex, $"Failed to scan MelonLoader mod DLL: {dll}");
            }
        }

        CompatLog.Info($"MelonLoader compatibility scan complete. Loaded {loadedCount} melon(s).");
        MelonEventPump.ApplicationStart();
        MelonEventPump.ApplicationLateStart();
    }

    public static int LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!LoadedLocations.Add(fullPath))
            return 0;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(SafeLocation(a), fullPath, StringComparison.OrdinalIgnoreCase))
            ?? InteropNamespaceRewriter.LoadAssemblyWithFixups(fullPath);

        return LoadFromAssembly(assembly, fullPath);
    }

    public static int LoadFromAssembly(Assembly assembly, string location)
    {
        if (assembly == typeof(MelonMod).Assembly)
            return 0;

        var infos = PullMelonInfos(assembly);
        if (infos.Length == 0)
            return 0;

        var processAttributes = PullAttributes<MelonProcessAttribute>(assembly);
        if (!IsProcessCompatible(processAttributes))
        {
            CompatLog.Warning($"Skipping {Path.GetFileName(location)} because its MelonProcess attribute does not match {Process.GetCurrentProcess().ProcessName}.");
            return 0;
        }

        var gameAttributes = PullAttributes<MelonGameAttribute>(assembly);
        var priority = PullAttributes<MelonPriorityAttribute>(assembly).FirstOrDefault()?.Priority ?? 0;
        var optionalDependencies = PullAttributes<MelonOptionalDependenciesAttribute>(assembly).FirstOrDefault();
        var additionalCredits = PullAttributes<MelonAdditionalCreditsAttribute>(assembly).FirstOrDefault();
        var platform = PullAttributes<MelonPlatformAttribute>(assembly).FirstOrDefault();
        var domain = PullAttributes<MelonPlatformDomainAttribute>(assembly).FirstOrDefault();
        var verifyVersion = PullAttributes<VerifyLoaderVersionAttribute>(assembly).FirstOrDefault();
        var verifyBuild = PullAttributes<VerifyLoaderBuildAttribute>(assembly).FirstOrDefault();
        var melonColor = PullAttributes<MelonColorAttribute>(assembly).FirstOrDefault()?.DrawingColor;
        var authorColor = PullAttributes<MelonAuthorColorAttribute>(assembly).FirstOrDefault()?.DrawingColor;
        var id = PullAttributes<MelonIDAttribute>(assembly).FirstOrDefault()?.ID;

        if (platform is not null && !platform.IsCompatible(MelonUtils.CurrentPlatform))
        {
            CompatLog.Warning($"Skipping {Path.GetFileName(location)} because its MelonPlatform attribute does not match {MelonUtils.CurrentPlatform}.");
            return 0;
        }

        if (domain is not null && !domain.IsCompatible(MelonUtils.CurrentDomain))
        {
            CompatLog.Warning($"Skipping {Path.GetFileName(location)} because its MelonPlatformDomain attribute does not match {MelonUtils.CurrentDomain}.");
            return 0;
        }

        var count = 0;
        foreach (var info in infos)
        {
            if (info.SystemType is null)
            {
                CompatLog.Warning($"Skipping melon metadata in {assembly.GetName().Name}: SystemType was null.");
                continue;
            }

            if (!typeof(MelonBase).IsAssignableFrom(info.SystemType))
            {
                CompatLog.Warning($"Skipping {info.Name}: {info.SystemType.FullName} does not inherit MelonBase.");
                continue;
            }

            try
            {
                var melon = (MelonBase?)Activator.CreateInstance(info.SystemType);
                if (melon is null)
                {
                    CompatLog.Warning($"Skipping {info.Name}: Activator returned null.");
                    continue;
                }

                melon.AttachCompatMetadata(
                    assembly,
                    location,
                    info,
                    priority,
                    optionalDependencies,
                    additionalCredits,
                    processAttributes,
                    gameAttributes,
                    platform,
                    domain,
                    verifyVersion,
                    verifyBuild,
                    melonColor,
                    authorColor,
                    id);

                if (!melon.Register())
                {
                    CompatLog.Warning($"Skipped {info.Name} from {Path.GetFileName(location)} because melon initialization failed.");
                    continue;
                }

                AutoPatchWithHarmony(melon);
                LogLoadedMelon(melon, location);
                count++;
            }
            catch (Exception ex)
            {
                CompatLog.Error(ex, $"Failed to instantiate Melon {info.Name} from {assembly.GetName().Name}.");
            }
        }

        return count;
    }

    private static bool ShouldConsiderDll(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("MelonLoader.dll", StringComparison.OrdinalIgnoreCase))
            return false;

        var assemblyName = Path.GetFileNameWithoutExtension(path);
        return !assemblyName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Mono.Cecil", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("MonoMod", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("0Harmony", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.Equals("HarmonyX", StringComparison.OrdinalIgnoreCase);
    }

    private static MelonInfoAttribute[] PullMelonInfos(Assembly assembly)
    {
        try
        {
            var infos = assembly.GetCustomAttributes<MelonInfoAttribute>().ToArray();
            if (infos.Length > 0)
                return infos;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Failed to read MelonInfo attributes from {assembly.GetName().Name}: {ex.Message}");
        }

        return TypeFallbackInfos(assembly).ToArray();
    }

    private static IEnumerable<MelonInfoAttribute> TypeFallbackInfos(Assembly assembly)
    {
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || !typeof(MelonBase).IsAssignableFrom(type))
                continue;

            yield return new MelonInfoAttribute(type, type.Name, assembly.GetName().Version?.ToString() ?? "0.0.0", assembly.GetName().Name ?? "Unknown", null);
        }
    }

    private static T[] PullAttributes<T>(Assembly assembly)
        where T : Attribute
    {
        try
        {
            return assembly.GetCustomAttributes<T>().ToArray();
        }
        catch
        {
            return Array.Empty<T>();
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static t => t is not null).Cast<Type>().ToArray();
        }
    }

    private static bool IsProcessCompatible(MelonProcessAttribute[] processAttributes)
    {
        if (processAttributes.Length == 0)
            return true;

        var exeName = Process.GetCurrentProcess().ProcessName;
        return processAttributes.Any(attribute => attribute.Universal || attribute.IsCompatible(exeName));
    }

    private static void AutoPatchWithHarmony(MelonBase melon)
    {
        if (melon.HarmonyDontPatchAll || melon.Assembly is null)
            return;

        var patchTypes = GetHarmonyPatchTypes(melon.Assembly);
        if (patchTypes.Length == 0)
            return;

        var filters = HarmonyPatchFilters.Load(melon.Location);
        melon.LoggerInstance.Msg($"Harmony patching {patchTypes.Length} patch class(es) one class at a time.");
        var patched = 0;
        var substituted = 0;
        var skipped = 0;
        foreach (var patchType in patchTypes)
        {
#if !BEPINEX_MONO
            var targetMethods = ResolveHarmonyTargetMethods(patchType);
#endif
            if (!filters.ShouldPatch(patchType, out var skipReason))
            {
                if (SkippedHarmonyPatchSubstitutes.TryRegister(patchType, skipReason, out var substituteReason))
                {
                    substituted++;
                    melon.LoggerInstance.Msg($"Substituted Harmony patch class {patchType.FullName}: {substituteReason}");
                    continue;
                }

                skipped++;
                melon.LoggerInstance.Warning($"Skipped Harmony patch class {patchType.FullName}: {skipReason}");
                continue;
            }

            try
            {
                new PatchClassProcessor(melon.HarmonyInstance, patchType).Patch();
#if !BEPINEX_MONO
                MelonEventPumpBehaviour.InstallTargetMethodPump(targetMethods, patchType.FullName ?? patchType.Name);
#endif
                patched++;
                melon.LoggerInstance.Msg($"Harmony patched {patchType.FullName}.");
            }
            catch (Exception ex)
            {
                melon.LoggerInstance.Warning($"Harmony patch class {patchType.FullName} failed{CompatExceptionDiagnostics.Describe(ex)}", ex);
            }
        }

        melon.LoggerInstance.Msg($"Harmony patched {patched}/{patchTypes.Length} patch class(es); substituted {substituted}; skipped {skipped}.");
    }

    private static MethodBase[] ResolveHarmonyTargetMethods(Type patchType)
    {
        try
        {
            var method = HarmonyMethodExtensions.GetMergedFromType(patchType);
            if (method is null)
                return Array.Empty<MethodBase>();

            if (method.method is not null)
                return new MethodBase[] { method.method };

            if (method.declaringType is null)
                return Array.Empty<MethodBase>();

            var argumentTypes = method.argumentTypes;
            if (method.methodType is HarmonyLib.MethodType.Getter && !string.IsNullOrWhiteSpace(method.methodName))
            {
                var getter = AccessTools.PropertyGetter(method.declaringType, method.methodName);
                return getter is null ? Array.Empty<MethodBase>() : new MethodBase[] { getter };
            }

            if (method.methodType is HarmonyLib.MethodType.Setter && !string.IsNullOrWhiteSpace(method.methodName))
            {
                var setter = AccessTools.PropertySetter(method.declaringType, method.methodName);
                return setter is null ? Array.Empty<MethodBase>() : new MethodBase[] { setter };
            }

            if (method.methodType is HarmonyLib.MethodType.Constructor)
            {
                var constructor = AccessTools.Constructor(method.declaringType, argumentTypes);
                return constructor is null ? Array.Empty<MethodBase>() : new MethodBase[] { constructor };
            }

            if (!string.IsNullOrWhiteSpace(method.methodName))
            {
                var target = AccessTools.Method(method.declaringType, method.methodName, argumentTypes);
                return target is null ? Array.Empty<MethodBase>() : new MethodBase[] { target };
            }
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Could not resolve Harmony target method for {patchType.FullName}: {ex.Message}");
        }

        return Array.Empty<MethodBase>();
    }

    private static Type[] GetHarmonyPatchTypes(Assembly assembly)
    {
        return GetLoadableTypes(assembly)
            .Where(static type => HasHarmonyPatchAttribute(type) || type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Any(HasHarmonyPatchAttribute))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasHarmonyPatchAttribute(MemberInfo member)
    {
        try
        {
            return member.GetCustomAttributes(inherit: false)
                .Any(static attribute => attribute.GetType().FullName is string name
                    && (name.StartsWith("HarmonyLib.Harmony", StringComparison.Ordinal)
                        || name.StartsWith("Harmony.Harmony", StringComparison.Ordinal)));
        }
        catch
        {
            return false;
        }
    }

    private sealed class HarmonyPatchFilters
    {
        private readonly HashSet<string> only;
        private readonly HashSet<string> skip;
        private readonly bool builtInGuardsEnabled;

        private HarmonyPatchFilters(HashSet<string> only, HashSet<string> skip, bool builtInGuardsEnabled)
        {
            this.only = only;
            this.skip = skip;
            this.builtInGuardsEnabled = builtInGuardsEnabled;
        }

        public static HarmonyPatchFilters Load(string melonLocation)
        {
            var roseModRoot = FindRoseModRoot(melonLocation);
            var only = new HashSet<string>(StringComparer.Ordinal);
            var skip = new HashSet<string>(StringComparer.Ordinal);
            var builtInGuardsEnabled = !IsTruthy(FirstEnvironmentValue("ROSEMOD_DISABLE_BUILTIN_PATCH_GUARDS", "MELONCOMPAT_DISABLE_BUILTIN_PATCH_GUARDS"));

            AddEntries(only, FirstEnvironmentValue("ROSEMOD_HARMONY_ONLY", "MELONCOMPAT_HARMONY_ONLY"));
            AddEntries(skip, FirstEnvironmentValue("ROSEMOD_HARMONY_SKIP", "MELONCOMPAT_HARMONY_SKIP"));

            if (roseModRoot is not null)
            {
                AddFileEntries(only, Path.Combine(roseModRoot, "UserData", "harmony-only.txt"));
                AddFileEntries(skip, Path.Combine(roseModRoot, "UserData", "harmony-skip.txt"));

                if (File.Exists(Path.Combine(roseModRoot, "UserData", "disable-built-in-patch-guards.txt")))
                    builtInGuardsEnabled = false;
            }

            return new HarmonyPatchFilters(only, skip, builtInGuardsEnabled);
        }

        public bool ShouldPatch(Type patchType, out string reason)
        {
            var fullName = patchType.FullName ?? patchType.Name;
            if (skip.Contains(fullName) || skip.Contains(patchType.Name))
            {
                reason = "matched RoseMod user skip filter.";
                return false;
            }

            if (builtInGuardsEnabled && BuiltInHarmonyPatchGuards.ShouldSkip(patchType, out reason))
                return false;

            if (only.Count > 0 && !only.Contains(fullName) && !only.Contains(patchType.Name))
            {
                reason = "not listed in RoseMod user only filter.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static string? FindRoseModRoot(string melonLocation)
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(melonLocation));
                while (!string.IsNullOrWhiteSpace(directory))
                {
                    var directoryName = Path.GetFileName(directory);
                    if (directoryName.Equals("MelonCompat", StringComparison.OrdinalIgnoreCase)
                        || directoryName.Equals("RoseMod", StringComparison.OrdinalIgnoreCase))
                        return directory;

                    directory = Path.GetDirectoryName(directory);
                }
            }
            catch
            {
            }

            return null;
        }

        private static void AddFileEntries(HashSet<string> entries, string path)
        {
            if (!File.Exists(path))
                return;

            AddEntries(entries, string.Join(";", File.ReadAllLines(path)));
        }

        private static void AddEntries(HashSet<string> entries, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (var entry in value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = entry.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    continue;

                entries.Add(trimmed);
            }
        }

        private static bool IsTruthy(string? value)
        {
            return value is not null
                && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("on", StringComparison.OrdinalIgnoreCase));
        }

        private static string? FirstEnvironmentValue(params string[] names)
        {
            foreach (var name in names)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }
    }

    private static class BuiltInHarmonyPatchGuards
    {
        public static bool ShouldSkip(Type patchType, out string reason)
        {
#if BEPINEX_MONO
            reason = string.Empty;
            return false;
#else
            var fullName = patchType.FullName ?? patchType.Name;
            if (fullName.Equals("BaldiHelpsGranny.DropDownOptionsStartPatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: this Granny Unity 6 IL2CPP postfix crashes the original-call trampoline; difficulty fallback uses menu polling.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.EnemyAIGrannyDecisionsPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.EnemyAIGrannyFollowPlayerPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.EnemyAIGrannyNewNavPatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: this Granny Unity 6 IL2CPP AI method is unstable as a native detour; Granny activity is mirrored by a controlled substitute sweep.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.DropDownOptionsDiffOptionsPatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: this Granny Unity 6 IL2CPP postfix crashes in dropDownOptions::diffOptions; difficulty fallback uses menu polling.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.AudioSourcePlayPatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: this Granny Unity 6 IL2CPP prefix crashes in UnityEngine.AudioSource::Play; menu music can still be handled by the mod update loop.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.SoundEffectsGunShootPatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: Granny Unity 6 IL2CPP crashes when all Baldi Helps Granny native detours are active; gun handling is still covered by GunShootHandleHitPatch.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.ShootGunUpdatePatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: shootGun.Update is a hot gameplay method; gun hit handling is still covered by GunShootHandleHitPatch.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.SoundEffectsPlayerCaughtPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.SoundEffectsPlayerCaughtNightmarePatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: soundEffects catch detours can destabilize Granny Unity 6 IL2CPP; catch flow falls back to original game sound handling.";
                return true;
            }

            if (fullName.Equals("BaldiHelpsGranny.EndDayPatch", StringComparison.Ordinal)
                || fullName.Equals("BaldiHelpsGranny.StartNewDayPatch", StringComparison.Ordinal))
            {
                reason = "RoseMod built-in guard: Granny Unity 6 IL2CPP string coroutine targets crash when detoured; day-transition notifications are handled by RoseMod's coroutine-call bridge.";
                return true;
            }

            reason = string.Empty;
            return false;
#endif
        }
    }

    private static void LogLoadedMelon(MelonBase melon, string location)
    {
        var kind = melon is MelonPlugin ? "MelonPlugin" : "MelonMod";
        var info = melon.Info;
        CompatLog.Message("------------------------------------------------------------");
        CompatLog.Message($"{info.Name} v{info.Version}");
        CompatLog.Message($"by {info.Author}");
        CompatLog.Message($"Type: {kind}");
        CompatLog.Message($"Assembly: {Path.GetFileName(location)}");
        if (!string.IsNullOrWhiteSpace(info.DownloadLink))
            CompatLog.Message($"Download: {info.DownloadLink}");
        CompatLog.Message("------------------------------------------------------------");
    }

    private static string FriendlyDirectoryName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var melonCompatIndex = normalized.LastIndexOf("/MelonCompat/", StringComparison.OrdinalIgnoreCase);
        if (melonCompatIndex >= 0)
            return normalized.Substring(melonCompatIndex + 1);

        var roseModIndex = normalized.LastIndexOf("/RoseMod/", StringComparison.OrdinalIgnoreCase);
        if (roseModIndex >= 0)
            return normalized.Substring(roseModIndex + 1);

        var bepinexIndex = normalized.LastIndexOf("/BepInEx/", StringComparison.OrdinalIgnoreCase);
        if (bepinexIndex >= 0)
            return normalized.Substring(bepinexIndex + 1);

        return path;
    }

    private static string? SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch
        {
            return null;
        }
    }
}
