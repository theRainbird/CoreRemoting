using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using stakx.DynamicProxy;

namespace CoreRemoting;

/// <summary>
/// Implements a proxy of a remote service that is hosted on a CoreRemoting server..
/// This is doing the RPC magic of CoreRemoting at client side.
/// </summary>
/// <typeparam name="TServiceInterface">Type of the remote service's interface (also known as contract of the service)</typeparam>
public class ServiceProxy<TServiceInterface> : AsyncInterceptor, IServiceProxy
{
    private RemotingClient _client;

    /// <summary>
    /// Creates a new instance of the ServiceProxy class.
    /// </summary>
    /// <param name="client">CoreRemoting client to be used for client/server communication</param>
    /// <param name="serviceName">Unique name of the remote service</param>
    public ServiceProxy(RemotingClient client, string serviceName = "")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        var serviceInterfaceType = typeof(TServiceInterface);

        ServiceName =
            string.IsNullOrWhiteSpace(serviceName)
                ? serviceInterfaceType.FullName
                : serviceName;
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~ServiceProxy()
    {
        ((IServiceProxy)this).Shutdown();
    }

    /// <summary>
    /// Shutdown service proxy and free resources.
    /// </summary>
    void IServiceProxy.Shutdown()
    {
        if (_client != null)
        {
            _ = _client.ClientDelegateRegistry.UnregisterClientDelegatesOfServiceProxy(this);
            _client = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the type of the service interface.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public Type ServiceInterfaceType => typeof(TServiceInterface);

    /// <summary>
    /// Gets the name of the service;
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public string ServiceName { get; }

    /// <summary>
    /// Intercepts a synchronous call of a member on the proxy object.
    /// </summary>
    /// <param name="invocation">Intercepted invocation details</param>
    /// <exception cref="RemotingException">Thrown if a remoting operation has been failed</exception>
    /// <exception cref="NotSupportedException">Thrown if a member of a type marked as OneWay is intercepted, that has another return type than void</exception>
    /// <exception cref="RemoteInvocationException">Thrown if an exception occurred when the remote method was invoked</exception>
    protected override void Intercept(IInvocation invocation)
    {
        var method = invocation.Method;
        var oneWay = method.GetCustomAttribute<OneWayAttribute>() != null;
        var returnType = method.ReturnType;

        if (oneWay && returnType != typeof(void))
            throw new NotSupportedException("OneWay methods must not have a return type.");

        var arguments = _client.MapArguments(this, invocation.Arguments);

        _client.SubscriptionTracker.Track(method,
            SubscriptionTracker.ExtractHandlerKey(arguments));

        var remoteMethodCallMessage =
            _client.MethodCallMessageBuilder.BuildMethodCallMessage(
                serializer: _client.Serializer,
                remoteServiceName: ServiceName,
                targetMethod: method,
                args: arguments);

        var sendTask = _client.InvokeRemoteMethod(remoteMethodCallMessage, oneWay);

        if (!sendTask.Wait(
                _client.Config.SendTimeout == 0
                    ? -1 // Infinite
                    : _client.Config.SendTimeout * 1000))
        {
            throw new TimeoutException($"Send timeout ({_client.Config.SendTimeout}) exceeded.");
        }

        var clientRpcContext = sendTask.Result;

        if (clientRpcContext.Error)
        {
            throw clientRpcContext.RemoteException ??
                new RemoteInvocationException();
        }

        var resultMessage = clientRpcContext.ResultMessage;

        if (resultMessage == null)
        {
            invocation.ReturnValue = null;
            return;
        }

        var parameterInfos = method.GetParameters();

        var serializer = _client.Serializer;

        foreach (var outParameterValue in resultMessage.OutParameters)
        {
            var parameterInfo =
                parameterInfos.First(p => p.Name == outParameterValue.ParameterName);

            if (outParameterValue.IsOutValueNull)
                invocation.Arguments[parameterInfo.Position] = null;
            else
            {
                if (serializer.EnvelopeNeededForParameterSerialization)
                {
                    var outParamEnvelope =
                        serializer.Deserialize<Envelope>((byte[])outParameterValue.OutValue);

                    invocation.Arguments[parameterInfo.Position] = outParamEnvelope.Value;
                }
                else
                {
                    var typeToDeserialize = parameterInfo.ParameterType.IsByRef ? parameterInfo.ParameterType.GetElementType() : parameterInfo.ParameterType;
                    var outParamValue =
                        serializer.Deserialize(typeToDeserialize, (byte[])outParameterValue.OutValue);

                    invocation.Arguments[parameterInfo.Position] = outParamValue;
                }
            }
        }

        invocation.ReturnValue = _client.UnmapReturnValue(resultMessage);

        CallContext.RestoreFromSnapshot(resultMessage.CallContextSnapshot);
    }

    /// <summary>
    /// Intercepts an asynchronous call of a member on the proxy object.
    /// </summary>
    /// <param name="invocation">Intercepted invocation details</param>
    /// <returns>Asynchronous running task</returns>
    /// <exception cref="RemotingException">Thrown if a remoting operation has been failed</exception>
    /// <exception cref="NotSupportedException">Thrown if a member of a type marked as OneWay is intercepted, that has another return type than void</exception>
    /// <exception cref="RemoteInvocationException">Thrown if an exception occurred when the remote method was invoked</exception>
    protected override async ValueTask InterceptAsync(IAsyncInvocation invocation)
    {
        var method = invocation.Method;
        var oneWay = method.GetCustomAttribute<OneWayAttribute>() != null;
        var returnType = method.ReturnType;

        if (oneWay && returnType != typeof(void))
            throw new NotSupportedException("OneWay methods must not have a return type.");

        var arguments = _client.MapArguments(this, invocation.Arguments);

        var remoteMethodCallMessage =
            _client.MethodCallMessageBuilder.BuildMethodCallMessage(
                serializer: _client.Serializer,
                remoteServiceName: ServiceName,
                targetMethod: method,
                args: arguments);

        var clientRpcContext =
            await _client.InvokeRemoteMethod(remoteMethodCallMessage, oneWay)
                .ConfigureAwait(false);

        if (clientRpcContext.Error)
        {
            throw clientRpcContext.RemoteException ??
                new RemoteInvocationException();
        }

        var resultMessage = clientRpcContext.ResultMessage;

        if (resultMessage == null)
        {
            invocation.Result = null;
            return;
        }

        invocation.Result = _client.UnmapReturnValue(resultMessage);

        CallContext.RestoreFromSnapshot(resultMessage.CallContextSnapshot);
    }
}
