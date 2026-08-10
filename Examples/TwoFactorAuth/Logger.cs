using System;

class Logger<T>
{
    private static void SetColor()
    {
        var isServer = typeof(T).Name.Contains("Server");
        Console.ForegroundColor = isServer ? ConsoleColor.Green : ConsoleColor.Gray;
    }

    public static void WriteLine(string format, params object[] args)
    {
        SetColor();
        Console.WriteLine(format, args);
    }

    public static void Write(string format, params object[] args)
    {
        SetColor();
        Console.Write(format, args);
    }

    public static string ReadLine() => Console.ReadLine();
}

record Server;
record Client;
