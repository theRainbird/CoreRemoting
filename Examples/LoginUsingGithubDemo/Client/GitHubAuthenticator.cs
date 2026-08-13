using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using Octokit;
using static Logger<Client>;

/// <summary>
/// Github authenticator demo.
/// This code runs on the client-side.
/// </summary>
public class GitHubAuthenticator : IAuthenticator
{
    public GitHubAuthenticator(string clientId)
    {
        ClientId = clientId;
    }

    private string ClientId { get; }

    public async Task Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
    {
        var client = new GitHubClient(new ProductHeaderValue("CoreRemotingDemo"));

        var request = new OauthDeviceFlowRequest(ClientId);
        request.Scopes.Add("user");
        request.Scopes.Add("public_repo"); // for getting list of public repos

        var deviceFlow = await client.Oauth.InitiateDeviceFlow(request);
        WriteLine($"Please visit: {deviceFlow.VerificationUri}");
        WriteLine($"And enter the code: {deviceFlow.UserCode}");
        OpenBrowser(deviceFlow.VerificationUri);

        var token = await client.Oauth.CreateAccessTokenForDeviceFlow(ClientId, deviceFlow);

        await authProxy.Authenticate(new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = "token", Value = token.AccessToken }
            ]
        });
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                WriteLine($"Please open manually: {url}");
        }
        catch
        {
            WriteLine($"Please open manually: {url}");
        }
    }
}
