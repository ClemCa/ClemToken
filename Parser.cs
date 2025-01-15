using System.Runtime.CompilerServices;

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
            var res = MatchRules(code, i, rules);
            var errors = new List<string>();
            foreach (Rule rule in res)
            {
                Tree temp = new();
                int cursor = i;
                if (rule.IsReserved)
                {
                    switch (rule.Name)
                    {
                        case "name":
                            var value = "";
                            while (cursor < code.Length && !char.IsWhiteSpace(code[cursor]))
                            {
                                value += code[cursor];
                                cursor++;
                            }
                            temp = new Tree(new Token(rule.Name, value));
                            break;
                    }
                }
                else
                {
                    var val = rule.Value ?? rule.Name;
                    temp = new Tree(new Token(rule.Name, val));
                    cursor += val.Length;
                }
                if (rule.Children.Count() == 0)
                {
                    tree.Children.Add(temp);
                    break;
                }
                else
                {
                    try {
                        tree.Children.Add(ActiveParsing(code, ref cursor, rule, temp));
                    } catch(Exception e) {
                        errors.Add(e.Message);
                    }
                }
            }
            if(errors.Count == res.Length) {
                throw new Exception("No rule matched: " + string.Join(", ", errors));
            }
        }
        return tree;
    }

    private static Tree ActiveParsing(string code, ref int cursor, Rule activeRule, Tree root)
    {
        for (int i = 0; i < activeRule.Children.Count(); i++)
        {
            if (cursor >= code.Length)
            {
                throw new Exception("Unexpected end of file, expected " + activeRule.Children.ElementAt(i).Name);
            }
            var child = activeRule.Children.ElementAt(i);
            var match = MatchToken(code, cursor, child);
            if (!match)
            {
                throw new Exception("Unexpected token, expected " + child.Name);
            }
            if (child.IsReserved)
            {
                switch (child.Name)
                {
                    case "name":
                        var value = "";
                        while (cursor < code.Length && !char.IsWhiteSpace(code[cursor]))
                        {
                            value += code[cursor];
                            cursor++;
                        }
                        root.Children.Add(new Tree(new Token(child.Name, value)));
                        break;
                }
            }
            else
            {
                root.Children.Add(new Tree(new Token(child.Name, child.Value ?? child.Name)));
                cursor += child.Value?.Length ?? child.Name.Length;
            }
            if (child.Children.Count() > 0)
            {
                root.Children[^1] = ActiveParsing(code, ref cursor, child, root.Children[^1]);
            }
        }
        return root;
    }

    private static Rule[] MatchRules(string code, int cursor, Rule[] rules)
    {
        List<Rule> matchedRules = new();
        foreach (Rule rule in rules)
        {
            if (MatchToken(code, cursor, rule))
            {
                matchedRules.Add(rule);
            }
        }
        if (matchedRules.Count == 0)
        {
            throw new Exception("No rule matched");
        }
        if (matchedRules.Count > 1)
        {
            matchedRules = [.. matchedRules.OrderBy(rule => rule.IsReserved ? 0 : rule.Value?.Length ?? 1)];
        }
        return matchedRules.ToArray();
    }

    private static bool MatchToken(string code, int cursor, Rule rule)
    {
        if (rule.Value == "" || rule.Value == null)
        {
            return true;
        }
        if (rule.IsReserved)
        {
            switch (rule.Name)
            {
                case "name":
                    return true;
            }
        }
        for (int i = 0; i < rule.Value.Length; i++)
        {
            if (code[cursor + i] != rule.Value[i])
            {
                return false;
            }
        }
        return true;
    }
}