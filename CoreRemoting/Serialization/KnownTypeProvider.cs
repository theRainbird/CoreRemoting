using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security;
using CoreRemoting.Channels;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting.Serialization
{
    /// <summary>
    /// Component to provide known types that are safe for deserialization.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "ClassWithVirtualMembersNeverInherited.Global")]
    public class KnownTypeProvider : IKnownTypeProvider
    {
        protected readonly ConcurrentDictionary<Type, List<Type>> _knownTypes;

        /// <summary>
        /// Creates a new instance of the KnownTypeProvider class.
        /// </summary>
        public KnownTypeProvider()
        {
            _knownTypes = new ConcurrentDictionary<Type, List<Type>>();
        }

        /// <summary>
        /// Gets a list of static known types that are safe for deserialization.
        /// </summary>
        public virtual List<Type> StaticKnownTypes =>
            new List<Type>()
            {
                typeof(System.Data.DataTable),
                typeof(System.Data.DataSet),
                typeof(System.Runtime.Serialization.SerializationException),
                typeof(ApplicationException),
                typeof(Exception),
                typeof(ArgumentNullException),
                typeof(ArgumentException),
                typeof(ArgumentOutOfRangeException),
                typeof(InvalidCastException),
                typeof(InvalidOperationException),
                typeof(InsufficientMemoryException),
                typeof(OutOfMemoryException),
                typeof(KeyNotFoundException),
                typeof(IOException),
                typeof(StackOverflowException),
                typeof(AggregateException),
                typeof(ArithmeticException),
                typeof(FormatException),
                typeof(OverflowException),
                typeof(RankException),
                typeof(SystemException),
                typeof(TargetException),
                typeof(TimeoutException),
                typeof(AccessViolationException),
                typeof(AmbiguousMatchException),
                typeof(FieldAccessException),
                typeof(FileLoadException),
                typeof(SecurityException),
                typeof(NotSupportedException),
                typeof(RemoteInvocationException),
                typeof(NetworkException),
                typeof(MethodCallParameterMessage),
                typeof(MethodCallParameterMessage[]),
                typeof(MethodCallOutParameterMessage),
                typeof(byte[]),
                typeof(int[]),
                typeof(short[]),
                typeof(float[]),
                typeof(double[]),
                typeof(long[]),
                typeof(decimal[]),
                typeof(string[]),
                typeof(bool[]),
                typeof(Guid[]),
                typeof(DateTime[]),
                typeof(TimeSpan[]),
                typeof(RemoteDelegateInfo),
                typeof(CallContextEntry),
                typeof(CallContextEntry[]),
                typeof(GoodbyeMessage)
            };

        /// <summary>
        /// Gets a list of types for one or more specified types.
        /// </summary>
        /// <param name="types">Type whose known types should be determined</param>
        /// <returns>List of known types safe for deserialization</returns>
        public virtual List<Type> GetKnownTypesByTypeList(IEnumerable<Type> types)
        {
            var knownTypeList = new List<Type>(StaticKnownTypes);
            
            foreach (var type in types)
            {
                if (!_knownTypes.ContainsKey(type))
                {
                    var typeKnownTypeList = new List<Type>();
                    
                    var serviceKnownTypes =
                        type.GetCustomAttributes<ServiceKnownTypeAttribute>().ToList();

                    foreach (var method in type.GetMethods())
                    {
                        serviceKnownTypes.AddRange(method.GetCustomAttributes<ServiceKnownTypeAttribute>());
                    }
                    
                    foreach (var property in type.GetProperties())
                    {
                        serviceKnownTypes.AddRange(property.GetCustomAttributes<ServiceKnownTypeAttribute>());
                    }
                    
                    foreach (var serviceKnownType in serviceKnownTypes)
                    {
                        if (!typeKnownTypeList.Contains(serviceKnownType.Type))
                            typeKnownTypeList.Add(serviceKnownType.Type);
                    }

                    _knownTypes.TryAdd(type, typeKnownTypeList);
                }
                
                knownTypeList.AddRange(_knownTypes[type].Except(knownTypeList));
            }
            
            return knownTypeList;
        }
    }
}