using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CoreRemoting.Encryption;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization.Bson;
using Xunit;

namespace CoreRemoting.Tests;

/// <summary>
/// Message signing and encryption run for every message. These tests make sure that the optimized implementations
/// stay wire-compatible with peers using the previous implementations and are safe to use from multiple threads.
/// </summary>
public class MessageEncryptionTests
{
    private const int KeySize = 2048;

    #region RSA signatures

    [Fact]
    public void RsaKeyPair_CreateSignature_should_be_identical_to_RsaSignature_CreateSignature()
    {
        using var keyPair = new RsaKeyPair(KeySize);
        var data = Encoding.UTF8.GetBytes("Message to sign");

        var signatureFromKeyPair = keyPair.CreateSignature(data);
        var signatureFromKeyBlob =
            RsaSignature.CreateSignature(
                keySize: KeySize,
                sendersPrivateKeyBlob: keyPair.PrivateKey,
                rawData: data);

        // PKCS#1 v1.5 signatures are deterministic, so peers with the previous implementation see no difference.
        Assert.Equal(signatureFromKeyBlob, signatureFromKeyPair);
    }

    [Fact]
    public void RsaKeyPair_CreateSignature_should_be_verifiable_with_public_key_only()
    {
        using var keyPair = new RsaKeyPair(KeySize);
        var data = Encoding.UTF8.GetBytes("Message to sign");

        var signature = keyPair.CreateSignature(data);

        Assert.Equal(KeySize / 8, signature.Length);
        Assert.True(
            RsaSignature.VerifySignature(
                keySize: KeySize,
                sendersPublicKeyBlob: keyPair.PublicKey,
                rawData: data,
                signature: signature));
    }

    [Fact]
    public void RsaKeyPair_CreateSignature_should_not_verify_for_modified_data()
    {
        using var keyPair = new RsaKeyPair(KeySize);
        var data = Encoding.UTF8.GetBytes("Message to sign");
        var signature = keyPair.CreateSignature(data);

        var modifiedData = Encoding.UTF8.GetBytes("Message to sigN");

        Assert.False(
            RsaSignature.VerifySignature(
                keySize: KeySize,
                sendersPublicKeyBlob: keyPair.PublicKey,
                rawData: modifiedData,
                signature: signature));
    }

    [Fact]
    public void RsaKeyPair_CreateSignature_should_create_valid_signatures_when_called_concurrently()
    {
        using var keyPair = new RsaKeyPair(KeySize);
        var publicKey = keyPair.PublicKey;

        var allSignaturesValid =
            Enumerable.Range(0, 200)
                .AsParallel()
                .WithDegreeOfParallelism(8)
                .Select(index =>
                {
                    var data = Encoding.UTF8.GetBytes("Message " + index);
                    var signature = keyPair.CreateSignature(data);
                    return RsaSignature.VerifySignature(KeySize, publicKey, data, signature);
                })
                .All(isValid => isValid);

        Assert.True(allSignaturesValid);
    }

    #endregion

