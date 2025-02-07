namespace ClemToken
{
    public class ClemToken
    {
        public static void GenerateGrammar(string grammar, string path)
        {
            var rules = Generator.Generate(grammar);
            var bytes = Serializer.Serialize(rules);
            File.WriteAllBytes(path, bytes);
        }
        public static void GenerateGrammar(string grammar, BinaryWriter writer)
        {
            var rules = Generator.Generate(grammar);
            var bytes = Serializer.Serialize(rules);
            writer.Write(bytes);
        }
        public static Navigator Parse(string text, string grammarPath)
        {
            var rules = Serializer.Deserialize(File.ReadAllBytes(grammarPath));
            var tree = Parser.Parse(text, rules);
            return new Navigator(tree);
        }
        public static Navigator Parse(string text, BinaryReader reader)
        {
            var rules = Serializer.Deserialize(reader.ReadBytes((int)reader.BaseStream.Length));
            // length would wrap if the file is too big, but at that point there's no looping solution with a binary reader (the position argument is int)
            // so I'd need to implement streaming deserialization
            // byte arrays are also limited to less than int max value, so I couldn't compound it if I wanted to
            var tree = Parser.Parse(text, rules);
            return new Navigator(tree);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
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
                if (args[2] == "-o")
                {
                    if (args.Length != 4)
                    {
                        Console.WriteLine("Usage: Tokenizer -f <grammar> -o <output>");
                        return;
                    }
                    string output = args[3];
                    string grammar = File.ReadAllText(file);
                    var result = Generator.Generate(grammar);
                    var bytes = Serializer.Serialize(result);
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
                    var rules = Serializer.Deserialize(File.ReadAllBytes(file));
                    var consoleOut = Console.Out;
                    var writer = new StringWriter();
                    Console.SetOut(writer);
                    try
                    {
                        var result = Parser.Parse(text, rules);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error during parsing: " + e.Message);
                        Console.SetOut(consoleOut);
                        Console.Write("Logs:\n" + writer.ToString());
                        return;
                    }
                    Console.SetOut(consoleOut);
                    // TODO output result one way or another + utility methods for tree traversal
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
