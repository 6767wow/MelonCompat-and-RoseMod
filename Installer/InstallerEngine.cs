using System.Reflection;
using System.Diagnostics;

namespace MelonCompatInstaller;

internal static class InstallerEngine
{
    private const string ModsFolderName = "MelonLoaderMods";

    public static async Task InstallAsync(GameInfo game, InstallerOptions options, IProgress<string>? progress = null)
    {
        if (options.InstallRoseMod)
        {
            await DeployRoseModPayload(game, options, progress);
            if (options.RemoveBepInEx)
            {
                RemoveBepInExInstall(game, progress);
                EnsureLegacyPatcherView(game, Path.Combine(game.RootDirectory, "RoseMod"), progress);
            }
            return;
        }

        var melonPaths = options.MelonPaths
            .Select(path => Path.GetFullPath(path.Trim('"')))
            .ToList();

        var melonLoader = MelonLoaderInstall.Detect(game.RootDirectory);
        if (melonLoader.Exists)
        {
            if (!options.RemoveMelonLoader)
                throw new InvalidOperationException("MelonLoader is installed for this game. Remove it first, or run with --remove-melonloader.");

            if (options.MigrateMelonMods)
            {
                foreach (var melonMod in StageMelonModsForMigration(melonLoader.ModDlls, progress))
                {
                    if (!melonPaths.Contains(melonMod, StringComparer.OrdinalIgnoreCase))
                        melonPaths.Add(melonMod);
                }
            }

            melonLoader.Remove(progress);
        }

        var existing = BepInExInstall.Detect(game.RootDirectory);
        if (!existing.Exists)
        {
            if (!options.InstallBepInEx)
                throw new InvalidOperationException("BepInEx is not installed for this game. Run with --install-bepinex or install BepInEx 6 first.");

            await BepInExPackageInstaller.InstallAsync(game, options.BepInExZipPath, progress);
            existing = ValidateExistingBepInEx(game);

            if (options.RunGameBeforeShim)
                await RunGameOnce(game, progress);
        }

        existing = ValidateExistingBepInEx(game);
        progress?.Report($"Detected BepInEx {existing.MajorVersion} {existing.Backend}.");

        var backend = game.Backend == UnityBackend.Unknown
            ? existing.Backend
            : game.Backend;

        DeployPayload(game, backend, options.ForcePayloadOverwrite, progress);
        CopyMelons(game, melonPaths, progress);
    }

