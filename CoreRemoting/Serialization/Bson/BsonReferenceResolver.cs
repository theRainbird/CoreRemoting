using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using CoreRemoting.Toolbox;
using Newtonsoft.Json.Serialization;

namespace CoreRemoting.Serialization.Bson;

/// <summary>
/// A copy of Newtonsoft.Json.Serialization.DefaultReferenceResolver
/// with a thread-safe reference counter and concurrent dictionaries.
/// </summary>
/// <remarks>
/// See https://github.com/JamesNK/Newtonsoft.Json/issues/870
/// And https://github.com/JamesNK/Newtonsoft.Json/pull/1393
/// And https://github.com/JamesNK/Newtonsoft.Json/pull/2170
/// </remarks>
internal class BsonReferenceResolver : IReferenceResolver
{
    private int _referenceCount;

    /// <summary>
    /// Based on Newtonsoft.Json.Utilities.BidirectionalDictionary,
    /// but using ConcurrentDictionary backend storage in both directions.
    /// </summary>
    internal class BidirectionalDictionary<TFirst, TSecond>
        where TFirst : notnull
        where TSecond : notnull
    {
        private readonly ConcurrentDictionary<TFirst, TSecond> _firstToSecond;
        private readonly ConcurrentDictionary<TSecond, TFirst> _secondToFirst;
        private readonly string _duplicateFirstErrorMessage;
        private readonly string _duplicateSecondErrorMessage;

        public BidirectionalDictionary()
            : this(EqualityComparer<TFirst>.Default, EqualityComparer<TSecond>.Default)
        {
        }

        public BidirectionalDictionary(IEqualityComparer<TFirst> firstEqualityComparer, IEqualityComparer<TSecond> secondEqualityComparer)
            : this(firstEqualityComparer, secondEqualityComparer,
                "Duplicate item already exists for '{0}'.",
                "Duplicate item already exists for '{0}'.")
        {
        }

        public BidirectionalDictionary(IEqualityComparer<TFirst> firstEqualityComparer, IEqualityComparer<TSecond> secondEqualityComparer,
            string duplicateFirstErrorMessage, string duplicateSecondErrorMessage)
        {
            _firstToSecond = new ConcurrentDictionary<TFirst, TSecond>(firstEqualityComparer);
            _secondToFirst = new ConcurrentDictionary<TSecond, TFirst>(secondEqualityComparer);
            _duplicateFirstErrorMessage = duplicateFirstErrorMessage;
            _duplicateSecondErrorMessage = duplicateSecondErrorMessage;
        }

        public void Set(TFirst first, TSecond second)
        {
            if (!_firstToSecond.GetOrAdd(first, second).Equals(second))
                throw new ArgumentException(string.Format(_duplicateFirstErrorMessage, first));

            if (!_secondToFirst.GetOrAdd(second, first).Equals(first))
                throw new ArgumentException(string.Format(_duplicateSecondErrorMessage, second));
        }

        public bool TryGetByFirst(TFirst first, out TSecond second) =>
            _firstToSecond.TryGetValue(first, out second);

        public bool TryGetBySecond(TSecond second, out TFirst first) =>
            _secondToFirst.TryGetValue(second, out first);
    }

    private BidirectionalDictionary<string, object> GetMappings(object context)
    {
        if (context.Get<BidirectionalDictionary<string, object>>(out var result))
        {
            return result;
        }

        var mappings = new BidirectionalDictionary<string, object>();
        context.Set(mappings);
        return mappings;
    }

    public object ResolveReference(object context, string reference)
    {
        GetMappings(context).TryGetByFirst(reference, out object value);
        return value;
    }

    public string GetReference(object context, object value)
    {
        var mappings = GetMappings(context);

        if (!mappings.TryGetBySecond(value, out string reference))
        {
            Interlocked.Increment(ref _referenceCount);
            reference = _referenceCount.ToString(CultureInfo.InvariantCulture);
            mappings.Set(reference, value);
        }

        return reference;
    }

    public void AddReference(object context, string reference, object value) =>
        GetMappings(context).Set(reference, value);

    public bool IsReferenced(object context, object value) =>
        GetMappings(context).TryGetBySecond(value, out _);
}
