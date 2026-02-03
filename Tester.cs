using System.Text.RegularExpressions;

namespace ClemToken
{

    struct TestingTree
    {
        public string? Name;
        public string? Value;
        public string? Ignored;
        public string? IsReserved;
        public string? Repeat;
        public List<TestingTree> Children;
        public TestingTree()
        {
            Name = null;
            Value = null;
            Ignored = null;
            IsReserved = null;
            Repeat = null;
            Children = new();
        }
        public TestingTree SetAtDepth(int depth, TestingTree changeTree)
        {
            if (depth == 0)
            {
                this.Name = changeTree.Name ?? this.Name;
                this.Value = changeTree.Value ?? this.Value;
                this.Ignored = changeTree.Ignored ?? this.Ignored;
                this.IsReserved = changeTree.IsReserved ?? this.IsReserved;
                this.Repeat = changeTree.Repeat ?? this.Repeat;
                this.Children = [.. this.Children, .. changeTree.Children ?? []];
                return this;
            }
            if (this.Children.Count == 0 || (depth == 1 && changeTree.Name == null && changeTree.Value == null && changeTree.Ignored == null && changeTree.IsReserved == null && changeTree.Repeat == null))
            {
                this.Children.Add(new());
            }
            this.Children[^1] = this.Children[^1].SetAtDepth(depth - 1, changeTree);
            return this;
        }
    }

    class Tester
    {
        public static void TestParser(string file, string grammar)
        {
            Console.WriteLine("[Parser Test]");
            if (!File.Exists(file))
            {
                throw new Exception("File not found: " + file);
            }
            if (!File.Exists(grammar))
            {
                throw new Exception("File not found: " + grammar);
            }
            var grammarText = File.ReadAllText(grammar);
            var (rules, specs) = Generator.Generate(grammarText);
            var text = File.ReadAllText(file);
            int test = 0;
            StringWriter writer = new();
            Logger.RawOutput += (msg) => { writer.WriteLine(msg); };
            while (!string.IsNullOrWhiteSpace(text))
            {
                test++;
                var (line, tree) = GetPair(ref text);
                var startTime = DateTime.Now;
                Tree generatedTree;
                try
                {
                    generatedTree = ParserV3.Parse(line, rules, specs);
                }
                catch (Exception e)
                {
                    // string used_grammar = Serializer.DisplayRules(rules);
                    // Console.WriteLine("Used grammar:\n" + used_grammar);
                    Console.WriteLine("Test " + test + " errored: " + e.Message + "\n" + e.StackTrace);
                    continue;
                }
                try
                {
                    var time = DateTime.Now - startTime;
                    DetailedComparison(generatedTree, tree);
                    Console.WriteLine("Test " + test + " passed in " + time.TotalMilliseconds + "ms");
                }
                catch (Exception e)
                {
                    // string used_grammar = Serializer.DisplayRules(rules);
                    // Console.WriteLine("Used grammar:\n" + used_grammar);
                    Console.WriteLine("Test " + test + " failed: " + e.Message);
                    Console.WriteLine();
                }
            }
            File.WriteAllText("parse_log.txt", writer.ToString());
        }
        public static void TestGenerator(string file)
        {
            Console.WriteLine("[Generator Test]");
            if (!File.Exists(file))
            {
                throw new Exception("File not found: " + file);
            }
            var text = File.ReadAllText(file);
            int test = 0;
            string generate_log = "";
            Logger.RawOutput += (msg) => { generate_log += msg + "\n"; };
            while (!string.IsNullOrWhiteSpace(text))
            {
                test++;
                var (line, tree) = GetPair(ref text);
                var start = DateTime.Now;
                var generatedTree = Generator.Generate(line).rules[0];
                try
                {
                    DetailedComparison(generatedTree, tree);
                    Console.WriteLine($"Test {test} passed in {(DateTime.Now - start).TotalMilliseconds}ms");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Test {test} failed: {e.Message}");
                }
            }
            File.WriteAllText("generate_log.txt", generate_log);
        }

