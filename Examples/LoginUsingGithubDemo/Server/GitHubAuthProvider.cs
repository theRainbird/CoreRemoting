using System;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using Octokit;
using static Logger<Server>;

/// <summary>
/// Github authentication provider demo.
/// This code runs on the server-side.
/// </summary>
public class GitHubAuthProvider : IAuthenticationProvider
{
    public async Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage request)
    {
        var token = request["token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AuthenticationResponseMessage { IsAuthenticated = false };
        }

        try
        {
            var client = new GitHubClient(new ProductHeaderValue("CoreRemotingValidator"))
            {
                Credentials = new Credentials(token)
            };

            var user = await client.User.Current();

            // Get repositories to use as "roles"
            var repos = await client.Repository.GetAllForCurrent();
            var roles = repos.Select(r => r.Name).ToArray();

            return new AuthenticationResponseMessage
            {
                IsAuthenticated = true,
                IsCompleted = true,
                AuthenticatedIdentity = new RemotingIdentity
                {
                    Name = user.Login,
                    IsAuthenticated = true,
                    Roles = roles
                }
            };
        }
        catch (AuthorizationException)
        {
            WriteLine("GitHub token validation failed (invalid or expired)");
            return new AuthenticationResponseMessage { IsAuthenticated = false };
        }
        catch (Exception ex)
        {
            WriteLine($"GitHub validation error: {ex.Message}");
            return new AuthenticationResponseMessage { IsAuthenticated = false };
        }
    }
}
