using System.Text;

namespace ClemToken
{
    public static class Utils
    {
        private static Rule[]? preloadedRules;
        private static Spec[]? preloadedSpecs;
        public static void ToGrammar(this string grammar, string path) => GenerateGrammar(grammar, path);
        public static void ToGrammar(this string grammar, BinaryWriter writer) => GenerateGrammar(grammar, writer);
        public static void PreloadAsGrammar(this string path) => PreloadGrammar(path);
        public static Navigator ParseWith(this string text, string grammarPath) => Parse(text, grammarPath);
        public static Navigator ParseWith(this string text, BinaryReader reader) => Parse(text, reader);
        public static Navigator Parse(this string text) => ParsePreloaded(text);
        public static void GenerateGrammar(string grammar, string path)
        {
            var (rules, specs) = Generator.Generate(grammar);
            var bytes = Serializer.Serialize(rules, specs);
            File.WriteAllBytes(path, bytes);
        }
        public static void GenerateGrammar(string grammar, BinaryWriter writer)
        {
            var (rules, specs) = Generator.Generate(grammar);
            var bytes = Serializer.Serialize(rules, specs);
            writer.Write(bytes);
        }
        public static void PreloadGrammar(string path)
        {
            (preloadedRules, preloadedSpecs) = Serializer.Deserialize(File.ReadAllBytes(path));
        }
        public static Navigator Parse(string text, string grammarPath)
        {
            var (rules, specs) = Serializer.Deserialize(File.ReadAllBytes(grammarPath));
            var tree = ParserV3.Parse(text, rules, specs);
            return new Navigator(tree);
        }
        public static Navigator Parse(string text, BinaryReader reader)
        {
            var (rules, specs) = Serializer.Deserialize(reader.ReadBytes((int)reader.BaseStream.Length));
            // length would wrap if the file is too big, but at that point there's no looping solution with a binary reader (the position argument is int)
            // so I'd need to implement streaming deserialization
            // byte arrays are also limited to less than int max value, so I couldn't compound it if I wanted to
            var tree = ParserV3.Parse(text, rules, specs);
            return new Navigator(tree);
        }
        public static Navigator ParsePreloaded(string text)
        {
            if (preloadedRules == null || preloadedSpecs == null)
            {
                throw new InvalidOperationException("No grammar has been preloaded");
            }
            var tree = ParserV3.Parse(text, preloadedRules, preloadedSpecs);
            return new Navigator(tree);
        }

        //! static grammar stats: counts and per-rule symbol bounds, no input needed
        public static GrammarStats AnalyzeGrammar(this string grammarText)
        {
            var (rules, specs) = Generator.Generate(grammarText);
            return GrammarAnalyzer.Analyze(rules, specs);
        }
        public static GrammarStats AnalyzeGrammarFile(this string path) => File.ReadAllText(path).AnalyzeGrammar();

        //! runtime parse stats: allocations and executions attributed per rule
        public static ParseMetrics MeasureParse(this string text, string grammarPath)
        {
            var (rules, specs) = Serializer.Deserialize(File.ReadAllBytes(grammarPath));
            return Measure(text, rules, specs);
        }
        public static ParseMetrics MeasureParse(this string text, BinaryReader reader)
        {
            var (rules, specs) = Serializer.Deserialize(reader.ReadBytes((int)reader.BaseStream.Length));
            return Measure(text, rules, specs);
        }
        public static ParseMetrics MeasurePreloaded(this string text)
        {
            if (preloadedRules == null || preloadedSpecs == null)
            {
                throw new InvalidOperationException("No grammar has been preloaded");
            }
            return Measure(text, preloadedRules, preloadedSpecs);
        }
        //! internal: Rule/Spec are not public, so this cannot be an outward-facing overload
        internal static ParseMetrics Measure(string text, Rule[] rules, Spec[] specs)
        {
            var collector = new MetricsCollector();
            ParserV3.Parse(text, rules, specs, collector);
            return collector.Build();
        }

