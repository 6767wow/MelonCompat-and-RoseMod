using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace MelonCompatInstaller;

internal static class BepInExPackageInstaller
{
    private const string BepInExBuildsUrl = "https://builds.bepinex.dev/projects/bepinex_be";

    public static async Task InstallAsync(GameInfo game, string? localZipPath, IProgress<string>? progress)
    {
        var zipPath = await ResolvePackageAsync(game, localZipPath, progress);

        if (!File.Exists(zipPath))
            throw new FileNotFoundException("BepInEx package zip was not found.", zipPath);

        progress?.Report("Extracting BepInEx package into game root...");
        ExtractZip(zipPath, game.RootDirectory);
        progress?.Report("Installed BepInEx package files.");
    }

    public static Task<string> ResolvePackageAsync(GameInfo game, string? localZipPath, IProgress<string>? progress, string packageDescription = "BepInEx 6 package")
    {
        if (!string.IsNullOrWhiteSpace(localZipPath))
            return Task.FromResult(Path.GetFullPath(localZipPath.Trim('"')));

        return DownloadLatestPackageAsync(game, progress, packageDescription);
    }

    public static void ExtractSelectedEntries(string zipPath, string destination, Func<string, bool> includeEntry, Func<string, string> mapEntry)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Package zip was not found.", zipPath);

        var root = Path.GetFullPath(destination);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var normalized = entry.FullName.Replace('\\', '/');
            if (!includeEntry(normalized))
                continue;

            var mapped = mapEntry(normalized).Replace('/', Path.DirectorySeparatorChar);
            var outputPath = Path.GetFullPath(Path.Combine(root, mapped));
            if (!outputPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Package contains an unsafe path: " + entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            entry.ExtractToFile(outputPath, overwrite: true);
        }
    }

    private static async Task<string> DownloadLatestPackageAsync(GameInfo game, IProgress<string>? progress, string packageDescription)
    {
        var target = game.Backend switch
        {
            UnityBackend.Mono => "Unity.Mono",
            UnityBackend.Il2Cpp => "Unity.IL2CPP",
            _ => throw new InvalidOperationException("Backend must be detected before selecting BepInEx.")
        };

        var runtime = game.Architecture == ProcessArchitecture.X86 ? "win-x86" : "win-x64";
        progress?.Report($"Finding latest {packageDescription} for {target} {runtime}...");

        using var http = NewHttpClient();
        var html = await http.GetStringAsync(BepInExBuildsUrl);
        var pattern = $"href=\"(?<href>[^\"]*BepInEx-{Regex.Escape(target)}-{Regex.Escape(runtime)}-[^\"]+\\.zip)\"";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException($"Could not find a BepInEx package for {target} {runtime}.");

        var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
        var url = Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(new Uri("https://builds.bepinex.dev"), href);

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "MelonCompatInstaller");
        Directory.CreateDirectory(cacheDirectory);
        var destination = Path.Combine(cacheDirectory, Path.GetFileName(url.LocalPath));

        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
        {
            progress?.Report($"Using cached {packageDescription}: " + destination);
            return destination;
        }

        progress?.Report($"Downloading {packageDescription}: " + url);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var targetFile = File.Create(destination);
        await source.CopyToAsync(targetFile);
        return destination;
    }

    private static void ExtractZip(string zipPath, string destination)
    {
        var root = Path.GetFullPath(destination);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var outputPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!outputPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BepInEx package contains an unsafe path: " + entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            entry.ExtractToFile(outputPath, overwrite: true);
        }
    }

    private static HttpClient NewHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MelonCompatInstaller/0.7.3");
        return client;
    }
}
