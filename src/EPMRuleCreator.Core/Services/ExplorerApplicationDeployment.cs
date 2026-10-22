using System.Security.Cryptography;

namespace EPMRuleCreator.Core.Services;

/// <summary>Deploy the standalone app independently of its download location.</summary>
public static class ExplorerApplicationDeployment
{
    public static string GetInstalledPath(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        return Path.Combine(AppDataPaths.GetRoot(localAppData), "App", "EPMRuleCreator.exe");
    }

    public static string Install(string executablePath, string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var source = Path.GetFullPath(executablePath);
        if (!File.Exists(source) || !source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(source), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run the standalone application EXE to register its context menu.");

        var destination = GetInstalledPath(localAppData);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(source, destination, comparison)) return destination;
        if (File.Exists(destination) && Identical(source, destination)) return destination;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, staging);
            // Stage the complete file before replacing an old copy. A failed
            // update leaves the old menu target in place; registry writes follow.
            File.Move(staging, destination, overwrite: true);
        }
        catch (IOException exception)
        {
            throw new IOException("Could not update the Explorer application copy. If an app instance launched from Explorer is running, close it and retry. " + exception.Message, exception);
        }
        finally
        {
            // Remove only this operation's staging file, never the installed app.
            try { File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return destination;
    }

    private static bool Identical(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
    }
}
