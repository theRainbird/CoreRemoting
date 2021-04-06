// using System; // *** Namespace is no longer needed
// using System.Configuration; // *** Namespace is no longer needed
using MigratedTaskDemoAppNetRemoting.Shared;
using CoreRemoting.ClassicRemotingApi; // *** Added namespace

namespace MigratedTaskDemoAppNetRemoting.Client
{
    public static class ServiceProxyHelper
    {
        // *** Server URL field is no longer needed
        //private static string _serverUrl;
        
        // *** Server URL property is no longer needed
        // public static string ServerUrl
        // {
        //     get
        //     {
        //         if (string.IsNullOrWhiteSpace(_serverUrl))
        //             _serverUrl = ConfigurationManager.AppSettings.Get("serverUrl");
        //
        //         return _serverUrl;
        //     }
        // }

        public static ITodoService GetTaskServiceProxy()
        {
            // *** Create a proxy using CoreRemoting.ClassicRemotingApi
            return (ITodoService) RemotingServices.Connect(
                interfaceType: typeof(ITodoService),
                serviceName: "TodoService");
            
            // Original .NET Remoting code
            // return (ITodoService) Activator.GetObject(typeof(ITodoService), ServerUrl + "/TodoService");
        }
    }
}