using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using CoreRemoting.Encryption;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests.Encryption;

/// <summary>
/// Performance benchmarks comparing RSA and ECDSA session key pairs.
/// Run in Release mode for accurate results.
/// </summary>
public class SessionKeyPairBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public SessionKeyPairBenchmarkTests(ITestOutputHelper output)
    {
        _output = new ConsoleRepeater(output);
    }

    private class ConsoleRepeater(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message)
        {
            output.WriteLine(message);
            Console.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            output.WriteLine(format, args);
            Console.WriteLine(format, args);
        }
    }

    /// <summary>
    /// Benchmarks key generation, signing, verification, and export for RSA and ECDSA.
    /// Key generation is FORCED by accessing PrivateKey, because RSACryptoServiceProvider
    /// uses lazy generation (the key is not actually created until first use).
    /// </summary>
    [Fact]
    public void Benchmark_RsaVsEcdsa_Performance()
    {
        const int genIterations = 5;
        const int signIterations = 500;
        const int verifyIterations = 1000;
        const int exportIterations = 2000;

        var challenge = new byte[32];
        new Random(42).NextBytes(challenge);

        _output.WriteLine("===========================================================");
        _output.WriteLine("  Session Key Pair Benchmark: RSA vs ECDSA P-256");
        _output.WriteLine("===========================================================");
        _output.WriteLine("");

        Warmup();

        // === Key Generation ===
        // IMPORTANT: force real key generation by accessing PrivateKey,
        // because RSACryptoServiceProvider creates keys lazily.
        _output.WriteLine($"Key Generation ({genIterations} iterations, median, generation FORCED):");
        _output.WriteLine("-----------------------------------------------------------");

        var rsa2048Gen = MeasureEach(genIterations, () =>
        {
            var key = new RsaKeyPair(2048);
            _ = key.PrivateKey;  // Forces actual key generation
            key.Dispose();
        });

        var rsa4096Gen = MeasureEach(genIterations, () =>
        {
            var key = new RsaKeyPair(4096);
            _ = key.PrivateKey;  // Forces actual key generation
            key.Dispose();
        });

        var ecdsaGen = MeasureEach(genIterations, () =>
        {
            var key = new EcdsaSessionKeyPair();
            _ = key.PrivateKey;
            key.Dispose();
        });

        PrintRow("RSA-2048", rsa2048Gen, rsa2048Gen);
        PrintRow("RSA-4096", rsa4096Gen, rsa2048Gen);
        PrintRow("ECDSA P-256", ecdsaGen, rsa2048Gen);
        _output.WriteLine("");

        // === Signing ===
        _output.WriteLine($"Signing ({signIterations} iterations, average):");
        _output.WriteLine("-----------------------------------------------------------");

        using var rsa2048Signer = new RsaKeyPair(2048);
        using var rsa4096Signer = new RsaKeyPair(4096);
        using var ecdsaSigner = new EcdsaSessionKeyPair();

        // Warm up each key before measuring (also forces key generation)
        rsa2048Signer.CreateSignature(challenge);
        rsa4096Signer.CreateSignature(challenge);
        ecdsaSigner.CreateSignature(challenge);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var rsa2048Sign = Measure(signIterations, () => rsa2048Signer.CreateSignature(challenge));
        var rsa4096Sign = Measure(signIterations, () => rsa4096Signer.CreateSignature(challenge));
        var ecdsaSign = Measure(signIterations, () => ecdsaSigner.CreateSignature(challenge));

        PrintRow("RSA-2048", TimeSpan.FromMilliseconds(rsa2048Sign.TotalMilliseconds / signIterations),
            TimeSpan.FromMilliseconds(ecdsaSign.TotalMilliseconds / signIterations));
        PrintRow("RSA-4096", TimeSpan.FromMilliseconds(rsa4096Sign.TotalMilliseconds / signIterations),
            TimeSpan.FromMilliseconds(ecdsaSign.TotalMilliseconds / signIterations));
        PrintRow("ECDSA P-256", TimeSpan.FromMilliseconds(ecdsaSign.TotalMilliseconds / signIterations),
            TimeSpan.FromMilliseconds(ecdsaSign.TotalMilliseconds / signIterations));
        _output.WriteLine("");

        // === Verification ===
        _output.WriteLine($"Verification ({verifyIterations} iterations, average):");
        _output.WriteLine("-----------------------------------------------------------");

        var rsa2048Sig = rsa2048Signer.CreateSignature(challenge);
        var rsa4096Sig = rsa4096Signer.CreateSignature(challenge);
        var ecdsaSig = ecdsaSigner.CreateSignature(challenge);

        using var rsa2048Verifier = new RsaKeyPair(rsa2048Signer.KeySize, rsa2048Signer.PublicKey);
        using var rsa4096Verifier = new RsaKeyPair(rsa4096Signer.KeySize, rsa4096Signer.PublicKey);
        using var ecdsaVerifier = EcdsaSessionKeyPair.FromPublicKey(ecdsaSigner.PublicKey);

        // Warm up
        rsa2048Verifier.VerifySignature(challenge, rsa2048Sig);
        rsa4096Verifier.VerifySignature(challenge, rsa4096Sig);
        ecdsaVerifier.VerifySignature(challenge, ecdsaSig);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var rsa2048Verify = Measure(verifyIterations, () => rsa2048Verifier.VerifySignature(challenge, rsa2048Sig));
        var rsa4096Verify = Measure(verifyIterations, () => rsa4096Verifier.VerifySignature(challenge, rsa4096Sig));
        var ecdsaVerify = Measure(verifyIterations, () => ecdsaVerifier.VerifySignature(challenge, ecdsaSig));

        PrintRow("RSA-2048", TimeSpan.FromMilliseconds(rsa2048Verify.TotalMilliseconds / verifyIterations),
            TimeSpan.FromMilliseconds(ecdsaVerify.TotalMilliseconds / verifyIterations));
        PrintRow("RSA-4096", TimeSpan.FromMilliseconds(rsa4096Verify.TotalMilliseconds / verifyIterations),
            TimeSpan.FromMilliseconds(ecdsaVerify.TotalMilliseconds / verifyIterations));
        PrintRow("ECDSA P-256", TimeSpan.FromMilliseconds(ecdsaVerify.TotalMilliseconds / verifyIterations),
            TimeSpan.FromMilliseconds(ecdsaVerify.TotalMilliseconds / verifyIterations));
        _output.WriteLine("");

        // === Export Public Key ===
        _output.WriteLine($"Public Key Export ({exportIterations} iterations, average):");
        _output.WriteLine("-----------------------------------------------------------");

        // Warm up (also forces key generation if not yet created)
        _ = rsa2048Signer.PublicKey;
        _ = rsa4096Signer.PublicKey;
        _ = ecdsaSigner.PublicKey;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var rsa2048Pub = Measure(exportIterations, () =>
        {
            var publicKey = rsa2048Signer.PublicKey;
            GC.KeepAlive(publicKey);
        });
        var rsa4096Pub = Measure(exportIterations, () =>
        {
            var publicKey = rsa4096Signer.PublicKey;
            GC.KeepAlive(publicKey);
        });
        var ecdsaPub = Measure(exportIterations, () =>
        {
            var publicKey = ecdsaSigner.PublicKey;
            GC.KeepAlive(publicKey);
        });

        PrintRow("RSA-2048", TimeSpan.FromMilliseconds(rsa2048Pub.TotalMilliseconds / exportIterations),
            TimeSpan.FromMilliseconds(ecdsaPub.TotalMilliseconds / exportIterations));
        PrintRow("RSA-4096", TimeSpan.FromMilliseconds(rsa4096Pub.TotalMilliseconds / exportIterations),
            TimeSpan.FromMilliseconds(ecdsaPub.TotalMilliseconds / exportIterations));
        PrintRow("ECDSA P-256", TimeSpan.FromMilliseconds(ecdsaPub.TotalMilliseconds / exportIterations),
            TimeSpan.FromMilliseconds(ecdsaPub.TotalMilliseconds / exportIterations));
        _output.WriteLine("");

        // === Key Sizes ===
        _output.WriteLine("Key Sizes:");
        _output.WriteLine("-----------------------------------------------------------");
        _output.WriteLine($"  RSA-2048 public:   {rsa2048Signer.PublicKey.Length,4} bytes");
        _output.WriteLine($"  RSA-4096 public:   {rsa4096Signer.PublicKey.Length,4} bytes");
        _output.WriteLine($"  ECDSA P-256 pub:   {ecdsaSigner.PublicKey.Length,4} bytes");
        _output.WriteLine("");
        _output.WriteLine($"  RSA-2048 private:  {rsa2048Signer.PrivateKey.Length,4} bytes");
        _output.WriteLine($"  RSA-4096 private:  {rsa4096Signer.PrivateKey.Length,4} bytes");
        _output.WriteLine($"  ECDSA P-256 priv:  {ecdsaSigner.PrivateKey.Length,4} bytes");
        _output.WriteLine("===========================================================");
    }

    /// <summary>
    /// Benchmarks key generation using the modern .NET crypto API directly,
    /// without the caching behavior of RSACryptoServiceProvider.
    /// Includes both RSA.Create() and ECDsa.Create() for an honest comparison.
    /// </summary>
    [Fact]
    public void Benchmark_ModernApi_NoCaching()
    {
        const int iterations = 5;

        _output.WriteLine("===========================================================");
        _output.WriteLine("  Modern API Benchmark: RSA.Create() vs ECDsa.Create()");
        _output.WriteLine("  (no caching, honest generation times)");
        _output.WriteLine("===========================================================");
        _output.WriteLine("");

        // Warm up to initialize crypto providers
        for (var i = 0; i < 3; i++)
        {
            using var rsa = RSA.Create(2048);
            var rsaParams = rsa.ExportParameters(true);
            GC.KeepAlive(rsaParams);

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ecParams = ecdsa.ExportParameters(true);
            GC.KeepAlive(ecParams);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        _output.WriteLine($"Key Generation + Export ({iterations} iterations, median):");
        _output.WriteLine("-----------------------------------------------------------");

        var rsaCreate2048 = MeasureEach(iterations, () =>
        {
            using var rsa = RSA.Create(2048);
            var parameters = rsa.ExportParameters(true);
            GC.KeepAlive(parameters);
        });

        var rsaCreate4096 = MeasureEach(iterations, () =>
        {
            using var rsa = RSA.Create(4096);
            var parameters = rsa.ExportParameters(true);
            GC.KeepAlive(parameters);
        });

        var ecdsaCreate = MeasureEach(iterations, () =>
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdsa.ExportParameters(true);
            GC.KeepAlive(parameters);
        });

        PrintRow("RSA.Create(2048)", rsaCreate2048, rsaCreate2048);
        PrintRow("RSA.Create(4096)", rsaCreate4096, rsaCreate2048);
        PrintRow("ECDsa.Create(P-256)", ecdsaCreate, rsaCreate2048);
        _output.WriteLine("");
        _output.WriteLine("Note: RSA.Create() uses RSACng on Windows (no caching),");
        _output.WriteLine("which gives honest generation times unlike RSACryptoServiceProvider.");
        _output.WriteLine("===========================================================");
    }

    /// <summary>
    /// Measures each iteration separately and returns the median.
    /// Best for slow operations like key generation where GC pressure matters.
    /// </summary>
    private static TimeSpan MeasureEach(int iterations, Action action)
    {
        var times = new List<double>();

        for (var i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        return TimeSpan.FromMilliseconds(times[times.Count / 2]); // median
    }

    /// <summary>
    /// Measures total time for all iterations (suitable for fast operations).
    /// </summary>
    private static TimeSpan Measure(int iterations, Action action)
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            action();
        sw.Stop();
        return sw.Elapsed;
    }

    /// <summary>
    /// Warms up crypto providers and JIT to avoid cold-start noise.
    /// </summary>
    private static void Warmup()
    {
        for (var i = 0; i < 5; i++)
        {
            using var rsa = new RsaKeyPair(2048);
            var data = new byte[32];
            var sig = rsa.CreateSignature(data);
            rsa.VerifySignature(data, sig);
            _ = rsa.PublicKey;

            using var ec = new EcdsaSessionKeyPair();
            var ecSig = ec.CreateSignature(data);
            ec.VerifySignature(data, ecSig);
            _ = ec.PublicKey;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Prints a benchmark row with time per operation and ratio to baseline.
    /// </summary>
    private void PrintRow(string name, TimeSpan avgPerOp, TimeSpan baseline)
    {
        var micros = avgPerOp.TotalMilliseconds * 1000.0;
        var ratio = avgPerOp.TotalMilliseconds / baseline.TotalMilliseconds;

        string ratioStr;
        if (ratio < 0.95)
            ratioStr = $"{1.0 / ratio:F1}× faster";
        else if (ratio > 1.05)
            ratioStr = $"{ratio:F1}× slower";
        else
            ratioStr = "baseline";

        _output.WriteLine($"  {name,-22} {micros,12:F2} μs/op   {ratioStr}");
    }
}