    public static void Doctor(GameInfo game, InstallerOptions options, IProgress<string>? progress = null)
    {
        progress?.Report("Running MelonCompat diagnostics...");
        progress?.Report("Game root: " + game.RootDirectory);
        progress?.Report("Backend: " + game.Backend);
        progress?.Report("Architecture: " + game.Architecture);

        ValidateShimPayload(UnityBackend.Mono);
        ValidateShimPayload(UnityBackend.Il2Cpp);
        ValidateRoseModPayload();
        progress?.Report("Embedded installer payload contains required MelonCompat and RoseMod files.");

        var bepinex = BepInExInstall.Detect(game.RootDirectory);
        progress?.Report("BepInEx: " + (bepinex.Exists ? $"{bepinex.MajorVersion} {bepinex.Backend}" : "not installed"));
        if (bepinex.Exists)
            ValidateExistingBepInEx(game);

        var melonLoader = MelonLoaderInstall.Detect(game.RootDirectory);
        progress?.Report("MelonLoader: " + (melonLoader.Exists ? $"installed ({melonLoader.ModDlls.Count} mod DLL(s) in Mods)" : "not installed"));

        var roseModRoot = Path.Combine(game.RootDirectory, "RoseMod");
        if (Directory.Exists(roseModRoot))
        {
            RequireInstalledFile(game.RootDirectory, "winhttp.dll");
            RequireInstalledFile(roseModRoot, "Core", "RoseMod.Core.dll");
            RequireInstalledFile(roseModRoot, "Core", "MelonLoader.dll");
            RequireInstalledFile(roseModRoot, "Core", "BepInEx.Core.dll");
            progress?.Report("Installed RoseMod native bootstrap and core files are present.");

            if (game.Backend == UnityBackend.Il2Cpp && !File.Exists(Path.Combine(roseModRoot, "interop", "UnityEngine.CoreModule.dll")))
                progress?.Report("WARNING: RoseMod IL2CPP interop is missing UnityEngine.CoreModule.dll. Install/update RoseMod to generate it.");
        }

        if (options.MelonPaths.Count > 0)
        {
            foreach (var melonPath in options.MelonPaths)
            {
                var fullPath = Path.GetFullPath(melonPath.Trim('"'));
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("Selected melon DLL was not found.", fullPath);
                if (!Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Selected melon path is not a DLL: " + fullPath);
            }

            progress?.Report($"Selected melon DLLs: {options.MelonPaths.Count}");
        }

        progress?.Report("Diagnostics found no blocking installer issues.");
    }

    public static BepInExInstall ValidateExistingBepInEx(GameInfo game)
    {
        var existing = BepInExInstall.Detect(game.RootDirectory);
        if (!existing.Exists)
            throw new InvalidOperationException("BepInEx is not installed for this game. Install BepInEx 6 first, then run this installer again.");

        if (existing.MajorVersion != 6)
            throw new InvalidOperationException($"BepInEx {existing.MajorVersion} was detected. This compatibility shim requires an existing BepInEx 6 install.");

        if (existing.Backend == UnityBackend.Unknown)
            throw new InvalidOperationException("BepInEx was detected, but the backend could not be identified. The game must have BepInEx.Unity.Mono.dll or BepInEx.Unity.IL2CPP.dll in BepInEx/core.");

        if (game.Backend is not UnityBackend.Unknown && existing.Backend != game.Backend)
            throw new InvalidOperationException($"BepInEx backend is {existing.Backend}, but the Unity game backend is {game.Backend}.");

        return existing;
    }

    private static void DeployPayload(GameInfo game, UnityBackend backend, bool overwrite, IProgress<string>? progress)
    {
        ValidateShimPayload(backend);
        var pluginDirectory = Path.Combine(game.RootDirectory, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var backendFolder = backend == UnityBackend.Il2Cpp ? "il2cpp" : "mono";
        ExtractResource($"Payload/{backendFolder}/MelonLoader.dll", Path.Combine(pluginDirectory, "MelonLoader.dll"), overwrite);
        progress?.Report("Installed MelonLoader.dll shim.");

        ExtractResource("Payload/common/Mono.Cecil.dll", Path.Combine(pluginDirectory, "Mono.Cecil.dll"), overwrite);
        progress?.Report("Installed Mono.Cecil.dll dependency.");
    }

    private static async Task DeployRoseModPayload(GameInfo game, InstallerOptions options, IProgress<string>? progress)
    {
        ValidateRoseModPayload();
        var roseModRoot = Path.Combine(game.RootDirectory, "RoseMod");
        var coreDirectory = Path.Combine(roseModRoot, "Core");
        BackupExistingBootstrapFiles(game, progress);
        Directory.CreateDirectory(coreDirectory);
        Directory.CreateDirectory(Path.Combine(roseModRoot, "MelonMods"));
        Directory.CreateDirectory(Path.Combine(roseModRoot, "BepInExPlugins"));
        Directory.CreateDirectory(Path.Combine(roseModRoot, "Patchers"));
        EnsureLegacyPatcherView(game, roseModRoot, progress);
        Directory.CreateDirectory(Path.Combine(roseModRoot, "interop"));
        Directory.CreateDirectory(Path.Combine(roseModRoot, "Il2CppAssemblies"));
        Directory.CreateDirectory(Path.Combine(roseModRoot, "UserData"));
        Directory.CreateDirectory(Path.Combine(roseModRoot, "UserLibs"));
        Directory.CreateDirectory(Path.Combine(roseModRoot, "Logs"));

        var assembly = Assembly.GetExecutingAssembly();
        var resourcePrefix = "Payload/rosemod/core/";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Replace('\\', '/').StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Length == 0)
            throw new InvalidOperationException("Installer payload does not contain RoseMod runtime files.");

        foreach (var resource in resources)
        {
            var normalized = resource.Replace('\\', '/');
            var fileName = normalized[resourcePrefix.Length..];
            var destination = Path.Combine(coreDirectory, fileName);
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Installer payload is missing resource: " + resource);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = File.Create(destination);
            stream.CopyTo(output);
        }

        await InstallRoseModNativeBootstrap(game, options, progress);
        ImportExistingIl2CppInterop(game, roseModRoot, progress);
        await RoseModInteropPreparer.PrepareAsync(game, roseModRoot, coreDirectory, progress);
        progress?.Report($"Installed RoseMod runtime to {roseModRoot}.");
        progress?.Report("Installed RoseMod native C++ bootstrap. RoseMod will start with the game.");
    }

    private static void EnsureLegacyPatcherView(GameInfo game, string roseModRoot, IProgress<string>? progress)
    {
        var bepinexRoot = Path.Combine(game.RootDirectory, "BepInEx");
        var bepinexCore = Path.Combine(bepinexRoot, "core");
        if (Directory.Exists(bepinexCore))
            return;

        var legacyPatchers = Path.Combine(bepinexRoot, "patchers");
        if (Directory.Exists(legacyPatchers))
            return;

        Directory.CreateDirectory(bepinexRoot);
        var roseModPatchers = Path.Combine(roseModRoot, "Patchers");
        if (TryCreateDirectoryJunction(legacyPatchers, roseModPatchers))
        {
            progress?.Report("Created BepInEx patchers compatibility view at BepInEx\\patchers.");
            return;
        }

        Directory.CreateDirectory(legacyPatchers);
        progress?.Report("Created BepInEx patchers compatibility folder at BepInEx\\patchers.");
    }

    private static bool TryCreateDirectoryJunction(string linkPath, string targetPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    private static async Task InstallRoseModNativeBootstrap(GameInfo game, InstallerOptions options, IProgress<string>? progress)
    {
        ExtractRoseModNativePayload(game, progress);

        if (game.Backend == UnityBackend.Il2Cpp)
        {
            var zipPath = await BepInExPackageInstaller.ResolvePackageAsync(game, options.BepInExZipPath, progress, "RoseMod CoreCLR support archive");
            progress?.Report("Installing RoseMod CoreCLR support files...");

            BepInExPackageInstaller.ExtractSelectedEntries(
                zipPath,
                game.RootDirectory,
                IsRoseModDotNetSupportEntry,
                entry => entry);

            BepInExPackageInstaller.ExtractSelectedEntries(
                zipPath,
                game.RootDirectory,
                IsRoseModIl2CppSupportEntry,
                entry => Path.Combine("RoseMod", "Core", Path.GetFileName(entry)));
        }

        File.WriteAllText(Path.Combine(game.RootDirectory, "rosemod_config.ini"), BuildRoseModNativeConfig(game));
        var oldDoorstopConfig = Path.Combine(game.RootDirectory, "doorstop_config.ini");
        if (File.Exists(oldDoorstopConfig))
            File.WriteAllText(oldDoorstopConfig, BuildDisabledDoorstopConfig());
    }

    private static void ExtractRoseModNativePayload(GameInfo game, IProgress<string>? progress)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePrefix = "Payload/rosemod/native/";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Replace('\\', '/').StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Length == 0)
            throw new InvalidOperationException("Installer payload does not contain the RoseMod native bootstrap.");

        foreach (var resource in resources)
        {
            var normalized = resource.Replace('\\', '/');
            var fileName = normalized[resourcePrefix.Length..];
            var destination = Path.Combine(game.RootDirectory, fileName);
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Installer payload is missing resource: " + resource);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = File.Create(destination);
            stream.CopyTo(output);
        }

        progress?.Report("Installed RoseMod C++ winhttp bootstrap.");
    }

