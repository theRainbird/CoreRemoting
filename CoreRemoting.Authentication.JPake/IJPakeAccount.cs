namespace CoreRemoting.Authentication.JPake;

/// <summary>
/// Represents a J-PAKE account with pre-shared password.
/// </summary>
public interface IJPakeAccount
{
    /// <summary>
    /// Gets the name of the user.
    /// </summary>
    string UserName { get; }

    /// <summary>
    /// Gets the password.
    /// </summary>
    string Password { get; }
}
