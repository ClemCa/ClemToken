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
        public Reserved(string value)
        {
            int start = value.IndexOf('[');
            int end = value.IndexOf(']');
            Name = value.Substring(start + 1, end - start - 1);
            Value = value;
            Repeat = (1, 1);
            Ignored = false;
            Children = [];
            IsReserved = true;
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
            Value = value;
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
                        throw new Exception("Invalid suffix: " + suffix);
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
        public Rule[] Children { get => ProcessChildren.Select((c) => c as Rule).ToArray(); set => ProcessChildren = value.Select((r) => r is ProcessRule tar ? tar : new ProcessRule(r, 0)).ToArray(); }
        public ProcessRule[] ProcessChildren { get; set; }
        public bool IsReserved { get; set; }
        public int Length { get; set; }
        public bool Empty { get; set; }
        public (string, int)[] Alternatives { get; set; }
        public ProcessRule(Rule rule, int length, (string, int)[]? alternatives = null)
        {
            Empty = rule is ProcessRule ? ((ProcessRule)rule).Empty : rule is Empty;
            Length = length;
            Alternatives = alternatives ?? [];
            Repeat = rule.Repeat;
            Name = rule.Name;
            Value = rule.Value;
            Ignored = rule.Ignored;
            ProcessChildren = []; // already set by Children but need to get rid of the warning;
            Children = rule.Children;
            IsReserved = rule.IsReserved;
        }
    }

    partial class Generator
    {
        private string[] reserved = ["name"];
        public static Rule[] Generate(string grammar)
        {
            string[] parts = Split(grammar);
            List<Rule> rules = new List<Rule>();
            foreach (var part in parts)
            {
                rules.Add(ParseLine(part));
            }
            return rules.ToArray();
        }

        private static Rule ParseLine(string line)
        {
            Rule? root = null;
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
                    else
                    {
                        Console.WriteLine("Found token: " + acc);
                    }
                    if (root == null)
                    {
                        root = tk;
                    }
                    else
                    {
                        root.Children = [..root.Children, tk];
                    }
                    continue;
                }
                var ch = ExploreSubgroups(ver.Item2);
                var token = GroupToToken(ver.Item2, ver.Item3, ver.Item4);
                if (ver.Item5 || token.Item2.Name.Contains(" "))
                    token.Item2.Name = "Anonymous";
                token.Item2.Children = ch;
                i += ver.Item2.Length + ver.Item3.Length + ver.Item4.Length;
                if (token.Item1)
                {
                    if (root == null)
                    {
                        throw new Exception("Cannot have a subgroup as the first token of a rule: " + line[(i - ver.Item2.Length - ver.Item3.Length - ver.Item4.Length)..]);
                    }
                    Console.WriteLine("Found subgroup: " + token.Item2.Name + $" ({token.Item2.Value})");
                    if (root.Children.Length > 0)
                    {
                        root.Children[^1].Children = [..root.Children[^1].Children, token.Item2];
                    }
                    else
                    {
                        root.Children = [..root.Children, token.Item2]; // every element after the first is already implicitely a subgroup, but it can be declared explicitly
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
                        root.Children = [..root.Children, token.Item2];
                    }
                }
            }
            if (root == null)
            {
                throw new Exception("No tokens found in line: " + line);
            }
            return root;
        }

        private static Rule[] ExploreSubgroups(string text)
        {
            List<Rule> rules = new List<Rule>();
            for (int i = 1; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    var search = VerifyGroup(text[i..]);
                    if (search.Item1)
                    {
                        Console.WriteLine($"Found subgroup: {search.Item2}");
                        var (subgroup, token) = GroupToToken(search.Item2, search.Item3, search.Item4);
                        if (subgroup && rules.Count > 0)
                        {
                            throw new Exception("Cannot have a subgroup as the first token of a rule: " + text[i..]);
                        }
                        var children = ExploreSubgroups(search.Item2);
                        token.Children = children;
                        i += search.Item2.Length + search.Item3.Length + search.Item4.Length;
                        if (subgroup)
                        {
                            rules[^1].Children = [..rules[^1].Children, token];
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
                            space = text.Length - 1;
                        }
                        else if (space >= text.Length)
                        {
                            space = text.Length - 1;
                        }
                        var (isGroup, tk) = ParseToken(text[i..space]);
                        if (isGroup)
                        {
                            throw new Exception("Group was misdetected, this is a bug: " + text[i..]);
                        }
                        Console.WriteLine("Found token: " + tk.Name);
                        i += tk.Value?.Length ?? tk.Name.Length;
                        rules.Add(tk);
                    }
                }
            }
            return [..rules];
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
            if (token.StartsWith("$[") && token.EndsWith(']'))
            {
                return (false, new Reserved(token));
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
            if (match.Groups["token"].Success)
            {
                return (true, match.Groups["token"].Value, match.Groups["prefix"].Value, match.Groups["suffix"].Value, match.Groups["subgroup"].Success);
            }
            else // TODO subgroup fix + tests
            {
                return (true, match.Groups["subgroup"].Success || match.Groups["token"].Value.Contains(" ") ? "" : match.Groups["token_suff"].Value, "", match.Groups["solo_suffix"].Value, match.Groups["subgroup"].Success);
            }
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
                        (?<subgroup>(?:[^\(\):\+\*\-]+:_?|_)?\(.*\)(?:\+|\*|\d+(?:-\d+)?|_|:)?)
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

        [GeneratedRegex(@"^(?:(?<prefix>[^\(\):\+\*\-\s]+:_?|_)(?<token>\((?:[^:\+\*\-()]|(?<subgroup>(?:[^\(\):\+\*\-]+:_?|_)?\(.*\)(?:\+|\*|\d+(?:-\d+)?|_|:)?))*\))(?<suffix>\+|\*|\d+(?:-\d+)?|_|:)?|(?<token_suff>\((?:[^:\+\*\-()]|(?<subgroup_suff>(?:[^\(\):\+\*\-]+:_?|_)?\(.*\)(?:\+|\*|\d+(?:-\d+)?|_|:)?))*\))(?<solo_suffix>\+|\*|\d+(?:-\d+)?|_|:))")]
        private static partial Regex GroupRegex();
    }
}