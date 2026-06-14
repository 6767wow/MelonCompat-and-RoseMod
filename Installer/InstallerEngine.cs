using System.Reflection;
using System.Diagnostics;

namespace MelonCompatInstaller;

internal static class InstallerEngine
{
    private const string ModsFolderName = "MelonLoaderMods";

    public static async Task InstallAsync(GameInfo game, InstallerOptions options, IProgress<string>? progress = null)
    {
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
        var pluginDirectory = Path.Combine(game.RootDirectory, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var backendFolder = backend == UnityBackend.Il2Cpp ? "il2cpp" : "mono";
        ExtractResource($"Payload/{backendFolder}/MelonLoader.dll", Path.Combine(pluginDirectory, "MelonLoader.dll"), overwrite);
        progress?.Report("Installed MelonLoader.dll shim.");

        ExtractResource("Payload/common/Mono.Cecil.dll", Path.Combine(pluginDirectory, "Mono.Cecil.dll"), overwrite);
        progress?.Report("Installed Mono.Cecil.dll dependency.");
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
}
