using System.Text;

namespace ClemToken;

//! bad command-line usage: printed with a hint and exit code 2
internal sealed class UsageException(string message) : Exception(message) { }

//! runtime failure (missing file, bad grammar, parse failure): exit code 1
internal sealed class CLIException(string message) : Exception(message) { }

internal sealed class CLIOptions
{
    public List<string> Positionals { get; } = [];
    public bool Help;
    public bool Verbose;
    public bool Json;
    public bool Tree;
    public bool AllowErrors;
    public bool Display;
    public bool Check;
    public string? Output;
    public string? Text;
    public string? Format;
    public string? Log;
}

//! Routes Logger.RawOutput to stderr and/or a log file. Only subscribes when someone is listening,
//! so the normal (quiet) path never formats a log string.
internal sealed class Diagnostics : IDisposable
{
    private Action<string>? handler;
    private StreamWriter? writer;

    public Diagnostics(bool verbose, string? logPath)
    {
        if (!verbose && logPath is null)
        {
            return;
        }
        if (logPath is not null)
        {
            writer = new StreamWriter(logPath, false, new UTF8Encoding(false));
        }
        handler = message =>
        {
            if (verbose)
            {
                Console.Error.WriteLine(message);
            }
            writer?.WriteLine(message);
        };
        Logger.RawOutput += handler;
    }

    public void Dispose()
    {
        if (handler is not null)
        {
            Logger.RawOutput -= handler;
        }
        writer?.Dispose();
    }
}

