using CoreRemoting.Channels;
using CoreRemoting.Channels.Null;

namespace CoreRemoting.Tests.Sessions;

public class SessionTests_NullChannel : SessionTests
{
    protected override IServerChannel ServerChannel => new NullServerChannel();

    protected override IClientChannel ClientChannel => new NullClientChannel();

    public SessionTests_NullChannel(ServerFixture serverFixture) : base(serverFixture)
    {
    }
}