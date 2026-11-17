using CoreRemoting.Channels;
using CoreRemoting.Channels.Quic;

namespace CoreRemoting.Tests.Sessions;

public class SessionTests_Quic : SessionTests
{
    protected override IServerChannel ServerChannel => new QuicServerChannel();

    protected override IClientChannel ClientChannel => new QuicClientChannel();

    public SessionTests_Quic(ServerFixture serverFixture) : base(serverFixture)
    {
    }
}