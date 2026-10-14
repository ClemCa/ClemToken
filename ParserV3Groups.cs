namespace ClemToken;
class GroupParser
{
    //! one resolved child slot, kept so a run can find and extend its trailing extendable child
    struct ChildSlot
    {
        public Tree? Tree;
        public int EndCursor;
        public int EndExpand;
        public int ExpandedBy;
        public Slack Slack;
    }

    //! scratch stacks reused across resolutions. ResolveFrom pops back to empty before it returns, so a
    //! handed-back pair is always clean (a nested group simply rents another pair). per-thread so
    //! concurrent parses stay independent
    [ThreadStatic] static Stack<List<Tree>>? _scratchTrees;
    [ThreadStatic] static Stack<List<ChildSlot>>? _scratchSlots;

    internal static bool CheckGroup(string code, int cursor, int expandAllowed, Rule group, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, out Slack slack, int depth, SpecStore specStore, ReferenceStore referenceStore, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;
        slack = Slack.None;
        Logger.Log("@?"+group.Name, depth);

        if (group.Repeat.Item1 == 0 && group.Repeat.Item2 == 0)
        {
            expandLimit = expandAllowed;
            Logger.Warning("Empty group "+group.Name, depth);
            return true;
        }

        int min = group.Repeat.Item1;
        int max = group.Repeat.Item2;
        bool variable = max == -1 || max > min;

        //! entries written while matching this group stay journaled so a demanded re-run can roll them back
        var checkpoint = optimizations.Mark();
        //! exact size for the guaranteed passes: List<T> would otherwise grow to its default capacity of 4
        List<Tree> instances = new(min);
        int successes = 0;
        Slack carried = Slack.None;
        int currentExpandAllowed = expandAllowed;

        //! lazy: only take the minimum passes, the rest is handed back as slack (below)
        for (int i = 0; i < min && newCursor < code.Length; i++)
        {
            int passStart = newCursor;
            if (!CheckGroupChildren(code, newCursor, currentExpandAllowed, group, out newCursor, out var childTree, out var childExpandLimit, out int childExpandedBy, out var childSlack, depth, specStore, referenceStore, optimizations))
            {
                Logger.Log("Group "+group.Name+" could not reach "+(i+1)+" passes", depth);
                optimizations.Rollback(checkpoint);
                return false;
            }
            //! a repeat pass that consumes nothing would loop forever when extended
            if (newCursor == passStart)
            {
                Logger.Log("Empty repeat pass on "+group.Name+", stopping", depth);
                optimizations.Rollback(checkpoint);
                return false;
            }
            successes++;
            carried = childSlack;
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
                currentExpandAllowed = childExpandLimit;
                expandLimit = childExpandLimit;
                instances.Add(childTree.Value);
                Logger.Log("Set expand limit to "+childExpandLimit, depth);
            }
        }
        if (successes < min)
        {
            Logger.Log("Failed to match group "+group.Name+" at least "+min+" times", depth);
            optimizations.Rollback(checkpoint);
            return false;
        }

        slack = BuildSlack(instances, newCursor, currentExpandAllowed, expandLimit, expandedBy, successes, carried);
        ruleTree = BuildTree(instances, newCursor);
        Logger.Log("Matched group "+group.Name+" "+successes+" times", depth);
        Logger.Log("expand limit: "+expandLimit, depth);
        return true;

        //! the slack walks one pass at a time: while the repeat can still take a pass it adds one, a
        //! saturated (or fixed) group instead grows the content of its last pass, so a repeat nested
        //! deeper (e.g. @integer's digit+ reached through @arguments) is what actually extends
        Slack BuildSlack(List<Tree> inst, int cur, int expandAll, int expLimit, int expBy, int succ, Slack carry)
        {
            int curMax = max == -1 ? int.MaxValue : max;
            if (variable && succ < curMax)
            {
                return new Slack(curMax - succ, expLimit, () => ExtendPass(inst, cur, expandAll, expLimit, expBy, succ));
            }
            if (carry.More != null)
            {
                return new Slack(variable ? Math.Max(0, curMax - succ) : 0, expLimit, () => ExtendContent(inst, cur, expandAll, expLimit, expBy, succ, carry));
            }
            return new Slack(variable ? (max == -1 ? int.MaxValue : Math.Max(0, curMax - succ)) : 0, expLimit);
        }

