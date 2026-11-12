using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ShackRemote.Core;

/// <summary>
/// Eigenes, selbst signiertes Zertifikat dieses Rechners (für Server- und Client-Rolle gleich).
/// Gespeichert als identity.pfx im Einstellungsordner: %APPDATA%\ShackRemote, im Dienstbetrieb C:\ProgramData\ShackRemote.
/// </summary>
public static class Identity
{
    /// <summary>Lädt das eigene Zertifikat (identity.pfx) oder erzeugt beim ersten Start ein neues selbst signiertes.</summary>
    public static X509Certificate2 LoadOrCreate()
    {
        var path = Path.Combine(AppSettings.Folder, "identity.pfx");
        if (File.Exists(path))
            return X509CertificateLoader.LoadPkcs12FromFile(path, (string?)null);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN=ShackRemote {Environment.MachineName}", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // Serverauthentifizierung
                new Oid("1.3.6.1.5.5.7.3.2"), // Clientauthentifizierung
            }, false));

        using var created = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        var pfx = created.Export(X509ContentType.Pkcs12);
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllBytes(path, pfx);

        // Über PFX neu laden, damit SslStream unter Windows den privaten Schlüssel nutzen kann.
        return X509CertificateLoader.LoadPkcs12(pfx, (string?)null);
    }

    /// <summary>SHA-256-Fingerabdruck als Hex-Text (64 Zeichen).</summary>
    public static string Fingerprint(X509Certificate cert) => cert.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>Kurzform für die Anzeige, z. B. "1A2B 3C4D 5E6F 7A8B".</summary>
    public static string Short(string fingerprint) =>
        fingerprint.Length < 16 ? fingerprint : string.Join(" ", Enumerable.Range(0, 4).Select(i => fingerprint.Substring(i * 4, 4)));
}
