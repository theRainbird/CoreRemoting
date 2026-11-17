using System.Linq;
using CoreRemoting;
using static Logger<Server>;

internal class SampleService : ISampleService
{
    public void SayHello()
    {
        var identity = RemotingSession.Current.Identity;
        var userName = identity?.Name ?? "unknown";
        var repos = identity?.Roles ?? [];

        WriteLine($"Hello! Your GitHub user name is: {userName}.");

        if (repos.Any())
        {
            WriteLine("Your repos:");
            foreach (var repo in repos)
                WriteLine($"  - {repo}");
        }
        else
        {
            WriteLine("You have no repositories.");
        }
    }
}