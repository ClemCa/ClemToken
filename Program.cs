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
        static int Main(string[] args)
        {
            //! pin both console directions to UTF-8; hosts that pipe source in would otherwise get
            //! the ambient OEM/ANSI code page and mangled offsets
            try
            {
                Console.InputEncoding = new UTF8Encoding(false);
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (IOException) { }

            return CLI.Run(args);
        }
    }
}
