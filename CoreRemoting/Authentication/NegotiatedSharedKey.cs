using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication;

/// <summary>
/// Describes the negotiated shared key data.
/// </summary>
[DataContract]
[Serializable]
public class NegotiatedSharedKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NegotiatedSharedKey"/> class.
    /// </summary>
    public NegotiatedSharedKey()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NegotiatedSharedKey"/> class.
    /// </summary>
    /// <param name="keyingMaterial">Gets or sets keying material used for session key derivation.</param>
    /// <param name="isSerialized">Gets or sets a value indicating whether the keying material should be serialized.</param>
    public NegotiatedSharedKey(byte[] keyingMaterial, bool isSerialized = false)
    {
        InputKeyMaterial = keyingMaterial;
        IsSerialized = isSerialized;
    }

    /// <summary>
    /// Gets or sets an optional shared key negotiated during authentication (e.g., the session key derived by SRP).
    /// If set, both endpoints switch to this key for symmetric message encryption as soon as the
    /// authentication is completed, replacing the default session key from the handshake.
    /// </summary>
    [DataMember]
    public byte[] InputKeyMaterial { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the shared key should be serialized and sent over the wire.
    /// </summary>
    /// <remarks>
    /// Authentication protocols like SRP and J-PAKE require that shared key is derived independently by client and server.
    /// </remarks>
    [DataMember]
    public bool IsSerialized { get; set; }

    /// <summary>
    /// Gets a value indicating whether input keying material exists.
    /// </summary>
    public bool ContainsKeyMaterial => InputKeyMaterial is { Length: > 0 };

    /// <summary>
    /// Clones the current instance of the negotiated shared key for serialization.
    /// If input keying material shouldn't be serialized, it will be excluded.
    /// </summary>
    public NegotiatedSharedKey GetSerializableCopy() =>
        new NegotiatedSharedKey(IsSerialized ? InputKeyMaterial : null);
}
