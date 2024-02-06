using System;
using System.Reflection;

namespace CoreRemoting.Serialization;

/// <summary>
/// Provides tools to support serialization between different .NET frameworks (i.e. .NET 6.x and .NET Framework 4.x)
/// </summary>
public static class CrossFrameworkSerialization
{
    /// <summary>
    /// Redirects all loading attempts from a specified assembly name to another assembly name.
    /// </summary>
    /// <param name="assemblyShortName">Name of the assembly that should be redirected</param>
    /// <param name="replacmentAssemblyShortName">Name of the assembly that should be used as replacement</param>
    public static void RedirectAssembly(string assemblyShortName, string replacmentAssemblyShortName)
    {
        Assembly HandleAssemblyResolve(object _, ResolveEventArgs args)
        {
            var requestedAssembly = new AssemblyName(args.Name);
            
            if (requestedAssembly.Name == assemblyShortName)
            {
                try
                {
                    var replacmentAssembly = Assembly.Load(replacmentAssemblyShortName);
                    return replacmentAssembly;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return null;
        }

        AppDomain.CurrentDomain.AssemblyResolve += HandleAssemblyResolve;
    }

    /// <summary>
    /// Redirects assembly "System.Private.CoreLib" to "mscorlib".
    /// </summary>
    public static void RedirectPrivateCoreLibToMscorlib()
    {
        RedirectAssembly("System.Private.CoreLib", "mscorlib");
    }
    
    /// <summary>
    /// Redirects assembly "mscorlib" to "System.Private.CoreLib".
    /// </summary>
    public static void RedirectMscorlibToPrivateCoreLib()
    {
        RedirectAssembly("mscorlib", "System.Private.CoreLib");
    }
}