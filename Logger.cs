namespace ClemToken;
public static class Logger
{
    public static LogMode Mode = LogMode.None;
    public static event Action<string>? Output;
    public static event Action<string>? RawOutput;
    public enum LogMode
    {
        None,
        Error,
        Warning,
        Verbose
    }
    public static void Log(string message, int depth = 0)
    {
        if (!Wanted(LogMode.Verbose)) return;
        string indented = Indent("", message, depth);
        RawOutput?.Invoke(indented);
        if (Mode < LogMode.Verbose)
            return;
        Console.WriteLine(indented);
        Output?.Invoke(indented);
    }
    public static void Warning(string message, int depth = 0)
    {
        if (!Wanted(LogMode.Warning)) return;
        string indented = Indent("WARNING: ", message, depth);
        RawOutput?.Invoke(indented);
        if (Mode < LogMode.Warning)
            return;
        Console.WriteLine(indented);
        Output?.Invoke(indented);
    }
    public static void Error(string message, int depth = 0)
    {
        if (!Wanted(LogMode.Error)) return;
        string indented = Indent("ERROR: ", message, depth);
        indented += "\nStack Trace:";
        indented += "\n--------------------";
        var stackTrace = new System.Diagnostics.StackTrace(1, true);
        indented += "\n" + stackTrace.ToString();
        RawOutput?.Invoke(indented);
        if (Mode < LogMode.Error)
            return;
        Console.WriteLine(indented);
        Output?.Invoke(indented);
    }

    //! a message is only worth formatting when something consumes it: the console at or above Mode, or an attached RawOutput
    static bool Wanted(LogMode level) => Mode >= level || RawOutput is not null;

    static string Indent(string prefix, string message, int depth) =>
        (depth > 0 ? new string(' ', depth * 2) : "") + prefix + message.Replace("\r\n", "\\n").Replace("\n", "\\n");
}
