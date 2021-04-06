using System;
using System.Configuration;
using System.Linq;
using CoreRemoting;
using CoreRemoting.ClassicRemotingApi; // *** Changed namespace from System.Runtime.Remoting to CoreRemoting.ClassicRemotingApi

namespace MigratedTaskDemoAppNetRemoting.Server
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var configFilePath =
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)
                    .FilePath;
            
            RemotingConfiguration.Configure(configFilePath); // *** Removed ensureSecurity parameter
            // RemotingConfiguration.Configure(configFilePath, ensureSecurity: true);
            
            Console.WriteLine("Server running (Press [Enter] to quit)");
            Console.ReadLine();
        }
    }
}