## Events and Callbacks

CoreRemoting has built-in support for events and callbacks.<br>

### Remote Callbacks
A client can pass delegates as parameters when invoking a method of a remote service via a proxy. If the service invokes the delegate, it is called on the remote client. Just as when calling a local delegate, the executing thread waits until the delegate execution has been completed and the result to be returned.<br><br>
The lifetime of client delegates, that are passed to remote services, is bound to the [ServiceProxy<T>](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#serviceproxy_t) object that is used on client side to access the remote service. Client delegates are no longer callable from remote services, after the proxy has be disposed. On server side lifetime of handlers to access client callbacks are bound to the session _(Same goes for handlers of remote event subscribers)_. 

The following shows how a remote callback can be used to notify the client on progress of a long running service method. Imagine your are developing a file archiving application and want to be notified about progress when archiving multiple files. 
```C#
    // Client
    Dictionary<string, byte[]> filesToArchive = ... // Create a dictionary of filenames and the file contents as byte arrays

    var proxy = _client.CreateProxy<IFileArchiveService>();

    proxy.ArchiveFiles(filesToArchive, (processed, total) => 
        Console.WriteLine("{0} of {1} files processed", processed, total));

    // Server
    public class FileArchiveService : IFileArchiveService
    {
        public void ArchiveFiles(
            Dictionary<string, byte[]> filesToArchive, 
            Action<int, int> progressCallback)
        {
            int total = filesToArchive.Count;
            int processed = 0;

            foreach(var fileToArchive in filesToArchive)
            {
                ArchiveFile(fileName: fileToArchive.Key, content: fileToArchive.Value);
                processed++;

                progressCallback?.Invoke(processed, total); // This calls the lambda expression with Console.WriteLine on client side
            }
        }
    }
```

### Remote Events
Also events can be used in a natural way, just like `button1_Click` on Windows.Forms application. Event subscribers are called sequential in order they subscribed. Because of this, remote events may not be the best pattern for notification, if you have hundreds of clients.<br>
Have a look at the HelloWorld example application _(A simple chat built with a remote event)_ to see remote events in action: https://github.com/theRainbird/CoreRemoting/tree/master/Examples/HelloWorld