internal static class CLI
{
    public static int Run(string[] args)
    {
        Logger.Mode = Logger.LogMode.None;
        bool verbose = args.Contains("--verbose");
        try
        {
            if (args.Length == 0)
            {
                PrintTopHelp();
                return 0;
            }
            if (args[0] is "--version" or "-V")
            {
                PrintVersion();
                return 0;
            }
            if (args[0] is "-h" or "--help")
            {
                PrintTopHelp();
                return 0;
            }
            if (args[0] == "help")
            {
                return RunHelp(args);
            }
            if (args[0] == "version")
            {
                if (args.Length != 1)
                {
                    throw new UsageException("'version' does not take arguments");
                }
                PrintVersion();
                return 0;
            }
            string command = args[0];
            return command switch
            {
                "build" => Dispatch(command, args, RunBuild),
                "parse" => Dispatch(command, args, RunParse),
                "tokens" => Dispatch(command, args, RunTokens),
                "analyze" => Dispatch(command, args, RunAnalyze),
                "profile" => Dispatch(command, args, RunProfile),
                "test" => Dispatch(command, args, RunTest),
                _ => throw new UsageException($"unknown command '{command}'")
            };
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            Console.Error.WriteLine("Run 'ClemToken help' for usage.");
            return 2;
        }
        catch (InsufficientExecutionStackException)
        {
            Console.Error.WriteLine("error: input is nested too deeply to analyze.");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(e);
            }
            return 1;
        }
    }

    private static int Dispatch(string command, string[] args, Func<CLIOptions, int> run)
    {
        CLIOptions options = ParseOptions(command, args, 1);
        if (options.Help)
        {
            PrintCommandHelp(command);
            return 0;
        }
        return run(options);
    }

    private static CLIOptions ParseOptions(string command, string[] args, int start)
    {
        CLIOptions options = new();
        for (int i = start; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--")
            {
                for (int j = i + 1; j < args.Length; j++)
                {
                    options.Positionals.Add(args[j]);
                }
                return options;
            }

            string name = arg;
            string? inline = null;
            if (arg.StartsWith("--") && arg.IndexOf('=') is int equals && equals > 2)
            {
                name = arg[..equals];
                inline = arg[(equals + 1)..];
            }

            bool isOption = name.StartsWith('-') && name != "-";
            if (isOption && !Known(name))
            {
                throw new UsageException($"unknown option '{name}'");
            }
            if (isOption && !Allowed(command, name))
            {
                throw new UsageException($"option '{name}' is not valid for '{command}'");
            }

            string Value()
            {
                if (inline is not null)
                {
                    return inline;
                }
                if (i + 1 >= args.Length)
                {
                    throw new UsageException($"option '{name}' requires a value");
                }
                return args[++i];
            }
            void Flag()
            {
                if (inline is not null)
                {
                    throw new UsageException($"option '{name}' does not take a value");
                }
            }

            switch (name)
            {
                case "-h":
                case "--help":
                    Flag();
                    options.Help = true;
                    break;
                case "--verbose":
                    Flag();
                    options.Verbose = true;
                    break;
                case "--log":
                    options.Log = Value();
                    break;
                case "-o":
                case "--output":
                    options.Output = Value();
                    break;
                case "-t":
                case "--text":
                    options.Text = Value();
                    break;
                case "-f":
                case "--format":
                    options.Format = Value();
                    break;
                case "--json":
                    Flag();
                    options.Json = true;
                    break;
                case "--tree":
                    Flag();
                    options.Tree = true;
                    break;
                case "--allow-errors":
                    Flag();
                    options.AllowErrors = true;
                    break;
                case "--display":
                    Flag();
                    options.Display = true;
                    break;
                case "--check":
                    Flag();
                    options.Check = true;
                    break;
                default:
                    options.Positionals.Add(arg);
                    break;
            }
        }
        return options;
    }

    private static bool Known(string name) => name is
        "-h" or "--help" or "--verbose" or "--log" or
        "-o" or "--output" or "-t" or "--text" or "-f" or "--format" or
        "--json" or "--tree" or "--allow-errors" or "--display" or "--check";

    private static bool Allowed(string command, string name) => name switch
    {
        "-h" or "--help" or "--verbose" or "--log" => true,
        "-o" or "--output" => command == "build",
        "-t" or "--text" => command is "parse" or "tokens" or "profile",
        "-f" or "--format" => command == "parse",
        "--json" => command is "analyze" or "profile",
        "--tree" => command == "profile",
        "--allow-errors" => command == "parse",
        "--display" or "--check" => command == "build",
        _ => false
    };

    private static int RunBuild(CLIOptions options)
    {
        RequirePositionalCount(options, 1, "usage: ClemToken build <grammar.ct> [-o <out.cta>]");
        if (options.Display && options.Check)
        {
            throw new UsageException("--display and --check cannot be combined");
        }
        if (options.Output is not null && (options.Display || options.Check))
        {
            throw new UsageException("--output cannot be combined with --display or --check");
        }

        string grammarPath = options.Positionals[0];
        if (grammarPath != "-" && !File.Exists(grammarPath))
        {
            throw new CLIException($"grammar not found: {grammarPath}");
        }
        if (grammarPath.EndsWith(".cta", StringComparison.OrdinalIgnoreCase) ||
            grammarPath.EndsWith(".at", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("'build' expects a .ct grammar source, not a compiled grammar");
        }
        if (grammarPath == "-" && options.Output is null && !options.Display && !options.Check)
        {
            throw new UsageException("'-o' is required when the grammar comes from stdin");
        }

        string text = grammarPath == "-" ? Console.In.ReadToEnd() : File.ReadAllText(grammarPath);
        using Diagnostics diagnostics = new(options.Verbose, options.Log);
        var (rules, specs) = GenerateGrammar(text, grammarPath);

        if (options.Display)
        {
            Console.Write(Serializer.DisplayRules(rules));
            return 0;
        }
        if (options.Check)
        {
            return 0;
        }

        string output = options.Output ?? DefaultOutput(grammarPath);
        byte[] bytes = Serializer.Serialize(rules, specs);
        if (output == "-")
        {
            using Stream stdout = Console.OpenStandardOutput();
            stdout.Write(bytes);
        }
        else
        {
            File.WriteAllBytes(output, bytes);
            if (options.Verbose)
            {
                Console.Error.WriteLine($"wrote {output}");
            }
        }
        return 0;
    }

    private static int RunParse(CLIOptions options)
    {
        var (grammarPath, input) = ResolveGrammarAndInput(options, "parse");
        string format = (options.Format ?? "tree").ToLowerInvariant();
        if (format is not ("tree" or "json"))
        {
            throw new UsageException("--format must be 'tree' or 'json'");
        }

        using Diagnostics diagnostics = new(options.Verbose, options.Log);
        var (rules, specs) = LoadGrammar(grammarPath);
        string text = ReadSource(input, options.Text);
        Tree tree = ParserV3.Parse(text, rules, specs);

        if (format == "json")
        {
            Console.Write(Serializer.TreeToJson(tree));
        }
        else
        {
            Console.Write(Serializer.StringSerializeTree(tree));
        }

        List<Token> errors = [];
        CollectErrors(tree, errors);
        string where = input is null ? "<text>" : input == "-" ? "<stdin>" : input;
        foreach (Token error in errors)
        {
            Console.Error.WriteLine($"{where}:{error.Line}:{error.Column}: error: unexpected input '{Preview(error.Value)}'");
        }
        return errors.Count == 0 || options.AllowErrors ? 0 : 1;
    }

    private static int RunTokens(CLIOptions options)
    {
        var (grammarPath, input) = ResolveGrammarAndInput(options, "tokens");
        using Diagnostics diagnostics = new(options.Verbose, options.Log);
        var (rules, specs) = LoadGrammar(grammarPath);
        string text = ReadSource(input, options.Text);
        Tree tree = ParserV3.Parse(text, rules, specs);
        Console.Write(SyntaxTokens.ToJson(SyntaxTokens.Collect(tree, rules)));
        return 0;
    }

    private static int RunAnalyze(CLIOptions options)
    {
        RequirePositionalCount(options, 1, "usage: ClemToken analyze <grammar> [--json]");
        using Diagnostics diagnostics = new(options.Verbose, options.Log);
        var (rules, specs) = LoadGrammar(options.Positionals[0]);
        GrammarStats stats = GrammarAnalyzer.Analyze(rules, specs);
        if (options.Json)
        {
            Console.Write(stats.ToJson());
        }
        else
        {
            stats.WriteTo(Console.Out);
        }
        return 0;
    }

    private static int RunProfile(CLIOptions options)
    {
        var (grammarPath, input) = ResolveGrammarAndInput(options, "profile");
        using Diagnostics diagnostics = new(options.Verbose, options.Log);
        var (rules, specs) = LoadGrammar(grammarPath);
        string text = ReadSource(input, options.Text);
        ParseMetrics metrics = Utils.Measure(text, rules, specs);
        if (options.Tree)
        {
            Console.Write(metrics.ToTreeJson());
        }
        else if (options.Json)
        {
            Console.Write(metrics.ToJson());
        }
        else
        {
            metrics.WriteTo(Console.Out);
        }
        return 0;
    }

    private static int RunTest(CLIOptions options)
    {
        if (options.Positionals.Count == 0)
        {
            throw new UsageException("usage: ClemToken test <generator|parser> ...");
        }

        string kind = options.Positionals[0];
        using Diagnostics diagnostics = new(options.Verbose, null);
        int failures;
        if (kind == "generator")
        {
            if (options.Positionals.Count != 2)
            {
                throw new UsageException("usage: ClemToken test generator <file>");
            }
            failures = Tester.TestGenerator(options.Positionals[1], options.Log);
        }
        else if (kind == "parser")
        {
            if (options.Positionals.Count != 3)
            {
                throw new UsageException("usage: ClemToken test parser <file> <grammar.ct>");
            }
            failures = Tester.TestParser(options.Positionals[1], options.Positionals[2], options.Log);
        }
        else
        {
            throw new UsageException($"unknown test '{kind}'; expected 'generator' or 'parser'");
        }
        return failures == 0 ? 0 : 1;
    }

    private static int RunHelp(string[] args)
    {
        if (args.Length == 1 || args[1] is "-h" or "--help")
        {
            PrintTopHelp();
            return 0;
        }
        if (args.Length > 2)
        {
            throw new UsageException("usage: ClemToken help [command]");
        }
        PrintCommandHelp(args[1]);
        return 0;
    }

    private static (string grammar, string? input) ResolveGrammarAndInput(CLIOptions options, string command)
    {
        if (options.Positionals.Count == 0)
        {
            throw new UsageException($"'{command}' needs a grammar path");
        }
        if (options.Positionals.Count > 2)
        {
            throw new UsageException($"unexpected argument '{options.Positionals[2]}'");
        }

        string grammar = options.Positionals[0];
        string? input = options.Positionals.Count == 2 ? options.Positionals[1] : null;
        if (options.Text is not null)
        {
            if (input is not null)
            {
                throw new UsageException("cannot use both an input file and --text");
            }
        }
        else if (input is null)
        {
            throw new UsageException($"'{command}' needs an input file or --text");
        }
        if (grammar == "-" && input == "-")
        {
            throw new UsageException("grammar and input cannot both come from stdin");
        }
        return (grammar, input);
    }

    private static (Rule[] rules, Spec[] specs) LoadGrammar(string path)
    {
        if (path == "-")
        {
            return GenerateGrammar(Console.In.ReadToEnd(), "-");
        }
        if (!File.Exists(path))
        {
            throw new CLIException($"grammar not found: {path}");
        }
        if (path.EndsWith(".ct", StringComparison.OrdinalIgnoreCase))
        {
            return GenerateGrammar(File.ReadAllText(path), path);
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            throw new CLIException($"could not read grammar '{path}': {e.Message}");
        }
        try
        {
            return Serializer.Deserialize(data);
        }
        catch (GrammarFormatException e)
        {
            throw new CLIException($"grammar '{path}' uses format {e.Actual}; this build expects {e.Expected}. Rebuild it with 'ClemToken build <grammar>.ct'.");
        }
        catch (Exception e) when (e is not InsufficientExecutionStackException)
        {
            throw new CLIException($"'{path}' is not a valid compiled grammar: {e.Message}");
        }
    }

    private static (Rule[] rules, Spec[] specs) GenerateGrammar(string text, string path)
    {
        try
        {
            return Generator.Generate(text);
        }
        catch (Exception e) when (e is not InsufficientExecutionStackException)
        {
            string where = path == "-" ? "<stdin>" : path;
            throw new CLIException($"{where}: {e.Message}");
        }
    }

    private static string ReadSource(string? input, string? text)
    {
        if (text is not null)
        {
            return text;
        }
        if (input is null)
        {
            throw new UsageException("no input");
        }
        if (input == "-")
        {
            return Console.In.ReadToEnd();
        }
        if (!File.Exists(input))
        {
            throw new CLIException($"input not found: {input}");
        }
        return File.ReadAllText(input);
    }

    private static string DefaultOutput(string grammarPath)
    {
        if (grammarPath == "-")
        {
            throw new UsageException("'-o' is required when the grammar comes from stdin");
        }
        return grammarPath.EndsWith(".ct", StringComparison.OrdinalIgnoreCase)
            ? grammarPath[..^3] + ".cta"
            : grammarPath + ".cta";
    }

    private static void CollectErrors(Tree tree, List<Token> errors)
    {
        if (tree.Token.Name == "Error")
        {
            errors.Add(tree.Token);
        }
        foreach (Tree child in tree.Children)
        {
            CollectErrors(child, errors);
        }
    }

    private static string Preview(string value)
    {
        string text = value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return text.Length <= 40 ? text : text[..40] + "...";
    }

    private static void RequirePositionalCount(CLIOptions options, int count, string usage)
    {
        if (options.Positionals.Count > count)
        {
            throw new UsageException($"unexpected argument '{options.Positionals[count]}'");
        }
        if (options.Positionals.Count < count)
        {
            throw new UsageException(usage);
        }
    }

    private static void PrintVersion()
    {
        System.Version? version = typeof(CLI).Assembly.GetName().Version;
        string text = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        Console.WriteLine($"ClemToken {text} (grammar format {Serializer.FormatVersion})");
    }

    private static void PrintTopHelp() => Console.WriteLine(TopHelp);

    private static void PrintCommandHelp(string command)
    {
        string? text = command switch
        {
            "build" => BuildHelp,
            "parse" => ParseHelp,
            "tokens" => TokensHelp,
            "analyze" => AnalyzeHelp,
            "profile" => ProfileHelp,
            "test" => TestHelp,
            "help" => HelpHelp,
            "version" => VersionHelp,
            _ => null
        };
        if (text is null)
        {
            throw new UsageException($"unknown command '{command}'");
        }
        Console.WriteLine(text);
    }

    private const string TopHelp = """
        ClemToken - grammar-driven parser

        Usage:
          ClemToken <command> [arguments] [options]

        Commands:
          build    Compile a .ct grammar into a .cta file
          parse    Parse input and print the parse tree
          tokens   Print syntax tokens as JSON
          analyze  Static analysis of a grammar
          profile  Runtime metrics for a parse
          test     Run the built-in golden tests
          help     Show help for a command
          version  Show the version and grammar format

        Options:
          -h, --help     Show help
          --version      Show the version
          --verbose      Print diagnostics to stderr
          --log <path>   Write diagnostics to a file

        Run 'ClemToken help <command>' for command details.
        """;

    private const string BuildHelp = """
        ClemToken build - compile a grammar

        Usage:
          ClemToken build <grammar.ct> [-o <out.cta>]
          ClemToken build <grammar.ct> --display
          ClemToken build <grammar.ct> --check

        Arguments:
          grammar.ct           Grammar source (or '-' for stdin)

        Options:
          -o, --output <path>  Output file (default: <grammar>.cta; '-' for stdout)
          --display            Print the lowered rules instead of writing a file
          --check              Validate the grammar and exit
          --log <path>         Write generator diagnostics to a file
          --verbose            Print diagnostics to stderr
          -h, --help           Show this help

        Examples:
          ClemToken build dsl.ct
          ClemToken build dsl.ct -o bin/dsl.cta
          ClemToken build dsl.ct --display
        """;

    private const string ParseHelp = """
        ClemToken parse - parse input and print the parse tree

        Usage:
          ClemToken parse <grammar> <input|-> [options]
          ClemToken parse <grammar> --text <text> [options]

        Arguments:
          grammar                  .ct source or compiled .cta grammar
          input                    Input file, or '-' for stdin

        Options:
          -t, --text <text>        Parse literal text instead of a file
          -f, --format <tree|json> Output format (default: tree)
          --allow-errors           Exit 0 even when the tree contains errors
          --log <path>             Write parser diagnostics to a file
          --verbose                Print diagnostics to stderr
          -h, --help               Show this help

        Examples:
          ClemToken parse dsl.cta input.txt
          ClemToken parse dsl.ct input.txt --format json
          ClemToken parse dsl.cta --text "import a from b;"
        """;

    private const string TokensHelp = """
        ClemToken tokens - print syntax tokens as JSON

        Usage:
          ClemToken tokens <grammar> <input|-> [options]
          ClemToken tokens <grammar> --text <text> [options]

        Arguments:
          grammar            .ct source or compiled .cta grammar
          input              Input file, or '-' for stdin

        Options:
          -t, --text <text>  Tokenize literal text instead of a file
          --log <path>       Write parser diagnostics to a file
          --verbose          Print diagnostics to stderr
          -h, --help         Show this help

        Examples:
          ClemToken tokens dsl.cta input.txt
          cat input.txt | ClemToken tokens dsl.cta -
        """;

    private const string AnalyzeHelp = """
        ClemToken analyze - static analysis of a grammar

        Usage:
          ClemToken analyze <grammar> [options]

        Arguments:
          grammar        .ct source, compiled .cta, or '-' for stdin source

        Options:
          --json         JSON output
          --log <path>   Write generator diagnostics to a file
          --verbose      Print diagnostics to stderr
          -h, --help     Show this help

        Examples:
          ClemToken analyze dsl.ct
          ClemToken analyze dsl.ct --json
          cat dsl.ct | ClemToken analyze - --json
        """;

    private const string ProfileHelp = """
        ClemToken profile - runtime metrics for a parse

        Usage:
          ClemToken profile <grammar> <input|-> [options]
          ClemToken profile <grammar> --text <text> [options]

        Arguments:
          grammar            .ct source or compiled .cta grammar
          input              Input file, or '-' for stdin

        Options:
          -t, --text <text>  Parse literal text instead of a file
          --tree             Include the merged call tree (JSON)
          --json             JSON output
          --log <path>       Write parser diagnostics to a file
          --verbose          Print diagnostics to stderr
          -h, --help         Show this help

        Examples:
          ClemToken profile dsl.cta input.txt
          ClemToken profile dsl.cta input.txt --tree --json
        """;

    private const string TestHelp = """
        ClemToken test - run the built-in golden tests

        Usage:
          ClemToken test generator <file>
          ClemToken test parser <file> <grammar.ct>

        Options:
          --log <path>   Write test diagnostics to a file
          --verbose      Print diagnostics to stderr
          -h, --help     Show this help
        """;

    private const string HelpHelp = """
        ClemToken help - show help

        Usage:
          ClemToken help [command]

        Examples:
          ClemToken help
          ClemToken help parse
        """;

    private const string VersionHelp = """
        ClemToken version - show the version

        Usage:
          ClemToken version
          ClemToken --version
        """;
}
