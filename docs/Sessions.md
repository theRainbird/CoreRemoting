## Sessions

CoreRemoting has an integrated session management. A session is created when a client is successfully connected to the server.<br> 
If authentication is configured and the [AuthenticationRequired](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#authenticationrequired-1) property is set to true, a session is only created, if the credentials are correct _(See [Security](https://github.com/theRainbird/CoreRemoting/wiki/Security#authentication) chapter for more information about authentication)_.

### Session Repository
A CoreRemoting server instance holds a session repository to manage all sessions. A session repository is a class that implements the [ISessionRepository](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#isessionrepository) interface. If no session repository is configured explicitly, a default session repository, which stores the session data in memory, is used.

### Session Lifetime
By default sessions are removed automatically, after 30 minutes of inactivity. This limit can be changed on server configuration _(See [Configuration](https://github.com/theRainbird/CoreRemoting/wiki/Configuration) chapter for details)_.
Every call that is made on a session resets this inactivity counter.
Client instances have a timer running, that are sending empty requests to the server in a defined interval, to keep the session alive, as long as the client is connected. By default the interval is set to 20 seconds. You can change this by setting the KeepSessionAliveInterval property of the [ClientConfig](https://github.com/theRainbird/CoreRemoting/wiki/Configuration#clientconfig). If the interval is set to 0, the timer is stopped and so the session is not kept alive automatically.

