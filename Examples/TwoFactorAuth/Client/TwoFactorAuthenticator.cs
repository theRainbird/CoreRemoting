using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Toolbox;
using static Logger<Client>;

/// <summary>
/// Two-factor authenticator demo.
/// This code runs on the client side.
/// </summary>
public class TwoFactorAuthenticator : IAuthenticator
{
    public async Task Authenticate(Credential[] credentials, IAuthenticationProvider authProxy)
    {
        // step1: send login + password => server responds with an incomplete auth message
        var resp = await authProxy.Authenticate(new()
        {
            Credentials = credentials
        });

        // step2: ask for 2fa code and send it => server accepts or rejects the code
        if (!resp.IsCompleted)
        {
            Write("Enter 2FA code sent by server: ");
            var code = ReadLine();

            // append auth code to the credentials
            resp = await authProxy.Authenticate(new()
            {
                Credentials = credentials.Append(new Credential
                {
                    Name = "code",
                    Value = code,
                })
            });
        }
    }
}
