using System.Linq;
using CoreRemoting;
using static Logger<Server>;

internal class SampleService : ISampleService
{
    public void SayHello()
    {
        var identity = RemotingSession.Current.Identity;
        var userName = identity?.Name ?? "unknown";
        var roles = identity?.Roles ?? [];

        WriteLine($"Hello! Your Keycloak user name (sub) is: {userName}.");

        if (roles.Any())
        {
            WriteLine("Your roles:");
            foreach (var role in roles)
                WriteLine($"  - {role}");
        }
        else
        {
            WriteLine("You have no roles.");
        }
    }
}
