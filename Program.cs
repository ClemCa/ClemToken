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
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("ClemToken");
            Logger.Mode = Logger.LogMode.Error;
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output> | Tokenizer -f <rule-file> -i <source> | Tokenizer -f <rule-file> -t <text> | Tokenizer -f <rule-file> -in | Tokenizer [-testG] <input> | Tokenizer [-testP] <input> <grammar>");
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
                    var (rules, specs) = Serializer.Deserialize(File.ReadAllBytes(file));
                    Tree result;
                    Console.WriteLine("Parsing...");
                    Console.WriteLine("Specs: " + specs.Length);
                    Console.WriteLine("Rules: " + rules.Length);
                    Console.WriteLine("Text: " + text);
                    string log_out = "";
                    Logger.RawOutput += (log) => { log_out += log + "\n"; };
                    foreach (var rule in rules)
                    {
                        Console.WriteLine($" - {rule.Name}");
                    }
                    try
                    {
                        result = ParserV3.Parse(text, rules, specs);
                        File.WriteAllText("parse_log.txt", log_out);
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
    }
}
