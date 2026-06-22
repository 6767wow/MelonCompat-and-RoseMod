namespace MelonCompatInstaller;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = InstallerOptions.Parse(args);
            if (options.ShowHelp || args.Length == 0)
            {
                PrintUsage();
                if (args.Length == 0 && Environment.UserInteractive && !Console.IsInputRedirected)
                {
                    Console.WriteLine();
                    Console.WriteLine("This is the CLI backend. For the graphical installer, run:");
                    Console.WriteLine("  MelonCompat Installer.exe");
                    Console.WriteLine();
                    Console.Write("Press Enter to close...");
                    Console.ReadLine();
                }
                return 0;
            }

            if (string.IsNullOrWhiteSpace(options.GamePath))
                options = options with { GamePath = Prompt("Game .exe or game folder") };

            if (options.MelonPaths.Count == 0 && !options.NonInteractive && !options.InstallRoseMod)
            {
                var melonInput = Prompt("MelonLoader mod DLL(s), separated by semicolon, or blank to skip");
                foreach (var path in SplitPathList(melonInput))
                    options.MelonPaths.Add(path);
            }

            var game = UnityGameDetector.Detect(options.GamePath!, options.Backend);
            var backend = game.Backend == UnityBackend.Unknown
                ? throw new InvalidOperationException("Could not detect Unity backend. Pass --backend mono or --backend il2cpp.")
                : game.Backend;

            PrintPlan(game, options);
            if (options.Doctor)
            {
                InstallerEngine.Doctor(game, options, new ConsoleProgress());
                Console.WriteLine();
                Console.WriteLine("Diagnostics complete.");
                return 0;
            }

            if (!options.AssumeYes && !Confirm("Install with this plan?"))
                return 2;

            if (!options.DryRun)
            {
                await InstallerEngine.InstallAsync(game, options, new ConsoleProgress());
            }

            Console.WriteLine();
            Console.WriteLine(options.DryRun ? "Dry run complete. No files were changed." : "Install complete.");
            if (options.InstallRoseMod)
            {
                Console.WriteLine("RoseMod path: " + Path.Combine(game.RootDirectory, "RoseMod"));
                if (game.Backend == UnityBackend.Il2Cpp)
                    Console.WriteLine("IL2CPP interop path: " + Path.Combine(game.RootDirectory, "RoseMod", "interop"));
            }
            else
            {
                Console.WriteLine("Start the game once so BepInEx can generate config and interop files.");
                Console.WriteLine("Log path: " + Path.Combine(game.RootDirectory, "BepInEx", "LogOutput.log"));
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Install failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintPlan(GameInfo game, InstallerOptions options)
    {
        var bepinex = BepInExInstall.Detect(game.RootDirectory);
        var melonLoader = MelonLoaderInstall.Detect(game.RootDirectory);
        var roseModMelons = FindRoseModMelons(game.RootDirectory);
        Console.WriteLine("Detected Unity game:");
        Console.WriteLine("  Root:    " + game.RootDirectory);
        Console.WriteLine("  Exe:     " + (game.ExecutablePath ?? "(not found)"));
        Console.WriteLine("  Data:    " + game.DataDirectory);
        Console.WriteLine("  Backend: " + game.Backend);
        Console.WriteLine("  Arch:    " + game.Architecture);
        Console.WriteLine("  BepInEx: " + (bepinex.Exists ? $"v{bepinex.MajorVersion} {bepinex.Backend}" : "not installed"));
        Console.WriteLine("  MelonLoader: " + (melonLoader.Exists ? $"installed ({melonLoader.ModDlls.Count} mod DLLs found)" : "not installed"));
        Console.WriteLine("  RoseMod: " + (Directory.Exists(Path.Combine(game.RootDirectory, "RoseMod")) ? "installed" : "not installed"));
        Console.WriteLine("  RoseMod install: " + (options.InstallRoseMod ? "yes" : "no"));
        Console.WriteLine("  BepInEx install: " + BepInExPlanText(game, options, bepinex));
        Console.WriteLine("  BepInEx removal: " + (options.RemoveBepInEx ? "yes" : "no"));
        Console.WriteLine("  First run: " + (options.InstallBepInEx && options.RunGameBeforeShim ? "will launch game before continuing" : "no"));
        Console.WriteLine("  Melons to install: " + (options.MelonPaths.Count == 0 ? "(none)" : string.Join(", ", options.MelonPaths)));
        Console.WriteLine("  Installed RoseMod melons: " + (roseModMelons.Length == 0 ? "(none)" : string.Join(", ", roseModMelons.Select(Path.GetFileName))));
        Console.WriteLine();
    }

    private static string[] FindRoseModMelons(string gameRoot)
    {
        var melonDirectory = Path.Combine(gameRoot, "RoseMod", "MelonMods");
        return Directory.Exists(melonDirectory)
            ? Directory.EnumerateFiles(melonDirectory, "*.dll", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
    }

    private static string BepInExPlanText(GameInfo game, InstallerOptions options, BepInExInstall bepinex)
    {
        if (options.InstallRoseMod && game.Backend == UnityBackend.Il2Cpp && options.InstallBepInEx)
            return "no (RoseMod installs as a standalone loader)";

        if (!options.InstallRoseMod && !bepinex.Exists && options.InstallBepInEx)
            return "will install BepInEx 6";

        return "no";
    }

    private static string Prompt(string label)
    {
        Console.Write(label + ": ");
        return Console.ReadLine()?.Trim().Trim('"') ?? string.Empty;
    }

    private static bool Confirm(string question)
    {
        Console.Write(question + " [y/N]: ");
        var answer = Console.ReadLine();
        return answer is not null && answer.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitPathList(string value)
    {
        return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MelonCompatInstaller");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  MelonCompatInstaller.exe --game <game.exe|game folder> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --melon <dll>              Copy a MelonLoader mod DLL into BepInEx/plugins/MelonLoaderMods. Can repeat.");
        Console.WriteLine("  --backend <auto|mono|il2cpp>");
        Console.WriteLine("  --install-rosemod         Install the optional standalone RoseMod runtime layout.");
        Console.WriteLine("  --install-bepinex          Download and install BepInEx 6 if it is missing for the MelonCompat shim.");
        Console.WriteLine("  --bepinex-zip <zip>        Install BepInEx 6 from a local zip, or use the archive as RoseMod's support source.");
        Console.WriteLine("  --run-game-before-shim     Launch the game once after installing BepInEx, then install the shim after the game exits.");
        Console.WriteLine("  --remove-bepinex           With --install-rosemod, move an existing BepInEx folder into RoseMod/Backups after install.");
        Console.WriteLine("  --remove-melonloader       Remove existing MelonLoader files before installing BepInEx/shim.");
        Console.WriteLine("  --migrate-melon-mods       Copy DLLs from the MelonLoader Mods folder into BepInEx/plugins/MelonLoaderMods.");
        Console.WriteLine("  --force-payload            Replace existing MelonLoader.dll and Mono.Cecil.dll in BepInEx/plugins.");
        Console.WriteLine("  --doctor                   Validate the selected game and embedded payload without installing.");
        Console.WriteLine("  --yes                      Do not ask for confirmation.");
        Console.WriteLine("  --dry-run                  Detect and print the plan without writing files.");
        Console.WriteLine("  --help");
    }
}

internal sealed class ConsoleProgress : IProgress<string>
{
    public void Report(string value)
    {
        Console.WriteLine(value);
    }
}

internal sealed record InstallerOptions(
    string? GamePath,
    UnityBackend Backend,
    List<string> MelonPaths,
    string? BepInExZipPath,
    bool InstallRoseMod,
    bool InstallBepInEx,
    bool RunGameBeforeShim,
    bool RemoveBepInEx,
    bool RemoveMelonLoader,
    bool MigrateMelonMods,
    bool ForcePayloadOverwrite,
    bool Doctor,
    bool AssumeYes,
    bool DryRun,
    bool ShowHelp,
    bool NonInteractive)
{
    public static InstallerOptions Parse(string[] args)
    {
        var options = new InstallerOptions(null, UnityBackend.Unknown, new List<string>(), null, false, false, false, false, false, false, false, false, false, false, false, false);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--game":
                case "-g":
                    options = options with { GamePath = RequireValue(args, ref i, arg) };
                    break;
                case "--melon":
                case "-m":
                    options.MelonPaths.Add(RequireValue(args, ref i, arg));
                    break;
                case "--backend":
                    options = options with { Backend = ParseBackend(RequireValue(args, ref i, arg)) };
                    break;
                case "--install-rosemod":
                    options = options with { InstallRoseMod = true };
                    break;
                case "--bepinex-zip":
                    options = options with { BepInExZipPath = RequireValue(args, ref i, arg), InstallBepInEx = true };
                    break;
                case "--install-bepinex":
                case "--force-bepinex":
                    options = options with { InstallBepInEx = true };
                    break;
                case "--run-game-before-shim":
                case "--run-game":
                    options = options with { RunGameBeforeShim = true };
                    break;
                case "--remove-bepinex":
                    options = options with { RemoveBepInEx = true };
                    break;
                case "--remove-melonloader":
                    options = options with { RemoveMelonLoader = true };
                    break;
                case "--migrate-melon-mods":
                    options = options with { MigrateMelonMods = true };
                    break;
                case "--no-download":
                    throw new ArgumentException(arg + " is no longer supported. Use --bepinex-zip for offline BepInEx installs.");
                case "--force-payload":
                    options = options with { ForcePayloadOverwrite = true };
                    break;
                case "--doctor":
                case "--diagnose":
                    options = options with { Doctor = true, NonInteractive = true };
                    break;
                case "--yes":
                case "-y":
                    options = options with { AssumeYes = true, NonInteractive = true };
                    break;
                case "--dry-run":
                    options = options with { DryRun = true };
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options = options with { ShowHelp = true };
                    break;
                default:
                    if (string.IsNullOrWhiteSpace(options.GamePath) && !arg.StartsWith("-", StringComparison.Ordinal))
                        options = options with { GamePath = arg };
                    else
                        throw new ArgumentException("Unknown argument: " + arg);
                    break;
            }
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException(name + " requires a value.");

        index++;
        return args[index];
    }

    private static UnityBackend ParseBackend(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "auto" => UnityBackend.Unknown,
            "mono" => UnityBackend.Mono,
            "il2cpp" => UnityBackend.Il2Cpp,
            "il2cppcoreclr" => UnityBackend.Il2Cpp,
            _ => throw new ArgumentException("Backend must be auto, mono, or il2cpp.")
        };
    }
}