        ExtendResult ExtendPass(List<Tree> inst, int cur, int expandAll, int expLimit, int expBy, int succ)
        {
            if (!CheckGroupChildren(code, cur, expandAll, group, out int nc, out var ct, out int ncl, out int ceb, out var cslack, depth, specStore, referenceStore, optimizations))
            {
                return ExtendResult.Fail;
            }
            if (nc == cur)
            {
                return ExtendResult.Fail;
            }
            //! one slot for the pass we are about to append, so the copy does not trigger a growth realloc
            var newInst = new List<Tree>(inst.Count + 1);
            newInst.AddRange(inst);
            int newExpBy = expBy;
            int newExpLimit = expLimit;
            int newExpandAll = expandAll;
            if (ceb > 0)
            {
                if (newInst.Count == 0)
                {
                    newExpBy = ceb;
                } else
                {
                    newInst[^1] = ParserV3.ExpandLastNode(newInst[^1], code, ceb);
                }
            }
            if (ct.HasValue)
            {
                newExpandAll = ncl;
                newExpLimit = ncl;
                newInst.Add(ct.Value);
            }
            int newSucc = succ + 1;
            Slack newSlack = BuildSlack(newInst, nc, newExpandAll, newExpLimit, newExpBy, newSucc, cslack);
            return new ExtendResult(true, nc, newExpLimit, newExpBy, BuildTree(newInst, nc), newSlack);
        }

        ExtendResult ExtendContent(List<Tree> inst, int cur, int expandAll, int expLimit, int expBy, int succ, Slack carry)
        {
            if (carry.More == null || inst.Count == 0)
            {
                return ExtendResult.Fail;
            }
            var r = carry.More();
            if (!r.Ok)
            {
                return ExtendResult.Fail;
            }
            var newInst = new List<Tree>(inst);
            newInst[^1] = r.Tree ?? newInst[^1];
            Slack newSlack = BuildSlack(newInst, r.Cursor, expandAll, r.ExpandLimit, expBy, succ, r.Slack);
            return new ExtendResult(true, r.Cursor, r.ExpandLimit, expBy, BuildTree(newInst, r.Cursor), newSlack);
        }

