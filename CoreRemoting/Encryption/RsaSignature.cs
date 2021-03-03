using System.Security.Cryptography;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Provides methods to create and verify RSA signatures.
    /// </summary>
    public static class RsaSignature
    {
        /// <summary>
        /// Creates a signature from the SHA256 hash value of the specified raw data.
        /// The private key of the provided key pair is used to create the signature.
        /// </summary>
        /// <param name="keySize">Key size (1024, 2048, 4096, ...)</param>
        /// <param name="sendersPrivateKeyBlob">Private key of the sender</param>
        /// <param name="rawData">Raw data to create a signature of</param>
        /// <returns>Signature</returns>
        public static byte[] CreateSignature(int keySize, byte[] sendersPrivateKeyBlob, byte[] rawData)
        {
            // Create SHA256 hash of the raw data
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(rawData);

            // Import the sender's private key
            using var sendersPrivateKey = new RSACryptoServiceProvider(dwKeySize: keySize);
            sendersPrivateKey.ImportCspBlob(sendersPrivateKeyBlob);

            // Create an RSAPKCS1SignatureFormatter object and pass it the RSA instance to transfer the private key.
            var signatureFormatter = new RSAPKCS1SignatureFormatter(sendersPrivateKey);

            // Set the hash algorithm to SHA256
            signatureFormatter.SetHashAlgorithm("SHA256");

            // Create signature from the hash
            return signatureFormatter.CreateSignature(hash);   
        }

        /// <summary>
        /// Verifies a signature with the public key of the sender for the provided raw data.   
        /// </summary>
        /// <param name="keySize">Key size (1024, 2048, 4096, ...)</param>
        /// <param name="sendersPublicKeyBlob">Public key of the sender</param>
        /// <param name="rawData">Raw data which signature of should be verified</param>
        /// <param name="signature">The signature to verify</param>
        /// <returns>True is the signature is valid, otherwise false</returns>
        public static bool VerifySignature(int keySize, byte[] sendersPublicKeyBlob, byte[] rawData, byte[] signature)
        {
            // Create SHA256 hash of the raw data
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(rawData);
            
            // Import the sender's public key
            using var sendersPublicKey = new RSACryptoServiceProvider(keySize) { PersistKeyInCsp = false };
            sendersPublicKey.ImportCspBlob(sendersPublicKeyBlob);

            // Create an RSAPKCS1SignatureDeformatter object and pass it the RSA instance to transfer the public key.
            var signatureDeformatter = new RSAPKCS1SignatureDeformatter(sendersPublicKey);
            
            // Set the hash algorithm to SHA256
            signatureDeformatter.SetHashAlgorithm("SHA256");

            // Verify the signature using the computed hash
            return signatureDeformatter.VerifySignature(hash, signature);
        }
    }
}