internal enum UnityBackend
{
    Unknown,
    Mono,
    Il2Cpp
}

internal enum ProcessArchitecture
{
    Unknown,
    X86,
    X64
}

internal sealed record GameInfo(
    string RootDirectory,
    string? ExecutablePath,
    string DataDirectory,
    UnityBackend Backend,
    ProcessArchitecture Architecture);

internal static class UnityGameDetector
{
    public static GameInfo Detect(string inputPath, UnityBackend backendOverride)
    {
        var normalized = Path.GetFullPath(inputPath.Trim('"'));
        var root = Directory.Exists(normalized)
            ? normalized
            : Path.GetDirectoryName(normalized) ?? throw new InvalidOperationException("Invalid game path.");

        if (Path.GetFileName(root).EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            root = Path.GetDirectoryName(root) ?? root;

        var exe = File.Exists(normalized) && Path.GetExtension(normalized).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : FindGameExecutable(root);

        var data = exe is null
            ? FindDataDirectory(root)
            : Path.Combine(root, Path.GetFileNameWithoutExtension(exe) + "_Data");

        if (!Directory.Exists(data))
            data = FindDataDirectory(root);

        if (!Directory.Exists(data))
            throw new InvalidOperationException("Could not find Unity *_Data folder beside the game executable.");

        var detectedBackend = DetectBackend(root, data);
        var backend = backendOverride == UnityBackend.Unknown ? detectedBackend : backendOverride;
        var architecture = exe is null ? ProcessArchitecture.X64 : PortableExecutable.ReadArchitecture(exe);
        if (architecture == ProcessArchitecture.Unknown)
            architecture = Environment.Is64BitOperatingSystem ? ProcessArchitecture.X64 : ProcessArchitecture.X86;

        return new GameInfo(root, exe, data, backend, architecture);
    }

    private static string? FindGameExecutable(string root)
    {
        return Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(path => Directory.Exists(Path.Combine(root, Path.GetFileNameWithoutExtension(path) + "_Data")))
            .OrderBy(path => Path.GetFileName(path).Length)
            .FirstOrDefault();
    }

    private static string FindDataDirectory(string root)
    {
        return Directory.EnumerateDirectories(root, "*_Data", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path).Length)
            .FirstOrDefault()
            ?? string.Empty;
    }

