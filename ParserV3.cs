using System.Text.RegularExpressions;

namespace ClemToken;

class SpecStore
{
    private Dictionary<string, Spec> _specs = new();
    private Dictionary<string, Regex> _regexCache = new();
    public void AddSpec(Spec spec)
    {
        _specs[spec.Target] = spec;
    }
    public bool HasSpec(string name)
    {
        return _specs.ContainsKey(name);
    }
    public Regex GetSpec(string name, string defaultPattern)
    {
        if (_regexCache.TryGetValue(name, out var regex))
        {
            return regex;
        }
        Regex rgx;
        if (_specs.TryGetValue(name, out var spec))
            rgx = new Regex(spec.Definition);
        else
            rgx = new Regex(defaultPattern);
        _regexCache[name] = rgx;
        Console.WriteLine($"Spec '{name}' resolved to regex: {rgx}");
        return rgx;
    }
}

class ParserV3
{
    public static Tree Parse(string code, Rule[] rules, Spec[] specs)
    {
        ReferenceStore referenceStore = new();
        foreach (var rule in rules)
        {
            referenceStore.AddIfReferenceable(rule);
        }
        SpecStore specStore = new SpecStore();
        foreach (var spec in specs)
        {
            specStore.AddSpec(spec);
        }
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(Serializer.DisplayRules(rules));
        Console.WriteLine("--------------------------------------------------");
        return Parse(code, rules, specStore, referenceStore);
    }
    static Tree ExpandLastNode(Tree target, string code, int by = 1)
    {
        if (target.Children.Count == 0)
        {
            target.Token.Value = code[target.Token.Line..(target.Token.Line + target.Token.Value.Length + by)];
            return target;
        }
        var lastNode = target.Children[^1];
        target.Children[^1] = ExpandLastNode(lastNode, code, by);
        target.Token.Value = code[target.Token.Line..(target.Token.Line + target.Token.Value.Length + by)];
        return target;
    }
    static Tree CleanIgnored(Tree tree)
    {
        if (tree.Children.Count == 0)
        {
            return tree;
        }
        tree.Children = GetCleanedChildren(tree);
        return tree;
    }
    static List<Tree> GetCleanedChildren(Tree tree)
    {
        // flatten
        List<Tree> newChildren = [];
        foreach (var child in tree.Children)
        {
            if (child.Token.Ignored)
            {
                newChildren.AddRange(GetCleanedChildren(child));
            }
            else
            {
                newChildren.Add(child);
            }
        }
        return [.. newChildren.Select(CleanIgnored)];
    }
    private static void LogAt(string message, int depth)
    {
        Console.WriteLine(new string(' ', depth * 2) + message.Replace("\r\n", "\\n").Replace("\n", "\\n"));
    }
    public static Tree Parse(string code, Rule[] rules, SpecStore specStore, ReferenceStore referenceStore)
    {
        var tree = new Tree(new Token("Root", "", 0, 0, false), []);
        int cursor = 0;
        int lastIndex = 0;
        int lastLine = 1;
        int lastColumn = 1;
        (int, int) GetCoordinates(int index)
        {
            for (; lastIndex < index; lastIndex++)
            {
                if (code[lastIndex] == '\n')
                {
                    lastLine++;
                    lastColumn = 1;
                }
                else
                {
                    lastColumn++;
                }
            }
            return (lastLine, lastColumn);
        }
        Tree WithCoordinates(Tree tree)
        {
            for (int i = 0; i < tree.Children.Count; i++)
            {
                tree.Children[i] = WithCoordinates(tree.Children[i]);
            }
            var (line, column) = GetCoordinates(tree.Token.Line);
            tree.Token.Line = line;
            tree.Token.Column = column;
            return tree;
        }
        int expandLimit = 0;
        while (cursor < code.Length)
        {
            if (char.IsWhiteSpace(code[cursor]))
            {
                // Skip whitespace in-between rules
                cursor++;
                continue;
            }
            if (Match(code, cursor, rules, specStore, referenceStore, out Tree? foundTree, out int newCursor, out int matchExpandLimit))
            {
                cursor = newCursor;
                expandLimit = matchExpandLimit;
                if (foundTree == null)
                    continue;
                tree.Children.Add(foundTree.Value);
                expandLimit = matchExpandLimit;
            }
            else
            {
                if (char.IsWhiteSpace(code[cursor]))
                {
                    expandLimit = 0; // Reset expand limit on whitespace
                    while (cursor < code.Length && char.IsWhiteSpace(code[cursor]))
                    {
                        cursor++; // Skip whitespace
                    }
                }
                else if (expandLimit > 0)
                {
                    expandLimit--;
                    cursor++; // Skip one character to continue parsing
                    tree.Children[^1] = ExpandLastNode(tree.Children[^1], code); // Expand the last node
                }
                else
                {
                    tree.Children.Add(new Tree(new Token("Error", "Unexpected character: " + code[cursor], cursor, 0, false), []));
                    cursor++; // skip one character to continue parsing
                }
            }
        }
        Console.WriteLine($"Completed parsing at cursor {cursor}/{code.Length}");
        var addressedTree = WithCoordinates(tree);
        addressedTree = CleanIgnored(addressedTree);
        // LogAt($"Parsed tree: {Serializer.StringSerializeTree(addressedTree)}", depth);
        return addressedTree;
    }


