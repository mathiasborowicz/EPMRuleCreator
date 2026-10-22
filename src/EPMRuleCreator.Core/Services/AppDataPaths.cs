namespace EPMRuleCreator.Core.Services;

public static class AppDataPaths
{
    public static string GetRoot(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        return Path.Combine(Path.GetFullPath(localAppData), "EPMRuleCreator");
    }

    public static string PrepareSettingsPath(string localAppData)
    {
        var destination = Path.Combine(GetRoot(localAppData), "settings.json");
        var legacy = Path.Combine(Path.GetFullPath(localAppData), "IntuneEpmRuleCreator", "settings.json");
        try
        {
            if (!File.Exists(destination) && File.Exists(legacy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(legacy, destination, overwrite: false);
            }
        }
        catch (IOException) { /* A concurrent migration or unavailable optional settings must not block startup. */ }
        catch (UnauthorizedAccessException) { }
        // Preserve the original settings and do not copy or decrypt token caches.
        return destination;
    }
}
