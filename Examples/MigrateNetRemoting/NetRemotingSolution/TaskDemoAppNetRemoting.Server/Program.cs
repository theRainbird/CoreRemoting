using System;
using System.Configuration;
using System.Runtime.Remoting;

namespace TaskDemoAppNetRemoting.Server
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var configFilePath =
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)
                    .FilePath;
            
            RemotingConfiguration.Configure(configFilePath, ensureSecurity: true);
            
            Console.WriteLine("Server running (Press [Enter] to quit)");
            Console.ReadLine();
        }
    }
}