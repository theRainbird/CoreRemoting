namespace CoreRemoting
{
    /// <summary>
    /// Marks an method as one way method. This means that CoreRemoting client will not wait for result and CoreRemoting
    /// server will not send any result message (even in case of an error).
    /// One way methods are treated a fire-and-forget.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Method)]  
    public class OneWayAttribute : System.Attribute  
    {
    }
}