        Tree? BuildTree(List<Tree> inst, int end)
        {
            if (inst.Count == 0)
            {
                return null;
            }
            if (inst.Count == 1)
            {
                return inst[0];
            }
            int start = inst[0].Token.Line;
            return new Tree(new Token(group.Name+"*", code[start..end], start, 0, group.Ignored), inst);
        }
    }

    static bool CheckGroupChildren(string code, int cursor, int expandAllowed, Rule group, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, out Slack slack, int depth, SpecStore specStore, ReferenceStore referenceStore, Optimizations optimizations)
    {
        newCursor = cursor;
        ruleTree = null;
        expandLimit = expandAllowed;
        expandedBy = 0;
        slack = Slack.None;
        int groupStart = cursor;
        int groupStartExpand = expandAllowed;

        var treePool = _scratchTrees ??= new();
        var slotPool = _scratchSlots ??= new();
        List<Tree> scratchTrees = treePool.Count > 0 ? treePool.Pop() : [];
        List<ChildSlot> scratchSlots = slotPool.Count > 0 ? slotPool.Pop() : [];
        try
        {
            if (!ResolveFrom(0, cursor, expandAllowed, scratchTrees, scratchSlots, out newCursor, out expandLimit, out expandedBy, out slack, out List<Tree> children))
            {
                return false;
            }
            int start = children.Count > 0 ? children[0].Token.Line : cursor;
            ruleTree = new Tree(new Token(group.Name, code[start..newCursor], start, 0, group.Ignored), children);
            return true;
        }
        finally
        {
            scratchTrees.Clear();
            scratchSlots.Clear();
            treePool.Push(scratchTrees);
            slotPool.Push(scratchSlots);
        }

        //! resolve children left to right: when a following sibling fails, extend this child one step
        //! through its own slack (the same path that reported it) instead of failing
        bool ResolveFrom(int index, int fromCursor, int fromExpand, List<Tree> pre, List<ChildSlot> slots, out int endCursor, out int endExpand, out int endExpandedBy, out Slack endSlack, out List<Tree> resolved, int overrideIndex = -1, ExtendResult? overrideResult = null)
        {
            if (index == group.Children.Length)
            {
                endCursor = fromCursor;
                endExpand = fromExpand;
                endExpandedBy = 0;
                endSlack = BuildChildrenSlack(slots, fromExpand);
                //! pre is the shared scratch, snapshot it: this list becomes the group's tree children
                resolved = [.. pre];
                return true;
            }
            ChildSlot slot = default;
            bool haveSlot = false;
            int appliedExpandedBy = 0;
            int appliedLocal = 0;
            int childCursor = fromCursor;
            Tree? childTree = null;
            int childExpandLimit = fromExpand;
            int childExpandedBy = 0;
            Slack childSlack = Slack.None;
            while (true)
            {
                var checkpoint = optimizations.Mark();
                if (!haveSlot)
                {
                    if (index == overrideIndex && overrideResult.HasValue)
                    {
                        var o = overrideResult.Value;
                        childCursor = o.Cursor;
                        childTree = o.Tree;
                        childExpandLimit = o.ExpandLimit;
                        childExpandedBy = o.ExpandedBy;
                        childSlack = o.Slack;
                    }
                    else if (!CheckChildWithExpansion(code, fromCursor, fromExpand, group.Children[index], specStore, referenceStore, out childCursor, out childTree, out childExpandLimit, out childExpandedBy, out childSlack, depth, optimizations))
                    {
                        optimizations.Rollback(checkpoint);
                        endCursor = fromCursor;
                        endExpand = fromExpand;
                        endExpandedBy = 0;
                        endSlack = Slack.None;
                        resolved = pre;
                        return false;
                    }
                    haveSlot = true;
                }
                else
                {
                    //! rest failed previously: extend this child one step through its slack
                    if (slot.Slack.More == null)
                    {
                        optimizations.Rollback(checkpoint);
                        break;
                    }
                    var r = slot.Slack.More();
                    if (!r.Ok || r.Cursor <= slot.EndCursor)
                    {
                        optimizations.Rollback(checkpoint);
                        break;
                    }
                    childCursor = r.Cursor;
                    childTree = r.Tree;
                    childExpandLimit = r.ExpandLimit;
                    childExpandedBy = r.ExpandedBy;
                    childSlack = r.Slack;
                }
                slot = new ChildSlot { Tree = childTree, EndCursor = childCursor, EndExpand = childExpandLimit, ExpandedBy = childExpandedBy, Slack = childSlack };

                //! a demanded re-run usually reports the same leading expansion; only apply the delta so
                //! the shared previous-sibling tree is not expanded twice. Mutating pre in place keeps the
                //! parent token update visible to later retries.
                int delta = childExpandedBy - appliedExpandedBy;
                if (delta > 0)
                {
                    if (pre.Count == 0)
                    {
                        appliedLocal += delta;
                    }
                    else
                    {
                        pre[^1] = ParserV3.ExpandLastNode(pre[^1], code, delta);
                    }
                }
                else if (delta < 0)
                {
                    Logger.Warning($"Following child {group.Children[index].Name} shrank its expansion by {-delta}, which cannot be undone", depth);
                }
                appliedExpandedBy = childExpandedBy;
                int localExpandedBy = appliedLocal;
                //! pre/slots are the shared scratch for this resolution: push the child, recurse, pop. the
                //! base case snapshots what it keeps, so nothing here escapes or is copied per level
                bool pushed = childTree.HasValue;
                if (pushed)
                {
                    pre.Add(childTree.GetValueOrDefault());
                }
                slots.Add(slot);
                //! a child that produced no tree (e.g. a repeat matching zero passes) leaves the expansion
                //! budget untouched, so the following sibling still sees the previous allowance
                int nextExpand = pushed ? childExpandLimit : fromExpand;

                bool reached = ResolveFrom(index + 1, childCursor, nextExpand, pre, slots, out endCursor, out endExpand, out int deeperExpandedBy, out endSlack, out resolved, overrideIndex, overrideResult);
                slots.RemoveAt(slots.Count - 1);
                if (pushed)
                {
                    pre.RemoveAt(pre.Count - 1);
                }
                if (reached)
                {
                    endExpandedBy = localExpandedBy + deeperExpandedBy;
                    return true;
                }
                //! the next sibling failed: extend this child one step and try again, on the same path
                if (slot.Slack.More == null)
                {
                    optimizations.Rollback(checkpoint);
                    break;
                }
                optimizations.Rollback(checkpoint);
            }
            endCursor = fromCursor;
            endExpand = fromExpand;
            endExpandedBy = 0;
            endSlack = Slack.None;
            resolved = pre;
            return false;
        }

        //! a resolved run can still be extended if any child can; try the trailing children first
        //! (their growth moves the cursor furthest right)
        Slack BuildChildrenSlack(List<ChildSlot> slots, int endExpand)
        {
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Slack.More != null)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                return Slack.None;
            }
            //! slots is the shared scratch, so snapshot it before it leaves this frame via the slack
            var captured = new List<ChildSlot>(slots);
            return new Slack(1, endExpand, () => ExtendChildren(captured));
        }

        ExtendResult ExtendChildren(List<ChildSlot> slots)
        {
            for (int j = slots.Count - 1; j >= 0; j--)
            {
                if (slots[j].Slack.More == null)
                {
                    continue;
                }
                var r = slots[j].Slack.More!();
                if (!r.Ok || r.Cursor <= slots[j].EndCursor)
                {
                    continue;
                }
                //! re-resolve the run with child j taken from the extension: everything before j is
                //! recomputed so expansions stay consistent, children after j re-resolve from the new cursor
                if (ResolveFrom(0, groupStart, groupStartExpand, [], [], out int eC, out int eE, out int eEB, out var eSlack, out var eChildren, overrideIndex: j, overrideResult: r))
                {
                    int start = eChildren.Count > 0 ? eChildren[0].Token.Line : groupStart;
                    var tree = new Tree(new Token(group.Name, code[start..eC], start, 0, group.Ignored), eChildren);
                    return new ExtendResult(true, eC, eE, eEB, tree, eSlack);
                }
            }
            return ExtendResult.Fail;
        }
    }

    static bool CheckChildWithExpansion(string code, int cursor, int expandAllowed, Rule child, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, out Slack slack, int depth, Optimizations optimizations)
    {
        Logger.Log("Checking child "+child.Name+" with expansion allowed "+expandAllowed+ $" ({code[cursor..Math.Min(cursor+10, code.Length)]})", depth);
        newCursor = cursor;
        ruleTree = null;
        expandLimit = 0;
        expandedBy = 0;
        slack = Slack.None;

        int targetCursor = cursor;
        int expanded = 0;
        int whitespaceExpansion = 0;
        int regularExpansion = 0;
        int justInCase = 1_000_000;
        while (justInCase-- > 0)
        {
            if (ParserV3.Check(code, targetCursor, expandAllowed, child, specStore, referenceStore, out int childCursor, out Tree? parsedTree, out int parsedExpandLimit, out int parsedExpandedBy, out var parsedSlack, optimizations, depth + 1))
            {
                if (parsedExpandedBy > 0)
                {
                    expanded += parsedExpandedBy;
                }
                ruleTree = parsedTree;
                expandLimit = parsedExpandLimit;                
                expandedBy = expanded - whitespaceExpansion;
                newCursor = childCursor;
                slack = parsedSlack;
                //! a successful child marks how far the failing rule actually got; a speculative miss
                //! reports nothing, so probing whitespace/comment runs cannot inflate the failed region
                optimizations.ReportReached(childCursor);
                return true;
            }
            if (targetCursor >= code.Length)
            {
                return false;
            }
            if (!optimizations.ExplicitSpacing && char.IsWhiteSpace(code[targetCursor]) && (regularExpansion == 0 || regularExpansion >= expandAllowed)) // can only expand whitespace before or after a token
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
