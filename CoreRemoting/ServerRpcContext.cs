using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting;

/// <summary>
/// Describes the server side context of a RPC call.
/// </summary>
public class ServerRpcContext
{
    /// <summary>
    /// Gets or sets the unique key of RPC call.
    /// </summary>
    public Guid UniqueCallKey { get; set; }

    /// <summary>
    /// Gets or sets the last exception that is occurred.
    /// </summary>
    public Exception Exception { get; set; }

    /// <summary>
    /// Gets or sets a value whether the authentication is required.
    /// </summary>
    public bool AuthenticationRequired { get; set; }

    /// <summary>
    /// Gets or sets a value whether the call is canceled by event handler.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Gets the message that describes the remote method call.
    /// </summary>
    public MethodCallMessage MethodCallMessage { get; internal set; }

    /// <summary>
    /// Gets or sets the unwrapped method call parameter values.
    /// </summary>
    public object[] MethodCallParameterValues { get; set; }

    /// <summary>
    /// Gets or sets the unwrapped method call parameter types.
    /// </summary>
    public Type[] MethodCallParameterTypes { get; set; }

    /// <summary>
    /// Gets or sets the message that contains the results of a remote method call.
    /// </summary>
    public MethodCallResultMessage MethodCallResultMessage { get; set; }

    /// <summary>
    /// Gets or sets service event stub.
    /// </summary>
    public EventStub EventStub { get; set; }

    /// <summary>
    /// Gets or sets the instance of the service, on which the method is called.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public object ServiceInstance { get; set; }

    /// <summary>
    /// Gets or sets the CoreRemoting session that is used to handle the RPC.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public RemotingSession Session { get; set; }
}