    private static void ImportExistingIl2CppInterop(GameInfo game, string roseModRoot, IProgress<string>? progress)
    {
        if (game.Backend != UnityBackend.Il2Cpp)
            return;

        var roseModInterop = Path.Combine(roseModRoot, "interop");
        if (File.Exists(Path.Combine(roseModInterop, "UnityEngine.CoreModule.dll")))
        {
            progress?.Report("RoseMod IL2CPP interop assemblies are already present.");
            return;
        }

        var existingInterop = Path.Combine(game.RootDirectory, "BepInEx", "interop");
        if (Directory.Exists(existingInterop))
        {
            CopyDirectory(existingInterop, roseModInterop, overwrite: true);
            progress?.Report("Imported existing generated IL2CPP interop assemblies into RoseMod/interop.");
            return;
        }

        progress?.Report("No existing IL2CPP interop assemblies were found to import; RoseMod will generate its own interop.");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (!overwrite && File.Exists(destinationPath))
                continue;

            File.Copy(sourcePath, destinationPath, overwrite);
        }
    }

    private static bool IsRoseModDotNetSupportEntry(string entry)
    {
        return entry.StartsWith("dotnet/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoseModIl2CppSupportEntry(string entry)
    {
        if (!entry.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = Path.GetFileName(entry);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        return fileName.StartsWith("Il2CppInterop.", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Cpp2IL.", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("LibCpp2IL", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("AsmResolver", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("AssetRipper.", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("MonoMod.", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Disarm.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Gee.External.Capstone.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Iced.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("SemanticVersioning.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("StableNameDotNet.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("WasmDisassembler.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRoseModNativeConfig(GameInfo game)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "# RoseMod native C++ bootstrap configuration",
            "[General]",
            "enabled = true",
            "bootstrap = native-winhttp",
            "managed_host = RoseMod\\Core\\RoseMod.Core.dll",
            "backend = " + game.Backend,
            "",
            "[Logs]",
            "native_log = RoseMod\\Logs\\RoseMod.native.log",
            "managed_log = RoseMod\\Logs\\RoseMod.log",
            ""
        });
    }

    private static string BuildDisabledDoorstopConfig()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "# RoseMod now uses its own native C++ bootstrap.",
            "# Doorstop is intentionally disabled for this RoseMod install.",
            "[General]",
            "enabled = false",
            "target_assembly =",
            ""
        });
    }

    private static void BackupExistingBootstrapFiles(GameInfo game, IProgress<string>? progress)
    {
        var candidates = new[]
        {
            Path.Combine(game.RootDirectory, "winhttp.dll"),
            Path.Combine(game.RootDirectory, "doorstop_config.ini"),
            Path.Combine(game.RootDirectory, ".doorstop_version")
        };

        if (!candidates.Any(File.Exists))
            return;

        var backupDirectory = Path.Combine(game.RootDirectory, "RoseMod", "Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupDirectory);

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            File.Copy(candidate, Path.Combine(backupDirectory, Path.GetFileName(candidate)), overwrite: true);
        }

        progress?.Report("Backed up existing Doorstop files to " + backupDirectory);
    }

    private static void RemoveBepInExInstall(GameInfo game, IProgress<string>? progress)
    {
        var bepinexDirectory = Path.Combine(game.RootDirectory, "BepInEx");
        if (!Directory.Exists(bepinexDirectory))
        {
            progress?.Report("BepInEx removal requested, but no BepInEx folder was found.");
            return;
        }

        var gameRoot = Path.GetFullPath(game.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var source = Path.GetFullPath(bepinexDirectory);
        if (!source.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to remove BepInEx because the resolved path is outside the game root.");

        var backupDirectory = Path.Combine(game.RootDirectory, "RoseMod", "Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"), "BepInEx");
        backupDirectory = UniqueDirectoryPath(backupDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(backupDirectory)!);
        try
        {
            Directory.Move(source, backupDirectory);
            progress?.Report("Moved BepInEx folder to " + backupDirectory);
        }
        catch (Exception ex) when (IsRecoverableBepInExRemovalFailure(ex))
        {
            progress?.Report($"WARNING: Could not move BepInEx folder out of the game directory: {ex.Message}");
            progress?.Report("WARNING: This usually means Steam's Program Files library denied the directory move. RoseMod install will continue.");

            var backupResult = TryCopyDirectoryLenient(source, backupDirectory);
            if (backupResult.CopiedFiles > 0)
                progress?.Report($"Backed up {backupResult.CopiedFiles} readable BepInEx file(s) to {backupDirectory}.");
            if (backupResult.SkippedFiles > 0)
                progress?.Report($"WARNING: Skipped {backupResult.SkippedFiles} BepInEx file(s) that Windows would not let the installer read.");

            TryWriteText(Path.Combine(source, "ROSEMOD_NOT_REMOVED.txt"), string.Join(Environment.NewLine, new[]
            {
                "RoseMod tried to remove this BepInEx folder but Windows denied the directory move.",
                "RoseMod still replaced the game bootstrap and disabled Doorstop, so this BepInEx folder should not load.",
                "Run the installer as administrator or delete this folder manually if you want it fully removed.",
                "Last error: " + ex.Message,
                ""
            }));

            progress?.Report("BepInEx folder was left in place, but RoseMod replaced winhttp.dll and disabled old Doorstop config.");
            progress?.Report("Run the installer as administrator if you want the old BepInEx folder moved into RoseMod/Backups.");
        }
    }

    private static bool IsRecoverableBepInExRemovalFailure(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException;
    }

    private static (int CopiedFiles, int SkippedFiles) TryCopyDirectoryLenient(string sourceDirectory, string destinationDirectory)
    {
        var copied = 0;
        var skipped = 0;

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
                    var destinationPath = Path.Combine(destinationDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                    copied++;
                }
                catch
                {
                    skipped++;
                }
            }
        }
        catch
        {
            skipped++;
        }

        return (copied, skipped);
    }

    private static bool TryWriteText(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunGameOnce(GameInfo game, IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
            throw new InvalidOperationException("Cannot run the game because the executable path was not detected.");

        progress?.Report("Launching game for the first BepInEx run. Close the game after it reaches the menu to continue installing the shim.");
        using var process = Process.Start(new ProcessStartInfo(game.ExecutablePath)
        {
            WorkingDirectory = game.RootDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Failed to launch the game executable.");

        await process.WaitForExitAsync();
        progress?.Report("Game exited. Continuing MelonCompat shim install.");
    }

    private static IReadOnlyList<string> StageMelonModsForMigration(IReadOnlyList<string> melonModPaths, IProgress<string>? progress)
    {
        if (melonModPaths.Count == 0)
            return Array.Empty<string>();

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "MelonCompatInstaller", "MigratedMelons", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);

        var stagedPaths = new List<string>();
        foreach (var melonMod in melonModPaths)
        {
            if (!File.Exists(melonMod))
                continue;

            var destination = UniquePath(stagingDirectory, Path.GetFileName(melonMod));
            File.Copy(melonMod, destination);
            stagedPaths.Add(destination);
        }

        progress?.Report($"Queued {stagedPaths.Count} MelonLoader mod DLL(s) for migration.");
        return stagedPaths;
    }

    private static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(directory, $"{name}.{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static string UniqueDirectoryPath(string candidate)
    {
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
            return candidate;

        var parent = Path.GetDirectoryName(candidate) ?? ".";
        var name = Path.GetFileName(candidate);
        for (var i = 2; ; i++)
        {
            var numbered = Path.Combine(parent, $"{name}.{i}");
            if (!Directory.Exists(numbered) && !File.Exists(numbered))
                return numbered;
        }
    }

    private static void CopyMelons(GameInfo game, IReadOnlyCollection<string> melonPaths, IProgress<string>? progress)
    {
        if (melonPaths.Count == 0)
            return;

        var melonDirectory = Path.Combine(game.RootDirectory, "BepInEx", "plugins", ModsFolderName);
        Directory.CreateDirectory(melonDirectory);

        foreach (var melonPath in melonPaths)
        {
            var fullPath = Path.GetFullPath(melonPath.Trim('"'));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Melon mod DLL was not found.", fullPath);

            if (!Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Melon mod path is not a DLL: " + fullPath);

            var destination = Path.Combine(melonDirectory, Path.GetFileName(fullPath));
            File.Copy(fullPath, destination, overwrite: true);
            progress?.Report("Copied melon mod: " + Path.GetFileName(fullPath));
        }
    }

    private static void ExtractResource(string resourceName, string destination, bool overwrite)
    {
        if (File.Exists(destination) && !overwrite)
            throw new IOException($"Refusing to overwrite {destination}. Enable payload overwrite to replace it.");

        var assembly = Assembly.GetExecutingAssembly();
        var actualResourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.Replace('\\', '/').Equals(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = actualResourceName is null ? null : assembly.GetManifestResourceStream(actualResourceName);
        if (stream is null)
            throw new InvalidOperationException("Installer payload is missing resource: " + resourceName);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var output = File.Create(destination);
        stream.CopyTo(output);
    }

    private static void ValidateShimPayload(UnityBackend backend)
    {
        var backendFolder = backend == UnityBackend.Il2Cpp ? "il2cpp" : "mono";
        RequireEmbeddedResource($"Payload/{backendFolder}/MelonLoader.dll");
        RequireEmbeddedResource("Payload/common/Mono.Cecil.dll");
    }

    private static void ValidateRoseModPayload()
    {
        foreach (var resource in new[]
        {
            "Payload/rosemod/core/RoseMod.Core.dll",
            "Payload/rosemod/core/MelonLoader.dll",
            "Payload/rosemod/core/BepInEx.Core.dll",
            "Payload/rosemod/core/BepInEx.Unity.IL2CPP.dll",
            "Payload/rosemod/core/BepInEx.Unity.Mono.dll",
            "Payload/rosemod/native/winhttp.dll",
            "Payload/rosemod/core/0Harmony.dll",
            "Payload/rosemod/core/Mono.Cecil.dll",
            "Payload/rosemod/core/SemanticVersioning.dll",
            "Payload/rosemod/core/Il2CppInterop.Runtime.dll",
            "Payload/rosemod/core/Cpp2IL.Core.dll",
            "Payload/rosemod/core/Il2CppInterop.Generator.dll",
            "Payload/rosemod/core/LibCpp2IL.dll"
        })
        {
            RequireEmbeddedResource(resource);
        }
    }

    private static void RequireEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var exists = assembly.GetManifestResourceNames()
            .Any(name => name.Replace('\\', '/').Equals(resourceName, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            throw new InvalidOperationException("Installer payload is missing resource: " + resourceName);
    }

    private static void RequireInstalledFile(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        if (!File.Exists(path))
            throw new FileNotFoundException("Installed RoseMod file is missing.", path);
    }
}
