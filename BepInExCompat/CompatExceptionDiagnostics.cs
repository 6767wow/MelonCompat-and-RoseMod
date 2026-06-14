namespace MelonLoader.BepInExCompat;

internal static class CompatExceptionDiagnostics
{
    public static string Describe(Exception exception)
    {
        var typeLoad = Find<TypeLoadException>(exception);
        if (typeLoad is not null)
        {
            return " Missing type while running the melon. "
                + "For IL2CPP mods this usually means the mod was built against a different game version, "
                + "or its generated interop assemblies do not match BepInEx's generated interop assemblies. "
                + $"Runtime message: {typeLoad.Message}";
        }

        var fileLoad = Find<FileLoadException>(exception);
        if (fileLoad is not null)
            return $" Assembly load failed: {fileLoad.Message}";

        var fileMissing = Find<FileNotFoundException>(exception);
        if (fileMissing is not null)
            return $" Missing dependency: {fileMissing.FileName ?? fileMissing.Message}";

        return string.Empty;
    }

    private static T? Find<T>(Exception exception)
        where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T typed)
                return typed;
        }

        return null;
    }
}
