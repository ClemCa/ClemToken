using System.Text.RegularExpressions;

namespace ClemToken
{
    interface Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
    }

    struct Simple : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Simple(string value)
        {
            Name = value;
            Value = value;
            Repeat = (1, 1);
            Ignored = false;
            Children = [];
            IsReserved = false;
        }
    }

    struct Reserved : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Reserved(string value, bool ignored = false)
        {
            int start = value.IndexOf('[');
            int end = value.IndexOf(']');
            Name = value.Substring(start + 1, end - start - 1);
            Value = value;
            Repeat = (1, 1);
            Ignored = ignored;
            Children = [];
            IsReserved = true;
        }
    }

    struct Reference : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Reference(string value)
        {
            int start = value.IndexOf('@');
            Name = value.Substring(start + 1);
            Value = value;
            Repeat = (1, 1);
            Ignored = false;
            Children = [];
            IsReserved = false;
        }
    }
    struct Anchor : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Anchor(string value, Rule[]? children = null)
        {
            int start = value.IndexOf('@');
            int end = value.IndexOf(':');
            Name = value.Substring(start + 1, end - start - 1);
            Value = value;
            Repeat = (1, 1);
            Ignored = false;
            Children = children ?? [];
            IsReserved = false;
        }
    }
    struct Optional : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Optional(Rule[]? options = null)
        {
            Name = "Optional";
            Ignored = false;
            Repeat = (1, 1);
            IsReserved = false;
            Children = options ?? [];
        }
    }
    struct Group : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Group((int, int) repeat, bool ignored)
        {
            Repeat = repeat;
            Name = "In Deserialization";
            Value = null;
            Ignored = ignored;
            Children = [];
            IsReserved = false;
        }
        public Group((int, int) repeat, string name, bool ignored, Rule[] children)
        {
            Repeat = repeat;
            Name = name;
            Value = null;
            Ignored = ignored;
            Children = children;
            IsReserved = false;
        }
        public Group(out bool subgroup, string? value, string? prefix, string? suffix)
        {
            IsReserved = false;
            Children = [];
            if (prefix != null && prefix.Trim() != "" && prefix.Contains(":"))
            {
                var parts = prefix.Split(':');
                if (parts.Length > 2)
                {
                    throw new Exception("Cannot have more than one name to a group: " + prefix);
                }
                Name = parts[0];
                prefix = parts[1] != "" ? parts[1] : null;
            }
            else
            {
                Name = value == null || value == "" ? "Anonymous" : value;
            }
            subgroup = prefix == "_";
            Value = null;
            int start = 1;
            int end = 1;
            if (suffix != null && suffix.Trim() != "")
            {
            redo:
                switch (suffix)
                {
                    case "*":
                        start = 0;
                        end = -1;
                        break;
                    case "+":
                        start = 1;
                        end = -1;
                        break;
                    case "?":
                        start = 0;
                        end = 1;
                        break;
                    case "_":
                        break;
                    case "":
                        break; // already looped
                    default:
                        if (suffix.Contains("-"))
                        {
                            string[] parts = suffix.Split('-');
                            if (int.TryParse(parts[0], out start) && int.TryParse(parts[1], out end))
                            {
                                break;
                            }
                            throw new Exception("Invalid (int, int): " + suffix);
                        }
                        else if (int.TryParse(suffix, out start))
                        {
                            end = start;
                            break;
                        }
                        else if (suffix.StartsWith(":"))
                        {
                            Ignored = true;
                            suffix = suffix.Substring(1);
                            goto redo;
                        }
                        throw new Exception("Invalid suffix: " + suffix + " for: " + Name);
                }
            }
            Repeat = (start, end);
        }
    }

    struct Empty : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children { get; set; }
        public bool IsReserved { get; set; }
        public Empty()
        {
            Repeat = (0, 0);
            Name = "Empty";
            Value = null;
            Ignored = false;
            Children = [];
            IsReserved = false;
        }
    }

    struct ProcessRule : Rule
    {
        public (int, int) Repeat { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public bool Ignored { get; set; }
        public Rule[] Children
        {
            get => ProcessChildren.Select((c) => c as Rule).ToArray(); set
            {
                int line = Line;
                int column = Column;
                ProcessChildren = [..value.Select((c) => {
                return c switch {
                    ProcessRule pr => pr,
                    Rule r => new ProcessRule(r, r.Value?.Length ?? 0, line, column),
                    _ => throw new Exception("Invalid type: " + c.GetType().Name)
                };
            })];
            }
        }
        public ProcessRule[] ProcessChildren { get; set; }
        public bool IsReserved { get; set; }
        public int Length { get; set; }
        public bool Empty { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public (string, int)[] Alternatives { get; set; }
        public ProcessRule(Rule rule, int length, int line, int column, (string, int)[]? alternatives = null)
        {
            Empty = rule is ProcessRule ? ((ProcessRule)rule).Empty : rule is Empty;
            Length = length;
            Alternatives = alternatives ?? [];
            Repeat = rule.Repeat;
            Name = rule.Name;
            Value = rule.Value;
            Ignored = rule.Ignored;
            ProcessChildren = [..rule.Children.Select((c) => {
                return c switch {
                    ProcessRule pr => pr,
                    Rule r => new ProcessRule(r, r.Value?.Length ?? 0, line, column),
                    _ => throw new Exception("Invalid type: " + c.GetType().Name)
                };
            })];
            IsReserved = rule.IsReserved;
            Line = line;
            Column = column;
        }
    }

    partial class Generator
    {
        private string[] reserved = ["name"];
        public static Rule[] Generate(string grammar)
        {
            string[] parts = [.. Split(grammar).Select((p) => p.Trim()).Where((p) => p != "")];
            List<Rule> rules = new List<Rule>();
            foreach (var part in parts)
            {
                Rule rule = ParseLine(part);
                if (rule is not ProcessRule)
                {
                    rules.Add(rule);
                }
            }
            return rules.ToArray();
        }

        private static Rule ParseLine(string line, bool inAnchor = false)
        {
            if (line.StartsWith("#"))
            {
                return new ProcessRule(); // comment
            }
            Rule? root = null;
            string anchor = "";
            if (line.StartsWith("@"))
            {
                for (int i = 0; i < line.Length; i++)
                {
                    if (char.IsWhiteSpace(line[i]))
                    {
                        break;
                    }
                    if (line[i] == ':')
                    {
                        anchor = line[..(i + 1)];
                        break;
                    }
                }
            }
            if (anchor != "")
            {
                if (inAnchor)
                {
                    throw new Exception("Rule cannot have more than one anchor: " + anchor);
                }
                Console.WriteLine("Found anchor: " + anchor);
                root = new Anchor(anchor);
                var result = ParseLine(line[(anchor.Length + 1)..], true);
                if (result is Anchor tmp)
                {
                    root.Children = tmp.Children;
                }
                else
                {
                    root.Children = [result];
                }
                return root;
            }
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    continue;
                }
                var ver = VerifyGroup(line[i..]);
                if (!ver.Item1)
                {
                    string acc = "";
                    while (i < line.Length && !char.IsWhiteSpace(line[i]))
                    {
                        acc += line[i];
                        i++;
                    }
                    var (isGroup, tk) = ParseToken(acc);
                    if (isGroup)
                    {
                        throw new Exception("Group was misdetected, this is a bug: " + acc);
                    }
                    if (tk.IsReserved)
                    {
                        Console.WriteLine("Found reserved token: " + acc);
                    }
                    else if (tk is Reference)
                    {
                        Console.WriteLine("Found reference: " + acc);
                    }
                    else if (tk is Anchor)
                    {
                        throw new Exception("Anchors are not authorized in the middle of a rule: " + acc);
                    }
                    else if (tk is Optional)
                    {
                        int closingBracket = line[i..].IndexOf(']');
                        if (closingBracket == -1)
                        {
                            throw new Exception("Unmatched opening bracket in optional: " + line[i..Math.Min(i + 10, line.Length)]);
                        }
                        tk = ExploreOptional(line[(i - acc.Length + 1)..(i+closingBracket)]);
                        tk.Value = line[(i - acc.Length)..(i+closingBracket + 1)];
                        i = i + closingBracket + 1;
                        Console.WriteLine("Found optional: " + tk.Value);
                    }
                    else
                    {
                        Console.WriteLine("Found token: " + acc);
                    }
                    if (root == null)
                    {
                        if (inAnchor)
                        {
                            root = new Anchor // intermediary to be extracted above
                            {
                                Children = [tk]
                            };
                        }
                        else
                            root = tk;
                    }
                    else
                    {
                        root.Children = [.. root.Children ?? [], tk];
                    }
                    continue;
                }
                var ch = ExploreSubgroups(ver.Item2);
                var token = GroupToToken(ver.Item2, ver.Item3, ver.Item4);
                if (token.Item2.Name.Contains(" ") || token.Item2.Name == token.Item2.Value)
                    token.Item2.Name = "Anonymous";
                token.Item2.Children = ch;
                i += ver.Item2.Length + ver.Item3.Length + ver.Item4.Length + 2;
                if (token.Item1)
                {
                    if (root == null)
                    {
                        throw new Exception("Cannot have a subgroup as the first token of a rule: " + line[(i - ver.Item2.Length - ver.Item3.Length - ver.Item4.Length)..]);
                    }
                    Console.WriteLine("Found subgroup: " + token.Item2.Name + $" ({token.Item2.Value})");
                    if (root.Children.Length > 0)
                    {
                        root.Children[^1].Children = [.. root.Children[^1].Children, token.Item2];
                    }
                    else
                    {
                        root.Children = [.. root.Children, token.Item2]; // every element after the first is already implicitely a subgroup, but it can be declared explicitly
                    }
                }
                else
                {
                    Console.WriteLine("Found group: " + token.Item2.Name);
                    if (root == null)
                    {
                        root = token.Item2;
                    }
                    else
                    {
                        root.Children = [.. root.Children, token.Item2];
                    }
                }
            }
            if (root == null)
            {
                throw new Exception("No tokens found in line (length " + line.Length + "): " + line);
            }
            return root;
        }

        private static Rule[] ExploreSubgroups(string text)
        {
            List<Rule> rules = new List<Rule>();
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    var search = VerifyGroup(text[i..]);
                    if (search.Item1)
                    {
                        Console.WriteLine($"Found subgroup: {search.Item2}");
                        var (subgroup, token) = GroupToToken(search.Item2, search.Item3, search.Item4);
                        // if (subgroup && rules.Count > 0)
                        // {
                        //     throw new Exception("Cannot have a subgroup as the first token of a rule: " + text[i..]);
                        // }
                        var children = ExploreSubgroups(search.Item2);
                        token.Children = children;
                        i += search.Item2.Length + search.Item3.Length + search.Item4.Length + 2;
                        if (subgroup)
                        {
                            rules[^1].Children = [.. rules[^1].Children, token];
                        }
                        else
                        {
                            rules.Add(token);
                        }
                    }
                    else
                    {
                        var space = text.IndexOf(' ', i);
                        if (space == -1)
                        {
                            space = text.Length;
                        }
                        else if (space > text.Length)
                        {
                            space = text.Length;
                        }
                        var (isGroup, tk) = ParseToken(text[i..space]);
                        if (isGroup)
                        {
                            throw new Exception("Group was misdetected, this is a bug: " + text[i..]);
                        }
                        if (tk is Optional)
                        {
                            int closingBracket = text.IndexOf(']', i);
                            if (closingBracket == -1)
                            {
                                throw new Exception("Unmatched opening bracket in optional: " + text[i..Math.Min(i + 10, text.Length)]);
                            }
                            tk = ExploreOptional(text[(i + 1)..closingBracket]);
                            tk.Value = text[i..(closingBracket + 1)];
                        }
                        if (tk is Anchor)
                        {
                            throw new Exception("Anchors are not authorized in subgroups: " + text[i..]);
                        }
                        Console.WriteLine("Found token: " + tk.Name);
                        i += tk.Value?.Length ?? tk.Name.Length;
                        rules.Add(tk);
                    }
                }
            }
            return [.. rules];
        }
        private static Rule ExploreOptional(string text)
        {
            var optional = new Optional();
            string[] split = text.Split(" | ");
            List<Rule> options = new List<Rule>();
            foreach (var part in split)
            {
                Rule[] ch = ExploreSubgroups(part);
                if (ch.Length > 1)
                {
                    throw new Exception("Optional can only have one child per branch: " + part);
                }
                if (ch.Length == 0)
                {
                    throw new Exception("Optional cannot have an empty branch: " + part);
                }
                options.Add(ch[0]);
            }
            optional.Children = [.. options];
            return optional;
        }
        private static (bool, Rule) GroupToToken(string value, string prefix, string suffix)
        {
            var group = new Group(out bool subgroup, value, prefix, suffix);
            return (subgroup, group);
        }
        private static (bool, Rule) ParseToken(string token)
        {
            bool isGroup = false;
            if (token.StartsWith('(') && token.EndsWith(')'))
            {
                isGroup = false; // the sole exception
            }
            else if (token.StartsWith('@') && token.EndsWith(':'))
            {
                return (false, new Anchor(token));
            }
            else if (token.StartsWith('[') && !char.IsWhiteSpace(token[1]) && !token.Contains('|') && !token.Contains(']'))
            {
                return (false, new Optional());
            }
            else if (token.Contains(':'))
            {
                var index = token.IndexOf(':');
                if (index != 0 && token[index - 1] != ')')
                {
                    isGroup = true;
                }
            }
            else if ((token.StartsWith("_(") || token.StartsWith('(')) && token.Contains(')'))
            {
                isGroup = true;
            }
            if (isGroup)
            {
                var start = token.IndexOf('(');
                var end = token.LastIndexOf(')');
                var group = new Group(out bool subgroup, token.Substring(start + 1, end - start - 1), token.Substring(0, start), token.Substring(end + 1));
                if (subgroup)
                {
                    return (subgroup, group);
                }
            }
            if (token.StartsWith("@") && token.Length > 1)
            {
                return (false, new Reference(token));
            }
            if ((token.StartsWith("$[") || token.StartsWith("$$[")) && token.EndsWith(']'))
            {
                return (false, new Reserved(token, token.StartsWith("$$[")));
            }
            return (false, new Simple(token));
        }

        private static (bool, string, string, string, bool) VerifyGroup(string text)
        {
            if (!text.Contains('('))
            {
                return (false, "", "", "", false);
            }
            if (!text.Contains(')'))
            {
                return (false, "", "", "", false);
            }
            var regex = GroupRegex(); //! in theory we should replace the regex with proper parsing, as regex is greedy
                                      //! but in our implementation it's likely all subgroups get parsed anyway, to test
            var match = regex.Match(text);
            if (!match.Success)
            {
                return (false, "", "", "", false);
            }
            var prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : "";
            var end = text.IndexOf('(');
            var stack = 0;
            for (; end < text.Length; end++)
            {
                if (text[end] == '(')
                {
                    stack++;
                }
                if (text[end] == ')')
                {
                    stack--;
                }
                if (stack == 0)
                {
                    break;
                }
            }
            if (end == text.Length)
            {
                end = text.Length - 1;
            }
            var postfix = end + 1;
            for (; postfix < text.Length; postfix++)
            {
                if (text[postfix] == ')' || text[postfix] == ' ' || text[postfix] == ']')
                {
                    break;
                }
            }
            var suffix = text[(end + 1)..postfix];
            var substart = prefix.Length + 1;
            var subgroup = regex.IsMatch(text[substart..end]);
            var subline = text[substart..end];
            return (true, subline, prefix, suffix, subgroup);
            // INVALID: (test)
            // _(test)
            // (test)+
            // (test)1-2
            // (tes(te_:st))+
            // testy:(test)
            // super_test:(test)
            // testy:(test)_
            // testy:_(test)_
            // \((?:[^:\+\*\-()]|\([^()]*\))*\)
            //^(([^\(\):\+\*\-__?]+:|_)\([^:\+\*\-]+\))$
            //^(\([^:\+\*\-]+\)(\+|\*|\d+(?:-\d+)?|_))
            //^([^\(\):\+\*\-]+:_?|_)\((?:[^:\+\*\-()]|\([^()]*\))*\)(\+|\*|\d+(?:-\d+)?|_)?$
            //^([^\(\):\+\*\-]+:_?|_)\((?:[^:\+\*\-()]|\([^()]*\))*\)(\+|\*|\d+(?:-\d+)?|_)?|\((?:[^:\+\*\-()]|\([^()]*\))*\)(\+|\*|\d+(?:-\d+)?|_)$
            //^(?<prefix>[^\(\):\+\*\-]+:_?|_)(?<token>\((?:[^:\+\*\-()]|\([^()]*\))*\))(\+|\*|\d+(?:-\d+)?|_)?|(?<token_suff>\((?:[^:\+\*\-()]|\([^()]*\))*\))(?<suffix>\+|\*|\d+(?:-\d+)?|_)

            //^(?<prefix>[^\(\):\+\*\-]+:_?|_)(?<token>\((?:[^:\+\*\-()]|\([^()]*\))*\))(?<suffix>\+|\*|\d+(?:-\d+)?|_)?|(?<token_suff>\((?:[^:\+\*\-()]|\([^()]*\))*\))(?<solo_suffix>\+|\*|\d+(?:-\d+)?|_)
            //^(?:(?<prefix>[^\(\):\+\*\-]+:_?|_)(?<token>\((?:[^:\+\*\-()]|\([^()]*\))*\))(?<suffix>\+|\*|\d+(?:-\d+)?|_)?|(?<token_suff>\((?:[^:\+\*\-()]|\([^()]*\))*\))(?<solo_suffix>\+|\*|\d+(?:-\d+)?|_))
            /*
            (
                ^(?<prefix>[^\(\):\+\*\-]+:_?|_)
                (?<token>\(
                    (?:
                        [^:\+\*\-()]
                        |
                        (?<subgroup>(?:[^\(\):\+\*\-]+:_?|_)?\(.*\)(?:\+|\*|\d+(?:-\d+)?|_|:)?)
                    )*
                    \)
                )
                (?<suffix>\+|\*|\d+(?:-\d+)?|_|:)?
                | (?<token_suff>\(
                    (?:
                        [^:\+\*\-()]
                        |
                        (?<subgroup_suff>(?:[^\(\):\+\*\-]+:_?|_)?\(.*\)(?:\+|\*|\d+(?:-\d+)?|_|:)?)
                    )
                    *\)
                )
                (?<solo_suffix>\+|\*|\d+(?:-\d+)?|_|:)
            )
            */
        }

        private static string[] Split(string grammar)
        {
            List<string> parts = [""];
            bool skipNext = false;
            for (int i = 0; i < grammar.Length; i++)
            {
                if (grammar[i] == '\r')
                {
                    continue;
                }
                if (grammar[i] == '\n')
                {
                    if (parts.Last() != "" && !skipNext)
                    {
                        parts.Add("");
                    }
                    skipNext = false;
                    continue;
                }
                if (skipNext)
                {
                    parts[parts.Count - 1] += '\\';
                }
                if (grammar[i] == '\\')
                {
                    skipNext = true;
                    continue;
                }
                parts[^1] += grammar[i];
            }
            return parts.ToArray();
        }

        private static string ReplaceOnce(string text, string search, string replace)
        {
            int index = text.IndexOf(search);
            if (index == -1)
            {
                return text;
            }
            return text[..index] + replace + text[(index + search.Length)..];
        }

        [GeneratedRegex(@"^(?:(?<prefix>[^\[\(\):\+\*\-\s]+:_?|_)(?<token>\((?:[^:\+\*\-()]|(?<subgroup>(?:[^\(\):\+\*\-]+:_?|_)?\(.*\):?(?:\+|\*|\d+(?:-\d+)?|_|:)?))*\))(?<suffix>:?(?:\+|\*|\d+(?:-\d+)?|_|:))?|(?<token_suff>\((?:[^:\+\*\-()]|(?<subgroup_suff>:?(?:[^\(\):\+\*\-]+:_?|_)?\(.*\)(?:\+|\*|\d+(?:-\d+)?|_|:)?))*\))(?<solo_suffix>:?(?:\+|\*|\d+(?:-\d+)?|_|:)))")]
        private static partial Regex GroupRegex();
    }
}