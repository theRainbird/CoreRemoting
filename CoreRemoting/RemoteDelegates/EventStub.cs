using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CoreRemoting.Toolbox;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Event stub caches all event handlers for the single-call or scoped components.
/// </summary>
public class EventStub
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventStub" /> class.
    /// </summary>
    /// <param name="interfaceType">Type of the interface.</param>
    public EventStub(Type interfaceType)
    {
        InterfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
        CreateDelegateHolders();
    }

    /// <summary>
    /// Gets or sets the invocation delegates for event handlers.
    /// </summary>
    private ConcurrentDictionary<string, IDelegateHolder> DelegateHolders { get; set; }

    /// <summary>
    /// Gets the type of the interface.
    /// </summary>
    public Type InterfaceType { get; private set; }

    /// <summary>
    /// Gets or sets the <see cref="Delegate" /> with the specified event property name.
    /// </summary>
    /// <param name="propertyName">Name of the event or delegate property.</param>
    public Delegate this[string propertyName] => DelegateHolders[propertyName].InvocationDelegate;

    /// <summary>
    /// Gets or sets the list of event of the reflected interface.
    /// </summary>
    private EventInfo[] EventProperties { get; set; }

    /// <summary>
    /// Gets or sets the list of delegate properties of the reflected interface.
    /// </summary>
    private PropertyInfo[] DelegateProperties { get; set; }

    private void CreateDelegateHolders()
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        DelegateHolders = new ConcurrentDictionary<string, IDelegateHolder>();
        EventProperties = GetEvents(InterfaceType, bindingFlags).ToArray();
        DelegateProperties = GetDelegateProperties(InterfaceType, bindingFlags).ToArray();

        foreach (var eventProperty in EventProperties)
        {
            DelegateHolders[eventProperty.Name] = CreateDelegateHolder(eventProperty.EventHandlerType);
        }

        foreach (var delegateProperty in DelegateProperties)
        {
            DelegateHolders[delegateProperty.Name] = CreateDelegateHolder(delegateProperty.PropertyType);
        }
    }

    private IEnumerable<Type> GetAllInterfaces(Type interfaceType)
    {
        if (interfaceType.IsInterface)
        {
            yield return interfaceType;
        }

        // Passing BindingFlags.FlattenHierarchy to one of the Type.GetXXX methods, such as Type.GetMembers,
        // will not return inherited interface members when you are querying on an interface type itself.
        // To get the inherited members, you need to query each implemented interface for its members.
        var inheritedInterfaces =
            from inheritedInterface in interfaceType.GetInterfaces()
            from type in GetAllInterfaces(inheritedInterface)
            select type;

        foreach (var type in inheritedInterfaces)
        {
            yield return type;
        }
    }

    private IEnumerable<EventInfo> GetEvents(Type interfaceType, BindingFlags flags) =>
        from type in GetAllInterfaces(interfaceType)
        from ev in type.GetEvents(flags)
        select ev;

    private IEnumerable<PropertyInfo> GetDelegateProperties(Type interfaceType, BindingFlags flags) =>
        from type in GetAllInterfaces(interfaceType)
        from prop in type.GetProperties(flags)
        where typeof(Delegate).IsAssignableFrom(prop.PropertyType)
        select prop;

    private static IDelegateHolder CreateDelegateHolder(Type delegateType)
    {
        var createDelegateHolder = createDelegateHolderMethod
            .MakeGenericMethod(delegateType)
            .CreateDelegate(typeof(Func<IDelegateHolder>)) as Func<IDelegateHolder>;

        return createDelegateHolder();
    }

    private static MethodInfo createDelegateHolderMethod =
        new Func<IDelegateHolder>(CreateDelegateHolder<Action>).Method.GetGenericMethodDefinition();

    private static IDelegateHolder CreateDelegateHolder<T>() =>
        new DelegateHolder<T>();

    /// <summary>
    /// Non-generic interface for the private generic delegate holder class.
    /// </summary>
    public interface IDelegateHolder
    {
        /// <summary>
        /// Gets the invocation delegate.
        /// </summary>
        Delegate InvocationDelegate { get; }

        /// <summary>
        /// Adds the handler.
        /// </summary>
        /// <param name="handler">The handler.</param>
        void AddHandler(Delegate handler);

        /// <summary>
        /// Removes the handler.
        /// </summary>
        /// <param name="handler">The handler.</param>
        void RemoveHandler(Delegate handler);

        /// <summary>
        /// Gets the handler count.
        /// </summary>
        int HandlerCount { get; }
    }

    /// <summary>
    /// Generic holder for delegates (such as event handlers).
    /// </summary>
    private class DelegateHolder<T> : IDelegateHolder
    {
        public DelegateHolder()
        {
            // create default return value for the delegate
            DefaultReturnValue = typeof(T).GetMethod("Invoke").ReturnType.GetDefaultValue();
        }

        public Delegate InvocationDelegate =>
            (Delegate)(object)InvocationMethod;

        private T invocationMethod;

        public T InvocationMethod
        {
            get
            {
                if (invocationMethod == null)
                {
                    // create strong-typed Invoke method that calls into DynamicInvoke
                    var dynamicInvokeMethod = GetType().GetMethod(nameof(DynamicInvoke), BindingFlags.NonPublic | BindingFlags.Instance);
                    invocationMethod = BuildInstanceDelegate<T>(dynamicInvokeMethod, this);
                }

                return invocationMethod;
            }
        }

        /// <summary>
        /// Builds the strong-typed delegate bound to the given target instance
        /// for the dynamicInvoke method: object DynamicInvoke(object[] args);
        /// </summary>
        /// <remarks>
        /// Relies on the dynamic methods. Delegate Target property is equal to the "target" parameter.
        /// Doesn't support static methods.
        /// </remarks>
        /// <typeparam name="TDelegate">Delegate type.</typeparam>
        /// <param name="dynamicInvoke"><see cref="MethodInfo"/> for the DynamicInvoke(object[] args) method.</param>
        /// <param name="target">Target instance.</param>
        /// <returns>Strong-typed delegate.</returns>
        public static TDelegate BuildInstanceDelegate<TDelegate>(MethodInfo dynamicInvoke, object target)
        {
            // validate generic argument
            if (!typeof(Delegate).IsAssignableFrom(typeof(TDelegate)))
            {
                throw new ApplicationException(typeof(TDelegate).FullName + " is not a delegate type.");
            }

            // reflect delegate type to get parameters and method return type
            var delegateType = typeof(TDelegate);
            var invokeMethod = delegateType.GetMethod("Invoke");

            // figure out parameters
            var paramTypeList = invokeMethod.GetParameters().Select(p => p.ParameterType).ToList();
            var paramCount = paramTypeList.Count;
            var ownerType = target.GetType();
            paramTypeList.Insert(0, ownerType);
            var paramTypes = paramTypeList.ToArray();
            var typedInvoke = new DynamicMethod("TypedInvoke", invokeMethod.ReturnType, paramTypes, ownerType);

            // create method body, declare local variable of type object[]
            var ilGenerator = typedInvoke.GetILGenerator();
            var argumentsArray = ilGenerator.DeclareLocal(typeof(object[]));

            // var args = new object[paramCount];
            ilGenerator.Emit(OpCodes.Nop);
            ilGenerator.Emit(OpCodes.Ldc_I4, paramCount);
            ilGenerator.Emit(OpCodes.Newarr, typeof(object));
            ilGenerator.Emit(OpCodes.Stloc, argumentsArray);

            // load method arguments one by one
            var index = 1;
            foreach (var paramType in paramTypes.Skip(1))
            {
                // load object[] array reference
                ilGenerator.Emit(OpCodes.Ldloc, argumentsArray);
                ilGenerator.Emit(OpCodes.Ldc_I4, index - 1); // array index
                ilGenerator.Emit(OpCodes.Ldarg, index++); // method parameter index

                // value type parameters need boxing
                if (typeof(ValueType).IsAssignableFrom(paramType))
                {
                    ilGenerator.Emit(OpCodes.Box, paramType);
                }

                // store reference
                ilGenerator.Emit(OpCodes.Stelem_Ref);
            }

            // this
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldloc, argumentsArray); // object[] args
            ilGenerator.Emit(OpCodes.Call, dynamicInvoke);

            // discard return value
            if (invokeMethod.ReturnType == typeof(void))
            {
                ilGenerator.Emit(OpCodes.Pop);
            }

            // unbox return value of value type
            else if (typeof(ValueType).IsAssignableFrom(invokeMethod.ReturnType))
            {
                ilGenerator.Emit(OpCodes.Unbox_Any, invokeMethod.ReturnType);
            }

            // return value
            ilGenerator.Emit(OpCodes.Ret);

            // bake dynamic method, create a gelegate
            var result = typedInvoke.CreateDelegate(delegateType, target);
            return (TDelegate)(object)result;
        }

        private object DynamicInvoke(object[] arguments)
        {
            // run in non-blocking mode
            Delegate.OneWayDynamicInvoke(arguments);
            return DefaultReturnValue;
        }

        private T TypedDelegate { get; set; }

        private object DefaultReturnValue { get; set; }

        private Delegate Delegate
        {
            get { return (Delegate)(object)TypedDelegate; }
            set { TypedDelegate = (T)(object)value; }
        }

        private object syncRoot = new();

        public void AddHandler(Delegate handler)
        {
            lock (syncRoot)
            {
                Delegate = Delegate.Combine(Delegate, handler);
            }
        }

        public void RemoveHandler(Delegate handler)
        {
            lock (syncRoot)
            {
                Delegate = Delegate.Remove(Delegate, handler);
            }
        }

        public int HandlerCount
        {
            get
            {
                if (Delegate == null)
                {
                    return 0;
                }

                return Delegate.GetInvocationList().Length;
            }
        }
    }

    private bool GetWiredTo(object instance) =>
        AttachmentHelper.Get(instance, out bool wired) ? wired : false;

    private void SetWiredTo(object instance, bool wired) =>
        AttachmentHelper.Set(instance, wired);

    private object WiredSync { get; } = new();

    private bool IsAlreadyWired(object instance)
    {
        lock (WiredSync)
        {
            if (GetWiredTo(instance))
                return true;

            SetWiredTo(instance, true);
            return false;
        }
    }

    private bool IsAlreadyUnwired(object instance)
    {
        lock (WiredSync)
        {
            if (!GetWiredTo(instance))
                return true;

            SetWiredTo(instance, false);
            return false;
        }
    }

    /// <summary>
    /// Wires all event handlers to the specified instance.
    /// </summary>
    /// <param name="instance">The instance.</param>
    public void WireTo(object instance)
    {
        if (instance == null ||
            EventProperties.Length +
            DelegateProperties.Length == 0 ||
            IsAlreadyWired(instance))
        {
            return;
        }

        foreach (var eventInfo in EventProperties)
        {
            eventInfo.AddEventHandler(instance, this[eventInfo.Name]);
        }

        foreach (var propInfo in DelegateProperties)
        {
            var value = propInfo.GetValue(instance, []) as Delegate;
            value = Delegate.Combine(value, this[propInfo.Name]);
            propInfo.SetValue(instance, value, []);
        }
    }

    /// <summary>
    /// Unwires all event handlers from the specified instance.
    /// </summary>
    /// <param name="instance">The instance.</param>
    public void UnwireFrom(object instance)
    {
        if (instance == null ||
            EventProperties.Length +
            DelegateProperties.Length == 0 ||
            IsAlreadyUnwired(instance))
        {
            return;
        }

        foreach (var eventInfo in EventProperties)
        {
            eventInfo.RemoveEventHandler(instance, this[eventInfo.Name]);
        }

        foreach (var propInfo in DelegateProperties)
        {
            var value = propInfo.GetValue(instance, []) as Delegate;
            value = Delegate.Remove(value, this[propInfo.Name]);
            propInfo.SetValue(instance, value, []);
        }
    }

    /// <summary>
    /// Adds the handler for the given event.
    /// </summary>
    /// <param name="name">The name of the event or delegate property.</param>
    /// <param name="handler">The handler.</param>
    public void AddHandler(string name, Delegate handler)
    {
        DelegateHolders[name].AddHandler(handler);
    }

    /// <summary>
    /// Removes the handler for the given event.
    /// </summary>
    /// <param name="name">The name of the event or delegate property.</param>
    /// <param name="handler">The handler.</param>
    public void RemoveHandler(string name, Delegate handler)
    {
        DelegateHolders[name].RemoveHandler(handler);
    }

    /// <summary>
    /// Gets the count of event handlers for the given event or delegate property.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    public static int GetHandlerCount(Delegate handler)
    {
        if (handler == null)
        {
            return 0;
        }

        var count = 0;
        foreach (var d in handler.GetInvocationList())
        {
            // check if it's a delegate holder
            if (d.Target is IDelegateHolder holder)
            {
                count += holder.HandlerCount;
                continue;
            }

            // it's an ordinary subscriber
            count++;
        }

        return count;
    }
}
