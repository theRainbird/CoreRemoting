using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Toolbox;
using Serialize.Linq.Extensions;
using Serialize.Linq.Nodes;

namespace CoreRemoting;

/// <summary>
/// Provides service call argument and return value mapping extensions for the CoreRemoting client.
/// </summary>
internal static class RemotingClientExtensions
{
    /// <summary>
    /// Unmaps a return value from the specified method call result message.
    /// </summary>
    /// <param name="client">CoreRemoting client that produced the result</param>
    /// <param name="resultMessage">Method call result message</param>
    /// <returns>Unmapped return value</returns>
    public static object UnmapReturnValue(
        this RemotingClient client,
        MethodCallResultMessage resultMessage)
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
            returnValue = client.CreateProxy(serviceReference);

        // Restore LINQ expression, if return type is a serializable expression
        if (returnValue is ExpressionNode exprNode)
            returnValue = exprNode.ToExpression();

        return returnValue;
    }

    /// <summary>
    /// Maps non serializable arguments into a serializable form.
    /// </summary>
    /// <param name="client">CoreRemoting client that owns the call</param>
    /// <param name="owner">Object that is paired with client delegates for cleanup</param>
    /// <param name="arguments">Arguments to be mapped</param>
    /// <returns>Array of mapped arguments</returns>
    public static object[] MapArguments(
        this RemotingClient client,
        object owner,
        IEnumerable<object> arguments)
    {
        return arguments
            .Select(argument =>
            {
                var type = argument?.GetType();

                if (MapDelegateArgument(client, owner, type, argument, out var mappedArgument))
                    return mappedArgument;

                if (MapLinqExpressionArgument(type, argument, out mappedArgument))
                    return mappedArgument;

                return argument;
            })
            .ToArray();
    }

    /// <summary>
    /// Maps a delegate argument into a serializable RemoteDelegateInfo object.
    /// </summary>
    /// <param name="client">CoreRemoting client that owns the call</param>
    /// <param name="owner">Object that is paired with client delegates for cleanup</param>
    /// <param name="argumentType">Type of argument to be mapped</param>
    /// <param name="argument">Argument to be wrapped</param>
    /// <param name="mappedArgument">Out: mapped argument</param>
    /// <returns>True if mapping applied, otherwise false</returns>
    private static bool MapDelegateArgument(
        RemotingClient client,
        object owner,
        Type argumentType,
        object argument,
        out object mappedArgument)
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
                handlerKey: client.ClientDelegateRegistry.RegisterClientDelegate((Delegate)argument, owner),
                delegateTypeName: argumentType.FullName + ", " + argumentType.Assembly.GetName().Name);

        mappedArgument = remoteDelegateInfo;
        return true;
    }

    /// <summary>
    /// Maps a LINQ expression argument into a serializable ExpressionNode object.
    /// </summary>
    /// <param name="argumentType">Type of argument to be mapped</param>
    /// <param name="argument">Argument to be wrapped</param>
    /// <param name="mappedArgument">Out: mapped argument</param>
    /// <returns>True if mapping applied, otherwise false</returns>
    private static bool MapLinqExpressionArgument(
        Type argumentType,
        object argument,
        out object mappedArgument)
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
}
