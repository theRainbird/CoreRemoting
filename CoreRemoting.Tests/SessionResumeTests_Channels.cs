using CoreRemoting.Channels;
using CoreRemoting.Channels.NamedPipe;
using CoreRemoting.Channels.Null;
using CoreRemoting.Channels.Websocket;

namespace CoreRemoting.Tests.Sessions;

/// <summary>
/// Channel-specific instantiations of the session resume tests.
/// Each nested class is discovered by xUnit as a separate test class.
/// </summary>
public static class SessionResumeTests_Channels
{
    public class NamedPipe : SessionResumeTests
    {
        protected override IServerChannel ServerChannel => new NamedPipeServerChannel();

        protected override IClientChannel ClientChannel => new NamedPipeClientChannel();

        public class NoEncryption : NamedPipe
        {
            protected override bool MessageEncryption => false;
        }

        public class NoAuth : NamedPipe
        {
            protected override bool AuthenticationRequiredForResumeTests => false;
        }

        public class NoAuthNoEncryption : NamedPipe
        {
            protected override bool MessageEncryption => false;

            protected override bool AuthenticationRequiredForResumeTests => false;
        }
    }

    public class NullChannel : SessionResumeTests
    {
        protected override IServerChannel ServerChannel => new NullServerChannel();

        protected override IClientChannel ClientChannel => new NullClientChannel();

        public class NoEncryption : NullChannel
        {
            protected override bool MessageEncryption => false;
        }

        public class NoAuth : NullChannel
        {
            protected override bool AuthenticationRequiredForResumeTests => false;
        }

        public class NoAuthNoEncryption : NullChannel
        {
            protected override bool MessageEncryption => false;

            protected override bool AuthenticationRequiredForResumeTests => false;
        }
    }

    public class NoEncryption : SessionResumeTests
    {
        protected override bool MessageEncryption => false;

        public class NoAuth : SessionResumeTests
        {
            protected override bool AuthenticationRequiredForResumeTests => false;
        }

        public class NoAuthNoEncryption : SessionResumeTests
        {
            protected override bool MessageEncryption => false;

            protected override bool AuthenticationRequiredForResumeTests => false;
        }
    }

    public class WebSockets : SessionResumeTests
    {
        protected override IServerChannel ServerChannel => new WebsocketServerChannel();

        protected override IClientChannel ClientChannel => new WebsocketClientChannel();

        public class NoEncryption : WebSockets
        {
            protected override bool MessageEncryption => false;
        }

        public class NoAuth : WebSockets
        {
            protected override bool AuthenticationRequiredForResumeTests => false;
        }

        public class NoAuthNoEncryption : WebSockets
        {
            protected override bool MessageEncryption => false;

            protected override bool AuthenticationRequiredForResumeTests => false;
        }
    }
}