        static void DetailedComparison(Rule rule, TestingTree tree, int step = 0)
        {
            if (tree.Name != null && rule.Name != tree.Name)
            {
                throw new Exception($"[Step {step}] Name mismatch: (Rule) {rule.Name} != (Test) {tree.Name}");
            }
            if (tree.Value != null && rule.Value != tree.Value)
            {
                throw new Exception($"[Step {step}] Value mismatch: (Rule) {rule.Value} != (Test) {tree.Value}");
            }
            if (tree.Ignored != null && rule.Ignored.ToString().ToLower() != tree.Ignored)
            {
                throw new Exception($"[Step {step}] Ignored mismatch: (Rule) {rule.Ignored} != (Test) {tree.Ignored}");
            }
            if (tree.IsReserved != null && rule.IsReserved.ToString().ToLower() != tree.IsReserved)
            {
                throw new Exception($"[Step {step}] IsReserved mismatch: (Rule) {rule.IsReserved} != (Test) {tree.IsReserved}");
            }
            if (tree.Repeat != null && rule.Repeat.Item1 + " " + rule.Repeat.Item2 != tree.Repeat)
            {
                throw new Exception($"[Step {step}] Repeat mismatch: (Rule) {rule.Repeat.Item1} {rule.Repeat.Item2} != (Test) {tree.Repeat}");
            }
            if (tree.Children.Count != rule.Children.Count())
            {
                Console.WriteLine($"(Rule) n:{rule.Name} v:{rule.Value} i:{rule.Ignored} r:{rule.IsReserved} rep:{rule.Repeat} c:{rule.Children.Count()}");
                Console.WriteLine($"(Test) n:{tree.Name} v:{tree.Value} i:{tree.Ignored} r:{tree.IsReserved} rep:{tree.Repeat} c:{tree.Children.Count}");
                for (int i = 0; i < Math.Max(tree.Children.Count, rule.Children.Count()); i++)
                {
                    Console.WriteLine("");
                    if (i < tree.Children.Count)
                    {
                        Console.WriteLine($"  (Test) n:{tree.Children[i].Name} v:{tree.Children[i].Value} i:{tree.Children[i].Ignored} r:{tree.Children[i].IsReserved} rep:{tree.Children[i].Repeat} c:{tree.Children[i].Children.Count}");
                        foreach (var c in tree.Children[i].Children)
                        {
                            Console.WriteLine($"    n:{c.Name} v:{c.Value} i:{c.Ignored} r:{c.IsReserved} rep:{c.Repeat} c:{c.Children.Count}");
                        }
                    }
                    if (i < rule.Children.Count())
                    {
                        Console.WriteLine($"  (Rule) n:{rule.Children.ElementAt(i).Name} v:{rule.Children.ElementAt(i).Value} i:{rule.Children.ElementAt(i).Ignored} r:{rule.Children.ElementAt(i).IsReserved} rep:{rule.Children.ElementAt(i).Repeat} c:{rule.Children.ElementAt(i).Children.Count()}");
                        foreach (var c in rule.Children.ElementAt(i).Children)
                        {
                            Console.WriteLine($"    n:{c.Name} v:{c.Value} i:{c.Ignored} r:{c.IsReserved} rep:{c.Repeat} c:{c.Children.Count()}");
                        }
                    }
                }
                throw new Exception($"[Step {step}] Children count mismatch: (Rule) {rule.Children.Count()} != (Test) {tree.Children.Count}");
            }
            for (int i = 0; i < tree.Children.Count; i++)
            {
                DetailedComparison(rule.Children.ElementAt(i), tree.Children[i], step + 1);
            }
        }

