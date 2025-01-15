

class Program
{
    static void Main(string[] args)
    {
        string text = "";
        if(args.Length < 2)
        {
            Console.WriteLine("Usage: Tokenizer -f <input> -o <output> | Tokenizer -t <text> | Tokenizer [-test] <input> | Tokenizer [-testP] <input> <grammar>");
            return;
        }
        if(args[0] == "-test")
        {
            Tester.TestGenerator(args[1]);
            return;
        }
        if(args[0] == "-testP" && args.Length != 3)
        {
            Console.WriteLine("Usage: Tokenizer [-testP] <input> <grammar>");
            return;
        }
        if(args[0] == "-testP")
        {
            Tester.TestParser(args[1], args[2]);
            return;
        }
        if(args[0] == "-f")
        {
            if(args.Length < 4 || args[2] != "-o")
            {
                Console.WriteLine("Usage: Tokenizer -f <input> -o <output>");
                return;
            }
            if(!File.Exists(args[1]))
            {
                Console.WriteLine("File not found: " + args[1]);
                return;
            }
            text = File.ReadAllText(args[1]);
        }
        else if(args[0] == "-t")
        {
            for(int i = 1; i < args.Length; i++)
            {
                text += args[i] + " ";
            }
        }
        else
        {
            Console.WriteLine("Usage: Tokenizer -f <input> -o <output> | Tokenizer -t <text>");
            return;
        }
        Tree tree = Tokenize(text);
    }

    static Tree Tokenize(string text)
    {
        Tree tree = new Tree(new Token("Root", ""), new List<Tree>());
        // TODO
        return tree;
    }
}

