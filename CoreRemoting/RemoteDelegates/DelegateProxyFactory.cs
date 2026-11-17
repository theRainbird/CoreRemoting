using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Factory component to create delegate proxy instances.
/// </summary>
public class DelegateProxyFactory : IDelegateProxyFactory
{
    private readonly ConcurrentDictionary<Type, DynamicMethod> _dynamicMethods = new();

    /// <summary>
    /// Creates a proxy for the specified delegate type.
    /// </summary>
    /// <param name="delegateType">Delegate type to be proxied</param>
    /// <param name="callInterceptionHandler">Function to be called when intercepting calls on the delegate</param>
    /// <returns>Delegate proxy</returns>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null</exception>
    /// <exception cref="ArgumentException">Thrown if the type is not a delegate or is an open generic</exception>
    /// <exception cref="NotSupportedException">Thrown if the delegate type is not supported</exception>
    public IDelegateProxy Create(Type delegateType, Func<object[], object> callInterceptionHandler)
    {
        if (delegateType == null)
            throw new ArgumentNullException(nameof(delegateType));

        if (callInterceptionHandler == null)
            throw new ArgumentNullException(nameof(callInterceptionHandler));

        if (!typeof(Delegate).IsAssignableFrom(delegateType))
            throw new ArgumentException($"Specified type ({delegateType.FullName}) must be a delegate type.", nameof(delegateType));

        if (delegateType.IsGenericTypeDefinition)
            throw new ArgumentException($"Open generic delegate type '{delegateType}' is not supported.", nameof(delegateType));

        var invokeMethod = delegateType.GetMethod("Invoke");

        if (invokeMethod == null)
            throw new NotSupportedException("Provided delegate type has no 'Invoke' method.");

        if (invokeMethod.GetParameters().Any(p => p.ParameterType.IsByRef))
            throw new NotSupportedException("Delegates with ref/out/in parameters are not supported.");

        var dynamicMethod = _dynamicMethods.GetOrAdd(delegateType, BuildDynamicMethod);

        return new DelegateProxy(delegateType, callInterceptionHandler, dynamicMethod);
    }

    private static DynamicMethod BuildDynamicMethod(Type delegateType)
    {
        var invokeMethod = delegateType.GetMethod("Invoke");
        var interceptorType = typeof(DelegateProxy);

        var interceptMethod = interceptorType.GetMethod(
            name: nameof(DelegateProxy.Intercept),
            bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);

        var parameterTypeList =
            invokeMethod
                .GetParameters()
                .Select(p => p.ParameterType)
                .ToList();

        var parameterCount = parameterTypeList.Count;
        parameterTypeList.Insert(0, interceptorType);
        var parameterTypes = parameterTypeList.ToArray();

        // Create dynamic method for delegate call interception
        var delegateProxyMethod =
            new DynamicMethod(
                name: "Intercept",
                returnType: invokeMethod.ReturnType,
                parameterTypes: parameterTypes,
                owner: interceptorType);

        // Create method body, declare local variable of type object[]
        var ilGenerator = delegateProxyMethod.GetILGenerator();
        var argumentsArray = ilGenerator.DeclareLocal(typeof(object[]));

        // var args = new object[paramCount];
        ilGenerator.Emit(OpCodes.Nop);
        ilGenerator.Emit(OpCodes.Ldc_I4, parameterCount);
        ilGenerator.Emit(OpCodes.Newarr, typeof(object));
        ilGenerator.Emit(OpCodes.Stloc, argumentsArray);

        var index = 1;

        // Load method arguments one by one
        foreach (var paramType in parameterTypes.Skip(1))
        {
            // Load object[] array reference
            ilGenerator.Emit(OpCodes.Ldloc, argumentsArray);
            ilGenerator.Emit(OpCodes.Ldc_I4, index - 1); // Array index
            ilGenerator.Emit(OpCodes.Ldarg, index++); // Method parameter index

            // Box parameter value, if it is a value type
            if (paramType.IsValueType)
                ilGenerator.Emit(OpCodes.Box, paramType);

            // Store reference
            ilGenerator.Emit(OpCodes.Stelem_Ref);
        }

        // Call intercept method and pass parameters as arguments array
        ilGenerator.Emit(OpCodes.Ldarg_0);
        ilGenerator.Emit(OpCodes.Ldloc, argumentsArray); // object[] args
        ilGenerator.Emit(OpCodes.Call, interceptMethod);

        // Discard return value, if return type of proxied delegate is void
        if (invokeMethod.ReturnType == typeof(void))
            ilGenerator.Emit(OpCodes.Pop);
        else if (invokeMethod.ReturnType.IsValueType) // Unbox return value, if it is a value type
            ilGenerator.Emit(OpCodes.Unbox_Any, invokeMethod.ReturnType);

        // Return the return value
        ilGenerator.Emit(OpCodes.Ret);

        return delegateProxyMethod;
    }
}
