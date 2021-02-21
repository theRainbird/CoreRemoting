using System;
using System.Linq;
using System.Reflection;

namespace CoreRemoting.DependencyInjection
{
    /// <summary>
    /// Provides extension methods for dependency injection containers.
    /// </summary>
    public static class DependencyInjectionContainerExtensions
    {
        /// <summary>
        /// Gets the method info of the RegisterService method.
        /// </summary>
        /// <param name="container">DI container</param>
        /// <param name="interfaceType">Service interface type</param>
        /// <param name="implementationType">Service implementation type</param>
        /// <returns>Method info of the RegisterService method</returns>
        /// <exception cref="ArgumentNullException">Thrown if container is set to null</exception>
        public static MethodInfo GetRegisterServiceMethodForWellknownServiceType(
            this IDependencyInjectionContainer container,
            Type interfaceType,
            Type implementationType)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));
            
            var registerServiceMethod =
                container
                    .GetType()
                    .GetMethods()
                    .Where(m =>
                        m.Name == "RegisterService" &&
                        m.IsGenericMethodDefinition)
                    .Select(m => new
                    {
                        Method = m,
                        Params = m.GetParameters(),
                        Args = m.GetGenericArguments()
                    })
                    .Where(x =>
                        x.Params.Length == 2 &&
                        x.Args.Length == 2)
                    .Select(x => x.Method)
                    .First()
                    .MakeGenericMethod(interfaceType, implementationType);

            return registerServiceMethod;
        }
        
        /// <summary>
        /// Gets the method info of the RegisterService method to register an object as service..
        /// </summary>
        /// <param name="container">DI container</param>
        /// <param name="interfaceType">Service interface type</param>
        /// <param name="serviceInstance">Service instance</param>
        /// <returns>Method info of the RegisterService method</returns>
        /// <exception cref="ArgumentNullException">Thrown if container is set to null</exception>
        public static MethodInfo GetRegisterServiceMethodForServiceInstance(
            this IDependencyInjectionContainer container,
            Type interfaceType,
            object serviceInstance)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));
            
            var registerServiceMethod =
                container
                    .GetType()
                    .GetMethods()
                    .Where(m =>
                        m.Name == "RegisterService" &&
                        m.IsGenericMethodDefinition)
                    .Select(m => new
                    {
                        Method = m,
                        Params = m.GetParameters(),
                        Args = m.GetGenericArguments()
                    })
                    .Where(x =>
                        x.Params.Length == 3 &&
                        x.Args.Length == 1)
                    .Select(x => x.Method)
                    .First()
                    .MakeGenericMethod(interfaceType);

            return registerServiceMethod;
        }
    }
}