using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using IntuneEpmRuleCreator.Core.Models;

namespace IntuneEpmRuleCreator.Core.Services;

public sealed class FileAttributeService(Sha256FileHasher hasher)
{
    private static readonly string[] ModuleCandidates =
    [
        @"C:\Program Files\Microsoft EPM Agent\EpmTools\EpmCmdlets.dll",
        @"C:\Program Files (x86)\Microsoft EPM Agent\EpmTools\EpmCmdlets.dll"
    ];

    public async Task<AnalyzedFileAttributes> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected file does not exist.", fullPath);

        var hash = await hasher.ComputeAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();
        Dictionary<string, string?> epm = new(StringComparer.OrdinalIgnoreCase);
        var module = ModuleCandidates.FirstOrDefault(File.Exists);
        if (module is null)
            warnings.Add("Microsoft EPM Tools were not found. EPM attributes were not collected; SHA256 and Windows file metadata are shown separately.");
        else if (OperatingSystem.IsWindows())
            epm = await GetEpmAttributesAsync(module, fullPath, cancellationToken).ConfigureAwait(false);
        else
            warnings.Add("Microsoft EPM Tools require Windows PowerShell and are unavailable on this operating system.");

        var version = FileVersionInfo.GetVersionInfo(fullPath);
        var certificate = TryReadCertificate(fullPath, warnings);
        var epmHash = Get(epm, "FileHash", "Hash");
        if (!string.IsNullOrWhiteSpace(epmHash) && !string.Equals(NormalizeHash(epmHash), hash, StringComparison.OrdinalIgnoreCase))
            warnings.Add("The EPM Tools hash differs from the independently calculated SHA256 hash.");

        return new AnalyzedFileAttributes
        {
            FileName = Path.GetFileName(fullPath), FilePath = fullPath, Sha256 = hash,
            EpmFileHash = epmHash, EpmHashAlgorithm = Get(epm, "HashAlgorithm"),
            ProductName = Get(epm, "ProductName") ?? NullIfEmpty(version.ProductName),
            InternalName = Get(epm, "InternalName") ?? NullIfEmpty(version.InternalName),
            Version = Get(epm, "Version") ?? NullIfEmpty(version.FileVersion),
            Description = Get(epm, "Description") ?? NullIfEmpty(version.FileDescription),
            CompanyName = Get(epm, "CompanyName") ?? NullIfEmpty(version.CompanyName),
            SignatureStatus = certificate is null ? "Publisher certificate unavailable; signature trust not established" : "Publisher certificate extracted; signature trust not established",
            PublisherCertificate = certificate,
            Warnings = warnings
        };
    }

    private static async Task<Dictionary<string, string?>> GetEpmAttributesAsync(string module, string path, CancellationToken cancellationToken)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new InvalidOperationException("Windows PowerShell 5.1 was not found.");

        const string script = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $p=ConvertFrom-Json -InputObject ([Console]::In.ReadToEnd()); Import-Module -Name $p.Module -ErrorAction Stop; $r=Get-FileAttributes -FilePath $p.Path -CertOutputPath $p.CertOutputPath -ErrorAction Stop; ConvertTo-Json -InputObject $r -Depth 10 -Compress";
        var certificateOutputPath = Path.Combine(Path.GetTempPath(), $"IntuneEpmRuleCreator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certificateOutputPath);
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script })
            start.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = start };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var input = JsonSerializer.Serialize(new { Module = module, Path = path, CertOutputPath = certificateOutputPath });
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"EPM Tools could not analyze this file. {error.Trim()}");

            try
            {
                using var document = JsonDocument.Parse(output);
                return FlattenJson(document.RootElement);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("EPM Tools returned an unrecognized response.", exception);
            }
        }
        finally
        {
            Directory.Delete(certificateOutputPath, recursive: true);
        }
    }

    private static Dictionary<string, string?> FlattenJson(JsonElement root)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind == JsonValueKind.Array)
            root = root.EnumerateArray().FirstOrDefault();
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a JSON object from Get-FileAttributes.");
        foreach (var property in root.EnumerateObject())
            values[property.Name] = property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : property.Value.ToString();
        return values;
    }

    private static CertificateAttributes? TryReadCertificate(string path, ICollection<string> warnings)
    {
        try
        {
#pragma warning disable SYSLIB0057 // This API extracts the signer certificate from the signed file itself; X509CertificateLoader accepts certificate data, not signed executables.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var now = DateTimeOffset.UtcNow;
            return new(certificate.Subject, certificate.Issuer, certificate.Thumbprint ?? string.Empty,
                certificate.NotBefore, certificate.NotAfter, certificate.NotBefore <= now && certificate.NotAfter >= now);
        }
        catch (CryptographicException)
        {
            warnings.Add("No embedded publisher certificate could be extracted. This does not establish whether the file has a valid Authenticode signature.");
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            warnings.Add("Certificate extraction from signed files is available only on Windows.");
            return null;
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static string NormalizeHash(string hash) => string.Concat(hash.Where(Uri.IsHexDigit)).ToUpperInvariant();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
