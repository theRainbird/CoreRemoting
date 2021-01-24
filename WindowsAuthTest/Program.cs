using CoreRemoting.Authentication;
using System;

namespace WindowsAuthTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Write("Username:");
            
            string userName = Console.ReadLine();

            Console.WriteLine();
            Console.Write("Password:");

            string password = Console.ReadLine();

            var credentials =
                new[]
                {
                    new Credential() { Name = "username", Value = userName },
                    new Credential() { Name = "password", Value = password }
                };

            var authProvider = new WindowsAuthProvider();
            if (authProvider.Authenticate(credentials, out RemotingIdentity identity))
            {
                Console.WriteLine("Success!");
                Console.WriteLine("Roles:");

                foreach (string role in identity.Roles)
                {
                    Console.WriteLine(role);
                }
            }
            else
                Console.WriteLine("Failed.");
        }
    }
}
