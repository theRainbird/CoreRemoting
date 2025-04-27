using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;

namespace CoreRemoting.Serialization.Bson
{
    /// <summary>
    /// Provides a registry for custom type conversions when deserializing BSON data.
    /// </summary>
    public static class BsonTypeConversionRegistry
    {
        private static readonly Dictionary<Type, Func<object, object>> _registry = CreateDefaultRegistry();

        /// <summary>
        /// Gets or sets the function used to convert a wrapped value to its expected type.
        /// </summary>
        /// <remarks>
        /// This strategy is invoked when the actual type of the wrapped value differs from the expected type.
        /// By default, it uses <see cref="Convert.ChangeType(object, Type)"/>, but it can be overridden
        /// to provide custom conversion logic for edge cases or unsupported types.
        /// </remarks>
        public static Func<object, Type, object> DefaultTypeConversion { get; set; } = Convert.ChangeType;

        /// <summary>
        /// Attempts to retrieve a registered type conversion function for a specified type.
        /// </summary>
        /// <param name="type">Type to retrieve a conversion function</param>
        /// <param name="converter">Contains the conversion function if found; otherwise null</param>
        /// <returns><c>true</c> if a conversion function was found for the given type.</returns>
        public static bool TryGetTypeConversion(Type type, out Func<object, object> converter)
        {
            return _registry.TryGetValue(type, out converter);
        }

        /// <summary>
        /// Adds a new type conversion function or updates an existing one for a specified type.
        /// </summary>
        /// <param name="type">The target type to associate with the conversion function.</param>
        /// <param name="converter">The conversion function that converts an object to the specified type.</param>
        public static void AddOrUpdateTypeConversion(Type type, Func<object, object> converter)
        {
            _registry[type] = converter;
        }

        /// <summary>
        /// Creates the default registry mapping common .NET types to corresponding converters.
        /// </summary>
        /// <returns>A dictionary of default type converters</returns>
        private static Dictionary<Type, Func<object, object>> CreateDefaultRegistry()
        {
            return new Dictionary<Type, Func<object, object>>
            {
                { typeof(DateTimeOffset), value => ConvertToDateTimeOffset(value) },
                { typeof(TimeSpan), value => TimeSpan.Parse(value.ToString()) },
                { typeof(Uri), value => new Uri(value.ToString()) },
                { typeof(CultureInfo), value => GetCultureInfo(value.ToString()) },
                { typeof(RegionInfo), value => new RegionInfo(value.ToString()) },
                { typeof(Version), value => new Version(value.ToString()) },
                { typeof(BigInteger), value => ConvertToBigInteger(value) },
                { typeof(IPAddress), value => IPAddress.Parse(value.ToString()) },
                { Type.GetType("System.RuntimeType"), value => Type.GetType(value.ToString()) },
            };
        }

        private static DateTimeOffset ConvertToDateTimeOffset(object value)
        {
            return value is DateTime dateTime
                ? new DateTimeOffset(dateTime)
                : DateTimeOffset.Parse(value.ToString());
        }

        private static CultureInfo GetCultureInfo(string value)
        {
            // InvariantCulture.Name = "(Default)" and cannot be retrieved through CultureInfo.GetCultureInfo
            return value == "(Default)"
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(value);
        }

        private static BigInteger ConvertToBigInteger(object value)
        {
            return value is byte[] byteArray
                ? new BigInteger(byteArray)
                : BigInteger.Parse(value.ToString());
        }
    }
}
