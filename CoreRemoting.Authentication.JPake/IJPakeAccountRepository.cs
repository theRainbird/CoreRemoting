using System.Threading.Tasks;

namespace CoreRemoting.Authentication.JPake;

/// <summary>
/// Repository for J-PAKE accounts.
/// </summary>
public interface IJPakeAccountRepository
{
    /// <summary>
    /// Finds the user account data by the given username.
    /// </summary>
    /// <param name="userName">Name of the user.</param>
    Task<IJPakeAccount> FindByName(string userName);

    /// <summary>
    /// Gets the identity for the given user account.
    /// </summary>
    /// <param name="account">The account.</param>
    Task<RemotingIdentity> GetIdentity(IJPakeAccount account);
}
