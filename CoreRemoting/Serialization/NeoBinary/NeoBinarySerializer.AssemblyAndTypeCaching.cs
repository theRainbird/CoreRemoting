using System;
using System.Collections.Generic;
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
				.Where(t => t != null && t.FullName != null)
				.GroupBy(t => t.FullName!)
				.ToDictionary(g => g.Key, g => g.First());
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types
				.Where(t => t != null && t.FullName != null)
				.GroupBy(t => t!.FullName!)
				.ToDictionary(g => g.Key, g => g.First()!);
		}
	}

	/// <summary>
	/// Gets cache statistics for monitoring performance.
	/// </summary>
	/// <returns>Tuple with assembly cache count and name cache count</returns>
	public (int AssemblyTypeCacheCount, int AssemblyNameCacheCount, int SearchCacheCount) GetCacheStatistics()
	{
		var searchCacheCount = _resolvedTypeCache.Keys.Count(key => key.StartsWith("search_"));
		return (_assemblyTypeCache.Count, _assemblyNameCache.Count, searchCacheCount);
	}

	/// <summary>
	/// Clears the assembly type cache to free memory.
	/// Should be called when memory pressure is high or when assemblies are unloaded.
	/// </summary>
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
				// Try to find in currently loaded assemblies
				return AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => a.GetName().Name == name || a.FullName == name);
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
				t.FullName == typeName || t.Name == typeName);

			if (foundType != null)
				break;
		}

		// Cache the search result (null is also cached to avoid repeated searches)
		_resolvedTypeCache[searchCacheKey] = foundType;
		return foundType;
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
				for (int i = 0; i < argNames.Length; i++)
				{
					if (string.IsNullOrEmpty(argNames[i]))
					{
						throw new InvalidOperationException(
							$"Cannot serialize generic type '{t.FullName}' - generic argument {i} resolved to empty string");
					}
				}
				
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
			// Fast path
			var t = Type.GetType(tn);
			if (t != null) return t;

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
			if (idx > 0)
			{
				var defName = tn.Substring(0, idx);
				var argsPart = tn.Substring(idx);

				var defType = FindTypeInLoadedAssemblies(defName) ?? Type.GetType(defName);
				if (defType == null)
					return null;

				var argNames = ParseGenericArgumentNames(argsPart);
				var argTypes = new Type[argNames.Count];
				for (var i = 0; i < argNames.Count; i++)
				{
					var at = ResolveAssemblyNeutralType(argNames[i]);
					if (at == null) return null;
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
			}

			// Non-generic: search loaded assemblies by FullName then by Name
			return FindTypeInLoadedAssemblies(tn);
		});
	}

	private static Type FindTypeInLoadedAssemblies(string fullOrSimpleName)
	{
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			try
			{
				var t = asm.GetType(fullOrSimpleName, throwOnError: false, ignoreCase: false);
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