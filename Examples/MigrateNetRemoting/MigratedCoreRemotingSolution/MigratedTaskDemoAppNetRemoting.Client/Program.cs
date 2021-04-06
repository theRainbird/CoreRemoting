using System;
// using System.Configuration; // *** Namespace is no longer needed
using System.Windows.Forms;
using CoreRemoting.ClassicRemotingApi; // *** Added CoreRemoting.ClassicRemotingApi as replacement for System.Runtime.Remoting
// using System.Runtime.Remoting; *** Namespace is no longer needed

namespace MigratedTaskDemoAppNetRemoting.Client
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // *** .NET Remoting Client configuration is no longer needed ***
            // var configFilePath =
            //     ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)
            //         .FilePath;
            //
            // RemotingConfiguration.Configure(configFilePath, ensureSecurity: true); 
            
            RemotingConfiguration.Configure();
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TodoForm());
        }
    }
}