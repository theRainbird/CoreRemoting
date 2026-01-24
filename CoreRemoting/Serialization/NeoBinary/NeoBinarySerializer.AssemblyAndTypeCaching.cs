using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CoreRemoting.Serialization.NeoBinary;

partial class NeoBinarySerializer
{
	/// <summary>
	/// Pre-builds type indexes for loaded assemblies to avoid expensive reflection calls during serialization.
	/// Call this method at application startup for optimal performance. This is shared across all NeoBinarySerializer instances.
	/// </summary>
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public static void BuildAssemblyTypeIndexes()
	{
		var assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (var assembly in assemblies)
			// This will populate the _assemblyTypeIndex cache
			_assemblyTypeIndex.GetOrAdd(assembly, BuildTypeIndexForAssembly);
	}

	/// <summary>
	/// Builds a type index dictionary for a given assembly.
	/// </summary>
	/// <param name="assembly">The assembly to build the index for</param>
	/// <returns>Dictionary mapping type full names to Type objects</returns>
	private static Dictionary<string, Type> BuildTypeIndexForAssembly(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes()
				.Where(t => t is { FullName: not null })
				.GroupBy(t => t.FullName!)
				.ToDictionary(g => g.Key, g => g.First());
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types
				.Where(t => t is { FullName: not null })
				.GroupBy(t => t!.FullName!)
				.ToDictionary(g => g.Key, g => g.First()!);
		}
	}

	/// <summary>
	/// Clears the assembly type cache to free memory.
	/// Should be called when memory pressure is high or when assemblies are unloaded.
	/// </summary>
	[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
	public void ClearAssemblyTypeCache()
	{
		_assemblyTypeCache.Clear();
		_assemblyNameCache.Clear();

		// Also clear search cache entries
		var keysToRemove = _resolvedTypeCache.Keys
			.Where(key => key.StartsWith("search_"))
			.ToList();

		foreach (var key in keysToRemove) _resolvedTypeCache.TryRemove(key, out _);
	}

	/// <summary>
	/// Performance-optimized method to get types from an assembly with caching.
	/// </summary>
	/// <param name="assembly">The assembly to get types from</param>
	/// <returns>Array of types in the assembly</returns>
	private Type[] GetAssemblyTypesCached(Assembly assembly)
	{
		return _assemblyTypeCache.GetOrAdd(assembly, asm =>
		{
			try
			{
				return asm.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				// Handle partial loading - return only successfully loaded types
				return ex.Types.Where(t => t != null).ToArray();
			}
		});
	}

	/// <summary>
	/// Performance-optimized method to load assembly with caching.
	/// </summary>
	/// <param name="assemblyName">The assembly name to load</param>
	/// <returns>The loaded assembly or null if not found</returns>
	private Assembly GetAssemblyCached(string assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
			return null;

		return _assemblyNameCache.GetOrAdd(assemblyName, name =>
		{
			try
			{
				return Assembly.Load(name);
			}
			catch
			{
				// Try to find in currently loaded assemblies - more comprehensive search
				return AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => a.GetName().Name == name || a.FullName == name || 
					               a.GetName().FullName == name);
			}
		});
	}

	/// <summary>
	/// Performance-optimized type search across loaded assemblies with caching.
	/// </summary>
	/// <param name="typeName">The type name to search for</param>
	/// <returns>The found type or null</returns>
	private Type FindTypeInLoadedAssembliesCached(string typeName)
	{
		// Create a cache key for assembly-wide type search
		var searchCacheKey = $"search_{typeName}";

		// Check if we've already searched for this type recently
		if (_resolvedTypeCache.TryGetValue(searchCacheKey, out var cachedResult))
			return cachedResult;

		Type foundType = null;
		var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

		// Search through assemblies with cached type arrays
		foreach (var assembly in loadedAssemblies)
		{
			var assemblyTypes = GetAssemblyTypesCached(assembly);
			foundType = assemblyTypes.FirstOrDefault(t =>
				t.FullName == typeName || t.Name == typeName || 
				t.AssemblyQualifiedName == typeName);

			if (foundType != null)
				break;
		}

		// Cache the search result (null is also cached to avoid repeated searches)
		// Note: Don't cache null results for too long to handle dynamic assembly loading
		if (foundType != null)
		{
			_resolvedTypeCache[searchCacheKey] = foundType;
		}
		else
		{
			// Cache null result with shorter lifetime - remove after 5 seconds
			// This allows retrying when assemblies are loaded dynamically
			_resolvedTypeCache.TryAdd(searchCacheKey, null);
		}

		return foundType;
	}

	/// <summary>
	/// Special type finder for anonymous types that handles assembly context issues.
	/// Anonymous types need exact matching since they can't be loaded across assembly boundaries.
	/// </summary>
	/// <param name="typeName">The anonymous type name</param>
	/// <param name="assemblyName">The assembly name where the type was created</param>
	/// <returns>The found type or null</returns>
	private static Type FindAnonymousTypeInLoadedAssemblies(string typeName, string assemblyName)
	{
		// For anonymous types, we need exact assembly matching
		var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
		var targetAssembly = loadedAssemblies.FirstOrDefault(a => 
			a.GetName().Name == assemblyName || a.FullName == assemblyName);

		if (targetAssembly != null)
		{
			var assemblyTypes = targetAssembly.GetTypes();
			// Try exact name match first, then fallback to pattern matching
			var foundType = assemblyTypes.FirstOrDefault(t => t.FullName == typeName);
			
			if (foundType == null)
			{
				// For anonymous types, try pattern matching ignoring the unique suffix
				var baseTypeName = ExtractAnonymousTypeBaseName(typeName);
				foundType = assemblyTypes.FirstOrDefault(t => 
					t.FullName?.StartsWith(baseTypeName) == true);
			}

			return foundType;
		}

		return null;
	}

	/// <summary>
	/// Extracts the base name from an anonymous type by removing the unique suffix.
	/// Anonymous types have names like "<>f__AnonymousType0`5_311923e08918438cb459b90fa4f9c314"
	/// We want to extract "<>f__AnonymousType0`5" for matching.
	/// </summary>
	/// <param name="fullTypeName">The full anonymous type name</param>
	/// <returns>The base name without unique suffix</returns>
	private static string ExtractAnonymousTypeBaseName(string fullTypeName)
	{
		if (string.IsNullOrEmpty(fullTypeName))
			return fullTypeName;

		// Find the underscore that separates the base name from the unique suffix
		var underscoreIndex = fullTypeName.LastIndexOf('_');
		if (underscoreIndex > 0)
		{
			return fullTypeName.Substring(0, underscoreIndex);
		}

		return fullTypeName;
	}

	/// <summary>
	/// Builds a type name string without embedding assembly information for generic argument types.
	/// The format is compatible with Type.GetType style generic notation, e.g.:
	/// Namespace.Generic`1[[Arg.Namespace.Type]]
	/// </summary>
	private string BuildAssemblyNeutralTypeName(Type type)
	{
		return _typeNameCache.GetOrAdd(type, t =>
		{
			string result;

			if (t.IsGenericType)
			{
				var genericDef = t.GetGenericTypeDefinition();
				var defName = genericDef.FullName; // e.g. System.Collections.Generic.List`1
				var args = t.GetGenericArguments();
				var argNames = args.Select(BuildAssemblyNeutralTypeName).ToArray();

				// Validate generic arguments before serializing
				for (var i = 0; i < argNames.Length; i++)
					if (string.IsNullOrEmpty(argNames[i]))
						throw new InvalidOperationException(
							$"Cannot serialize generic type '{t.FullName}' - generic argument {i} resolved to empty string");

				var sb = new StringBuilder();
				for (var i = 0; i < argNames.Length; i++)
				{
					if (i > 0) sb.Append("],[");
					sb.Append(argNames[i]);
				}

				result = $"{defName}[[{sb}]]";
			}
			else if (t.IsArray)
			{
				// Handle arrays by composing element type and rank suffix
				var elem = t.GetElementType();
				var rank = t.GetArrayRank();
				var suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
				result = BuildAssemblyNeutralTypeName(elem) + suffix;
			}
			else
			{
				result = t.FullName ?? t.Name;
			}

			return _serializerCache.GetOrCreatePooledString(result);
		});
	}

	/// <summary>
	/// Resolves a type name written without assembly qualifiers for generic arguments.
	/// Tries Type.GetType, then searches loaded assemblies, and for generics parses and resolves recursively.
	/// </summary>
	private Type ResolveAssemblyNeutralType(string typeName)
	{
		return _resolvedTypeCache.GetOrAdd(typeName, tn =>
		{
			// First try assembly search (for custom types)
			var t = FindTypeInLoadedAssemblies(tn);
			if (t != null) 
				return t;

			// Fallback to Type.GetType (for system types)
			// Clean invalid PublicKeyToken=null
			var cleanTn = tn.Replace(", PublicKeyToken=null", "");
			t = Type.GetType(cleanTn);
			if (t != null) 
				return t;

			// Handle simple array types (single dimension)
			if (tn.EndsWith("[]", StringComparison.Ordinal))
			{
				var elementTypeName = tn.Substring(0, tn.Length - 2);
				var elementType = ResolveAssemblyNeutralType(elementTypeName);
				if (elementType != null)
					return elementType.MakeArrayType();
			}

			// If looks like a generic with our [[...]] notation
			var idx = tn.IndexOf("[[", StringComparison.Ordinal);
			if (idx <= 0) 
				return FindTypeInLoadedAssemblies(tn);
			
			var defName = tn.Substring(0, idx);
			var argsPart = tn.Substring(idx);

			var defType = FindTypeInLoadedAssemblies(defName) ?? Type.GetType(defName);
			if (defType == null)
				return null;

			var argNames = ParseGenericArgumentNames(argsPart);
			var argTypes = new Type[argNames.Count];
			for (var i = 0; i < argNames.Count; i++)
			{
				var at = ResolveAssemblyNeutralTypeWithFallback(argNames[i]);
					
				if (at == null) 
					return null;
					
				argTypes[i] = at;
			}

			try
			{
				return defType.MakeGenericType(argTypes);
			}
			catch
			{
				return null;
			}
		});
	}

	private Type FindTypeInLoadedAssemblies(string fullOrSimpleName)
	{
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			try
			{
				var t = asm.GetType(fullOrSimpleName, false, false);
				if (t != null) return t;
			}
			catch
			{
				/* ignore problematic assemblies */
			}

		// Fallback: search by simple name across all types (could be expensive)
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			try
			{
				var t = asm.GetTypes()
					.FirstOrDefault(x => x.FullName == fullOrSimpleName || x.Name == fullOrSimpleName);
				if (t != null) return t;
			}
			catch
			{
				/* ignore */
			}

		return null;
	}
}