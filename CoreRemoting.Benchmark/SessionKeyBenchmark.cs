using System;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using CoreRemoting.Encryption;

namespace CoreRemoting.Benchmark;

[MemoryDiagnoser]
public class SessionKeyPairBenchmark
{
    private byte[] _challenge = null!;
    private RsaKeyPair _rsa2048 = null!;
    private RsaKeyPair _rsa4096 = null!;
    private EcdsaSessionKeyPair _ecdsa = null!;
    private byte[] _rsa2048Sig = null!;
    private byte[] _rsa4096Sig = null!;
    private byte[] _ecdsaSig = null!;

    [GlobalSetup]
    public void Setup()
    {
        _challenge = new byte[32];
        new Random(42).NextBytes(_challenge);

        _rsa2048 = new RsaKeyPair(2048);
        _rsa4096 = new RsaKeyPair(4096);
        _ecdsa = new EcdsaSessionKeyPair();

        // Force key generation (RSA is lazy)
        _ = _rsa2048.PrivateKey;
        _ = _rsa4096.PrivateKey;
        _ = _ecdsa.PrivateKey;

        // Pre-cache public keys to measure only property access, not export
        _ = _rsa2048.PublicKey;
        _ = _rsa4096.PublicKey;
        _ = _ecdsa.PublicKey;

        _rsa2048Sig = _rsa2048.CreateSignature(_challenge);
        _rsa4096Sig = _rsa4096.CreateSignature(_challenge);
        _ecdsaSig = _ecdsa.CreateSignature(_challenge);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rsa2048?.Dispose();
        _rsa4096?.Dispose();
        _ecdsa?.Dispose();
    }

    // --- Key generation ---
    [Benchmark]
    public void Rsa2048_Generate()
    {
        using var key = new RsaKeyPair(2048);
        _ = key.PrivateKey;
    }

    [Benchmark]
    public void Rsa4096_Generate()
    {
        using var key = new RsaKeyPair(4096);
        _ = key.PrivateKey;
    }

    [Benchmark]
    public void Ecdsa_Generate()
    {
        using var key = new EcdsaSessionKeyPair();
        _ = key.PrivateKey;
    }

    // --- Signing ---
    [Benchmark]
    public byte[] Rsa2048_Sign() => _rsa2048.CreateSignature(_challenge);

    [Benchmark]
    public byte[] Rsa4096_Sign() => _rsa4096.CreateSignature(_challenge);

    [Benchmark]
    public byte[] Ecdsa_Sign() => _ecdsa.CreateSignature(_challenge);

    // --- Verification ---
    [Benchmark]
    public bool Rsa2048_Verify() => _rsa2048.VerifySignature(_challenge, _rsa2048Sig);

    [Benchmark]
    public bool Rsa4096_Verify() => _rsa4096.VerifySignature(_challenge, _rsa4096Sig);

    [Benchmark]
    public bool Ecdsa_Verify() => _ecdsa.VerifySignature(_challenge, _ecdsaSig);

    // --- Public key export (now measures only property access) ---
    [Benchmark]
    public byte[] Rsa2048_ExportPublic() => _rsa2048.PublicKey;

    [Benchmark]
    public byte[] Rsa4096_ExportPublic() => _rsa4096.PublicKey;

    [Benchmark]
    public byte[] Ecdsa_ExportPublic() => _ecdsa.PublicKey;
}
