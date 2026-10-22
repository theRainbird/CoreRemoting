using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Proxy for intercepting calls on a specified delegate type.
/// </summary>
public sealed class DelegateProxy : IDelegateProxy
{
    private Func<object[], object> _callInterceptionHandler;

    /// <summary>
    /// Creates a new instance of the DelegateProxy class.
    /// </summary>
    /// <param name="delegateType">Delegate type to be proxied</param>
    /// <param name="callInterceptionHandler">Function to be called when intercepting calls on the delegate</param>
    internal DelegateProxy(Type delegateType, Func<object[], object> callInterceptionHandler)
    {
        _callInterceptionHandler =
            callInterceptionHandler ??
                throw new ArgumentNullException(nameof(callInterceptionHandler));

        var interceptMethod =
            GetType()
                .GetMethod(
                    name: nameof(Intercept),
                    bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);

        ProxiedDelegate =
            CreateProxiedDelegate(
                delegateType: delegateType,
                interceptMethod: interceptMethod,
                interceptor: this);
    }

    /// <summary>
    /// Gets the proxied delegate.
    /// </summary>
    public Delegate ProxiedDelegate { get; private set; }

    /// <summary>
    /// Method called by delegate proxy, when the proxies delegate is called.
    /// </summary>
    /// <param name="args">Arguments passed to the proxied delegate by caller</param>
    /// <returns>Return value provided by call interception handler</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the proxy has been disposed</exception>
    private object Intercept(object[] args)
    {
        var handler = Volatile.Read(ref _callInterceptionHandler);

        if (handler == null)
            throw new ObjectDisposedException(nameof(DelegateProxy));

        return handler(args);
    }

    /// <summary>
    /// Creates a delegate for intercepting calls on a specified delegate type.
    /// </summary>
    /// <param name="delegateType">The delegate type to proxy</param>
    /// <param name="interceptMethod">Method to call when intercepting calls on the proxied delegate</param>
    /// <param name="interceptor">Object on which the intercept method is called</param>
    /// <returns>Proxied delegate</returns>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null</exception>
    /// <exception cref="ArgumentException">Thrown if argument 'delegateType' is not a delegate or is an open generic</exception>
    /// <exception cref="NotSupportedException">Thrown if the delegate type is not supported</exception>
    private Delegate CreateProxiedDelegate(Type delegateType, MethodInfo interceptMethod, object interceptor)
    {
        if (delegateType == null)
            throw new ArgumentNullException(nameof(delegateType));

        if (interceptMethod == null)
            throw new ArgumentNullException(nameof(interceptMethod));

        if (interceptor == null)
            throw new ArgumentNullException(nameof(interceptor));

        if (!typeof(Delegate).IsAssignableFrom(delegateType))
            throw new ArgumentException($"Specified type ({delegateType.FullName}) must be a delegate type.", nameof(delegateType));

        if (delegateType.IsGenericTypeDefinition)
            throw new ArgumentException($"Open generic delegate type '{delegateType}' is not supported.", nameof(delegateType));

        var interceptorObjectType = interceptor.GetType();
        var invokeMethod = delegateType.GetMethod("Invoke");

        if (invokeMethod == null)
            throw new NotSupportedException("Provided delegate type has no 'Invoke' method.");

        if (invokeMethod.GetParameters().Any(p => p.ParameterType.IsByRef))
            throw new NotSupportedException("Delegates with ref/out/in parameters are not supported.");

        var parameterTypeList =
            invokeMethod
                .GetParameters()
                .Select(p => p.ParameterType)
                .ToList();

        var parameterCount = parameterTypeList.Count;
        parameterTypeList.Insert(0, interceptorObjectType);
        var parameterTypes = parameterTypeList.ToArray();

        // Create dynamic method for delegate call interception
        var delegateProxyMethod =
            new DynamicMethod(
                name: nameof(Intercept),
                returnType: invokeMethod.ReturnType,
                parameterTypes: parameterTypes,
                owner: interceptorObjectType);

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
            if (typeof(ValueType).IsAssignableFrom(paramType))
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
        else if (typeof(ValueType).IsAssignableFrom(invokeMethod.ReturnType)) // Unbox return value, if it is a value type
            ilGenerator.Emit(OpCodes.Unbox_Any, invokeMethod.ReturnType);

        // Return the return value
        ilGenerator.Emit(OpCodes.Ret);

        // Bake dynamic method and create a delegate of it
        var result =
            delegateProxyMethod.CreateDelegate(delegateType, interceptor);

        // Return as proxied delegate type
        return result;
    }

    /// <summary>
    /// Frees managed resources.
    /// </summary>
    public void Dispose()
    {
        ProxiedDelegate = null;
        Interlocked.Exchange(ref _callInterceptionHandler, null);
    }
}