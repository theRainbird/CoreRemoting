using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Toolbox;
using Serialize.Linq.Extensions;
using Serialize.Linq.Nodes;

namespace CoreRemoting;

/// <summary>
/// Implements a service invoker that provides access to a remote service without generating a proxy.
/// Remote method calls are described by LINQ expressions instead of a generated proxy interface.
/// </summary>
/// <typeparam name="TService">Type of the remote service contract (interface or class)</typeparam>
public sealed class ServiceInvoker<TService> : IServiceInvoker<TService> where TService : class
{
    private static readonly MethodInfo CastResultMethodDefinition =
        new Func<Task<object>, Task<object>>(CastResultAsync<object>)
            .Method.GetGenericMethodDefinition();

    private static readonly ConcurrentDictionary<Type, Func<Task<object>, object>> CastResultCache = new();

    private RemotingClient _client;

    /// <summary>
    /// Creates a new instance of the ServiceInvoker class.
    /// </summary>
    /// <param name="client">CoreRemoting client to be used for client/server communication</param>
    /// <param name="serviceName">Unique name of the remote service</param>
    public ServiceInvoker(RemotingClient client, string serviceName = "")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        ServiceName =
            string.IsNullOrWhiteSpace(serviceName)
                ? typeof(TService).FullName
                : serviceName;
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~ServiceInvoker()
    {
        Shutdown();
    }

    /// <summary>
    /// Gets the name of the remote service.
    /// </summary>
    public string ServiceName { get; }

    private RemotingClient Client =>
        _client ?? throw new ObjectDisposedException(nameof(ServiceInvoker<TService>));

    /// <inheritdoc/>
    public TResult Invoke<TResult>(Expression<Func<TService, TResult>> call)
    {
        var client = Client;
        var (method, arguments) = ExtractMethodArguments(call);
        return (TResult)InvokeCore<TResult>(client, method, arguments);
    }

    /// <inheritdoc/>
    public void Invoke(Expression<Action<TService>> call)
    {
        var client = Client;
        var (method, arguments) = ExtractMethodArguments(call);
        InvokeCore(client, method, arguments).JustWait();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }

    private void Shutdown()
    {
        if (_client is { } client)
        {
            client.ClientDelegateRegistry.UnregisterClientDelegatesOfServiceProxy(this);
            _client = null;
        }
    }

    private object InvokeCore<TResult>(RemotingClient client, MethodInfo method, object[] arguments)
    {
        var type = typeof(TResult);
        var task = InvokeCore(client, method, arguments);

        if (type == typeof(Task))
            return task;

        if (type == typeof(ValueTask))
            return new ValueTask(task);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(Task<>))
                return CastResult(type.GetGenericArguments()[0], task);

            if (definition == typeof(ValueTask<>))
                return Activator.CreateInstance(type, CastResult(type.GetGenericArguments()[0], task));
        }

        return task.JustWait();
    }

    private async Task<object> InvokeCore(RemotingClient client, MethodInfo method, object[] arguments)
    {
        var oneWay = method.GetCustomAttribute<OneWayAttribute>() != null;

        if (oneWay && method.ReturnType != typeof(void))
            throw new NotSupportedException("OneWay methods must not have a return type.");

        var mappedArguments = client.MapArguments(this, arguments);

        client.SubscriptionTracker.Track(method,
            SubscriptionTracker.ExtractHandlerKey(mappedArguments));

        var remoteMethodCallMessage =
            client.MethodCallMessageBuilder.BuildMethodCallMessage(
                serializer: client.Serializer,
                remoteServiceName: ServiceName,
                targetMethod: method,
                args: mappedArguments);

        var clientRpcContext =
            await client.InvokeRemoteMethod(remoteMethodCallMessage, oneWay)
                .ConfigureAwait(false);

        if (clientRpcContext.Error)
            throw clientRpcContext.RemoteException ?? new RemoteInvocationException();

        var resultMessage = clientRpcContext.ResultMessage;

        if (resultMessage == null)
            return null;

        var result = client.UnmapReturnValue(resultMessage);

        CallContext.RestoreFromSnapshot(resultMessage.CallContextSnapshot);

        return result;
    }

    private static object CastResult(Type innerType, Task<object> task)
    {
        var converter = CastResultCache.GetOrAdd(innerType, static type =>
        {
            var method = CastResultMethodDefinition.MakeGenericMethod(type);

            var parameter = Expression.Parameter(typeof(Task<object>), "task");
            var call = Expression.Call(method, parameter);
            var boxed = Expression.Convert(call, typeof(object));

            return Expression.Lambda<Func<Task<object>, object>>(boxed, parameter).Compile();
        });

        return converter(task);
    }

    private static async Task<T> CastResultAsync<T>(Task<object> task) =>
        (T)await task.ConfigureAwait(false);

    private static (MethodInfo Method, object[] Arguments) ExtractMethodArguments(LambdaExpression expression)
    {
        if (expression.Body is not MethodCallExpression call)
            throw new InvalidOperationException("Expression must be a direct method call.");

        if (call.Method.IsDefined(typeof(ExtensionAttribute), false))
            throw new NotSupportedException("Extension methods are not supported.");

        var arguments = new object[call.Arguments.Count];

        for (var i = 0; i < call.Arguments.Count; i++)
            arguments[i] = Evaluate(call.Arguments[i]);

        return (call.Method, arguments);
    }

    private static object Evaluate(Expression expression) =>
        expression is ConstantExpression constant
            ? constant.Value
            : Expression.Lambda(expression).Compile().DynamicInvoke();
}