    #region AES encryption

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1024 * 1024 + 3)]
    public void AesEncryption_should_be_compatible_with_previous_stream_based_implementation(int dataLength)
    {
        var data = CreateRandomBytes(dataLength);
        var sharedSecret = Guid.NewGuid().ToByteArray();
        var iv = AesEncryption.GenerateIv();

        var encryptedData = AesEncryption.Encrypt(data, sharedSecret, iv);
        var encryptedDataOfPreviousImplementation = EncryptLikePreviousImplementation(data, sharedSecret, iv);

        Assert.Equal(encryptedDataOfPreviousImplementation, encryptedData);
        Assert.Equal(data, AesEncryption.Decrypt(encryptedDataOfPreviousImplementation, sharedSecret, iv));
        Assert.Equal(data, DecryptLikePreviousImplementation(encryptedData, sharedSecret, iv));
    }

    [Fact]
    public void AesEncryption_Decrypt_should_not_return_original_data_for_wrong_shared_secret()
    {
        var data = Encoding.UTF8.GetBytes("Secret message with more than one AES block");
        var iv = AesEncryption.GenerateIv();
        var encryptedData = AesEncryption.Encrypt(data, Guid.NewGuid().ToByteArray(), iv);

        byte[] decryptedData;
        try
        {
            decryptedData = AesEncryption.Decrypt(encryptedData, Guid.NewGuid().ToByteArray(), iv);
        }
        catch (CryptographicException)
        {
            // Expected in almost all cases: the padding of the wrongly decrypted last block is invalid.
            return;
        }

        Assert.NotEqual(data, decryptedData);
    }

    [Fact]
    public void AesEncryption_GenerateIv_should_return_unique_values_of_AES_block_size()
    {
        var ivs = Enumerable.Range(0, 100).Select(_ => AesEncryption.GenerateIv()).ToList();

        Assert.All(ivs, iv => Assert.Equal(16, iv.Length));
        Assert.Equal(ivs.Count, ivs.Select(Convert.ToBase64String).Distinct().Count());
    }

    [Fact]
    public void AesEncryption_should_round_trip_when_used_concurrently_with_different_keys()
    {
        var allRoundTripsCorrect =
            Enumerable.Range(0, 500)
                .AsParallel()
                .WithDegreeOfParallelism(8)
                .Select(index =>
                {
                    var data = CreateRandomBytes(100 + index);
                    var sharedSecret = Guid.NewGuid().ToByteArray();
                    var iv = AesEncryption.GenerateIv();
                    var encryptedData = AesEncryption.Encrypt(data, sharedSecret, iv);
                    return AesEncryption.Decrypt(encryptedData, sharedSecret, iv).SequenceEqual(data);
                })
                .All(isCorrect => isCorrect);

        Assert.True(allRoundTripsCorrect);
    }

    #endregion

    #region Wire messages

    [Fact]
    public void MessageEncryptionManager_should_return_original_data_from_signed_and_encrypted_wire_message()
    {
        var manager = new MessageEncryptionManager();
        var serializer = new BsonSerializerAdapter();
        using var keyPair = new RsaKeyPair(KeySize);
        var sharedSecret = Guid.NewGuid().ToByteArray();
        var serializedMessage = Encoding.UTF8.GetBytes("Serialized method call");

        var wireMessage = manager.CreateWireMessage("rpc", serializedMessage, serializer, keyPair, sharedSecret);
        var decryptedData = manager.GetDecryptedMessageData(wireMessage, serializer, sharedSecret, keyPair.PublicKey);

        Assert.NotEqual(serializedMessage, wireMessage.Data);
        Assert.Equal(serializedMessage, decryptedData);
    }

    [Fact]
    public void MessageEncryptionManager_should_reject_wire_message_signed_by_another_key()
    {
        var manager = new MessageEncryptionManager();
        var serializer = new BsonSerializerAdapter();
        using var sendersKeyPair = new RsaKeyPair(KeySize);
        using var otherKeyPair = new RsaKeyPair(KeySize);
        var sharedSecret = Guid.NewGuid().ToByteArray();

        var wireMessage =
            manager.CreateWireMessage("rpc", Encoding.UTF8.GetBytes("Serialized method call"), serializer, sendersKeyPair, sharedSecret);

        Assert.Throws<SecurityException>(() =>
            manager.GetDecryptedMessageData(wireMessage, serializer, sharedSecret, otherKeyPair.PublicKey));
    }

    #endregion

    #region Reference implementation (CoreRemoting 1.3.7)

    private static byte[] EncryptLikePreviousImplementation(byte[] data, byte[] sharedSecret, byte[] iv)
    {
        using var aes = CreateAesLikePreviousImplementation(sharedSecret, iv);
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var memoryStream = new MemoryStream();
        using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
        {
            cryptoStream.Write(data, 0, data.Length);
            cryptoStream.FlushFinalBlock();
        }

        return memoryStream.ToArray();
    }

    private static byte[] DecryptLikePreviousImplementation(byte[] encryptedData, byte[] sharedSecret, byte[] iv)
    {
        using var aes = CreateAesLikePreviousImplementation(sharedSecret, iv);
        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var memoryStream = new MemoryStream(encryptedData);
        using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
        using var decryptedStream = new MemoryStream();
        cryptoStream.CopyTo(decryptedStream);
        return decryptedStream.ToArray();
    }

    private static Aes CreateAesLikePreviousImplementation(byte[] sharedSecret, byte[] iv)
    {
        var aes = Aes.Create();
        using var sha256 = SHA256.Create();
        aes.Key = sha256.ComputeHash(sharedSecret);
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    private static byte[] CreateRandomBytes(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);
        return data;
    }

    #endregion
}
