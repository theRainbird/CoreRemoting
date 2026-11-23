using System;
using System.Runtime.Serialization;

namespace CoreRemoting;

/// <summary>
/// Exception to be thrown, if a server refuses to resume a session the client
/// requested (e.g. the parked session was lost or the resume signature did not
/// match) and the server's <c>AllowSessionResumeFallback</c> policy forbids the
/// client to silently continue with a newly created session.
/// </summary>
/// <remarks>
/// The rejection is terminal for the requested session identity: retrying the
/// resume of the same session ID can never succeed. The automatic reconnect loop
/// stops on this exception instead of retrying indefinitely; an explicit call to
/// <c>Connect</c> by the application may still be used to establish a fresh
/// session.
/// </remarks>
[Serializable]
public class SessionResumeRejectedException : RemotingException
{
    /// <summary>
    /// Creates a new instance of the SessionResumeRejectedException class.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerEx">Optional inner exception</param>
    public SessionResumeRejectedException(string message, Exception innerEx = null) :
        base(message, innerEx)
    {
    }

    /// <summary>
    /// Without this constructor, deserialization will fail.
    /// </summary>
    /// <param name="info">Serialization info</param>
    /// <param name="context">Streaming context</param>
    [Obsolete("Only to preserve backward compatibility")]
    public SessionResumeRejectedException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
