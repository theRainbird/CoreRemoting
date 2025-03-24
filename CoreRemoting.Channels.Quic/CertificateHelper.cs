using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CoreRemoting.Channels.Quic;

/// <summary>
/// Self-signed certificate generator for the QUIC channel.
/// </summary>
internal class CertificateHelper
{
    public static X509Certificate2 LoadFromPfx(string pfxFilePath, string pfxPassword) =>
        X509CertificateLoader.LoadPkcs12FromFile(pfxFilePath, pfxPassword);

    public static X509Certificate2 GenerateSelfSigned(string hostName = "localhost")
    {
        // generate a new certificate
        var now = DateTimeOffset.UtcNow;
        SubjectAlternativeNameBuilder sanBuilder = new();
        sanBuilder.AddDnsName(hostName);

        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest req = new($"CN={hostName}", ec, HashAlgorithmName.SHA256);

        // Adds purpose
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1") // serverAuth
		},
        false));

        // Adds usage
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));

        // Adds subject alternate names
        req.CertificateExtensions.Add(sanBuilder.Build());

        // Sign
        using var crt = req.CreateSelfSigned(now, now.AddDays(14)); // 14 days is the max duration of a certificate for this type

        var password = Guid.NewGuid().ToString();
        var pfx = crt.Export(X509ContentType.Pfx, password);
        var cert = X509CertificateLoader.LoadPkcs12(pfx, password);
        return cert;
    }
}
