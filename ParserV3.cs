
using System.Diagnostics;
using System.IO.Pipes;

namespace ClemToken;

class ParserV3
{
    public static Tree Parse(string code, Rule[] rules)
    {
        ReferenceStore referenceStore = new ReferenceStore();
        foreach (var rule in rules)
        {
            Console.WriteLine($"Adding rule: {rule.Name} ({rule.GetType().Name})");
            referenceStore.AddIfReferenceable(rule);
        }
        return Parse(code, rules, referenceStore);
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
        return [..newChildren.Select(CleanIgnored)];
    }
    public static Tree Parse(string code, Rule[] rules, ReferenceStore referenceStore)
    {
        var tree = new Tree(new Token("Root", "", 0, 0, false), new List<Tree>());
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
            if (Match(code, cursor, rules, referenceStore, out Tree? foundTree, out int newCursor, out int matchExpandLimit))
            {
                cursor = newCursor;
                if (foundTree == null)
                {
                    continue;
                }
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
        var addressedTree = WithCoordinates(tree);
        addressedTree = CleanIgnored(addressedTree);
        return addressedTree;
    }


    static bool Match(string code, int cursor, Rule[] rules, ReferenceStore referenceStore, out Tree? foundTree, out int newCursor, out int expandLimit)
    {
        newCursor = cursor; // No match found, return the original cursor
        foreach (var rule in rules)
        {
            if (Check(code, cursor, rule, referenceStore, out int ruleCursor, out Tree? ruleTree, out expandLimit))
            {
                newCursor = ruleCursor;
                foundTree = ruleTree;
                return true;
            }
        }
        expandLimit = 0;
        foundTree = null; // No match found
        return false; // No match found
    }

    static bool Check(string code, int cursor, Rule rule, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit)
    {
        newCursor = cursor;
        expandLimit = 0;
        switch (rule)
        {
            case Anchor anchor:
                // 0 width rule
                if (anchor.Children.Length == 0)
                {
                    ruleTree = null;
                    return true;
                }
                if (SingleGroupCheck(code, cursor, new Group((1, 1), anchor.Name, false, anchor.Children), referenceStore, out newCursor, out ruleTree, out expandLimit))
                {
                    if (ruleTree == null)
                    {
                        return true; // Anchor matched but no tree created
                    }
                    var rt = ruleTree.Value;
                    rt.Token.Name = anchor.Name;
                    ruleTree = rt;
                    return true; // Anchor matched
                }
                return false;
            case Simple simple:
                if (simple.Value == null)
                {
                    ruleTree = simple.Ignored ? null : new Tree(new Token(simple.Name, "", cursor, 0, simple.Ignored), new List<Tree>());
                    return true; // Empty simple rule matches any position
                }
                for (int i = 0; i < simple.Value.Length; i++)
                {
                    if (cursor + i >= code.Length || code[cursor + i] != simple.Value[i])
                    {
                        ruleTree = null; // No match for simple rule
                        return false;
                    }
                }
                newCursor = cursor + simple.Value.Length;
                ruleTree = simple.Ignored ? null : new Tree(new Token(simple.Name, simple.Value, cursor, 0, simple.Ignored), new List<Tree>());
                return true;
            case Reserved reserved:
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
                        newCursor = cursor + 1; // just one for the minimum
                        ruleTree = new Tree(new Token("name", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                        while ((newCursor + expandLimit) < code.Length && (!char.IsWhiteSpace(code[newCursor + expandLimit])))
                        {
                            expandLimit++;
                        }
                        return true;
                    default:
                        throw new NotSupportedException($"Reserved rule '{reserved.Name}' is not supported.");
                }
            case Group group:
                return CheckGroup(code, cursor, group, referenceStore, out newCursor, out ruleTree, out expandLimit);
            case Reference reference:
                var referenceRule = referenceStore.GetReference(reference.Name);
                bool success = Check(code, cursor, referenceRule, referenceStore, out newCursor, out ruleTree, out expandLimit);
                return success;
            default:
                throw new NotSupportedException($"Rule type {rule.GetType().Name} is not supported.");
        }
    }
    static bool CheckGroup(string code, int cursor, Group group, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit)
    {
        newCursor = cursor;
        expandLimit = 0;
        if (group.Repeat.Item1 == 0 && group.Repeat.Item2 == 0)
        {
            ruleTree = new Tree(new Token(group.Name, "", cursor, 0, group.Ignored), []);
            return true;
        }
        List<Tree> children = [];
        for (int i = 0; newCursor < code.Length && (i < group.Repeat.Item2 || group.Repeat.Item2 == -1); i++)
        {
            if (!SingleGroupCheck(code, newCursor, group, referenceStore, out int iterationCursor, out Tree? groupTree, out int groupExpandLimit))
            {
                expandLimit = 0;
                if (i < group.Repeat.Item1) // If we haven't matched the minimum required repetitions
                {
                    ruleTree = null; // Not enough repetitions matched
                    return false; // Not enough repetitions matched
                }
                break; // Stop if we reach the maximum or if no more matches are possible
            }
            if (groupTree == null)
            {
                throw new InvalidOperationException("Group tree should not be null after a successful match.");
            }
            children.Add(groupTree.Value);
            newCursor = iterationCursor;
            expandLimit = groupExpandLimit;
        }
        if (children.Count == 1)
        {
            ruleTree = children[0];
        }
        else
        {
            ruleTree = new Tree(new Token(group.Name + "*", code[cursor..newCursor], cursor, 0, group.Ignored), children);
        }
        return true;
    }

    static bool SingleGroupCheck(string code, int cursor, Group group, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit) // no repetition, just the children
    {
        newCursor = cursor; // always at the latest validated place
        int targetCursor = newCursor; // can creep forward to check against whitespace
        bool previousWasExpandable = false;
        int expanded = 0;
        int expandIndex = 0;
        ruleTree = null;
        List<Tree> children = [];
        expandLimit = 0;
        int previousExpandLimit = 0;
        for (int i = 0; i < group.Children.Length; i++)
        {
            var child = group.Children[i];
            if (!Check(code, targetCursor, child, referenceStore, out int childCursor, out Tree? childTree, out int childExpandLimit))
            {
                if (targetCursor >= code.Length)
                {
                    newCursor = targetCursor; // No more characters to check
                    return false; // Child rule did not match
                }
                if (char.IsWhiteSpace(code[targetCursor]))
                {
                    previousWasExpandable = false;
                    i--;
                    targetCursor++;
                    if (children.Count == 0)
                    {
                        cursor++; // offset the whitespace at the start of the group
                    }
                    continue; // Skip whitespace and try the same child again
                }
                else if (previousWasExpandable)
                {
                    expanded++;
                    i--;
                    targetCursor++;
                    continue; // If the previous child was expandable, try expanding and checking the same child again
                }
                targetCursor = newCursor; // reset so the next child is checked against the original cursor if optional
                if (child is Group childGroup && childGroup.Repeat.Item1 == 0) // optional children can be skipped
                {
                    previousWasExpandable = expanded > 0;
                    continue;
                }
                return false; // Child rule did not match
            }
            if (expanded > 0)
            {
                children[expandIndex] = ExpandLastNode(children[expandIndex], code, expanded);
            }
            expanded = 0;
            previousWasExpandable = childExpandLimit > 0;
            if (childTree != null)
            {
                if (previousWasExpandable)
                {
                    expandIndex = children.Count;
                    previousExpandLimit = childExpandLimit;
                }
                children.Add(childTree.Value);
            }
            else if (previousWasExpandable)
            {
                throw new InvalidOperationException("Returned child tree should not be null for expandable rules.");
            }
            newCursor = childCursor;
            targetCursor = childCursor;
        }
        if(previousWasExpandable)
        {
            expandLimit = previousExpandLimit;
        }
        ruleTree = new Tree(new Token(group.Name, code[cursor..newCursor], cursor, 0, group.Ignored), children);
        return true; // All children matched
    }
}