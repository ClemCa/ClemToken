namespace ClemToken;
public static class Logger
{
    public static LogMode Mode = LogMode.None;
    public static event Action<string> Output = delegate { };
    public static event Action<string> RawOutput = delegate { };
    public enum LogMode
    {
        None,
        Error,
        Warning,
        Verbose
    }
    public static void Log(string message, int depth = 0)
    {
        string indented = new string(' ', depth * 2) + message.Replace("\r\n", "\\n").Replace("\n", "\\n");
        RawOutput(indented);
        if (Mode < LogMode.Verbose)
            return;
        Console.WriteLine(indented);
        Output(indented);
    }
    public static void Warning(string message, int depth = 0)
    {
        string indented = new string(' ', depth * 2) + "WARNING: " + message.Replace("\r\n", "\\n").Replace("\n", "\\n");
        RawOutput(indented);
        if (Mode < LogMode.Warning)
            return;
        Console.WriteLine(indented);
        Output(indented);
    }
    public static void Error(string message, int depth = 0)
    {
        string indented = new string(' ', depth * 2) + "ERROR: " + message.Replace("\r\n", "\\n").Replace("\n", "\\n");
        indented += "\nStack Trace:";
        indented += "\n--------------------";
        var stackTrace = new System.Diagnostics.StackTrace(1, true);
        indented += "\n" + stackTrace.ToString();
        RawOutput(indented);
        if (Mode < LogMode.Error)
            return;
        Console.WriteLine(indented);
        Output(indented);
    }
}