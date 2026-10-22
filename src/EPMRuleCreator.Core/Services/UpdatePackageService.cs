using System.IO.Compression;
using System.Security.Cryptography;

namespace EPMRuleCreator.Core.Services;

public static class UpdatePackageService
{
    public const long MaximumPackageBytes = 256L * 1024 * 1024;

    public static async Task DownloadAsync(HttpClient client, ReleaseUpdate update, string archivePath, CancellationToken cancellationToken)
    {
        var expected = $"https://github.com/mathiasborowicz/EPMRuleCreator/releases/download/v{update.Version}/EPMRuleCreator-win-x64.zip";
        if (update.DownloadUrl?.AbsoluteUri != expected || update.Sha256 is not { Length: 64 } || !update.Sha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("This release has no verified update package. Open the release page instead.");
        using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumPackageBytes) throw new InvalidDataException("Update package is too large.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > MaximumPackageBytes) throw new InvalidDataException("Update package is too large.");
            hash.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Sha256)))
            throw new InvalidDataException("Update checksum does not match GitHub. Nothing was installed.");
    }

    public static void ExtractExecutable(string archivePath, string executablePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var executables = archive.Entries.Where(entry => entry.FullName == "EPMRuleCreator.exe").ToArray();
        if (executables.Length != 1 || executables[0].Length < 2 || executables[0].Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Update package does not contain exactly one supported executable.");
        // Extract only the known root entry, never arbitrary ZIP paths.
        executables[0].ExtractToFile(executablePath, overwrite: false);
        using var executable = File.OpenRead(executablePath);
        if (executable.ReadByte() != 'M' || executable.ReadByte() != 'Z')
            throw new InvalidDataException("Update executable is not a Windows application.");
    }
}
