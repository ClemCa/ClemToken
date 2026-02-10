namespace ClemToken;
class GroupParser
{
    internal static bool CheckGroup(string code, int cursor, int expandAllowed, Rule group, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, int depth, SpecStore specStore, ReferenceStore referenceStore, AntiCompute antiCompute, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;
        
        if (group.Repeat.Item1 == 0 && group.Repeat.Item2 == 0)
        {
            expandLimit = expandAllowed;
            return true;
        }
        List<Tree> instances = new List<Tree>();
        for (int i = 0; newCursor < code.Length && (i < group.Repeat.Item2 || group.Repeat.Item2 == -1); i++) {
            if (!CheckGroupChildren(code, newCursor, expandAllowed, group, out newCursor, out var childTree, out var childExpandLimit, out int childExpandedBy, depth, specStore, referenceStore, antiCompute, optimizations))
            {
                if (i < group.Repeat.Item1)
                {
                    return false;
                }
                break;
            }
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
            }
        }
        if (instances.Count < group.Repeat.Item1)
        {
            return false;
        }
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
        ruleTree = new Tree(new Token(group.Name+"*", code[cursor..newCursor], cursor, 0, group.Ignored), instances);
        return true;
    }

    static bool CheckGroupChildren(string code, int cursor, int expandAllowed, Rule group, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, int depth, SpecStore specStore, ReferenceStore referenceStore, AntiCompute antiCompute, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;
        List<Tree> children = [];
        foreach (var child in group.Children)
        {
            if (!CheckChildWithExpansion(code, cursor, expandAllowed, child, specStore, referenceStore, out newCursor, out var childTree, out var childExpandLimit, out int childExpandedBy, depth, antiCompute, optimizations))
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
                children.Add(childTree.Value);
            }
        }
        return true;
    }

    static bool CheckChildWithExpansion(string code, int cursor, int expandAllowed, Rule child, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, int depth, AntiCompute antiCompute, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;

        int targetCursor = cursor;
        int expanded = 0;
        while (true)
        {
            if (ParserV3.Check(code, targetCursor, expandAllowed, child, specStore, referenceStore, out int childCursor, out Tree? parsedTree, out int parsedExpandLimit, out int parsedExpandedBy, antiCompute, optimizations, depth + 1))
            {
                if (parsedExpandedBy > 0)
                {
                    expanded += parsedExpandedBy;
                }
                ruleTree = parsedTree;
                expandLimit = parsedExpandLimit;                
                expandedBy = expanded;
                newCursor = childCursor;
                return true;
            }
            if (targetCursor >= code.Length)
            {
                return false;
            }
            if (char.IsWhiteSpace(code[targetCursor]))
            {
                targetCursor++;
                expanded++;
                continue;
            }
            else if (expandAllowed > expanded)
            {
                targetCursor++;
                expanded++;
                continue;
            }
            return false;
        }
    }
}