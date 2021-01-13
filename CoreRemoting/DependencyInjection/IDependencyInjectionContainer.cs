using System;
using System.Collections.Generic;

namespace CoreRemoting.DependencyInjection
{
    public interface IDependencyInjectionContainer : IDisposable
    {
        object GetService(string serviceName);

        TServiceInterface GetService<TServiceInterface>(string serviceName = "") 
            where TServiceInterface : class;

        void RegisterService<TServiceInterface, TServiceImpl>(
            ServiceLifetime lifetime, 
            string serviceName = "")
            where TServiceInterface : class
            where  TServiceImpl : class, TServiceInterface;

        void RegisterService<TServiceInterface>(
            Func<TServiceInterface> factoryDelegate, 
            ServiceLifetime lifetime, 
            string serviceName = "")
            where TServiceInterface : class;

        Type GetServiceInterfaceType(string serviceName);

        IEnumerable<Type> GetAllRegisteredTypes();
        
        bool IsRegistered<TServiceInterface>(string serviceName = "") where TServiceInterface : class;
    }
}