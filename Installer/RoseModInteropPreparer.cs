using System.Collections;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MelonCompatInstaller;

internal static class RoseModInteropPreparer
{
    private const string UnityBaseLibrariesUrlTemplate = "https://unity.bepinex.dev/libraries/{VERSION}.zip";
    private static bool instructionSetsRegistered;

    public static async Task PrepareAsync(GameInfo game, string roseModRoot, string coreDirectory, IProgress<string>? progress)
    {
        if (game.Backend != UnityBackend.Il2Cpp)
            return;

        var interopDirectory = Path.Combine(roseModRoot, "interop");
        Directory.CreateDirectory(interopDirectory);
        if (File.Exists(Path.Combine(interopDirectory, "UnityEngine.CoreModule.dll")))
        {
            progress?.Report("RoseMod IL2CPP interop assemblies are ready.");
            return;
        }

        var gameAssemblyPath = Path.Combine(game.RootDirectory, "GameAssembly.dll");
        var metadataPath = Path.Combine(game.DataDirectory, "il2cpp_data", "Metadata", "global-metadata.dat");
        if (!File.Exists(gameAssemblyPath))
            throw new FileNotFoundException("GameAssembly.dll was not found for RoseMod interop generation.", gameAssemblyPath);
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException("global-metadata.dat was not found for RoseMod interop generation.", metadataPath);

        EnsureAssemblyResolver(coreDirectory, Path.Combine(game.RootDirectory, "dotnet"));
        RequireGeneratorFile(coreDirectory, "Cpp2IL.Core.dll");
        RequireGeneratorFile(coreDirectory, "Il2CppInterop.Generator.dll");
        RequireGeneratorFile(coreDirectory, "LibCpp2IL.dll");

        progress?.Report("Preparing RoseMod IL2CPP interop assemblies...");
        var cpp2Il = Assembly.LoadFrom(Path.Combine(coreDirectory, "Cpp2IL.Core.dll"));
        var libCpp2Il = Assembly.LoadFrom(Path.Combine(coreDirectory, "LibCpp2IL.dll"));
        var generatorAssembly = Assembly.LoadFrom(Path.Combine(coreDirectory, "Il2CppInterop.Generator.dll"));

        RegisterCpp2IlSupport(cpp2Il, libCpp2Il);
        var unityVersion = ResolveUnityVersion(cpp2Il, coreDirectory, game, progress);
        await EnsureUnityBaseLibrariesAsync(unityVersion, roseModRoot, progress);

        var sourceAssemblies = RunCpp2Il(cpp2Il, gameAssemblyPath, metadataPath, unityVersion, progress);
        RunInteropGenerator(generatorAssembly, gameAssemblyPath, sourceAssemblies, interopDirectory, Path.Combine(roseModRoot, "unity-libs"), progress);

        if (!File.Exists(Path.Combine(interopDirectory, "UnityEngine.CoreModule.dll")))
            throw new InvalidOperationException("RoseMod interop generation finished, but UnityEngine.CoreModule.dll was not produced.");

        progress?.Report("Generated RoseMod IL2CPP interop assemblies.");
    }

    private static void EnsureAssemblyResolver(params string[] directories)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    continue;

