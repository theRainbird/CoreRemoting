using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using static CoreRemoting.Authentication.AuthenticationResponseMessage;
using static Logger<Server>;


/// <summary>
/// Two-factor authentication provider demo.
/// This code runs on the server-side.
/// </summary>
internal class TwoFactorAuthProvider : IAuthenticationProvider
{
    static ConcurrentDictionary<string, string> GeneratedCodes { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage request)
    {
        var login = request["login"];
        var password = request["password"];
        var code = request["code"];

        if (string.IsNullOrWhiteSpace(login) ||
            string.IsNullOrWhiteSpace(password))
        {
            return Error("No login or password specified");
        }

        // step1: verify userName & password, generate random code
        if (string.IsNullOrWhiteSpace(code))
        {
            var genCode = RandomNumberGenerator.GetHexString(4);
            GeneratedCodes[login] = genCode;

            await SendSmsToUser(login, genCode);
            return new()
            {
                IsCompleted = false
            };
        }

        // step2: verify generated code
        if (GeneratedCodes.TryRemove(login, out var newCode))
        {
            if (newCode.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                return new()
                {
                    IsAuthenticated = true,
                    IsCompleted = true,
                    AuthenticatedIdentity = new RemotingIdentity
                    {
                        Name = login,
                    }
                };
            }
        }

        return Error();
    }

    private async Task SendSmsToUser(string userName, string genCode)
    {
        WriteLine($"Pretending to send {userName} a code via SMS: {genCode}");

        // deliver the code to the userName's device
        await Task.Delay(100);
    }
}
