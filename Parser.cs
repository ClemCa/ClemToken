namespace ClemToken
{
    public struct Tree
    {
        public Token Token;
        public List<Tree> Children;
        public Tree(Token token, List<Tree>? children = null)
        {
            Token = token;
            Children = children ?? [];
        }
    }
    public struct Token
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
            for (int i = 0; i < code.Length;)
            {
                if (char.IsWhiteSpace(code[i]))
                {
                    i++;
                    continue;
                }
                var res = MatchRules(code, i, rules);
                if (res.Length == 0)
                {
                    var position = CountPosition(code, i);
                    Console.Out.Flush();
                    throw new Exception($"No rule matched at line {position.Item1}, column {position.Item2} ({code[i..Math.Min(code.Length, (i + 10))]} ...)");
                }
                foreach (var r in res[0])
                {
                    i += r.Length;
                    tree.Children.Add(RuleToTree(r));
                }
            }
            return tree;
        }

        private static (int, int) CountPosition(string code, int cursor)
        {
            return code.Select((c, i) => (c, i))
                        .Where(c => c.Item2 < cursor)
                        .Aggregate((0, 0), (acc, c) =>
                        {
                            if (c.Item1 == '\n')
                            {
                                return (acc.Item1 + 1, 0);
                            }
                            return (acc.Item1, acc.Item2 + 1);
                        });
        }

        private static Tree RuleToTree(ProcessRule rule)
        {
            var subTokens = new List<Tree>();
            foreach (ProcessRule child in rule.ProcessChildren)
            {
                var childTree = RuleToTree(child);
                if(child.Ignored) subTokens.AddRange(childTree.Children); // then we flatten the children
                else subTokens.Add(childTree);
            }
            return new Tree(new Token(rule.Name, rule.Value ?? rule.Name), subTokens);
        }

        private static ProcessRule[][] MatchRules(string code, int cursor, Rule[] rules)
        {
            Console.WriteLine($"Starting root match at {CountPosition(code, cursor)}");
            List<(Rule, ProcessRule[])> matchedRules = new();
            foreach (Rule rule in rules)
            {
                if (MatchToken(code, cursor, rule, out ProcessRule[] r) && !(r.Length == 1 && r[0].Empty))
                {
                    Console.WriteLine("Matched rule: " + rule.Name);
                    matchedRules.Add((rule, r));
                }
                else
                {
                    Console.WriteLine("Rule not matched: " + rule.Name);
                }
            }
            if (matchedRules.Count == 0)
            {
                return [];
            }
            if (matchedRules.Count > 1)
            {
                return [..matchedRules.OrderBy(rule => rule.Item2.Sum(r => r.IsReserved ? 0 : r.Value?.Length ?? 1)).Select((r) => r.Item2)];
            }
            return [matchedRules[0].Item2];
        }
        private static bool MatchToken(string code, int cursor, Rule rule, out ProcessRule[] modified, int iterations = 1)
        {
            Console.WriteLine("> " + rule.Name);
            bool match;
            int length = 0;
            modified = [new ProcessRule(rule, length)];
            modified[0].Children = [];
            while (cursor + length < code.Length && char.IsWhiteSpace(code[cursor + length]))
            {
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
                        int original = length;
                        while (cursor + length < code.Length && !char.IsWhiteSpace(code[cursor + length]))
                        {
                            length++;
                        }
                        if (original + 2 < length)
                        {
                            modified[0].Alternatives = [..Enumerable.Range(original + 2, Math.Min(length + 1, code.Length - cursor) - (original + 2)).Select((i) => (code[cursor..(cursor + i)].TrimStart(), i))];
                        }
                        length = Math.Min(original + 1, code.Length - cursor);
                        break;
                    default:
                        throw new Exception("Unknown reserved token: " + rule.Name);
                }
            }
            else
            {
                match = true;
                int k = 0;
                while (cursor + k < code.Length - 1 && char.IsWhiteSpace(code[cursor + k]))
                {
                    k++;
                }
                for (int i = 0; i < rule.Value.Length; i++, k++)
                {
                    if (cursor + k >= code.Length)
                    {
                        match = false;
                        break;
                    }
                    if (code[cursor + k] != rule.Value[i])
                    {
                        match = false;
                        break;
                    }
                }
                Console.WriteLine("match: " + match + " for '" + code[cursor..Math.Min(code.Length, cursor + rule.Value.Length)] + "' vs '" + rule.Value + "'");
                length = k;
            }
            modified[0].Length = length;
            modified[0].Value = code[cursor..(cursor + length)].Trim();
            if (!match || rule.Children.Count() == 0)
            {
                Console.WriteLine(" -> " + (match ? "+" : "-") + $" ({rule.Name})");
                return match;
            }
            int prevJ = -1;
            int j = 0;
            int altMode = 0;
            bool altEmpty = false;
            bool failureProof = rule.Repeat.Item1 == 0;
            for (int i = 0; i < rule.Children.Count(); i++)
            {
                var target = prevJ == -1 ? modified[0] : modified[0].ProcessChildren[prevJ];
                int currentLength = altMode == 0 ? length : length - target.Length + target.Alternatives[altMode - 1].Item2;
                if (cursor + currentLength >= code.Length)
                {
                    modified = [new ProcessRule(new Empty(), 0)];
                    Console.WriteLine(" -> " + (failureProof ? "skip" : "-") + $" ({rule.Name})");
                    return failureProof;
                }
                var child = rule.Children.ElementAt(i);
                var childMatch = MatchToken(code, cursor + currentLength, child, out ProcessRule[] r);
                if (!childMatch || (r.Length == 1 && r[0].Empty))
                {
                    if (altMode != -1)
                    {
                        // backtracking with self-repetitions is too much of a pain, and anyway it's not worth it for these to have alternatives
                        // especially considering they can just check for the next child before checking for their own repetition
                        // this will come with priorities
                        if (target.Alternatives.Length > altMode)
                        {
                            altMode++;
                            i--;
                            if (altMode == 1)
                            {
                                altEmpty = childMatch;
                            }
                            continue;
                        }
                        if (altEmpty)
                        {
                            continue;
                        }
                        altMode = -1;
                        altEmpty = false;
                    }
                }
                if (!childMatch)
                {
                    modified = [new ProcessRule(new Empty(), 0)];
                    Console.WriteLine(" -> " + (failureProof ? "skip" : "-") + $" ({rule.Name})");
                    return failureProof;
                }
                if (altMode > 0)
                {
                    (target.Value, target.Length) = target.Alternatives[altMode - 1];
                    if (prevJ == -1)
                    {
                        modified[0] = target;
                    }
                    else
                    {
                        modified[0].ProcessChildren[prevJ] = target;
                    }
                    length = currentLength;
                    altEmpty = false;
                }
                altMode = 0; // reset for the next child
                prevJ = j;
                for (int k = 0; k < r.Length; k++, j++)
                {
                    if (r[k].Empty)
                    {
                        j--;
                        continue;
                    }
                    modified[0].ProcessChildren = [..modified[0].ProcessChildren, r[k]];
                    length += r[k].Length;
                }
            }
            modified[0].Length = length;
            if (iterations < rule.Repeat.Item2 || rule.Repeat.Item2 == -1)
            {
                if (MatchToken(code, cursor + length, rule, out ProcessRule[] ruleOut, iterations + 1))
                {
                    modified = [..modified, ..ruleOut.Where((r) => !r.Empty)]; // the other iterations are done by recursion
                    Console.WriteLine(" -> +" + $" ({rule.Name}:{ruleOut.Select((r) => r.Length.ToString()).Aggregate((a, b) => a + "," + b)})");
                    return true;
                }
                if (iterations < rule.Repeat.Item1)
                {
                    Console.WriteLine($" -> - (not enough iterations: {iterations} < {rule.Repeat.Item1}) ({rule.Name})");
                    return false;
                }
                Console.WriteLine(" -> +" + $" ({rule.Name}:{CountPosition(code, cursor + length)})");
                return true;
            }
            Console.WriteLine(" -> +" + $" ({rule.Name}:{CountPosition(code, cursor + length)})");
            return true;
        }
    }
}