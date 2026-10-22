using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EPMRuleCreator.Core.Models;

namespace EPMRuleCreator.Core.Services;

public sealed class FileAttributeService(Sha256FileHasher hasher, IEpmCmdletsReader? epmReader = null)
{
    private readonly IEpmCmdletsReader _epmReader = epmReader ?? new EpmCmdletsReader();
    public bool IsEpmCmdletsAvailable => _epmReader.IsAvailable;

    public Task<AnalyzedFileAttributes> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
        => AnalyzeAsync(path, useEpmCmdlets: false, cancellationToken);

    public async Task<AnalyzedFileAttributes> AnalyzeAsync(string path, bool useEpmCmdlets, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected file does not exist.", fullPath);

        var hash = await hasher.ComputeAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();
        IReadOnlyDictionary<string, string?> epm = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (useEpmCmdlets)
        {
            if (!IsEpmCmdletsAvailable)
                warnings.Add("EpmCmdlets.dll is unavailable. Using local SHA256 and Windows file metadata.");
            else
            {
                try { epm = await _epmReader.ReadAsync(fullPath, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException
                    or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    warnings.Add($"EpmCmdlets could not be loaded or read the file. Using local SHA256 and Windows file metadata. {exception.Message}");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var version = FileVersionInfo.GetVersionInfo(fullPath);
        var certificate = TryReadCertificate(fullPath, warnings);
        CertificateChainResult? certificateChain = null;
        if (certificate is not null)
        {
            try
            {
                certificateChain = await Task.Run(() => CertificateChainService.FromSignedFile(fullPath, certificate), cancellationToken).ConfigureAwait(false);
                if (!certificateChain.IsComplete)
                    warnings.Add("The certificate chain is incomplete. A missing issuer or root was not available in the file or local certificate stores. No certificates were downloaded.");
            }
            catch (CryptographicException exception)
            {
                warnings.Add($"Certificate chain lookup failed; the signing certificate is still available. {exception.Message}");
                certificateChain = new([certificate], false, [exception.Message]);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
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
            CertificateChain = certificateChain,
            Warnings = warnings
        };
    }

    private static CertificateAttributes? TryReadCertificate(string path, ICollection<string> warnings)
    {
        try
        {
#pragma warning disable SYSLIB0057 // Extract the signer from a signed file, not a certificate blob.
            using var signedCertificate = X509Certificate.CreateFromSignedFile(path);
            using var certificate = new X509Certificate2(signedCertificate);
#pragma warning restore SYSLIB0057
            return CertificateChainService.Describe(certificate);
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
