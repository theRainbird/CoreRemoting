using System;

class Logger<T>
{
    private static void SetColor()
    {
        var isServer = Name.Contains("Server");
        Console.ForegroundColor = isServer ? ConsoleColor.Green : ConsoleColor.Gray;
    }

    private static string Name => typeof(T).Name;

    public static void WriteLine(string format, params object[] args)
    {
        SetColor();
        Console.WriteLine($"{Name}: " + format, args);
    }

    public static void Write(string format, params object[] args)
    {
        SetColor();
        Console.Write($"{Name}: " + format, args);
    }

    public static string ReadLine() => Console.ReadLine();
}

