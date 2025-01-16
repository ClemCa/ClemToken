using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

struct Tree
{
    public Token Token;
    public List<Tree> Children;
    public Tree(Token token, List<Tree>? children = null)
    {
        Token = token;
        Children = children ?? [];
    }
}
struct Token
{
    public string Name;
    public string Value;
    public Token(string name, string value)
    {
        Name = name;
        Value = value;
    }
}

class Parser
{
    public static Tree Parse(string code, Rule[] rules)
    {
        Tree tree = new Tree(new Token("Root", ""), new List<Tree>());
        for (int i = 0; i < code.Length; i++)
        {
            if (char.IsWhiteSpace(code[i]))
            {
                i++;
                continue;
            }
            Console.WriteLine("Step 0");
            var res = MatchRules(code, i, rules);
            Console.WriteLine("Step 1");
            if (res.Length == 0)
            {
                var position = CountPosition(code, i);
                throw new Exception($"No rule matched at line {position.Item1}, column {position.Item2} ({code[i..(i + 10)]} ...)");
            }
            foreach(var r in res[0]) {
                tree.Children.Add(RuleToTree(r, ref i));
            }
            Console.WriteLine("Step 2");
        }
        return tree;
    }

    private static (int, int) CountPosition(string code, int cursor)
    {
        return code.Select((c, i) => (c, i))
                    .Where(c => c.Item2 < cursor)
                    .Aggregate((0, 0), (acc, c) => {
                        if(c.Item1 == '\n') {
                            return (acc.Item1 + 1, 0);
                        }
                        return (acc.Item1, acc.Item2 + 1);
                    });
    }

    private static Tree RuleToTree(Rule rule, ref int cursor) {
        if(rule.Value != "" && rule.Value != null && !(rule.Value.StartsWith('(') && rule.Value.EndsWith(')'))) {
            cursor += rule.Value.Length;
        }
        var subTokens = new List<Tree>();
        foreach(Rule child in rule.Children) {
            subTokens.Add(RuleToTree(child, ref cursor));
        }
        return new Tree(new Token(rule.Name, rule.Value ?? rule.Name), subTokens);
    }

    private static ProcessRule[][] MatchRules(string code, int cursor, Rule[] rules)
    {
        List<(Rule, ProcessRule[])> matchedRules = new();
        foreach (Rule rule in rules)
        {
            if (MatchToken(code, cursor, rule, out ProcessRule[] r) && !(r.Length == 1 && r[0].Empty))
            {
                matchedRules.Add((rule, r));
            }
        }
        if (matchedRules.Count == 0)
        {
            throw new Exception("No rule matched");
        }
        if (matchedRules.Count > 1)
        {
            return [..matchedRules.OrderBy(rule => rule.Item2.Sum(r => r.IsReserved ? 0 : r.Value?.Length ?? 1)).Select((r) => r.Item2)];
        }
        return [matchedRules[0].Item2];
    }
    private static bool MatchToken(string code, int cursor, Rule rule, out ProcessRule[] modified, int iterations = 1)
    {
        Console.WriteLine($"Starting {rule.Name} {rule.Value} {cursor}");
        bool match;
        int length = 0;
        while(cursor+length < code.Length && char.IsWhiteSpace(code[cursor+length])) {
            length++;
        }
        if (rule.Value == "" || rule.Value == null || (rule.Value.StartsWith('(') && rule.Value.EndsWith(')')))
        {
            match = true;
            length = 0; // pure groups don't take space
        }
        else if (rule.IsReserved)
        {
            switch (rule.Name)
            {
                case "name":
                    match = true;
                    while (cursor + length < code.Length && !char.IsWhiteSpace(code[cursor+length]))
                    {
                        length++;
                    }
                    break;
                default:
                    throw new Exception("Unknown reserved token: " + rule.Name);
            }
        }
        else
        {
            match = true;
            int k = 0;
            while(cursor + k < code.Length - 1 && char.IsWhiteSpace(code[cursor+k])) {
                k++;
            }
            Console.WriteLine("starting at " + code[(cursor+k)..]);
            for (int i = 0; i < rule.Value.Length; i++, k++)
            {
                if(cursor + k >= code.Length) {
                    Console.WriteLine("EOF (left: " + rule.Value.Substring(i) + ")");
                    match = false;
                    break;
                }
                if (code[cursor + k] != rule.Value[i])
                {
                    match = false;
                    break;
                }
            }
            length = k;
        }
        modified = [new ProcessRule(rule, length)];
        modified[0].Value = code[cursor..(cursor + length)].Trim();
        if(!match || rule.Children.Count() == 0)
        {
            return match;
        }
        int j = 0;
        bool failureProof = rule.Repeat.Item1 == 0;
        for (int i = 0; i < rule.Children.Count(); i++)
        {
            Console.WriteLine("Next letter: " + code[cursor+length]+ $"({cursor}, {length})");
            Console.WriteLine("Parent: " + rule.Name + " " + rule.Value);
            if (cursor+length >= code.Length)
            {
                Console.WriteLine("EOF");
                modified = [new ProcessRule(new Empty(), 0)];
                return failureProof;
            }
            var child = rule.Children.ElementAt(i);
            var childMatch = MatchToken(code, cursor+length, child, out ProcessRule[] r);
            if (!childMatch)
            {
                Console.WriteLine("Parent: " + rule.Name + " " + rule.Value);
                modified = [new ProcessRule(new Empty(), 0)];
                return failureProof;
            }
            for (int k = 0; k < r.Length; k++, j++)
            {
                if (r[k].Empty)
                {
                    j--;
                    continue;
                }
                if (modified[0].Children.Length <= j)
                {
                    modified[0].Children = [..modified[0].Children, r[k]];
                }
                else
                {
                    modified[0].Children[j] = r[k];
                }
                length += r[k].Length;
            }
        }
        if(iterations < rule.Repeat.Item2 || rule.Repeat.Item2 == -1) {
            if(MatchToken(code, cursor+length, rule, out ProcessRule[] r, iterations+1)) {
                modified = [..modified, ..r.Where((r) => !r.Empty)]; // the other iterations are done by recursion
                return true;
            }
            if(iterations < rule.Repeat.Item1) {
                Console.WriteLine("Repetition failed");
                return false;
            }
            return true;
        }
        return true;
    }
    private static string CleanLineReturn(string code)
    {
        return code.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}