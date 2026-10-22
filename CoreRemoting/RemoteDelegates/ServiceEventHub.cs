using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CoreRemoting.Toolbox;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Event hub caches all event handlers for the single-call or scoped components.
/// </summary>
public class ServiceEventHub
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceEventHub" /> class.
    /// </summary>
    /// <param name="interfaceType">Type of the interface.</param>
    public ServiceEventHub(Type interfaceType)
    {
        InterfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
        CreateDelegateHolders();
    }

    /// <summary>
    /// Gets or sets the delegate invoker for remote event invocations.
    /// </summary>
    public static IDelegateInvoker DelegateInvoker
    {
        get => _delegateInvoker;
        set => _delegateInvoker = value ?? new SafeDynamicInvoker();
    }

    private static IDelegateInvoker _delegateInvoker = new SafeDynamicInvoker();

    /// <summary>
    /// Gets or sets the invocation delegates for event handlers.
    /// </summary>
    private ConcurrentDictionary<string, IDelegateHolder> DelegateHolders { get; set; }

    /// <summary>
    /// Gets the type of the interface.
    /// </summary>
    public Type InterfaceType { get; private set; }

    /// <summary>
    /// Gets the <see cref="Delegate" /> with the specified event property name.
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
            DelegateHolders[eventProperty.Name] =
                CreateDelegateHolder(eventProperty.EventHandlerType, DelegateInvoker);
        }

        foreach (var delegateProperty in DelegateProperties)
        {
            DelegateHolders[delegateProperty.Name] =
                CreateDelegateHolder(delegateProperty.PropertyType, DelegateInvoker);
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

    private static IDelegateHolder CreateDelegateHolder(Type delegateType, IDelegateInvoker delegateInvoker)
    {
        var createDelegateHolder = createDelegateHolderMethod
            .MakeGenericMethod(delegateType)
            .CreateDelegate(typeof(Func<IDelegateInvoker, IDelegateHolder>)) as Func<IDelegateInvoker, IDelegateHolder>;

        return createDelegateHolder(delegateInvoker);
    }

    private static MethodInfo createDelegateHolderMethod =
        new Func<IDelegateInvoker, IDelegateHolder>(CreateDelegateHolder<Action>).Method.GetGenericMethodDefinition();

    private static IDelegateHolder CreateDelegateHolder<T>(IDelegateInvoker delegateInvoker) =>
        new DelegateHolder<T>(delegateInvoker);

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
        /// <param name="handlerKey">Unique handler key of the client delegate.</param>
        /// <param name="sessionId">Session identifier of the subscriber.</param>
        /// <param name="handler">The handler.</param>
        void AddHandler(Guid handlerKey, Guid sessionId, Delegate handler);

        /// <summary>
        /// Removes a single occurrence of the handler with the specified key.
        /// </summary>
        /// <param name="handlerKey">Unique handler key of the client delegate.</param>
        /// <returns>True if an entry was removed, otherwise false.</returns>
        bool RemoveHandler(Guid handlerKey);

        /// <summary>
        /// Removes all handlers registered by the specified session.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <returns>Number of removed occurrences.</returns>
        int RemoveAllForSession(Guid sessionId);

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
        private readonly ConcurrentDictionary<Guid, SubscriptionEntry> _entries = new();
        private readonly object _syncRoot = new();

        public DelegateHolder(IDelegateInvoker delegateInvoker)
        {
            // create default return value for the delegate
            DefaultReturnValue = typeof(T).GetMethod("Invoke").ReturnType.GetDefaultValue();
            DelegateInvoker = delegateInvoker;
        }

        private IDelegateInvoker DelegateInvoker { get; }

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
            // ConcurrentDictionary can be enumerated safely while it is modified,
            // so no snapshot is taken and no lock is required here.
            foreach (var entry in _entries)
            {
                // an entry may represent multiple subscriptions of the same handler key,
                // which must be fired separately to preserve multicast semantics
                for (var i = 0; i < entry.Value.Count; i++)
                    DelegateInvoker.Invoke(entry.Value.Handler, arguments);
            }

            return DefaultReturnValue;
        }

        private object DefaultReturnValue { get; }

        public void AddHandler(Guid handlerKey, Guid sessionId, Delegate handler)
        {
            lock (_syncRoot)
            {
                if (_entries.TryGetValue(handlerKey, out var existing))
                {
                    // the same handler key must always resolve to the same delegate
                    // (handler keys come from ClientDelegateRegistry, which guarantees this)
                    if (!ReferenceEquals(existing.Handler, handler))
                    {
                        throw new InvalidOperationException(
                            $"Handler key {handlerKey} is already registered with a different delegate.");
                    }

                    _entries[handlerKey] = existing with { Count = existing.Count + 1 };
                }
                else
                {
                    _entries[handlerKey] = new SubscriptionEntry(sessionId, handler, 1);
                }
            }
        }

        public bool RemoveHandler(Guid handlerKey)
        {
            lock (_syncRoot)
            {
                if (!_entries.TryGetValue(handlerKey, out var existing))
                    return false;

                if (existing.Count <= 1)
                    _entries.TryRemove(handlerKey, out _);
                else
                    _entries[handlerKey] = existing with { Count = existing.Count - 1 };

                return true;
            }
        }

        public int RemoveAllForSession(Guid sessionId)
        {
            lock (_syncRoot)
            {
                var removed = 0;
                var keysToRemove = new List<Guid>();

                foreach (var pair in _entries)
                {
                    if (pair.Value.SessionId != sessionId)
                        continue;

                    keysToRemove.Add(pair.Key);
                    removed += pair.Value.Count;
                }

                foreach (var key in keysToRemove)
                    _entries.TryRemove(key, out _);

                return removed;
            }
        }

        public int HandlerCount
        {
            get
            {
                var count = 0;

                foreach (var entry in _entries)
                    count += entry.Value.Count;

                return count;
            }
        }

        private sealed record SubscriptionEntry(Guid SessionId, Delegate Handler, int Count);
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
    /// <param name="handlerKey">Unique handler key of the client delegate.</param>
    /// <param name="sessionId">Session identifier of the subscriber.</param>
    /// <param name="handler">The handler.</param>
    public void AddHandler(string name, Guid handlerKey, Guid sessionId, Delegate handler)
    {
        DelegateHolders[name].AddHandler(handlerKey, sessionId, handler);
    }

    /// <summary>
    /// Removes a single occurrence of the handler with the specified key.
    /// </summary>
    /// <param name="name">The name of the event or delegate property.</param>
    /// <param name="handlerKey">Unique handler key of the client delegate.</param>
    /// <returns>True if an entry was removed, otherwise false.</returns>
    public bool RemoveHandler(string name, Guid handlerKey)
    {
        return DelegateHolders[name].RemoveHandler(handlerKey);
    }

    /// <summary>
    /// Removes all handlers registered by the specified session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Number of removed occurrences.</returns>
    public int RemoveAllForSession(Guid sessionId)
    {
        var removed = 0;

        foreach (var holder in DelegateHolders.Values)
            removed += holder.RemoveAllForSession(sessionId);

        return removed;
    }

    /// <summary>
    /// Gets the count of event handlers for the given event or delegate property.
    /// </summary>
    /// <param name="name">Name of the event or delegate property.</param>
    public int GetHandlerCount(string name) =>
        DelegateHolders[name].HandlerCount;

    /// <summary>
    /// Invokes the specified event with provided arguments.
    /// </summary>
    public void Invoke(string eventName, params object[] arguments) =>
        DelegateInvoker.Invoke(this[eventName], arguments);

    /// <summary>
    /// Creates a strong-typed <see cref="TypedEventHub{TInterface}"/> wrapping this hub.
    /// </summary>
    /// <typeparam name="TInterface">Interface type.</typeparam>
    public TypedEventHub<TInterface> CreateTyped<TInterface>() where TInterface : class =>
        new TypedEventHub<TInterface>(this);

    /// <summary>
    /// Strong-typed wrapper around <see cref="ServiceEventHub"/>, for unit tests.
    /// Does not own the hub — all calls are delegated to <see cref="Hub"/>.
    /// </summary>
    /// <typeparam name="TInterface">Interface.</typeparam>
    public sealed class TypedEventHub<TInterface> where TInterface : class
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TypedEventHub{TInterface}"/>.
        /// Wraps the given <paramref name="hub"/>, or creates a fresh
        /// <see cref="ServiceEventHub"/> when <paramref name="hub"/> is <c>null</c>.
        /// </summary>
        /// <param name="hub">The event hub to wrap, or <c>null</c> to create a new one.</param>
        public TypedEventHub(ServiceEventHub hub = null)
        {
            Hub = hub ?? new ServiceEventHub(typeof(TInterface));

            var expected = typeof(TInterface);
            var actual = Hub.InterfaceType;

            if (!expected.IsAssignableFrom(actual) && !actual.IsAssignableFrom(expected))
            {
                throw new ArgumentException(
                    $"Event hub for '{actual}' is not compatible with '{expected}'.",
                    nameof(hub));
            }
        }

        /// <summary>
        /// Gets the wrapped event hub.
        /// </summary>
        public ServiceEventHub Hub { get; }

        /// <summary>
        /// Gets the <see cref="Delegate" /> with the specified event or delegate property name.
        /// </summary>
        /// <param name="name">Name of the event or delegate property.</param>
        public Delegate this[string name] => Hub[name];

        /// <summary>
        /// Invokes the specified event with provided arguments.
        /// </summary>
        /// <param name="getName">
        /// A function that returns event name. Must not use its argument
        /// (e.g. <c>s => nameof(s.SimpleEvent)</c>).
        /// </param>
        /// <param name="arguments">Event arguments.</param>
        public void Invoke(Func<TInterface, string> getName, params object[] arguments) =>
            Hub.Invoke(getName(null), arguments);

        /// <summary>
        /// Invokes the specified event with provided arguments.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="arguments">Event arguments.</param>
        public void Invoke(string eventName, params object[] arguments) =>
            Hub.Invoke(eventName, arguments);

        /// <summary>
        /// Adds the handler for the given event or delegate property.
        /// </summary>
        /// <param name="getName">
        /// A function that returns event name. Must not use its argument
        /// (e.g. <c>s => nameof(s.SimpleEvent)</c>).
        /// </param>
        /// <param name="handlerKey">Unique handler key of the client delegate.</param>
        /// <param name="sessionId">Session identifier of the subscriber.</param>
        /// <param name="handler">Event handler to add.</param>
        public void AddHandler(Func<TInterface, string> getName, Guid handlerKey, Guid sessionId, Delegate handler) =>
            Hub.AddHandler(getName(null), handlerKey, sessionId, handler);

        /// <summary>
        /// Adds the handler for the given event or delegate property.
        /// </summary>
        /// <param name="name">The event name.</param>
        /// <param name="handlerKey">Unique handler key of the client delegate.</param>
        /// <param name="sessionId">Session identifier of the subscriber.</param>
        /// <param name="handler">Event handler to add.</param>
        public void AddHandler(string name, Guid handlerKey, Guid sessionId, Delegate handler) =>
            Hub.AddHandler(name, handlerKey, sessionId, handler);

        /// <summary>
        /// Removes a single occurrence of the handler with the specified key.
        /// </summary>
        /// <param name="getName">
        /// A function that returns event name. Must not use its argument
        /// (e.g. <c>s => nameof(s.SimpleEvent)</c>).
        /// </param>
        /// <param name="handlerKey">Unique handler key of the client delegate.</param>
        /// <returns>True if an entry was removed, otherwise false.</returns>
        public bool RemoveHandler(Func<TInterface, string> getName, Guid handlerKey) =>
            Hub.RemoveHandler(getName(null), handlerKey);

        /// <summary>
        /// Removes a single occurrence of the handler with the specified key.
        /// </summary>
        /// <param name="name">The event name.</param>
        /// <param name="handlerKey">Unique handler key of the client delegate.</param>
        /// <returns>True if an entry was removed, otherwise false.</returns>
        public bool RemoveHandler(string name, Guid handlerKey) =>
            Hub.RemoveHandler(name, handlerKey);
    }
}
