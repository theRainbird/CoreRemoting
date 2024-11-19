using System;

namespace CoreRemoting.Tests.Tools;

public static class ExceptionExtensions
{
    public static Exception GetInnermostException(this Exception ex)
    {
        while (ex?.InnerException != null)
            ex = ex.InnerException;

        return ex;
    }
}
