namespace CoreRemoting
{
    /// <summary>
    /// Instances of a service interface annotated with this attribute will be passed as proxies when returned
    /// to a client.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Interface)]
    public class ReturnAsProxyAttribute : System.Attribute
    {
    }
}