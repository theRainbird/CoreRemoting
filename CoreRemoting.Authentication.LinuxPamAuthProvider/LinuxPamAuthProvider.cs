using System.Linq;
using CoreRemoting.Authentication;
using Npam;

namespace CoreRemoting.Authentication
{
    public class LinuxPamAuthProvider : IAuthenticationProvider
    {
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            authenticatedIdentity = null;
            
            if (credentials == null)
                return false;

            var userName =
                credentials
                    .Where(c => c.Name.ToLower() == "username")
                    .Select(c => c.Value)
                    .FirstOrDefault();
            
            var password =
                credentials
                    .Where(c => c.Name.ToLower() == "password")
                    .Select(c => c.Value)
                    .FirstOrDefault();

            var isAuthenticated = NpamUser.Authenticate("passwd", userName, password);

            if (isAuthenticated)
            {
                var accountInfo = NpamUser.GetAccountInfo(userName);
                
                authenticatedIdentity =
                    new RemotingIdentity()
                    {
                        Name = accountInfo.Username,
                        IsAuthenticated = true,
                        Roles = new []{ accountInfo.GroupID.ToString() }
                    };

                return true;
            }

            return false;
        }
    }
}