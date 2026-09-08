using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging;

/// <summary>
/// Message containing client's handshake metadata.
/// </summary>
[DataContract]
public class ClientHandshakeMessage
{
    /// <summary>
    /// Gets all client handshake metadata.
    /// </summary>
    [DataMember]
    public Dictionary<string, string> Metadata { get; } = new();

    /// <summary>
    /// Sets the value of the given property.
    /// Supports: string, bool, int, decimal, byte[], Guid, Guid?, DateTime, DateTimeOffset, IPAddress.
    /// Null values are removed from the dictionary.
    /// </summary>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The value to set.</param>
    public void SetValue<T>(string name, T value)
    {
        if (value == null)
        {
            Metadata.Remove(name);
            return;
        }

        Metadata[name] = value switch
        {
            byte[] bytes => Convert.ToBase64String(bytes),
            Guid guid => guid.ToString("D"),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            IPAddress ip => ip.ToString(),
            bool b => b ? "1" : "0",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Gets the value of the given property.
    /// Supports: string, bool, int, decimal, byte[], Guid, Guid?, DateTime, DateTimeOffset, IPAddress.
    /// Returns defaultValue if key is not found or value cannot be converted.
    /// </summary>
    /// <typeparam name="T">The type of the property</typeparam>
    /// <param name="name">The name of the property</param>
    /// <param name="defaultValue">Default value to return if key is not found</param>
    public T GetValue<T>(string name, T defaultValue = default)
    {
        if (!Metadata.TryGetValue(name, out var strValue) || string.IsNullOrEmpty(strValue))
            return defaultValue;

        try
        {
            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            return (T)(underlyingType switch
            {
                Type t when t == typeof(byte[]) => Convert.FromBase64String(strValue),
                Type t when t == typeof(Guid) => Guid.Parse(strValue),
                Type t when t == typeof(DateTime) => DateTime.Parse(strValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Type t when t == typeof(DateTimeOffset) => DateTimeOffset.Parse(strValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Type t when t == typeof(IPAddress) => IPAddress.Parse(strValue),
                Type t when t == typeof(bool) => strValue switch { "1" => true, "0" => false, _ => bool.Parse(strValue) },
                _ => Convert.ChangeType(strValue, underlyingType, CultureInfo.InvariantCulture)
            });
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether message encryption is enabled.
    /// </summary>
    [IgnoreDataMember]
    public bool MessageEncryption
    {
        get => GetValue(nameof(MessageEncryption), false);
        set => SetValue(nameof(MessageEncryption), value);
    }

    /// <summary>
    /// Gets or sets client's public key used for validating signatures.
    /// </summary>
    [IgnoreDataMember]
    public byte[] ClientPublicKey
    {
        get => GetValue<byte[]>(nameof(ClientPublicKey));
        set => SetValue(nameof(ClientPublicKey), value);
    }

    /// <summary>
    /// Gets or sets client's resumable session identity.
    /// </summary>
    [IgnoreDataMember]
    public Guid? ResumableSessionId
    {
        get => GetValue<Guid?>(nameof(ResumableSessionId));
        set => SetValue(nameof(ResumableSessionId), value);
    }

    /// <summary>
    /// Gets or sets client's session signature.
    /// </summary>
    [IgnoreDataMember]
    public byte[] SessionSignature
    {
        get => GetValue<byte[]>(nameof(SessionSignature));
        set => SetValue(nameof(SessionSignature), value);
    }
}