                var candidate = Path.Combine(directory, name + ".dll");
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }

            return null;
        };
    }

    private static void RequireGeneratorFile(string coreDirectory, string fileName)
    {
        var path = Path.Combine(coreDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("RoseMod interop generator dependency is missing.", path);
    }

    private static void RegisterCpp2IlSupport(Assembly cpp2Il, Assembly libCpp2Il)
    {
        if (instructionSetsRegistered)
            return;

        var instructionSetRegistry = cpp2Il.GetType("Cpp2IL.Core.Api.InstructionSetRegistry", throwOnError: true)!;
        var x86InstructionSet = cpp2Il.GetType("Cpp2IL.Core.InstructionSets.X86InstructionSet", throwOnError: true)!;
        var defaultInstructionSets = libCpp2Il.GetType("LibCpp2IL.DefaultInstructionSets", throwOnError: true)!;
        var register = instructionSetRegistry
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "RegisterInstructionSet" && method.IsGenericMethodDefinition);

        TryRegisterInstructionSet(register, x86InstructionSet, defaultInstructionSets.GetField("X86_32", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!);
        TryRegisterInstructionSet(register, x86InstructionSet, defaultInstructionSets.GetField("X86_64", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!);

        libCpp2Il.GetType("LibCpp2IL.LibCpp2IlBinaryRegistry", throwOnError: true)!
            .GetMethod("RegisterBuiltInBinarySupport", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);

        instructionSetsRegistered = true;
    }

    private static void TryRegisterInstructionSet(MethodInfo register, Type instructionSetType, object instructionSetId)
    {
        try
        {
            register.MakeGenericMethod(instructionSetType).Invoke(null, new[] { instructionSetId });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException)
        {
            // Cpp2IL keeps this registry globally; repeated installer runs in one process can hit duplicate keys.
        }
    }

    private static object ResolveUnityVersion(Assembly cpp2Il, string coreDirectory, GameInfo game, IProgress<string>? progress)
    {
        var cpp2IlApi = cpp2Il.GetType("Cpp2IL.Core.Cpp2IlApi", throwOnError: true)!;
        var unityPlayerPath = Path.Combine(game.RootDirectory, "UnityPlayer.dll");
        if (!File.Exists(unityPlayerPath))
            unityPlayerPath = game.ExecutablePath ?? game.RootDirectory;

        try
        {
            var version = cpp2IlApi.GetMethod("DetermineUnityVersion", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { unityPlayerPath, game.DataDirectory });
            if (version is not null)
            {
                progress?.Report("Detected Unity version " + version);
                return version;
            }
        }
        catch (Exception ex)
        {
            progress?.Report("WARNING: Cpp2IL Unity version detection failed: " + Unwrap(ex).Message);
        }

        var parsed = DetectUnityVersionString(game);
        var primitives = Assembly.LoadFrom(Path.Combine(coreDirectory, "AssetRipper.Primitives.dll"));
        var unityVersionType = primitives.GetType("AssetRipper.Primitives.UnityVersion", throwOnError: true)!;
        var versionObject = unityVersionType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) })!
            .Invoke(null, new object[] { parsed });
        progress?.Report("Using Unity version " + parsed + " for RoseMod interop generation.");
        return versionObject!;
    }

    private static async Task EnsureUnityBaseLibrariesAsync(object unityVersion, string roseModRoot, IProgress<string>? progress)
    {
        var unityLibs = Path.Combine(roseModRoot, "unity-libs");
        Directory.CreateDirectory(unityLibs);
        if (Directory.EnumerateFiles(unityLibs, "UnityEngine.CoreModule.dll", SearchOption.TopDirectoryOnly).Any())
            return;

        foreach (var dll in Directory.EnumerateFiles(unityLibs, "*.dll", SearchOption.TopDirectoryOnly))
            File.Delete(dll);

        var version = GetUnityVersionShort(unityVersion);
        var url = UnityBaseLibrariesUrlTemplate.Replace("{VERSION}", version);
        var zipPath = Path.Combine(unityLibs, Path.GetFileName(new Uri(url).AbsolutePath));

        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
        {
            progress?.Report("Downloading Unity base libraries for " + version + "...");
            using var http = NewHttpClient();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(zipPath);
            await source.CopyToAsync(target);
        }
        else
        {
            progress?.Report("Using cached Unity base libraries: " + zipPath);
        }

        progress?.Report("Extracting Unity base libraries...");
        ZipFile.ExtractToDirectory(zipPath, unityLibs, overwriteFiles: true);
    }

    private static object RunCpp2Il(Assembly cpp2Il, string gameAssemblyPath, string metadataPath, object unityVersion, IProgress<string>? progress)
    {
        progress?.Report("Running Cpp2IL for RoseMod...");
        var cpp2IlApi = cpp2Il.GetType("Cpp2IL.Core.Cpp2IlApi", throwOnError: true)!;
        cpp2IlApi.GetMethod("InitializeLibCpp2Il", BindingFlags.Public | BindingFlags.Static, new[]
        {
            typeof(string),
            typeof(string),
            unityVersion.GetType(),
            typeof(bool)
        })!.Invoke(null, new[] { gameAssemblyPath, metadataPath, unityVersion, false });

        var appContext = cpp2IlApi.GetField("CurrentAppContext", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)
            ?? throw new InvalidOperationException("Cpp2IL did not create an application analysis context.");

        var layerBase = cpp2Il.GetType("Cpp2IL.Core.Api.Cpp2IlProcessingLayer", throwOnError: true)!;
        var layer = Activator.CreateInstance(cpp2Il.GetType("Cpp2IL.Core.ProcessingLayers.AttributeInjectorProcessingLayer", throwOnError: true)!)!;
        var layerListType = typeof(List<>).MakeGenericType(layerBase);
        var layers = (IList)Activator.CreateInstance(layerListType)!;
        layers.Add(layer);

        layerBase.GetMethod("PreProcess", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(layer, new object?[] { appContext, layers });
        layerBase.GetMethod("Process", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(layer, new object?[] { appContext, new Action<int, int>((_, _) => { }) });

        var output = Activator.CreateInstance(cpp2Il.GetType("Cpp2IL.Core.OutputFormats.AsmResolverDllOutputFormatDefault", throwOnError: true)!)!;
        var assemblies = output.GetType().BaseType!.GetMethod("BuildAssemblies", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(output, new[] { appContext })
            ?? throw new InvalidOperationException("Cpp2IL did not return dummy assemblies.");

        ResetCpp2Il(cpp2Il);
        return assemblies;
    }

    private static void RunInteropGenerator(Assembly generatorAssembly, string gameAssemblyPath, object sourceAssemblies, string interopDirectory, string unityBaseLibs, IProgress<string>? progress)
    {
        progress?.Report("Running Il2CppInterop generator for RoseMod...");
        Directory.CreateDirectory(interopDirectory);
        foreach (var dll in Directory.EnumerateFiles(interopDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            File.Delete(dll);

        var optionsType = generatorAssembly.GetType("Il2CppInterop.Generator.GeneratorOptions", throwOnError: true)!;
        var options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("GameAssemblyPath")!.SetValue(options, gameAssemblyPath);
        optionsType.GetProperty("Source")!.SetValue(options, sourceAssemblies);
        optionsType.GetProperty("OutputDir")!.SetValue(options, interopDirectory);
        optionsType.GetProperty("UnityBaseLibsDir")!.SetValue(options, Directory.Exists(unityBaseLibs) ? unityBaseLibs : null);

        var generatorType = generatorAssembly.GetType("Il2CppInterop.Generator.Il2CppInteropGenerator", throwOnError: true)!;
        var generator = generatorType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new[] { options })
            ?? throw new InvalidOperationException("Could not create Il2CppInterop generator.");
        generatorAssembly.GetType("Il2CppInterop.Generator.Runners.InteropAssemblyGenerator", throwOnError: true)!
            .GetMethod("AddInteropAssemblyGenerator", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new[] { generator });
        generator.GetType().GetMethod("Run", BindingFlags.Public | BindingFlags.Instance)!.Invoke(generator, null);
    }

    private static void ResetCpp2Il(Assembly cpp2Il)
    {
        try
        {
            cpp2Il.GetType("Cpp2IL.Core.Cpp2IlApi", throwOnError: true)!
                .GetMethod("ResetInternalState", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
        }
        catch
        {
        }
    }

    private static string GetUnityVersionShort(object unityVersion)
    {
        var type = unityVersion.GetType();
        var major = type.GetProperty("Major")!.GetValue(unityVersion);
        var minor = type.GetProperty("Minor")!.GetValue(unityVersion);
        var build = type.GetProperty("Build")!.GetValue(unityVersion);
        return $"{major}.{minor}.{build}";
    }

    private static string DetectUnityVersionString(GameInfo game)
    {
        foreach (var candidate in new[]
        {
            (Path.Combine(game.DataDirectory, "globalgamemanagers"), new[] { 20, 48 }),
            (Path.Combine(game.DataDirectory, "data.unity3d"), new[] { 18 }),
            (Path.Combine(game.DataDirectory, "mainData"), new[] { 20 })
        })
        {
            foreach (var offset in candidate.Item2)
            {
                if (TryReadUnityVersion(candidate.Item1, offset, out var version))
                    return version;
            }
        }

        return "2019.4.0f1";
    }

    private static bool TryReadUnityVersion(string path, int offset, out string version)
    {
        version = string.Empty;
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length <= offset)
                return false;

            stream.Position = offset;
            var bytes = new List<byte>();
            for (var next = stream.ReadByte(); next > 0 && bytes.Count < 64; next = stream.ReadByte())
                bytes.Add((byte)next);

            var match = Regex.Match(System.Text.Encoding.ASCII.GetString(bytes.ToArray()), @"\d+\.\d+\.\d+[abfp]?\d*");
            if (!match.Success)
                return false;

            version = match.Value;
            if (Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
                version += "f1";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient NewHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RoseMod/0.1.0");
        return client;
    }

    private static Exception Unwrap(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
    }
}