    static bool Match(string code, int cursor, Rule[] rules, SpecStore specStore, ReferenceStore referenceStore, out Tree? foundTree, out int newCursor, out int expandLimit)
    {
        newCursor = cursor; // No match found, return the original cursor
        foreach (var rule in rules)
        {
            if (Check(code, cursor, 0, rule, specStore, referenceStore, out int ruleCursor, out Tree? ruleTree, out expandLimit))
            {
                Console.WriteLine($"Matched rule {rule} {rule.Name} at cursor: {cursor} size: {cursor - ruleCursor}");
                newCursor = ruleCursor;
                foundTree = ruleTree;
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to match rule {rule} {rule.Name} at cursor: {cursor}");
            }
        }
        expandLimit = 0;
        foundTree = null; // No match found
        return false; // No match found
    }

    static bool Check(string code, int cursor, int expandAllowed, Rule rule, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, int depth = 0)
    {
        newCursor = cursor;
        expandLimit = 0;
        bool NonGroupChildrenCheck(Rule target, int startCursor, int expandAllowed, out int newCursor, out Tree? ruleTree, out int expandLimit)
        {
            if (SingleGroupCheck(code, startCursor, expandAllowed, new Group((1, 1), target.Name, false, target.Children), specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, depth + 1))
            {
                if (ruleTree == null)
                {
                    return true; // Anchor matched but no tree created
                }
                LogAt($"Anchor matched: {target.Name} at cursor {startCursor} ({ruleTree.Value.Token.Value}) | ({code[startCursor..Math.Min(startCursor + 5, code.Length)]})", depth);
                var rt = ruleTree.Value;
                rt.Token.Name = target.Name;
                ruleTree = rt;
                return true; // Anchor matched
            }
            return false;
        }
        switch (rule)
        {
            case Anchor anchor:
                // 0 width rule
                if (anchor.Children.Length == 0)
                {
                    ruleTree = null;
                    return true;
                }
                return NonGroupChildrenCheck(anchor, cursor, expandAllowed, out newCursor, out ruleTree, out expandLimit);
            case Optional optional:
                LogAt("> " + optional.Name + " (" + optional.Value + ")", depth);
                var tempGroup = new Group((1, 1), false) // workaround to reuse the expansion code
                {
                    Children = [optional.Children[0]]
                };
                for (int i = 0; i < optional.Children.Length; i++)
                {
                    tempGroup.Name = optional.Name + "_" + i;
                    tempGroup.Children[0] = optional.Children[i]; // only child set here to avoid reallocation
                    if (SingleGroupCheck(code, cursor, expandAllowed, tempGroup, specStore, referenceStore, out newCursor, out var singleRuleTree, out expandLimit, depth + 1))
                    {
                        LogAt($"Optional matched: {optional.Name} ({optional.Value}) at cursor {cursor}, for {optional.Children[i].Name} ({optional.Children[i].Value}): ({singleRuleTree?.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                        ruleTree = singleRuleTree?.Children[0] ?? null; // unwrap the group
                        return true;
                    }
                    else
                    {
                        LogAt($"Optional not matched: {optional.Name} ({optional.Value}) at cursor {cursor} | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                    }
                }
                ruleTree = null;
                return false;
            case Simple simple:
                if (simple.Value == null)
                {
                    throw new Exception("Empty simple rule");
                }
                for (int i = 0; i < simple.Value.Length; i++)
                {
                    if (cursor + i >= code.Length || code[cursor + i] != simple.Value[i])
                    {
                        if (expandAllowed > 0)
                        {
                            bool res = Check(code, cursor + 1, expandAllowed - 1, simple, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, depth + 1);
                            LogAt($"Simple expanded: {simple.Name} ({simple.Value}) at cursor {cursor}->{newCursor}: ({ruleTree?.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                            return res;
                        }
                        ruleTree = null; // No match for simple rule
                        return false;
                    }
                }
                bool ngSuccess = NonGroupChildrenCheck(simple, cursor + simple.Value.Length, 0, out newCursor, out ruleTree, out expandLimit);
                if (ngSuccess && ruleTree.HasValue)
                {
                    var rt = ruleTree.Value;
                    rt.Token.Value = code[cursor..(cursor + simple.Value.Length)] + rt.Token.Value;
                    rt.Token.Line = cursor;
                    ruleTree = rt;
                }
                LogAt($"Simple matched: {simple.Name} ({simple.Value}) at cursor {cursor}->{newCursor}: ({ruleTree?.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                return ngSuccess;
            case Reserved reserved:
                if (reserved.Children.Length > 0)
                {
                    throw new Exception("Reserved rules with children are not supported in this version of the parser.");
                }
                if (cursor >= code.Length)
                {
                    ruleTree = null;
                    return false;
                }
                switch (reserved.Name)
                {
                    case "name":
                        if (char.IsWhiteSpace(code[cursor]))
                        {
                            ruleTree = null;
                            return false;
                        }
                        Regex nameRgx = specStore.GetSpec("name", @"[^\s]*");
                        if (!nameRgx.IsMatch(code[cursor..(cursor + 1)]))
                        {
                            ruleTree = null;
                            return false;
                        }
                        newCursor = cursor + 1; // just one for the minimum
                        ruleTree = new Tree(new Token("name", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                        { // new context to avoid reserving maxCursor in the switch
                            var maxCursor = newCursor;
                            while (maxCursor < code.Length && nameRgx.FullMatch(code[cursor..(maxCursor + 1)]))
                            {
                                maxCursor++;
                            }
                            expandLimit = Math.Max(0, maxCursor - newCursor);
                        }
                        Console.WriteLine($"Name regex match: {nameRgx} against '{code[cursor..(newCursor + expandLimit)]}' with expand limit {expandLimit}");
                        return true;
                    case "greedy": // like a name, but once it latches on it expands to the end of the file
                        if (char.IsWhiteSpace(code[cursor]))
                        {
                            ruleTree = null;
                            return false;
                        }
                        newCursor = cursor + 1; // just one for the minimum
                        ruleTree = new Tree(new Token("greedy", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                        expandLimit = code.Length - newCursor; // expand to the end of the file
                        return true;
                    case "digit":
                        Regex digitRgx = specStore.GetSpec("digit", @"[0-9]");
                        if (digitRgx.IsMatch(code.AsSpan(cursor, 1)))
                        {
                            newCursor = cursor + 1;
                            ruleTree = new Tree(new Token("digit", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                        ruleTree = null;
                        return false;
                    default:
                        if (specStore.HasSpec(reserved.Name)) // custom equivalent to name, except with support for starting with a whitespace
                        {
                            Regex customRgx = specStore.GetSpec(reserved.Name, @".");
                            if (!customRgx.IsMatch(code[cursor..(cursor + 1)]))
                            {
                                ruleTree = null;
                                return false;
                            }
                            newCursor = cursor + 1;
                            var maxCursor = newCursor;
                            while (maxCursor < code.Length && customRgx.FullMatch(code[cursor..(maxCursor + 1)]))
                            {
                                maxCursor++;
                            }
                            expandLimit = Math.Max(0, maxCursor - newCursor);
                            ruleTree = new Tree(new Token(reserved.Name, code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                        throw new NotSupportedException($"Reserved rule '{reserved.Name}' is not supported. If it's a custom spec, make sure it is defined.");
                }
            case Group group:
                return CheckGroup(code, cursor, expandAllowed, group, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, depth);
            case Reference reference:
                if (reference.Children.Length > 0)
                {
                    throw new Exception("References with children are not supported in this version of the parser.");
                }
                var referenceRule = referenceStore.GetReference(reference.Name);
                if (referenceRule.Name == "lambda")
                {
                    LogAt($"{referenceRule.Name} start", depth);
                }
                bool success = Check(code, cursor, expandAllowed, referenceRule, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, depth + 1);
                LogAt($"{referenceRule.Name} {(success ? "matched" : "not matched")} at cursor {cursor}->{newCursor}", depth);
                return success;
            default:
                throw new NotSupportedException($"Rule type {rule.GetType().Name} is not supported.");
        }
    }
    static bool CheckGroup(string code, int cursor, int expandAllowed, Group group, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, int depth)
    {
        newCursor = cursor;
        expandLimit = 0;
        int lastExpandLimit = 0;
        if (group.Repeat.Item1 == 0 && group.Repeat.Item2 == 0)
        {
            ruleTree = null;
            expandLimit = expandAllowed;
            return true;
        }
        List<Tree> children = [];
        for (int i = 0; newCursor < code.Length && (i < group.Repeat.Item2 || group.Repeat.Item2 == -1); i++)
        {
            if (!SingleGroupCheck(code, newCursor, i == 0 ? expandAllowed : 0, group, specStore, referenceStore, out int iterationCursor, out Tree? groupTree, out int groupExpandLimit, depth))
            {
                if (i < group.Repeat.Item1) // If we haven't matched the minimum required repetitions
                {
                    expandLimit = 0;
                    ruleTree = null; // Not enough repetitions matched
                    return false; // Not enough repetitions matched
                }
                expandLimit = lastExpandLimit;
                break; // Stop if we reach the maximum or if no more matches are possible
            }
            if (groupTree == null)
            {
                throw new InvalidOperationException("Group tree should not be null after a successful match.");
            }
            children.Add(groupTree.Value);
            newCursor = iterationCursor;
            expandLimit = groupExpandLimit;
            lastExpandLimit = groupExpandLimit;
        }
        if (children.Count == 0)
        {
            expandLimit = expandAllowed;
            ruleTree = null;
        }
        else if (children.Count == 1)
        {
            ruleTree = children[0];
        }
        else
        {
            ruleTree = new Tree(new Token(group.Name + "*", code[cursor..newCursor], cursor, 0, group.Ignored), children);
        }
        return true;
    }

    static bool SingleGroupCheck(string code, int cursor, int expandAllowed, Group group, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, int depth) // no repetition, just the children
    {
        LogAt($">> {group.Name} ({group.Value})", depth);
        newCursor = cursor; // always at the latest validated place
        int targetCursor = newCursor; // can creep forward to check against whitespace
        bool previousWasExpandable = false;
        int expanded = 0;
        int expandIndex = 0;
        ruleTree = null;
        List<Tree> children = [];
        expandLimit = 0;
        int previousExpandLimit = 0;
        int whitespaceExpansion = 0;
        int nullExpansionT1 = 0;
        int nullExpansionT2 = 0;
        int nullExpansionT3 = 0;
        for (int i = 0; i < group.Children.Length; i++)
        {
            var child = group.Children[i];
            LogAt($">? {child.Name} ({child.Value})", depth);
            if (!Check(code, targetCursor, 0, child, specStore, referenceStore, out int childCursor, out Tree? childTree, out int childExpandLimit, depth + 1))
            {
                if (targetCursor >= code.Length)
                {
                    newCursor = targetCursor; // No more characters to check
                    return false; // Child rule did not match
                }
                if (char.IsWhiteSpace(code[targetCursor]))
                {
                    LogAt($"Match not found, but whitespace found, trying to expand with {children.Count} children ({code[(targetCursor + 1)..Math.Min(targetCursor + 10, code.Length)]})", depth);
                    whitespaceExpansion++;
                    previousWasExpandable = previousWasExpandable && (whitespaceExpansion + expanded) < previousExpandLimit;
                    expandAllowed = Math.Max(expandAllowed - 1, 0);
                    i--;
                    targetCursor++;
                    if (children.Count == 0)
                    {
                        cursor++; // offset the whitespace at the start of the group
                    }
                    continue; // Skip whitespace and try the same child again
                }
                else if (expandAllowed > 0)
                {
                    LogAt($"Match not found, but expand allowed, trying to expand ({code[(targetCursor + 1)..Math.Min(targetCursor + 10, code.Length)]})", depth);
                    whitespaceExpansion++; // this will count as whitespace for the purposes of our program
                    expandAllowed--;
                    i--;
                    targetCursor++;
                    cursor++; // offset the start of the group
                    continue;
                }
                else if (previousWasExpandable && expanded < previousExpandLimit) // would be false anyway if whitespace expansion made it overflow
                {
                    LogAt($"Match not found, but previous child was expandable, trying to expand to {expanded + 1} ({code[(targetCursor + 1)..Math.Min(targetCursor + 10, code.Length)]})", depth);
                    if (whitespaceExpansion > 0)
                    {
                        expanded += whitespaceExpansion;
                        whitespaceExpansion = 0; // reset whitespace expansion
                    }
                    expanded++;
                    i--;
                    targetCursor++;
                    continue; // If the previous child was expandable, try expanding and checking the same child again
                }
                targetCursor = newCursor; // reset so the next child is checked against the original cursor if optional
                if (child is Group childGroup && childGroup.Repeat.Item1 == 0) // optional children can be skipped
                {
                    //! Modified
                    // Keep expandable tail from previous matches so nested groups/optionals can still expand.
                    // Skipping an optional child should not clear expandability of the last matched rule.
                    continue;
                }
                return false; // Child rule did not match
            }
            if (childTree != null)
            {
                whitespaceExpansion = 0;
                if (expandAllowed > 0)
                {
                    expandAllowed = 0;
                }
                if (expanded > 0)
                {
                    children[expandIndex] = ExpandLastNode(children[expandIndex], code, expanded);
                }
                expanded = 0;
                previousWasExpandable = childExpandLimit > 0;
                if (previousWasExpandable)
                {
                    expandIndex = children.Count;
                    previousExpandLimit = childExpandLimit;
                }
                children.Add(childTree.Value);
                if (children.Count == 1)
                {
                    cursor = childCursor - childTree.Value.Token.Value.Length;
                    // set the cursor to the start of the first child, so we accidentally get the space or waste it skipped
                }
                LogAt($"Child found: {childTree.Value.Token.Name} ({childTree.Value.Token.Value}), cursor: {targetCursor}->{childCursor} expandable: {previousWasExpandable}, expand limit: {childExpandLimit}, null expansion: {nullExpansionT1 + nullExpansionT2 + nullExpansionT3}", depth);
            }
            else if (childExpandLimit > 0)
            {
                throw new InvalidOperationException("Returned child tree should not be null for expandable rules.");
            }
            else
            {
                if (expandAllowed > 0)
                {
                    LogAt($"Null expansion type 1", depth);
                    expandAllowed--;
                    i--;
                    targetCursor++;
                    nullExpansionT1++;
                    continue;
                }
                else if (previousWasExpandable && expanded < previousExpandLimit)
                {
                    LogAt($"Null expansion type 2", depth);
                    expanded++;
                    nullExpansionT2++;
                    i--;
                    targetCursor++;
                    continue;
                }
                else if (i != 0 && targetCursor < code.Length && char.IsWhiteSpace(code[targetCursor]))
                {
                    LogAt($"Null expansion type 3", depth);
                    i--;
                    targetCursor++;
                    nullExpansionT3++;
                    continue;
                }
                if (nullExpansionT1 > 0 || nullExpansionT2 > 0 || nullExpansionT3 > 0)
                {
                    LogAt($"Resetting null expansions", depth);
                    // reset
                    expandAllowed += nullExpansionT1;
                    expanded -= nullExpansionT2;
                    childCursor -= nullExpansionT1 + nullExpansionT2 + nullExpansionT3;
                    nullExpansionT1 = 0;
                    nullExpansionT2 = 0;
                    nullExpansionT3 = 0;
                }
            }
            newCursor = childCursor;
            targetCursor = childCursor;
        }
        if (previousWasExpandable)
        {
            expandLimit = previousExpandLimit;
        }
        ruleTree = new Tree(new Token(group.Name, code[cursor..newCursor], cursor, 0, group.Ignored), children);
        LogAt($"Group matched: {group.Name} ({group.Value}) at cursor {cursor}->{newCursor}: ({ruleTree.Value.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
        LogAt($"Rule:\n{Serializer.DisplayRules([group])}", depth);
        return true; // All children matched
    }
}