using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization
{
    public interface IKnownTypeProvider
    {
        List<Type> GetKnownTypesByTypeList(IEnumerable<Type> types);

        List<Type> StaticKnownTypes { get; }
    }
}