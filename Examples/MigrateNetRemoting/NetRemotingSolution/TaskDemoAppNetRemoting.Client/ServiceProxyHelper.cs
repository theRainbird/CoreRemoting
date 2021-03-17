using System;
using System.Configuration;
using TaskDemoAppNetRemoting.Shared;

namespace TaskDemoAppNetRemoting.Client
{
    public static class ServiceProxyHelper
    {
        private static string _serverUrl;

        public static string ServerUrl
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_serverUrl))
                    _serverUrl = ConfigurationManager.AppSettings.Get("serverUrl");

                return _serverUrl;
            }
        }

        public static ITodoService GetTaskServiceProxy()
        {
            return (ITodoService) Activator.GetObject(typeof(ITodoService), ServerUrl + "/TodoService");
        }
    }
}