    private static UnityBackend DetectBackend(string root, string data)
    {
        if (File.Exists(Path.Combine(root, "GameAssembly.dll"))
            || Directory.Exists(Path.Combine(data, "il2cpp_data"))
            || File.Exists(Path.Combine(data, "il2cpp_data", "Metadata", "global-metadata.dat")))
            return UnityBackend.Il2Cpp;

        if (Directory.Exists(Path.Combine(data, "Managed")))
            return UnityBackend.Mono;

        return UnityBackend.Unknown;
    }
}

internal static class PortableExecutable
{
    public static ProcessArchitecture ReadArchitecture(string exePath)
    {
        try
        {
            using var stream = File.OpenRead(exePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D)
                return ProcessArchitecture.Unknown;

            stream.Position = 0x3C;
            var peHeaderOffset = reader.ReadInt32();
            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
                return ProcessArchitecture.Unknown;

            var machine = reader.ReadUInt16();
            return machine switch
            {
                0x014c => ProcessArchitecture.X86,
                0x8664 => ProcessArchitecture.X64,
                _ => ProcessArchitecture.Unknown
            };
        }
        catch
        {
            return ProcessArchitecture.Unknown;
        }
    }
}

internal sealed record BepInExInstall(bool Exists, int MajorVersion, UnityBackend Backend)
{
    public static BepInExInstall Detect(string gameRoot)
    {
        var core = Path.Combine(gameRoot, "BepInEx", "core");
        if (!Directory.Exists(core))
            return new BepInExInstall(false, 0, UnityBackend.Unknown);

        var major = File.Exists(Path.Combine(core, "BepInEx.Core.dll")) ? 6 :
            File.Exists(Path.Combine(core, "BepInEx.dll")) ? 5 : 0;

        var backend = File.Exists(Path.Combine(core, "BepInEx.Unity.IL2CPP.dll"))
            ? UnityBackend.Il2Cpp
            : File.Exists(Path.Combine(core, "BepInEx.Unity.Mono.dll"))
                ? UnityBackend.Mono
                : UnityBackend.Unknown;

        return new BepInExInstall(true, major, backend);
    }
}
