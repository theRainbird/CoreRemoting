using System.Text;
using CoreRemoting.Encryption;
using NUnit.Framework;

namespace CoreRemoting.Tests
{
    public class SignatureTests
    {
        [Test]
        public void VerifySignature_should_return_true_if_signature_is_valid()
        {
            int keySize = 4096;
            var keyPair = new RsaKeyPair(keySize);

            var data = Encoding.UTF8.GetBytes("Test");

            var signature =
                RsaSignature.CreateSignature(
                    keySize: keySize,
                    sendersPrivateKeyBlob: keyPair.PrivateKey,
                    rawData: data);

            var result =
                RsaSignature.VerifySignature(
                    keySize: keySize,
                    sendersPublicKeyBlob: keyPair.PublicKey,
                    rawData: data,
                    signature: signature);
            
            Assert.AreEqual(512, signature.Length);
            Assert.IsTrue(result);
        }
    }
}