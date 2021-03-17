using System;
using System.Configuration;
using System.Runtime.Remoting;
using System.Windows.Forms;

namespace TaskDemoAppNetRemoting.Client
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var configFilePath =
                ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None)
                    .FilePath;
            
            RemotingConfiguration.Configure(configFilePath, ensureSecurity: true);
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TodoForm());

        }
    }
}