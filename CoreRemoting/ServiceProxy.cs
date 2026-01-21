using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Toolbox;
using Serialize.Linq.Extensions;
using Serialize.Linq.Nodes;
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
            _client.ClientDelegateRegistry.UnregisterClientDelegatesOfServiceProxy(this);
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

        var arguments = MapArguments(invocation.Arguments);

        var remoteMethodCallMessage =
            _client.MethodCallMessageBuilder.BuildMethodCallMessage(
                serializer: _client.Serializer,
                remoteServiceName: ServiceName,
                targetMethod: method,
                args: arguments);

        var sendTask =
            _client.InvokeRemoteMethod(remoteMethodCallMessage, oneWay);
        sendTask.ConfigureAwait(false);

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

        invocation.ReturnValue = UnmapReturnValue(resultMessage);

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

        var arguments = MapArguments(invocation.Arguments);

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

        invocation.Result = UnmapReturnValue(resultMessage);

        CallContext.RestoreFromSnapshot(resultMessage.CallContextSnapshot);
    }

    /// <summary>
    /// Unmaps the return value back to its original type.
    /// </summary>
    /// <param name="resultMessage">Method call result message.</param>
    private object UnmapReturnValue(MethodCallResultMessage resultMessage)
    {
        // Unwrap an enveloped value
        var returnValue =
            resultMessage.IsReturnValueNull
                ? null
                : resultMessage.ReturnValue is Envelope returnValueEnvelope
                    ? returnValueEnvelope.Value
                    : resultMessage.ReturnValue;

        // Create a proxy to remote service, if return type is a service reference
        if (returnValue is ServiceReference serviceReference)
            returnValue = _client.CreateProxy(serviceReference);

        // Restore LINQ expression, if return type is a serializable expression
        if (returnValue is ExpressionNode exprNode)
            returnValue = exprNode.ToExpression();

        return returnValue;
    }

    /// <summary>
    /// Maps a delegate argument into a serializable RemoteDelegateInfo object.
    /// </summary>
    /// <param name="argumentType">Type of argument to be mapped</param>
    /// <param name="argument">Argument to be wrapped</param>
    /// <param name="mappedArgument">Out: Mapped argument</param>
    /// <returns>True if mapping applied, otherwise false</returns>
    private bool MapDelegateArgument(Type argumentType, object argument, out object mappedArgument)
    {
        if (argumentType == null || !typeof(Delegate).IsAssignableFrom(argumentType))
        {
            mappedArgument = argument;
            return false;
        }

        var delegateReturnType = argumentType.GetMethod("Invoke")?.ReturnType;

        if (delegateReturnType != null && delegateReturnType != typeof(void))
            throw new NotSupportedException("Only void delegates are supported.");

        var remoteDelegateInfo =
            new RemoteDelegateInfo(
                handlerKey: _client.ClientDelegateRegistry.RegisterClientDelegate((Delegate)argument, this),
                delegateTypeName: argumentType.FullName + ", " + argumentType.Assembly.GetName().Name);

        mappedArgument = remoteDelegateInfo;
        return true;
    }

    /// <summary>
    /// Maps a Linq expression argument into a serializable ExpressionNode object.
    /// </summary>
    /// <param name="argumentType">Type of argument to be mapped</param>
    /// <param name="argument">Argument to be wrapped</param>
    /// <param name="mappedArgument">Out: Mapped argument</param>
    /// <returns>True if mapping applied, otherwise false</returns>
    private static bool MapLinqExpressionArgument(Type argumentType, object argument, out object mappedArgument)
    {
        if (!argumentType.IsLinqExpressionType())
        {
            mappedArgument = argument;
            return false;
        }

        var expression = (Expression)argument;
        mappedArgument = expression.ToExpressionNode();

        return true;
    }

    /// <summary>
    /// Maps non serializable arguments into a serializable form.
    /// </summary>
    /// <param name="arguments">Arguments</param>
    /// <returns>Array of arguments (includes mapped ones)</returns>
    private object[] MapArguments(IEnumerable<object> arguments)
    {
        var mappedArguments =
            arguments.Select(argument =>
            {
                var type = argument?.GetType();

                if (MapDelegateArgument(type, argument, out var mappedArgument))
                    return mappedArgument;

                if (MapLinqExpressionArgument(type, argument, out mappedArgument))
                    return mappedArgument;

                return argument;
            }).ToArray();

        return mappedArguments;
    }
}