        static void DetailedComparison(Tree parsed, TestingTree tree, int step = 0)
        {
            if (tree.Name != null && parsed.Token.Name != tree.Name)
            {
                throw new Exception($"[Step {step}] Name mismatch: (Parsed) {parsed.Token.Name} != (Test) {tree.Name}");
            }
            if (tree.Value != null && parsed.Token.Value.Replace("\r\n", "\n") != tree.Value.Replace("\r\n", "\n"))
            {
                throw new Exception($"[Step {step}] Value mismatch: (Parsed, length {parsed.Token.Value.Length}) {parsed.Token.Value} != (Test, length {tree.Value.Length}) {tree.Value} --- {parsed.Token.Name}");
            }
            if (tree.Children.Count != parsed.Children.Count)
            {
                for (int i = 0; i < Math.Max(tree.Children.Count, parsed.Children.Count); i++)
                {
                    Console.WriteLine("");
                    if (i < tree.Children.Count)
                    {
                        Console.WriteLine($"  (Test) n:{tree.Children[i].Name} v:{tree.Children[i].Value} c:{tree.Children[i].Children.Count}");
                    }
                    if (i < parsed.Children.Count)
                    {
                        Console.WriteLine($"  (Parsed) n:{parsed.Children[i].Token.Name} v:{parsed.Children[i].Token.Value} c:{parsed.Children[i].Children.Count}");
                    }
                }
                throw new Exception($"[Step {step}] Children count mismatch: (Parsed) {parsed.Children.Count} != (Test) {tree.Children.Count}");
            }
            for (int i = 0; i < tree.Children.Count; i++)
            {
                DetailedComparison(parsed.Children[i], tree.Children[i], step + 1);
            }
        }

        static (string, TestingTree) GetPair(ref string text)
        {
            string line;
            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            if (text[i..].StartsWith("```"))
            {
                i += 3;
                var end = text.IndexOf("```", i);
                if (end == -1)
                {
                    throw new Exception("Invalid syntax: expected '```', got end of file after " + text[i..]);
                }
                line = text[i..end];
                text = text.Substring(end + 3);
                i = 0;
            }
            else
            {
                do
                {
                    var nextLine = text.IndexOf('\n');
                    line = text.Substring(0, nextLine);
                    text = text.Substring(nextLine + 1);
                } while (line.Trim() == "");
                i = 0;
            }
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i == text.Length)
            {
                throw new Exception("Invalid syntax: expected '(', got end of file after " + line);
            }
            if (text[i] != '(')
            {
                throw new Exception("Invalid syntax: expected '(', got " + text[i..Math.Min(i + 10, text.Length)] + " after " + line);
            }
            TestingTree tree = new();
            int depth = 1;
            for (++i; i < text.Length && depth > 0; i++)
            {
                if (text[i] == '(')
                {
                    tree = tree.SetAtDepth(depth, new());
                    depth++;
                }
                else if (text[i] == ')')
                {
                    depth--;
                }
                else if (!Char.IsWhiteSpace(text[i]))
                {
                    var index = text.IndexOf(':', i);
                    var name = text[i..index].Trim();
                    i = text.IndexOf('\n', index);
                    var value = DeEscape(text[(index + 1)..i].Trim(), 'n', 'r', 't');
                    TestingTree changeTree = new();
                    switch (name)
                    {
                        case "Name":
                            changeTree.Name = value;
                            break;
                        case "Value":
                            changeTree.Value = value;
                            break;
                        case "Ignored":
                            changeTree.Ignored = value;
                            break;
                        case "IsReserved":
                            changeTree.IsReserved = value;
                            break;
                        case "Repeat":
                            changeTree.Repeat = value;
                            break;
                        default:
                            throw new Exception("Invalid syntax: unknown field " + name + " after " + line);
                    }
                    tree = tree.SetAtDepth(depth - 1, changeTree);
                }
            }
            text = text.Substring(i);
            return (line, tree);
        }
        static string DeEscape(string text, params char[] codes)
        {
            int depth = 0;
            string result = "";
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    depth++;
                    result += text[i];
                    continue;
                }
                bool matched = false;
                foreach (var code in codes)
                {
                    if (text[i] == code)
                    {
                        matched = true;
                        if (depth % 2 == 1)
                        {
                            switch (code)
                            {
                                case 'n':
                                    result = result[..^1];
                                    result += '\n';
                                    break;
                                case 'r':
                                    result = result[..^1];
                                    result += '\r';
                                    break;
                                case 't':
                                    result = result[..^1];
                                    result += '\t';
                                    break;
                                default:
                                    result += code;
                                    break;
                            }
                            depth = 0;
                            break;
                        }
                        result += text[i];
                        depth = 0;
                        break;
                    }
                }
                if (matched) continue;
                result += text[i];
                depth = 0;
            }
            return result;
        }
    }
}