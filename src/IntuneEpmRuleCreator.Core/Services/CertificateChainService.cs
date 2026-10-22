using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using IntuneEpmRuleCreator.Core.Models;

namespace IntuneEpmRuleCreator.Core.Services;

public enum CertificateChainExportFormat { Pkcs7, Pem }

public static class CertificateChainService
{
    public static CertificateAttributes Describe(X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;
        return new(certificate.Subject, certificate.Issuer, certificate.Thumbprint,
            certificate.NotBefore, certificate.NotAfter, certificate.NotBefore.ToUniversalTime() <= now && certificate.NotAfter.ToUniversalTime() >= now)
        {
            CertificateBase64 = Convert.ToBase64String(certificate.RawData)
        };
    }

    public static CertificateChainResult Build(CertificateAttributes signer, IEnumerable<CertificateAttributes>? extraCertificates = null)
    {
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(signer.CertificateBase64));
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        var extras = new List<X509Certificate2>();
        try
        {
            foreach (var extra in extraCertificates ?? [])
            {
                var item = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(extra.CertificateBase64));
                extras.Add(item);
                chain.ChainPolicy.ExtraStore.Add(item);
            }
            chain.Build(certificate);
            var certificates = chain.ChainElements.Cast<X509ChainElement>().Select(element => Describe(element.Certificate)).ToArray();
            if (certificates.Length == 0) certificates = [Describe(certificate)];
            var last = chain.ChainElements.Count > 0 ? chain.ChainElements[^1].Certificate : certificate;
            var complete = last.SubjectName.RawData.AsSpan().SequenceEqual(last.IssuerName.RawData)
                && !chain.ChainStatus.Any(status => (status.Status & X509ChainStatusFlags.PartialChain) != 0);
            return new(certificates, complete, chain.ChainStatus.Select(status => $"{status.Status}: {status.StatusInformation.Trim()}").ToArray());
        }
        finally
        {
            foreach (var extra in extras) extra.Dispose();
        }
    }

    public static byte[] Export(CertificateChainResult chain, CertificateChainExportFormat format)
    {
        if (chain.Certificates.Count == 0) throw new InvalidOperationException("No certificates are available to export.");
        var certificates = new X509Certificate2Collection();
        try
        {
            foreach (var item in chain.Certificates)
                certificates.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(item.CertificateBase64)));
            return format switch
            {
                CertificateChainExportFormat.Pkcs7 => certificates.Export(X509ContentType.Pkcs7)
                    ?? throw new CryptographicException("Certificate chain export failed."),
                CertificateChainExportFormat.Pem => Encoding.ASCII.GetBytes(string.Join("\n", certificates.Cast<X509Certificate2>().Select(item => item.ExportCertificatePem())) + "\n"),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
        }
        finally
        {
            foreach (var certificate in certificates) certificate.Dispose();
        }
    }

    public static CertificateChainResult FromSignedFile(string path, CertificateAttributes signer)
    {
        // Use embedded intermediate certificates as well as the local Windows stores.
        // Do not import certificates into a system store or download missing issuers.
        if (!OperatingSystem.IsWindows()) return Build(signer);
        IntPtr storeHandle = IntPtr.Zero;
        IntPtr messageHandle = IntPtr.Zero;
        try
        {
            // CERT_QUERY_OBJECT_FILE = 1; PKCS7_SIGNED_EMBED = 10; FORMAT_BINARY = 1.
            if (!CryptQueryObject(1, path, 1u << 10, 1u << 1, 0, out _, out _, out _, out storeHandle, out messageHandle, IntPtr.Zero)
                || storeHandle == IntPtr.Zero)
                return Build(signer);
            using var store = new X509Store(storeHandle);
            var embedded = store.Certificates;
            try { return Build(signer, embedded.Cast<X509Certificate2>().Select(Describe)); }
            finally { foreach (var certificate in embedded) certificate.Dispose(); }
        }
        finally
        {
            if (messageHandle != IntPtr.Zero) CryptMsgClose(messageHandle);
            if (storeHandle != IntPtr.Zero) CertCloseStore(storeHandle, 0);
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptQueryObject(uint objectType, string filePath, uint contentFlags, uint formatFlags, uint flags,
        out uint encoding, out uint contentType, out uint formatType, out IntPtr store, out IntPtr message, IntPtr context);

    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptMsgClose(IntPtr message);

    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertCloseStore(IntPtr store, uint flags);
}
