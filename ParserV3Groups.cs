namespace ClemToken;
class GroupParser
{
    internal static bool CheckGroup(string code, int cursor, int expandAllowed, Rule group, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, int depth, SpecStore specStore, ReferenceStore referenceStore, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;
        Logger.Log("@?"+group.Name, depth);
        
        if (group.Repeat.Item1 == 0 && group.Repeat.Item2 == 0)
        {
            expandLimit = expandAllowed;
            return true;
        }
        List<Tree> instances = [];
        int successes = 0;
        for (int i = 0; newCursor < code.Length && (i < group.Repeat.Item2 || group.Repeat.Item2 == -1); i++) {
            if (!CheckGroupChildren(code, newCursor, expandAllowed, group, out newCursor, out var childTree, out var childExpandLimit, out int childExpandedBy, depth, specStore, referenceStore, optimizations))
            {
                if (i < group.Repeat.Item1)
                {
                    return false;
                }
                break;
            }
            successes++;
            if (childExpandedBy > 0)
            {
                if (instances.Count == 0)
                {
                    expandedBy = childExpandedBy;
                } else
                {
                    instances[^1] = ParserV3.ExpandLastNode(instances[^1], code, childExpandedBy);
                }
            }
            if (childTree.HasValue)
            {
                expandAllowed = childExpandLimit;
                expandLimit = childExpandLimit;
                instances.Add(childTree.Value);
                Logger.Log("Set expand limit to "+childExpandLimit);
            }
        }
        if (successes < group.Repeat.Item1)
        {
            return false;
        }
        Logger.Log("expand limit: "+expandLimit, depth);
        if (instances.Count == 0)
        {
            ruleTree = null;
            return true;
        }
        if (instances.Count == 1)
        {
            ruleTree = instances[0];
            return true;
        }
        cursor = instances[0].Token.Line;
        ruleTree = new Tree(new Token(group.Name+"*", code[cursor..newCursor], cursor, 0, group.Ignored), instances);
        return true;
    }

    static bool CheckGroupChildren(string code, int cursor, int expandAllowed, Rule group, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, int depth, SpecStore specStore, ReferenceStore referenceStore, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;
        List<Tree> children = [];
        foreach (var child in group.Children)
        {
            if (!CheckChildWithExpansion(code, newCursor, expandAllowed, child, specStore, referenceStore, out int childCursor, out var childTree, out var childExpandLimit, out int childExpandedBy, depth, optimizations))
            {
                return false;
            }
            if (childExpandedBy > 0)
            {
                if (children.Count == 0)
                {
                    expandedBy = childExpandedBy;
                } else
                {
                    children[^1] = ParserV3.ExpandLastNode(children[^1], code, childExpandedBy);
                }
            }
            if (childTree.HasValue)
            {
                expandAllowed = childExpandLimit;
                expandLimit = childExpandLimit;
                if (children.Count == 0)
                {
                    cursor = childTree.Value.Token.Line;
                }
                children.Add(childTree.Value);
            }
            newCursor = childCursor;
        }
        ruleTree = new Tree(new Token(group.Name, code[cursor..newCursor], cursor, 0, group.Ignored), children);
        return true;
    }

    static bool CheckChildWithExpansion(string code, int cursor, int expandAllowed, Rule child, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, int depth, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;

        int targetCursor = cursor;
        int expanded = 0;
        int whitespaceExpansion = 0;
        int regularExpansion = 0;
        int justInCase = 1_000_000;
        while (justInCase-- > 0)
        {
            if (ParserV3.Check(code, targetCursor, expandAllowed, child, specStore, referenceStore, out int childCursor, out Tree? parsedTree, out int parsedExpandLimit, out int parsedExpandedBy, optimizations, depth + 1))
            {
                if (parsedExpandedBy > 0)
                {
                    expanded += parsedExpandedBy;
                }
                ruleTree = parsedTree;
                expandLimit = parsedExpandLimit;                
                expandedBy = expanded - whitespaceExpansion;
                newCursor = childCursor;
                return true;
            }
            if (targetCursor >= code.Length)
            {
                return false;
            }
            if (char.IsWhiteSpace(code[targetCursor]) && (regularExpansion == 0 || regularExpansion >= expandAllowed)) // can only expand whitespace before or after a token
            {
                targetCursor++;
                expanded++;
                whitespaceExpansion++;
                Logger.Log("Whitespace expansion", depth);
                continue;
            }
            else if (expandAllowed > expanded)
            {
                targetCursor++;
                expanded++;
                regularExpansion++;
                Logger.Log("Regular expansion", depth);
                continue;
            }
            return false;
        }
        throw new Exception("Too many whitespaces or infinite expansion");
    }
}