        // parser-driven syntax tokens, serialized as the versioned JSON contract editors consume
        public static string TokensJson(this string text, string grammarPath)
        {
            var (rules, specs) = Serializer.Deserialize(File.ReadAllBytes(grammarPath));
            return Tokens(text, rules, specs);
        }
        public static string TokensJson(this string text)
        {
            if (preloadedRules == null || preloadedSpecs == null)
            {
                throw new InvalidOperationException("No grammar has been preloaded");
            }
            return Tokens(text, preloadedRules, preloadedSpecs);
        }
        //! internal: Rule/Spec are not public, so this cannot be an outward-facing overload
        internal static string Tokens(string text, Rule[] rules, Spec[] specs) =>
            SyntaxTokens.ToJson(SyntaxTokens.Collect(ParserV3.Parse(text, rules, specs), rules));
    }

    class Program
    {
        static void Main(string[] args)
        {
            Logger.Mode = Logger.LogMode.Error;

            //! the ambient console code page is not UTF-8 (OEM/ANSI on Windows), so editors that pipe
            //! source in would get byte offsets and mangled values unless both directions are pinned;
            //! a host with no console has nothing to pin and already defaults to UTF-8
            try
            {
                Console.InputEncoding = new UTF8Encoding(false);
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (IOException) { }

            //! -stats is order-independent: pull it (with an optional mode/format token) out before dispatch
            string? statsMode = null;
            string statsFormat = "table";
            {
                var arguments = new List<string>(args);
                int statsIndex = arguments.IndexOf("-stats");
                if (statsIndex >= 0)
                {
                    arguments.RemoveAt(statsIndex);
                    statsMode = "parse"; // default when no explicit mode
                    if (statsIndex < arguments.Count && (arguments[statsIndex] == "grammar" || arguments[statsIndex] == "parse"))
                    {
                        statsMode = arguments[statsIndex];
                        arguments.RemoveAt(statsIndex);
                    }
                    if (statsIndex < arguments.Count && arguments[statsIndex] == "json")
                    {
                        statsFormat = "json";
                        arguments.RemoveAt(statsIndex);
                    }
                    args = [.. arguments];
                }
            }
            //! -tokens is order-independent too; it emits the parser's syntax tokens as JSON
            bool tokensMode = false;
            {
                var arguments = new List<string>(args);
                int tokensIndex = arguments.IndexOf("-tokens");
                if (tokensIndex >= 0)
                {
                    arguments.RemoveAt(tokensIndex);
                    tokensMode = true;
                    // `json` is the only format; accept it explicitly for symmetry with -stats
                    if (tokensIndex < arguments.Count && arguments[tokensIndex] == "json")
                    {
                        arguments.RemoveAt(tokensIndex);
                    }
                    args = [.. arguments];
                }
            }
            //! keep stdout clean when it is meant to be machine-readable
            if (statsFormat != "json" && !tokensMode)
            {
                Console.WriteLine("ClemToken");
            }

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output> | Tokenizer -f <rule-file> -i <source> | Tokenizer -f <rule-file> -t <text> | Tokenizer -f <rule-file> -in | Tokenizer -f <grammar|-> -stats grammar [json] | Tokenizer -f <rule-file> -i <source> -stats [parse] [json] | Tokenizer -f <rule-file> -i <source> -tokens [json] | Tokenizer [-testG] <input> | Tokenizer [-testP] <input> <grammar>");
                return;
            }
            if (statsMode == "grammar")
            {
                if (args.Length != 2 || args[0] != "-f")
                {
                    Console.WriteLine("Usage: Tokenizer -f <grammar|-> -stats grammar [json]");
                    return;
                }
                //! `-f -` reads the grammar from stdin, so an editor can analyze the buffer it holds
                //! without round-tripping it through a temp file (matches the `-in` convention below)
                PrintStats(args[1] == "-" ? Console.In.ReadToEnd().AnalyzeGrammar() : Utils.AnalyzeGrammarFile(args[1]), statsFormat);
                return;
            }
            if (args[0] == "-testG")
            {
                Tester.TestGenerator(args[1]);
                return;
            }
            if (args[0] == "-testP" && args.Length != 3)
            {
                Console.WriteLine("Usage: Tokenizer [-testP] <input> <grammar>");
                return;
            }
            if (args[0] == "-testP")
            {
                Tester.TestParser(args[1], args[2]);
                return;
            }
            if (args[0] == "-f")
            {
                string file = args[1];
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output> | Tokenizer -f <grammar|-> -stats grammar [json] | Tokenizer -f <rule-file> -i <source> -stats [parse] [json] | Tokenizer -f <rule-file> -i <source> -tokens [json]");
                    return;
                }
                if (args[2] == "-preload")
                {
                    if (args.Length != 3)
                    {
                        Console.WriteLine("Usage: Tokenizer -f <grammar> -preload");
                        return;
                    }
                    Utils.PreloadGrammar(file);
                    Console.WriteLine("Grammar preloaded.");
                    return;
                }
                if (args[2] == "-o" || args[2] == "-O")
                {
                    if (args.Length != 4)
                    {
                        Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output>");
                        return;
                    }
                    string output = args[3];
                    string grammar = File.ReadAllText(file);
                    var (result, specs) = Generator.Generate(grammar);
                    if (args[2] == "-O")
                    {
                        var text = Serializer.DisplayRules(result);
                        File.WriteAllText(output, text);
                        return;
                    }
                    var bytes = Serializer.Serialize(result, specs);
                    File.WriteAllBytes(output, bytes);
                    return;
                }
                else if (args[2] == "-t" || args[2] == "-in" || args[2] == "-i")
                {
                    string text;
                    switch (args[2])
                    {
                        case "-t":
                            if (args.Length < 4)
                            {
                                Console.WriteLine("Usage: Tokenizer -f <rule-file> -t <text>");
                                return;
                            }
                            text = args[3..].Aggregate((a, b) => a + " " + b);
                            break;
                        case "-in":
                            if (args.Length != 3)
                            {
                                Console.WriteLine("Usage: Tokenizer -f <rule-file> -in");
                                return;
                            }
                            text = Console.In.ReadToEnd();
                            break;
                        case "-i":
                            if (args.Length != 4)
                            {
                                Console.WriteLine("Usage: Tokenizer -f <rule-file> -i <source>");
                                return;
                            }
                            text = File.ReadAllText(args[3]);
                            break;
                        default:
                            Console.WriteLine("Usage: Tokenizer -f <rule-file> -i <source> | Tokenizer -f <rule-file> -t <text> | Tokenizer -f <rule-file> -in");
                            return;
                    }
                    if (statsMode == "parse")
                    {
                        PrintStats(Utils.MeasureParse(text, file), statsFormat);
                        return;
                    }
                    if (tokensMode)
                    {
                        Console.Write(Utils.TokensJson(text, file));
                        return;
                    }
                    var (rules, specs) = Serializer.Deserialize(File.ReadAllBytes(file));
                    Tree result;
                    Console.WriteLine("Parsing...");
                    Console.WriteLine("Specs: " + specs.Length);
                    Console.WriteLine("Rules: " + rules.Length);
                    Console.WriteLine("Text: " + text);
                    StringBuilder log_out = new();
                    Logger.RawOutput += (log) => { log_out.Append(log).Append('\n'); };
                    foreach (var rule in rules)
                    {
                        Console.WriteLine($" - {rule.Name}");
                    }
                    try
                    {
                        result = ParserV3.Parse(text, rules, specs);
                        File.WriteAllText("parse_log.txt", log_out.ToString());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error during parsing: " + e.Message);
                        Console.WriteLine(e.StackTrace);
                        Console.Write("Logs:\n" + log_out);
                        return;
                    }
                    string output = Serializer.StringSerializeTree(result);
                    Console.Write(output); // output to stdout, the user can redirect it to a file or use it in their program
                }
                else
                {
                    Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output> | Tokenizer -f <rule-file> -i <source> | Tokenizer -f <rule-file> -t <text> | Tokenizer -f <rule-file> -in");
                }
            }
            else
            {
                Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output> | Tokenizer -f <rule-file> -t <text>");
                return;
            }
        }

        static void PrintStats(GrammarStats stats, string format)
        {
            if (format == "json") Console.Write(stats.ToJson());
            else stats.WriteTo(Console.Out);
        }

        static void PrintStats(ParseMetrics stats, string format)
        {
            if (format == "json") Console.Write(stats.ToJson());
            else stats.WriteTo(Console.Out);
        }
    }
}
