using System.Text.RegularExpressions;

namespace ClemToken
{
    //! how a rule/scope treats implicit whitespace skipping between its children:
    //! Inherit keeps the enclosing scope, Explicit disables skipping, Implicit re-enables it.
    enum SpacingMode
    {
        Inherit = 0,
        Explicit = 1,
        Implicit = 2
    }

    abstract class Rule
    {
        public abstract (int, int) Repeat { get; set; }
        public abstract string Name { get; set; }
        public abstract string? Value { get; set; }
        public abstract bool Ignored { get; set; }
        public abstract Rule[] Children { get; set; }
        public abstract bool IsReserved { get; set; }
        public abstract string UID { get; set; }
        //! concrete on purpose: every rule carries its own spacing modifier (Inherit by default)
        public SpacingMode Spacing { get; set; } = SpacingMode.Inherit;
    }

    class Simple : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        public Simple() { }
        public Simple(string value)
        {
            Name = value;
            Value = value;
            Repeat = (1, 1);
            Ignored = false;
            Children = [];
            IsReserved = false;
            UID = "";
        }
    }

    class Reserved : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        public Reserved() { }
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
            UID = "";
        }
    }

    class Reference : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        public Reference() { }
        public Reference(string value, bool optional = false, string? name = null)
        {
            int start = value.IndexOf('@');
            Name = value[(start + 1)..];
            Value = name ?? value;
            Repeat = optional ? (0, 1) : (1, 1);
            Ignored = false;
            Children = [];
            IsReserved = false;
            UID = "";
        }
    }
    class Anchor : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        public Anchor() { }
        public Anchor(string value, Rule[]? children = null)
        {
            Ignored = value.EndsWith("?:");
            if (Ignored)
            {
                value = value[..^2] + ":";
            }
            int start = value.IndexOf('@');
            int end = value.IndexOf(':');
            Name = value.Substring(start + 1, end - start - 1);
            Value = value;
            Repeat = (1, 1);
            Children = children ?? [];
            IsReserved = false;
            UID = "";
        }
    }
    class Optional : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        //! explicit parameterless ctor: as a struct, new Optional() produced the default value
        //! (Name/Children null) because the all-optional-parameter ctor is not used by new S().
        //! As a class it would otherwise bind to that ctor and pre-fill Children, breaking deserialization.
        public Optional() { }
        public Optional(Rule[]? options = null)
        {
            Name = "Optional";
            Ignored = false;
            Repeat = (1, 1);
            IsReserved = false;
            Children = options ?? [];
            UID = "";
        }
    }
    class Lookahead : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public bool Optional { get; set; }
        public override string UID { get; set; } = null!;
        public Lookahead() { }
        public Lookahead(string value, bool optional = false)
        {
            Name = "Lookahead";
            Value = value;
            Repeat = (1, 1);
            Ignored = false;
            Children = [];
            IsReserved = false;
            Optional = optional;
            UID = "";
        }
    }
    class Group : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        public Group() { }
        public Group((int, int) repeat, bool ignored)
        {
            Repeat = repeat;
            Name = "In Deserialization";
            Value = null;
            Ignored = ignored;
            Children = [];
            IsReserved = false;
            UID = "";
        }
        public Group((int, int) repeat, string name, bool ignored, Rule[] children)
        {
            Repeat = repeat;
            Name = name;
            Value = null;
            Ignored = ignored;
            Children = children;
            IsReserved = false;
            UID = "";
        }
        public Group(out bool subgroup, string? value, string? prefix, string? suffix)
        {
            IsReserved = false;
            Children = [];
            if (prefix != null && prefix.Trim() != "" && prefix.Contains(':'))
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
                    case "":
                        break; // already looped
                    default:
                        if (suffix.StartsWith(':')) // ignored
                        {
                            Ignored = true;
                            suffix = suffix[1..];
                            goto redo;
                        }
                        else if (suffix.StartsWith('_')) // no_op
                        {
                            suffix = suffix[1..];
                            goto redo;
                        }
                        else if (suffix.Contains('-'))
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
                        else
                            throw new Exception($"Invalid suffix: {suffix} for {Name}, prefix: {prefix} ({prefix?.Length}), value: {value} ({value?.Length})");
                }
            }
            Repeat = (start, end);
            UID = "";
        }
    }

    class Empty : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public override string UID { get; set; } = null!;
        public Empty()
        {
            Repeat = (0, 0);
            Name = "Empty";
            Value = null;
            Ignored = false;
            Children = [];
            IsReserved = false;
            UID = "";
        }
    }

    class ProcessRule : Rule
    {
        public override (int, int) Repeat { get; set; }
        public override string Name { get; set; } = null!;
        public override string? Value { get; set; }
        public override bool Ignored { get; set; }
        public override Rule[] Children
        {
            get => [.. ProcessChildren.Select((c) => c as Rule)]; set
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
        public ProcessRule[] ProcessChildren { get; set; } = null!;
        public override bool IsReserved { get; set; }
        public int Length { get; set; }
        public bool Empty { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public (string, int)[] Alternatives { get; set; } = null!;
        public override string UID { get; set; } = null!;
        public ProcessRule() { }
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
            UID = "";
        }
    }

    struct Spec
    {
        public string Target { get; set; }
        public string Definition { get; set; }
        public string Mode { get; set; }
    }

    partial class Generator
    {
        private string[] reserved = ["name"];
        public static (Rule[] rules, Spec[] specs) Generate(string grammar)
        {
            string[] parts = [.. Split(grammar).Select((p) => p.Trim()).Where((p) => p != "")];
            List<Rule> rules = new List<Rule>();
            List<Spec> specs = new List<Spec>();
            foreach (var part in parts)
            {
                if (ParseSpec(part, out Spec spec))
                {
                    Logger.Log("Found spec: " + spec.Target + " (mode: " + (spec.Mode == "" ? "default" : spec.Mode) + ")" + " = " + spec.Definition);
                    specs.Add(spec);
                    continue;
                }
                Rule rule = ParseLine(part);
                if (rule is not ProcessRule)
                {
                    rules.Add(rule);
                }
            }
            return ([.. rules], [.. specs]);
        }

        private static bool ParseSpec(string line, out Spec spec)
        {
            spec = new Spec();
            if (line.StartsWith('$'))
            {
                if (TryConsumeSpacingModifier(line, out _, out _))
                {
                    return false; // $explicit:/$implicit: prefix, not a spec
                }
                int colonIndex = line.IndexOf(':');
                if (colonIndex == -1)
                {
                    throw new Exception("Invalid spec, missing colon: " + line);
                }
                string target = line[1..colonIndex].Trim();
                string rest = line[(colonIndex + 1)..].Trim();
                string mode = "";
                int modeColonIndex = rest.IndexOf(':');
                if (modeColonIndex > 0)
                {
                    string modeString = rest[..modeColonIndex].Trim();
                    // a mode is a bare identifier directly followed by `:` and then whitespace (e.g.
                    // `$comment_begin:simple: /*`). Requiring both keeps colons that belong to the
                    // definition from being swallowed: `$colon: :` and `$x: [0-9]+:[0-9]+` stay intact.
                    bool isValid = modeString.Length > 0;
                    for (int i = 0; isValid && i < modeString.Length; i++)
                    {
                        char c = modeString[i];
                        if (!char.IsLetterOrDigit(c) && c != '_')
                        {
                            isValid = false;
                        }
                    }
                    if (isValid && modeColonIndex + 1 < rest.Length && !char.IsWhiteSpace(rest[modeColonIndex + 1]))
                    {
                        isValid = false;
                    }
                    if (isValid)
                    {
                        rest = rest[(modeColonIndex + 1)..].Trim();
                        mode = modeString;
                    }
                }
                spec = new Spec
                {
                    Target = target,
                    Definition = rest,
                    Mode = mode
                };
                return true;
            }
            return false;
        }

        //! spacing modifier grammar: `$explicit:` / `$implicit:` are the long forms, `!` is shorthand for
        //! `$explicit:`. A modifier must be glued to its target (`@`, `(` or `[`) so a lone `!` or a
        //! `$spec: value` line is not mistaken for one.
        private static bool TryConsumeSpacingModifier(string text, out SpacingMode mode, out int consumed)
        {
            mode = SpacingMode.Inherit;
            consumed = 0;
            const string explicitPrefix = "$explicit:";
            const string implicitPrefix = "$implicit:";
            if (text.StartsWith(explicitPrefix, StringComparison.Ordinal) && IsModifierTarget(text, explicitPrefix.Length))
            {
                mode = SpacingMode.Explicit;
                consumed = explicitPrefix.Length;
                return true;
            }
            if (text.StartsWith(implicitPrefix, StringComparison.Ordinal) && IsModifierTarget(text, implicitPrefix.Length))
            {
                mode = SpacingMode.Implicit;
                consumed = implicitPrefix.Length;
                return true;
            }
            if (text.StartsWith('!') && IsModifierTarget(text, 1))
            {
                mode = SpacingMode.Explicit;
                consumed = 1;
                return true;
            }
            return false;
        }

        private static bool IsModifierTarget(string text, int index)
        {
            if (index >= text.Length)
            {
                return false;
            }
            char c = text[index];
            return c == '@' || c == '(' || c == '[';
        }

        private static Rule ParseLine(string line, bool inAnchor = false)
        {
            // a leading `#` only starts a comment at the top level: inside an anchor body it is a
            // normal token, so a rule like `@comment: # $[rest]` can describe comment lines
            if (line.StartsWith('#') && !inAnchor)
            {
                return new ProcessRule(); // comment
            }
            SpacingMode rootSpacing = SpacingMode.Inherit;
            if (TryConsumeSpacingModifier(line, out SpacingMode startingSpacing, out int startingConsumed))
            {
                rootSpacing = startingSpacing;
                line = line[startingConsumed..].TrimStart();
            }
            Rule? root = null;
            string anchor = "";
            if (line.StartsWith('@'))
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
                Logger.Log("Found anchor: " + anchor);
                root = new Anchor(anchor);
                var result = ParseLine(line[(anchor.Length + 1)..], true);
                if (rootSpacing != SpacingMode.Inherit)
                {
                    root.Spacing = rootSpacing;
                }
                if (result is Anchor tmp)
                {
                    root.Children = tmp.Children;
                    //! a modifier written on the body itself (`@rule: $explicit: ...`) is more local and
                    //! wins over the one on the anchor (`!@rule:`)
                    if (tmp.Spacing != SpacingMode.Inherit)
                    {
                        root.Spacing = tmp.Spacing;
                    }
                }
                else
                {
                    root.Children = [result];
                }
                return root;
            }
            SpacingMode pendingSpacing = SpacingMode.Inherit;
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    continue;
                }
                if (TryConsumeSpacingModifier(line[i..], out SpacingMode inlineSpacing, out int inlineConsumed))
                {
                    pendingSpacing = inlineSpacing;
                    i += inlineConsumed - 1;
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
                        Logger.Log("Found reserved token: " + acc);
                    }
                    else if (tk is Reference)
                    {
                        Logger.Log("Found reference: " + acc);
                    }
                    else if (tk is Anchor)
                    {
                        throw new Exception("Anchors are not authorized in the middle of a rule: " + acc);
                    }
                    else if (tk is Optional)
                    {
                        int closingBracket = PairedIndex(line, '[', ']', i - acc.Length + 1, startState: 1);
                        if (closingBracket == -1)
                        {
                            throw new Exception("Unmatched opening bracket in optional: " + line[i..Math.Min(i + 10, line.Length)]);
                        }
                        tk = ExploreOptional(line[(i - acc.Length + 1)..closingBracket]);
                        i = closingBracket + 1;
                        if (i < line.Length && line[i] == ':')
                        {
                            tk.Ignored = true;
                            i++;
                        }
                        Logger.Log("Found optional: " + tk.Value+" (ignored: "+tk.Ignored+")");
                    }
                    else if (tk is Lookahead lk && lk.Optional)
                    {
                        int closingBracket = -1;
                        int j = i;
                        while (j < line.Length)
                        {
                            if (line[j] == ']' && line[j - 1] != '\\')
                            {
                                closingBracket = j;
                                break;
                            }
                            j++;
                        }
                        if (closingBracket == -1)
                        {
                            throw new Exception("Unmatched opening bracket in lookahead: " + line[i..line.Length]);
                        }
                        tk = ExploreLookahead(line[(i - 1)..closingBracket]);
                        i = closingBracket + 1;
                    }
                    else
                    {
                        Logger.Log("Found token: " + acc+ " ("+tk.GetType().Name+")");
                    }
                    tk.Spacing = pendingSpacing;
                    pendingSpacing = SpacingMode.Inherit;
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
                var ch = ExploreSubgroups(ver.Item2, false);
                var token = GroupToToken(ver.Item2, ver.Item3, ver.Item4);
                if (token.Item2.Name.Contains(' ') || token.Item2.Name == token.Item2.Value)
                    token.Item2.Name = "Anonymous";
                token.Item2.Children = ch;
                token.Item2.Spacing = pendingSpacing;
                pendingSpacing = SpacingMode.Inherit;
                i += ver.Item2.Length + ver.Item3.Length + ver.Item4.Length + 2;
                if (token.Item1)
                {
                    if (root == null)
                    {
                        throw new Exception("Cannot have a subgroup as the first token of a rule: " + line[(i - ver.Item2.Length - ver.Item3.Length - ver.Item4.Length)..]);
                    }
                    Logger.Log("Found subgroup: " + token.Item2.Name + $" ({token.Item2.Value})");
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
                    Logger.Log("Found group: " + token.Item2.Name + $" {token.Item2.Repeat}");
                    if (root == null)
                    {
                        if (inAnchor)
                        {
                            root = new Anchor // intermediary to be extracted above
                            {
                                Children = [token.Item2]
                            };
                        }
                        else
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
            if (rootSpacing != SpacingMode.Inherit)
            {
                root.Spacing = rootSpacing;
            }
            return root;
        }
        private static string[] PairedSplit(string text, string split, char open, char close, int splitDepth = 0, int startState = 0)
        {
            List<string> parts = [""];
            int depth = startState;
            bool skipNext = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && !skipNext)
                {
                    skipNext = true;
                    continue;
                }
                if (text[i] == open)
                {
                    if (!skipNext) depth++;
                    else skipNext = false;
                }
                if (text[i] == close)
                {
                    if (!skipNext) depth--;
                    else skipNext = false;
                }
                if (depth < 0)
                {
                    throw new Exception("Unmatched closing character: " + close + " in text: " + text);
                }
                if (skipNext)
                {
                    parts[^1] += '\\';
                    skipNext = false;
                }
                if (depth == splitDepth && text[i..].StartsWith(split))
                {
                    parts.Add("");
                    i += split.Length - 1; // skip the split
                    continue;
                }
                parts[^1] += text[i];
            }
            return [.. parts];
        }
        private static int PairedIndex(string text, char open, char close, int start = 0, int startState = 0)
        {
            int depth = startState;
            bool skipNext = false;
            for (int i = start; i < text.Length; i++)
            {
                if (!skipNext)
                {
                    if (text[i] == '\\')
                    {
                        skipNext = true;
                        continue;
                    }
                    if (text[i] == open) depth++;
                    if (text[i] == close) depth--;
                }
                if (skipNext)
                {
                    skipNext = false;
                }
                if (depth == 0) return i;
            }
            return -1;
        }

        private static Rule[] ExploreSubgroups(string text, bool inOptional)
        {
            List<Rule> rules = new List<Rule>();
            SpacingMode pendingSpacing = SpacingMode.Inherit;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    if (TryConsumeSpacingModifier(text[i..], out SpacingMode inlineSpacing, out int inlineConsumed))
                    {
                        pendingSpacing = inlineSpacing;
                        i += inlineConsumed - 1;
                        continue;
                    }
                    var search = VerifyGroup(text[i..]);
                    if (search.Item1)
                    {
                        Logger.Log($"Found subgroup: {search.Item2}");
                        var (subgroup, token) = GroupToToken(search.Item2, search.Item3, search.Item4);
                        if (subgroup)
                        {
                            if (inOptional)
                                throw new Exception("Subgroups are not authorized in optionals: " + text[i..]);
                            if (rules.Count == 0)
                                throw new Exception("Cannot have a subgroup as the first token of a rule: " + text[i..]);
                        }
                        var children = ExploreSubgroups(search.Item2, false);
                        token.Children = children;
                        token.Spacing = pendingSpacing;
                        pendingSpacing = SpacingMode.Inherit;
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
                            int closingBracket = PairedIndex(text, '[', ']', i + 1, startState: 1);
                            if (closingBracket == -1)
                            {
                                throw new Exception("Unmatched opening bracket in optional: " + text[i..text.Length]);
                            }
                            tk = ExploreOptional(text[(i + 1)..closingBracket]);
                            if (closingBracket + 1 < text.Length && text[closingBracket + 1] == ':')
                            {
                                tk.Ignored = true;
                                i++;
                            }
                        }
                        if (tk is Reference)
                        {
                            Logger.Log("Found reference: " + text[i..space]);
                            if (!tk.Value!.Contains('@'))
                            {
                                i += tk.Name.Length + 1; 
                            }
                        }
                        if (tk is Lookahead lk && lk.Optional)
                        {
                            int closingBracket = -1;
                            int j = i;
                            while (j < text.Length)
                            {
                                if (text[j] == ']' && text[j - 1] != '\\')
                                {
                                    closingBracket = j;
                                    break;
                                }
                                j++;
                            }
                            if (closingBracket == -1)
                            {
                                throw new Exception("Unmatched opening bracket in lookahead: " + text[i..text.Length]);
                            }
                            tk = ExploreLookahead(text[(i - 1)..closingBracket]);
                            i = closingBracket + 1;
                        }
                        if (tk is Anchor)
                        {
                            throw new Exception("Anchors are not authorized in subgroups: " + text[i..]);
                        }
                        Logger.Log("Found token: " + tk.Name+ " ("+tk.GetType().Name+")");
                        tk.Spacing = pendingSpacing;
                        pendingSpacing = SpacingMode.Inherit;
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
            optional.Value = "[" + text + "]";
            string[] split = PairedSplit(text, " | ", '[', ']');
            List<Rule> options = new List<Rule>();
            foreach (var part in split)
            {
                Rule[] ch = ExploreSubgroups(part, inOptional: true);
                if (ch.Length > 1)
                {
                    throw new Exception("Optional can only have one child per branch: " + part + $"::::: {split.Select((v) => "(" + v + ")").Aggregate((a, b) => a + " | " + b)}");
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
        private static Rule ExploreLookahead(string text)
        {
            var lookahead = new Lookahead(text, optional: true);
            lookahead.Value = text;
            string[] split = text.Split(" | ");
            List<Rule> options = new List<Rule>();
            foreach (var part in split)
            {
                options.Add(new Simple(part.Replace("\\]", "]")));
            }
            lookahead.Children = [.. options];
            return lookahead;
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
            else if (token.StartsWith('[') && token.Length > 1 && !char.IsWhiteSpace(token[1]) && !token.Contains('|') && !token.Contains(']'))
            {
                return (false, new Optional());
            }
            else if (token.Contains(':') && token.Contains('('))
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
                if (end > start)
                {
                    var group = new Group(out bool subgroup, token.Substring(start + 1, end - start - 1), token[..start], token[(end + 1)..]);
                    if (subgroup)
                    {
                        return (subgroup, group);
                    }
                }
            }
            if (token.StartsWith("@>"))
            {
                if (token.Length == 2)
                {
                    throw new Exception("Lookahead token cannot be empty: " + token);
                }
                if (token.StartsWith("@>>["))
                {
                    return (false, new Lookahead(token[4..], true));
                }
                return (false, new Lookahead(token[2..]));
            }
            if (token.StartsWith('@') && token.Length > 1)
            {
                return (false, new Reference(token));
            }
            if (token.StartsWith("?@") && token.Length > 2)
            {
                return (false, new Reference(token[1..], optional: true));
            }
            if (token.Contains('@'))
            {
                if (VerifyNamedReference(token, out Reference reference))
                {
                    return (false, reference);
                }
            }
            if ((token.StartsWith("$[") || token.StartsWith("$$[")) && token.EndsWith(']'))
            {
                return (false, new Reserved(token, token.StartsWith("$$[")));
            }
            return (false, new Simple(token));
        }

        private static bool VerifyNamedReference(string text, out Reference reference)
        {
            bool optional = false;
            string name = "";
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    reference = new();
                    return false;
                }
                if (text[i] == '@')
                {
                    reference = new();
                    return false;
                }
                if (text[i] == ':')
                {
                    name = text[..i];
                    break;
                }
            }
            if (name == "")
            {
                reference = new();
                return false;
            }
            string rest = text[(name.Length + 1)..];
            if (rest.StartsWith('?'))
            {
                optional = true;
                rest = rest[1..];
            }
            if (!rest.StartsWith('@'))
            {
                reference = new();
                return false;
            }
            reference = new Reference(rest, optional, name);
            return true;
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
            if (char.IsWhiteSpace(text[prefix.Length]))
            {
                return (false, "", "", "", false);
            }
            var end = text.IndexOf('(');
            if (end + 1 >= text.Length || char.IsWhiteSpace(text[end + 1]))
            {
                return (false, "", "", "", false);
            }
            var stack = 0;
            for (; end < text.Length; end++)
            {
                if (text[end] == '(')
                {
                    if (end + 1 >= text.Length || char.IsWhiteSpace(text[end + 1]))
                    {
                        continue;
                    }
                    stack++;
                }
                if (text[end] == ')')
                {
                    if (char.IsWhiteSpace(text[end - 1]))
                    {
                        continue;
                    }
                    stack--;
                }
                if (stack == 0)
                {
                    break;
                }
            }
            if (end == text.Length)
            {
                return (false, "", "", "", false);
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
        }

        private static string[] Split(string grammar)
        {
            List<string> parts = [""];
            bool skipNext = false;
            for (int i = 0; i < grammar.Length; i++)
            {
                if (grammar[i] == '\r')
                {
                    if (skipNext && i + 1 < grammar.Length && grammar[i + 1] == '\n')
                    {
                        parts[^1] += "\r\n";
                        skipNext = false;
                        i++; // skip \n
                    }
                    continue;
                }
                if (grammar[i] == '\n')
                {
                    if (parts[^1] != "" && !skipNext)
                    {
                        parts.Add("");
                    }
                    skipNext = false;
                    continue;
                }
                if (grammar[i] == '\\')
                {
                    if (!skipNext)
                    {
                        skipNext = true;
                        continue;
                    }
                }
                else if (skipNext)
                {
                    parts[^1] += '\\';
                    skipNext = false;
                }
                parts[^1] += grammar[i];
            }
            if (skipNext)
            {
                parts[^1] += '\\';
            }
            return [.. parts];
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

        [GeneratedRegex(@"^(?:(?<prefix>[^\[\(\):\+\*\-\s]+:_?|_)(?<token>\((?:(?<subgroup>(?:[^\[\(\):\+\*\-\s]+:_?|_)?\( .* \):?(?:\+|\?|\*|\d+(?:-\d+)?|_|:))|.)*\))(?<suffix>:?(?:\+|\?|\*|\d+(?:-\d+)?|_|:)?))|^(?:(?<post_token>\((?:(?<subgroup_post>(?:[^\[\(\):\+\*\-\s]+:_?|_)?\( .* \):?(?:\+|\?|\*|\d+(?:-\d+)?|_|:))|.)*\))(?<solo_suffix>:?(?:\+|\?|\*|\d+(?:-\d+)?|_|:)?))")]
        private static partial Regex GroupRegex();
    }
}