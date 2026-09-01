using System;
using System.Net;
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
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostName);

        // TODO: add related IP addresses explicitly
        if ("localhost".Equals(hostName, StringComparison.OrdinalIgnoreCase))
        {
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        }

        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest req = new($"CN={hostName}", ec, HashAlgorithmName.SHA256);

        // RSA should also work but slower
        // using var rsa = RSA.Create(2048);
        // CertificateRequest req = new($"CN={hostName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Adds purpose
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([
            new("1.3.6.1.5.5.7.3.1"), // serverAuth
            new("1.3.6.1.5.5.7.3.2")  // clientAuth (optional)
        ],
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
