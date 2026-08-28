using System;
using static System.BitConverter;

namespace CoreRemoting.Channels.Quic;

internal class QuicHandshakeMessage
{
    private const byte SessionIdMarker = 0x02, SignatureMarker = 0x04, PublicKeyMarker = 0x03;

    /// <summary>
    /// Gets or sets a flag indicating whether message encryption is enabled.
    /// </summary>
    public bool MessageEncryption { get; set; }

    /// <summary>
    /// Gets or sets resumable session identity.
    /// </summary>
    public Guid? ResumableSessionId { get; set; }

    /// <summary>
    /// Gets or sets resumable session signature.
    /// </summary>
    public byte[] SessionSignature { get; set; }

    /// <summary>
    /// Gets or sets client's public key.
    /// </summary>
    public byte[] ClientPublicKey { get; set; }

    /// <summary>
    /// Serializes the handshake message into a byte array.
    /// </summary>
    public byte[] ToByteArray()
    {
        var result = new[] { Convert.ToByte(MessageEncryption) };

        if (ResumableSessionId is Guid guid)
            result = [.. result, SessionIdMarker, .. guid.ToByteArray()];

        if (SessionSignature is { Length: > 0 } signature)
            result = [.. result, SignatureMarker, .. GetBytes(signature.Length), .. signature];

        if (ClientPublicKey is { Length: > 0 } key)
            result = [.. result, PublicKeyMarker, .. GetBytes(key.Length), .. key];

        return result;
    }

    /// <summary>
    /// Deserializes the handshake message from a byte array.
    /// </summary>
    /// <param name="data">Serialized byte array, can be null.</param>
    public static QuicHandshakeMessage FromByteArray(byte[]? data)
    {
        if (data is not { Length: > 0 })
            return new QuicHandshakeMessage();

        int i = 0;
        var msg = new QuicHandshakeMessage
        {
            MessageEncryption = data[i++] == 1
        };

        if (i < data.Length && data[i] == SessionIdMarker)
        {
            i++;
            msg.ResumableSessionId = new Guid(data.AsSpan(i, 16));
            i += 16;
        }

        if (i < data.Length && data[i] == SignatureMarker)
        {
            i++;
            int len = ToInt32(data, i);
            i += 4;
            msg.SessionSignature = data.AsSpan(i, len).ToArray();
            i += len;
        }

        if (i < data.Length && data[i] == PublicKeyMarker)
        {
            i++;
            int len = ToInt32(data, i);
            i += 4;
            msg.ClientPublicKey = data.AsSpan(i, len).ToArray();
        }

        return msg;
    }
}