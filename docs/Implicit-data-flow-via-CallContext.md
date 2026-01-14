CoreRemoting has a [CallContext](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#callcontext) implementation that works in the same way as with classic .NET Remoting. Data that is set on client side flows implicitly to the server and back. The data is set, like in a dictionary, as name value pairs. 
This can be very useful for implementing cross-cutting concerns. Have a look at the following example.

#### Example (Client)
This example shows how to send the culture name without passing it explicitly as a parameter.  
```C#
    // Get the name of the current culture (e.g. "de-DE" for German)
    var cultureName = CultureInfo.CurrentCulture.Name;

    // Put the culture name in the call context
    CallContext.SetData("ClientCulture", clutureName);

    // Call a remote method
    string salutation = letterServiceProxy.GetLocalizedSalutation();

    // Get the effective culture that was used to localize the salutation
    string effectiveCultureName = (string)CallContext.GetData("EffectiveCulture"); 

    Console.WriteLine(salutation);
    Console.WriteLine("Language: {0}", effectiveCultureName);
```
#### Example (Server)
On remote service method the culture from client can be read from call context.
Serialization, transport and thread correlation is automatically done by CoreRemoting.
```C#
    public string GetLocalizedSalutation()
    {
        // Get the name of the culture of the calling client from call context
        object clientCulture = CallContext.GetData("ClientCulture")
        string cultureName = 
            clientCulture == null // Value may be null if client hasn't set a value
                ? "en-US" 
                : (string)clientCulture; 
        
        // Put the effective culture name in the call context
        CallContext.SetData("EffectiveCulture", cultureName);

        // Return salutation in German if culture name starts with "de" and otherwise use English
        return 
            cultureName.StartWith("de") 
                ? "Sehr geehrte Damen und Herren"
                : "Dear Ladies and Gentlemen";
    }
```

## Avoid stumbling blocks when using CallContext

* CallContext can also be modified at server side. Then the client's CallContext is changed too, when the RPC result is received
* Remember that **all** data in CallContext will be sent on **every** remote call, so avoid large data structures
* Communication via CallContext is not part of the service interface and may be forgotten, if implementations are changed
* Using CallContext ties your code base tight to CoreRemoting, because other RPC libraries may not have a similar mechanism to implicitly transfer data between client and server 