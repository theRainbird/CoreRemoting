using System;
using System.Security.Cryptography;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Represents a single RSA public key from a JSON Web Key Set (JWKS).
/// </summary>
public class JwksKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JwksKey"/> class.
    /// </summary>
    internal JwksKey(string kid, RSAParameters rsaParameters)
    {
        Kid = kid;
        RsaParameters = rsaParameters;
    }

    /// <summary>
    /// Gets the key identifier ("kid") or null.
    /// </summary>
    public string Kid { get; }

    /// <summary>
    /// Gets the RSA parameters (modulus and exponent).
    /// </summary>
    public RSAParameters RsaParameters { get; }
}
