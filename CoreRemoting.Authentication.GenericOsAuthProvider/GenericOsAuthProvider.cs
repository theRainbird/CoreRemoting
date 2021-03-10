using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CoreRemoting.Authentication
{
    /// <summary>
    /// Authentication provider to check credentials against local operation system user accounts.
    /// Works with Windows user accounts (local or domain) and local linux user accounts (passwd).
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class GenericOsAuthProvider : IAuthenticationProvider
    {
        public const string CREDENTIAL_TYPE_USERNAME = "username";
        public const string CREDENTIAL_TYPE_PASSWORD = "password";
        
        /// <summary>
        /// Authenticates the provided credentials and returns the authenticated identity, if successful.
        /// </summary>
        /// <param name="credentials">Array of credentials ("username", "password" and optional "domain" [Windows AD only])</param>
        /// <param name="authenticatedIdentity">Authenticated Identity</param>
        /// <returns>Indicates whether the authentication was successful.</returns>
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            authenticatedIdentity = null;

            IAuthenticationProvider authProvider = null;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                authProvider = new WindowsAuthProvider();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                authProvider = new LinuxPamAuthProvider();
                    
            if (authProvider == null)
                throw new PlatformNotSupportedException();
            
            return authProvider.Authenticate(credentials, out authenticatedIdentity);
        }
    }
}