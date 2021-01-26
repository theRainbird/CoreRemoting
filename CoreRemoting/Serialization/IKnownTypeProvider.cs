using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization
{
    /// <summary>
    /// Interface that known type provider components must implement.
    /// </summary>
    public interface IKnownTypeProvider
    {
        /// <summary>
        /// Gets a list of types for one or more specified types.
        /// </summary>
        /// <param name="types">Type whose known types should be determined</param>
        /// <returns>List of known types safe for deserialization</returns>
        List<Type> GetKnownTypesByTypeList(IEnumerable<Type> types);

        /// <summary>
        /// Gets a list of static known types that are safe for deserialization.
        /// </summary>
        List<Type> StaticKnownTypes { get; }
    }
}