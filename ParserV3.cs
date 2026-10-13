using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ClemToken;

class SpecStore
{
    private Dictionary<string, Spec> _specs = new();
    private Dictionary<string, Checker> _checkerCache = new();
    public void AddSpec(Spec spec)
    {
        _specs[spec.Target] = spec;
    }
    public bool HasSpec(string name)
    {
        return _specs.ContainsKey(name);
    }
    public string GetSpecDefinition(string name, string defaultPattern)
    {
        if (_specs.TryGetValue(name, out var spec))
        {
            return spec.Definition;
        }
        return defaultPattern;
    }
    public string GetSpecMode(string name, string defaultMode)
    {
        if (_specs.TryGetValue(name, out var spec))
        {
            return spec.Mode;
        }
        return defaultMode;
    }
    public Checker GetSpec(string name, string defaultPattern)
    {
        if (_checkerCache.TryGetValue(name, out var chk))
        {
            return chk;
        }
        Checker checker;
        if (_specs.TryGetValue(name, out var spec))
        {
            checker = GenerateChecker(spec);
        }
        else
            checker = new RegexChecker(defaultPattern);
        _checkerCache[name] = checker;
        // Logger.Log($"Spec '{name}' resolved to regex: {checker}");
        return checker;
    }
    public static Checker GenerateChecker(Spec spec)
    {
        if (spec.Mode == "simple")
            return new SimpleChecker(spec.Definition);
        else // default to regex
            return new RegexChecker(spec.Definition);
    }
}

class Optimizations
{
    //! optional per-parse metrics sink; null when profiling is off, so the Check wrapper stays free
    public MetricsCollector? Metrics;

    //! current scope's spacing mode: true means implicit whitespace skipping is disabled. Set and
    //! restored around each rule check, so it always reflects the enclosing scope during a check.
    public bool ExplicitSpacing;

    //! Retroactive cache journal
    // A demanded repeat re-run happens after the demanded node already returned, so the caches it
    // wrote during the lower-demand attempt are still live. Rolling back to a checkpoint taken
    // before that attempt (then recomputing at the higher demand) is what keeps them from shadowing
    // the new attempt. Entries are only final once DefinitiveProgression commits them.
    public readonly record struct Checkpoint(int Invalidated, int Lookahead, int Stack);

    private List<((string UID, int Cursor) key, bool hadPrevious, int previousDemand)> _invalidatedJournal = [];
    private List<(int index, (string stack, (string UID, int foundIndex) signature) entry, bool added)> _lookaheadJournal = [];

    public Checkpoint Mark() => new(_invalidatedJournal.Count, _lookaheadJournal.Count, searchStack.Count);

    public void Rollback(Checkpoint checkpoint)
    {
        if (checkpoint.Invalidated > _invalidatedJournal.Count || checkpoint.Lookahead > _lookaheadJournal.Count)
        {
            return; // the checkpoint was already committed away, nothing to undo
        }
        for (int i = _invalidatedJournal.Count - 1; i >= checkpoint.Invalidated; i--)
        {
            var (key, hadPrevious, previousDemand) = _invalidatedJournal[i];
            if (hadPrevious)
            {
                _invalidated[key] = previousDemand;
            }
            else
            {
                _invalidated.Remove(key);
            }
        }
        _invalidatedJournal.RemoveRange(checkpoint.Invalidated, _invalidatedJournal.Count - checkpoint.Invalidated);
        for (int i = _lookaheadJournal.Count - 1; i >= checkpoint.Lookahead; i--)
        {
            var (index, entry, added) = _lookaheadJournal[i];
            if (added)
            {
                _lookaheadCache.RemoveAt(index);
            }
            else
            {
                _lookaheadCache.Insert(index, entry);
            }
        }
        _lookaheadJournal.RemoveRange(checkpoint.Lookahead, _lookaheadJournal.Count - checkpoint.Lookahead);
        while (searchStack.Count > checkpoint.Stack)
        {
            searchStack.Pop();
            _stackKeys.RemoveAt(_stackKeys.Count - 1);
        }
    }

    public void CommitAll()
    {
        _invalidatedJournal.Clear();
        _lookaheadJournal.Clear();
    }

    public void DefinitiveProgression(int cursor)
    {
        CommitAll();
        List<(string UID, int Cursor)> staleInvalidated = [];
        foreach (var entry in _invalidated)
        {
            if (entry.Key.Cursor < cursor)
            {
                staleInvalidated.Add(entry.Key);
            }
        }
        foreach (var key in staleInvalidated)
        {
            _invalidated.Remove(key);
        }
        for (int i = _lookaheadCache.Count - 1; i >= 0; i--)
        {
            if (_lookaheadCache[i].signature.foundIndex < cursor)
            {
                _lookaheadCache.RemoveAt(i);
            }
        }
    }
    //! Search stack
    private Stack<(string UID, int cursor)> searchStack = new();
    //! StackKey() is read on every lookahead probe, so the key is kept one entry per depth instead of
    //! rebuilt from scratch (O(depth^2) per probe). It reads outermost-first, making StartsWith an
    //! ancestor test; each frame ends with a separator so cursors cannot prefix-match (1 vs 10).
    private List<string> _stackKeys = new();
    public int SearchStackDepth => searchStack.Count;
    public IEnumerable<(string UID, int cursor)> SearchStack => searchStack;
    private string StackKey() => _stackKeys.Count > 0 ? _stackKeys[^1] : "";
    public IEnumerable<(Rule rule, int cursor)> SearchStackResolved(Rule[] rules)
    {
        return searchStack.Select(entry => (rules.First(r => r.UID == entry.UID), entry.cursor));
    }
    public void PushSearch(Rule rule, int cursor)
    {
        searchStack.Push((rule.UID, cursor));
        string frame = $"{rule.UID}@{cursor}|";
        _stackKeys.Add(_stackKeys.Count == 0 ? frame : _stackKeys[^1] + frame);
    }
    public (string UID, int cursor) PeekSearch()
    {
        return searchStack.Peek();
    }
    public void PopSearch()
    {
        searchStack.Pop();
        _stackKeys.RemoveAt(_stackKeys.Count - 1);
        ClearLookahead();
    }
    //! Preemptive invalidation
    // Stores the *minimal* demand at which (rule, cursor) is known to fail. Failure is monotone in
    // demand (requiring more passes is strictly harder), so a failure recorded at demand d is safely
    // reusable for any demand >= d.
    private Dictionary<(string UID, int Cursor), int> _invalidated = new();
    public void Invalidate(Rule rule, int cursor, int demand = 0)
    {
        var key = (rule.UID, cursor);
        if (_invalidated.TryGetValue(key, out var existing))
        {
            if (existing <= demand)
            {
                return; // already known to fail at (at most) this demand
            }
            _invalidatedJournal.Add((key, true, existing));
            _invalidated[key] = demand;
        }
        else
        {
            _invalidatedJournal.Add((key, false, 0));
            _invalidated[key] = demand;
        }
    }
    public bool IsInvalidated(Rule rule, int cursor, int demand = 0)
    {
        return _invalidated.TryGetValue((rule.UID, cursor), out var failingDemand) && failingDemand <= demand;
    }
    //! Lookahead uniqueness
    private List<(string stack, (string UID, int foundIndex) signature)> _lookaheadCache = [];
    public void CacheLookahead(Rule lookahead, int foundIndex, int depth)
    {
        string key = StackKey();
        Logger.Log($"Caching lookahead: {lookahead.Name} at index {foundIndex} with stack {key}", depth);
        _lookaheadJournal.Add((_lookaheadCache.Count, (key, (lookahead.UID, foundIndex)), true));
        _lookaheadCache.Add((key, (lookahead.UID, foundIndex)));
    }
    private void ClearLookahead()
    {
        string key = StackKey();
        for (int i = _lookaheadCache.Count - 1; i >= 0; i--)
        {
            // strictly longer so it's only children
            if (_lookaheadCache[i].stack.Length > key.Length && _lookaheadCache[i].stack.StartsWith(key))
            {
                _lookaheadJournal.Add((i, _lookaheadCache[i], false));
                _lookaheadCache.RemoveAt(i);
            }
        }
    }
    public bool IsCached(Rule lookahead, int foundIndex)
    {
        string key = StackKey();
        foreach (var entry in _lookaheadCache)
        {
            if (key.StartsWith(entry.stack) && entry.signature.foundIndex == foundIndex && entry.signature.UID == lookahead.UID)
            {
                return true;
            }
        }
        return false;
    }
}

//! one step of delayed consumption: grow the match that produced a slack by exactly one repetition
//! pass, returning the new end state and the next step's slack. built by the repeat that owns the
//! slack and forwarded (re-framed) by references/anchors/optionals, so a failing following sibling
//! demands on the same path instead of re-running or searching alternatives.
readonly record struct ExtendResult(bool Ok, int Cursor, int ExpandLimit, int ExpandedBy, Tree? Tree, Slack Slack)
{
    public static ExtendResult Fail => new(false, 0, 0, 0, null, Slack.None);
}

//! Delayed expansion budget a match can still give back:
// Passes = extra repetitions a variable repeat can still take, Chars = characters a name/greedy
// token can still absorb, More = the one-step continuation above. Consumed lazily by the caller
// (and demanded one step at a time by a failing following sibling) instead of being spent up front.
readonly record struct Slack(int Passes, int Chars, Func<ExtendResult>? More = null)
{
    public static Slack None => new(0, 0);
    public bool HasPasses => More != null;
}

abstract class Checker
{
    protected string lastSearch = "";
    protected int lastIndex = -1;
    protected int lastLength = -1;
    public abstract bool IsMatch(string text);
    public abstract bool IsMatch(Span<char> text);
    public abstract bool IsMatch(ReadOnlySpan<char> text);
    public abstract bool ContainsMatch(string text);
    public abstract bool FullMatch(string text);
    public abstract int GetMatch(string text);
    public abstract int GetMatch(Span<char> text);
    public abstract int GetMatch(ReadOnlySpan<char> text);
    public abstract int GetMatchLength(string text);
}

class RegexChecker(string regexString) : Checker
{
    private Regex _regex = new(regexString);
    public override bool IsMatch(string text)
    {
        return _regex.IsMatch(text);
    }
    public override bool IsMatch(Span<char> text)
    {
        return _regex.IsMatch(text);
    }
    public override bool IsMatch(ReadOnlySpan<char> text)
    {
        return _regex.IsMatch(text);
    }

    public override bool ContainsMatch(string text) => IsMatch(text);
    public override bool FullMatch(string text)
    {
        return _regex.FullMatch(text);
    }
    public override int GetMatch(string text)
    {
        if (text == lastSearch) return lastIndex;
        var match = _regex.Match(text);
        lastSearch = text;
        lastIndex = match.Success ? match.Index : -1;
        lastLength = match.Success ? match.Length : -1;
        return match.Success ? match.Index : -1;
    }
    public override int GetMatch(Span<char> text)
    {
        if (text.CompareTo(lastSearch, StringComparison.Ordinal) == 0) return lastIndex;
        var match = _regex.Match(text.ToString());
        lastSearch = text.ToString();
        lastIndex = match.Success ? match.Index : -1;
        lastLength = match.Success ? match.Length : -1;
        return match.Success ? match.Index : -1;
    }
    public override int GetMatch(ReadOnlySpan<char> text)
    {
        if (text.CompareTo(lastSearch.AsSpan(), StringComparison.Ordinal) == 0) return lastIndex;
        var match = _regex.Match(text.ToString());
        lastSearch = text.ToString();
        lastIndex = match.Success ? match.Index : -1;
        lastLength = match.Success ? match.Length : -1;
        return match.Success ? match.Index : -1;
    }
    public override int GetMatchLength(string text)
    {
        if (text == lastSearch) return lastLength;
        var match = _regex.Match(text);
        lastSearch = text;
        lastIndex = match.Success ? match.Index : -1;
        lastLength = match.Success ? match.Length : -1;
        return match.Success ? match.Length : -1;
    }
}

class SimpleChecker(string checkString) : Checker
{
    public override bool IsMatch(string text)
    {
        return text.StartsWith(checkString);
    }
    public override bool IsMatch(Span<char> text)
    {
        if (text.Length < checkString.Length)
            return false;
        for (int i = 0; i < checkString.Length; i++)
        {
            if (text[i] != checkString[i])
                return false;
        }
        return true;
    }
    public override bool IsMatch(ReadOnlySpan<char> text)
    {
        if (text.Length < checkString.Length)
            return false;
        for (int i = 0; i < checkString.Length; i++)
        {
            if (text[i] != checkString[i])
                return false;
        }
        return true;
    }
    public override bool ContainsMatch(string text)
    {
        return text.Contains(checkString);
    }
    public override bool FullMatch(string text)
    {
        return text == checkString;
    }
    public override int GetMatch(string text)
    {
        if (text == lastSearch) return lastIndex;
        lastSearch = text;
        lastIndex = text.IndexOf(checkString);
        return lastIndex;
    }
    public override int GetMatch(Span<char> text)
    {
        if (text.CompareTo(lastSearch, StringComparison.Ordinal) == 0) return lastIndex;
        lastSearch = text.ToString();
        lastIndex = text.ToString().IndexOf(checkString);
        return lastIndex;
    }
    public override int GetMatch(ReadOnlySpan<char> text)
    {
        if (text.CompareTo(lastSearch.AsSpan(), StringComparison.Ordinal) == 0) return lastIndex;
        lastSearch = text.ToString();
        lastIndex = text.ToString().IndexOf(checkString);
        return lastIndex;
    }
    public override int GetMatchLength(string text)
    {
        return checkString.Length;
    }
}


class AntiCompute()
{
    private static int recursion_limit = 25;
    private static int recursion_repeat_limit = 5;
    public static void ApplySpecs(SpecStore specStore)
    {
        recursion_limit = int.Parse(specStore.GetSpecDefinition("recursion_limit", "25"));
        recursion_repeat_limit = int.Parse(specStore.GetSpecDefinition("recursion_repeat_limit", "5"));
    }
    public static bool Studying(Optimizations optimizations, Rule rule, int cursor)
    {
        if (optimizations.SearchStackDepth > recursion_limit)
        {
            return false;
        }
        if (optimizations.SearchStack.Contains((rule.UID, cursor)))
        {
            var stackList = optimizations.SearchStack.ToList(); // top-first order
            if (stackList.Count((v) => v.Item1 == rule.UID && v.Item2 == cursor) > recursion_repeat_limit)
            {
                return false;
            }
            int matchIndex = stackList.FindIndex(entry => entry.Item1 == rule.UID && entry.Item2 == cursor);
            List<(string, int)> sequence = stackList.Take(matchIndex).ToList();
            if (sequence.Count == 0)
            {
                return false;
            }
            if (sequence.Count >= optimizations.SearchStackDepth / 2)
            {
                optimizations.PushSearch(rule, cursor);
                return true;
            }
            var tail = stackList.Skip(matchIndex).ToList();
            bool hasRepeat = false;
            for (int i = 0; i + sequence.Count <= tail.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < sequence.Count; j++)
                {
                    if (tail[i + j].Item1 != sequence[j].Item1 || tail[i + j].Item2 != sequence[j].Item2)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    hasRepeat = true;
                    break;
                }
            }
            if (hasRepeat)
            {
                return false;
            }
            optimizations.PushSearch(rule, cursor);
            return true;
        }
            optimizations.PushSearch(rule, cursor);
            return true;
        }
        public static void Finished(Optimizations optimizations, Rule rule, int cursor)
        {
            if (optimizations.PeekSearch() != (rule.UID, cursor))
            {
                throw new InvalidOperationException("AntiCompute stack corrupted.");
            }
            optimizations.PopSearch();
        }
    }
    
class ParserV3
{
    public static Tree Parse(string code, Rule[] rules, Spec[] specs, MetricsCollector? metrics = null)
    {
        metrics?.Start();
        ReferenceStore referenceStore = new();
        if (string.IsNullOrEmpty(rules[0].UID))
        {
            rules = [.. rules.Select((r, i) => { r.UID = Guid.NewGuid().ToString(); return r; })];
        }
        foreach (var rule in rules)
        {
            referenceStore.AddIfReferenceable(rule);
        }
        SpecStore specStore = new SpecStore();
        foreach (var spec in specs)
        {
            if (spec.Target == "comment" || spec.Target == "comment_begin" || spec.Target == "comment_end" || spec.Target == "comment_escape")
            {
                continue; // skip comment specs
            }
            specStore.AddSpec(spec);
        }
        Logger.Log("--------------------------------------------------");
        Logger.Log(Serializer.DisplayRules(rules));
        Logger.Log("--------------------------------------------------");
        string cleanedCode = CleanSource(specs, code);
        Logger.Log("Cleaned source:\n" + cleanedCode);
        Logger.Log("--------------------------------------------------");
        Optimizations optimizations = new()
        {
            Metrics = metrics,
            ExplicitSpacing = specStore.GetSpecDefinition("spacing", "implicit").Trim().Equals("explicit", StringComparison.OrdinalIgnoreCase)
        };
        AntiCompute.ApplySpecs(specStore);
        try
        {
            return Parse(cleanedCode, rules, specStore, referenceStore, optimizations);
        }
        catch (StackOverflowException)
        {
            Logger.Error("Stack overflow during parsing. Possible infinite recursion in grammar rules. Search stack:\n" + string.Join(" -> ", optimizations.SearchStack.Select(r => $"{r.Item1} ({r.Item2})").Reverse()));
            throw;
        }
        catch (Exception)
        {
            Logger.Error("Exception during parsing. Search stack:\n" + string.Join(" -> ", optimizations.SearchStackResolved(rules).Select(r => $"{r.Item1.Name} ({r.Item2})").Reverse()));
            throw;
        }
    }
    static string CleanSource(Spec[] specs, string code)
    {
        // replace comment content with spaces, to keep line/column positions & lengths
        List<Checker> commentCheckers = [.. specs.Where(s => s.Target == "comment").Select(s => SpecStore.GenerateChecker(s))];
        List<Checker> beginCheckers = [.. specs.Where(s => s.Target == "comment_begin").Select(s => SpecStore.GenerateChecker(s))];
        List<Checker> endCheckers = [.. specs.Where(s => s.Target == "comment_end").Select(s => SpecStore.GenerateChecker(s))];
        List<Checker> escapeCheckers = [.. specs.Where(s => s.Target == "comment_escape").Select(s => SpecStore.GenerateChecker(s))];
        char[] result = [.. code];
        for (int i = 0; i < result.Length; i++)
        {
            while (i < result.Length && (result[i] == '\r' || result[i] == '\n'))
            {
                i++; // skip newlines
            }
            Checker? earliestMatcher = null;
            int earliestIndex = result.Length - i;
            bool lineMatch = false;
            foreach (var checker in commentCheckers)
            {
                var span = result.AsSpan(i);
                if (checker.IsMatch(span))
                {
                    int commentStart = checker.GetMatch(span);
                    if (commentStart >= earliestIndex)
                    {
                        continue;
                    }
                    if (SearchForEscape(result[i..(i + commentStart)], escapeCheckers))
                    {
                        continue; // skip this match, it's escaped
                    }
                    earliestMatcher = checker;
                    earliestIndex = commentStart;
                    lineMatch = true;
                }
            }
            int newLineIndex = result.AsSpan(i).IndexOfAny(['\r', '\n']);
            if (newLineIndex == -1)
                newLineIndex = result.Length - i;
            newLineIndex += i;
            foreach (var checker in beginCheckers)
            {
                var match = new string(result[i..newLineIndex]);
                if (checker is RegexChecker)
                {
                    throw new Exception("In the grammar it's a simple?");
                }
                if (checker.ContainsMatch(match))
                {
                    int beginMatch = checker.GetMatch(match);
                    if (beginMatch >= earliestIndex)
                    {
                        continue;
                    }
                    if (SearchForEscape(result[i..(i + beginMatch)], escapeCheckers))
                    {
                        continue; // skip this match, it's escaped
                    }
                    earliestMatcher = checker;
                    earliestIndex = beginMatch;
                    lineMatch = false;
                }
            }
            if (earliestMatcher != null)
            {
                if (lineMatch)
                {
                    for (int j = i + earliestIndex; j < newLineIndex; j++)
                    {
                        result[j] = ' ';
                    }
                    i = newLineIndex;
                    continue;
                }
                // multiline
                int k = i + earliestIndex;
                Checker? earliestEndChecker = null;
                string rest = new string(result[k..]);
                int earliestEndIndex = rest.Length;
                foreach (var checker in endCheckers)
                {
                    if (checker.ContainsMatch(rest))
                    {
                        int endMatch = checker.GetMatch(rest);
                        if (endMatch >= earliestEndIndex)
                        {
                            continue;
                        }
                        if (SearchForEscape(result[k..(k + endMatch)], escapeCheckers))
                        {
                            continue; // skip this match, it's escaped
                        }
                        earliestEndChecker = checker;
                        earliestEndIndex = endMatch;
                    }
                }
                int limit = earliestEndChecker != null ? k + earliestEndIndex + earliestEndChecker.GetMatchLength(rest) : result.Length;
                for (; k < limit; k++)
                {
                    result[k] = ' ';
                }
                i = limit;
            }
        }
        return new string(result);
    }
    static bool SearchForEscape(char[] text, List<Checker> escapeCheckers)
    {
        foreach (var checker in escapeCheckers)
        {
            for (int i = 0; i < text.Length; i++)
            {
                var span = text.AsSpan(i);
                if (checker.IsMatch(span))
                {
                    return true;
                }
            }
        }
        return false;
    }
    internal static Tree ExpandLastNode(Tree target, string code, int by = 1)
    {
        if (target.Children.Count == 0)
        {
            target.Token.Value = code[target.Token.Line..(target.Token.Line + target.Token.Value.Length + by)];
            Logger.Log($"Expanded node by {by} to: \"{target.Token.Value}\" (size {target.Token.Value.Length})", 1);
            return target;
        }
        var lastNode = target.Children[^1];
        target.Children[^1] = ExpandLastNode(lastNode, code, by);
        target.Token.Value = code[target.Token.Line..(target.Token.Line + target.Token.Value.Length + by)];
        return target;
    }

    //! re-frame a slack continuation for a reference: the extended tree is the referenced rule's, so it
    //! needs the same renaming/ignored treatment as the first match, without re-running that rule
    static Slack WrapReferenceSlack(Slack inner, Rule referenceRule, Reference reference)
    {
        if (inner.More == null)
        {
            return inner;
        }
        return inner with
        {
            More = () =>
            {
                var r = inner.More!();
                if (!r.Ok)
                {
                    return r;
                }
                Tree? tree = r.Tree;
                if (tree.HasValue)
                {
                    var val = tree.Value;
                    if (referenceRule.Ignored == true)
                    {
                        val.Token.Ignored = true;
                    }
                    if (reference.Value != null && !reference.Value.Contains('@'))
                    {
                        val.Token.Name = reference.Value;
                    }
                    tree = val;
                }
                return r with { Tree = tree, Slack = WrapReferenceSlack(r.Slack, referenceRule, reference) };
            }
        };
    }

    //! same for an optional: unwrap the temp group's single child and re-apply ignored
    static Slack WrapOptionalSlack(Slack inner, bool ignored)
    {
        if (inner.More == null)
        {
            return inner;
        }
        return inner with
        {
            More = () =>
            {
                var r = inner.More!();
                if (!r.Ok)
                {
                    return r;
                }
                Tree? tree = null;
                if (r.Tree.HasValue && r.Tree.Value.Children.Count > 0)
                {
                    tree = r.Tree.Value.Children[0];
                }
                if (ignored && tree.HasValue)
                {
                    var ct = tree.Value;
                    ct.Token.Ignored = true;
                    tree = ct;
                }
                return r with { Tree = tree, Slack = WrapOptionalSlack(r.Slack, ignored) };
            }
        };
    }

    //! same for a simple-with-children: re-prepend its literal span to the extended tree's token
    static Slack WrapSimpleSlack(Slack inner, string code, int line)
    {
        if (inner.More == null)
        {
            return inner;
        }
        return inner with
        {
            More = () =>
            {
                var r = inner.More!();
                if (!r.Ok)
                {
                    return r;
                }
                Tree? tree = r.Tree;
                if (tree.HasValue)
                {
                    var rt = tree.Value;
                    rt.Token.Value = code[line..r.Cursor] + rt.Token.Value;
                    rt.Token.Line = line;
                    tree = rt;
                }
                return r with { Tree = tree, Slack = WrapSimpleSlack(r.Slack, code, line) };
            }
        };
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
        List<Tree> newChildren = new(tree.Children.Count);
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
        //! clean in place: the result is this list (it becomes tree.Children), so no second list/iterator
        for (int i = 0; i < newChildren.Count; i++)
        {
            newChildren[i] = CleanIgnored(newChildren[i]);
        }
        return newChildren;
    }
    public static Tree Parse(string code, Rule[] rules, SpecStore specStore, ReferenceStore referenceStore, Optimizations optimizations)
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
            if (!optimizations.ExplicitSpacing && char.IsWhiteSpace(code[cursor]))
            {
                // Skip whitespace in-between rules
                cursor++;
                continue;
            }
            if (Match(code, cursor, rules, specStore, referenceStore, out Tree? foundTree, out int newCursor, out int matchExpandLimit, out Slack matchSlack, optimizations))
            {
                cursor = newCursor;
                expandLimit = matchExpandLimit;
                optimizations.DefinitiveProgression(cursor);
                if (foundTree == null)
                    continue;
                tree.Children.Add(foundTree.Value);
                //! consume the match's slack one step at a time: at top level nothing follows to demand
                //! it, so this loop drives the continuation until nothing more can be consumed
                Slack lastSlack = matchSlack;
                while (lastSlack.More != null)
                {
                    var extensionCheckpoint = optimizations.Mark();
                    var extended = lastSlack.More();
                    if (extended.Ok && extended.Tree.HasValue && extended.Cursor > cursor)
                    {
                        cursor = extended.Cursor;
                        tree.Children[^1] = extended.Tree.Value;
                        expandLimit = extended.ExpandLimit;
                        lastSlack = extended.Slack;
                        optimizations.DefinitiveProgression(cursor);
                    }
                    else
                    {
                        optimizations.Rollback(extensionCheckpoint);
                        break;
                    }
                }
            }
            else
            {
                if (!optimizations.ExplicitSpacing && char.IsWhiteSpace(code[cursor]))
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
        Logger.Log($"Completed parsing at cursor {cursor}/{code.Length}");
        var addressedTree = WithCoordinates(tree);
        addressedTree = CleanIgnored(addressedTree);
        // Logger.Log($"Parsed tree: {Serializer.StringSerializeTree(addressedTree)}", depth);
        return addressedTree;
    }


    static bool Match(string code, int cursor, Rule[] rules, SpecStore specStore, ReferenceStore referenceStore, out Tree? foundTree, out int newCursor, out int expandLimit, out Slack slack, Optimizations optimizations)
    {
        newCursor = cursor; // No match found, return the original 
        slack = Slack.None;
        foreach (var rule in rules)
        {
            // check if current call stack is near stack overflow
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                throw new StackOverflowException("Stack overflow during parsing. Possible infinite recursion in grammar rules.");
            }
            if (Check(code, cursor, 0, rule, specStore, referenceStore, out int ruleCursor, out Tree? ruleTree, out expandLimit, out int expandedBy, out slack, optimizations))
            {
                Logger.Log($"Matched rule {rule} {rule.Name} at cursor: {cursor} size: {cursor - ruleCursor}");
                newCursor = ruleCursor;
                foundTree = ruleTree;
                optimizations.DefinitiveProgression(cursor);
                //! expanded by is unused, as the expandLimit input is always 0 here
                return true;
            }
            Logger.Log($"Failed to match rule {rule} {rule.Name} at cursor: {cursor}");
        }
        expandLimit = 0;
        slack = Slack.None;
        foundTree = null; // No match found
        return false; // No match found
    }

    //! instrumentation wrapper: only pays for metrics when a collector is attached, otherwise it
    //! forwards straight to the parser body
    internal static bool Check(string code, int cursor, int expandAllowed, Rule rule, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, out Slack slack, Optimizations optimizations, int depth = 0)
    {
        //! the scope this rule runs in decides whitespace skipping; resolve the rule's own modifier
        //! against the enclosing scope, and restore it afterwards so siblings are unaffected
        bool previousSpacing = optimizations.ExplicitSpacing;
        optimizations.ExplicitSpacing = ResolveSpacing(rule, previousSpacing);
        try
        {
            var metrics = optimizations.Metrics;
            if (metrics == null)
            {
                return CheckCore(code, cursor, expandAllowed, rule, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, optimizations, depth);
            }
            var frame = metrics.Enter(rule);
            bool matched = false;
            try
            {
                matched = CheckCore(code, cursor, expandAllowed, rule, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, optimizations, depth);
                return matched;
            }
            finally
            {
                metrics.Exit(frame, matched);
            }
        }
        finally
        {
            optimizations.ExplicitSpacing = previousSpacing;
        }
    }

    static bool ResolveSpacing(Rule rule, bool ambient) => rule.Spacing switch
    {
        SpacingMode.Explicit => true,
        SpacingMode.Implicit => false,
        _ => ambient
    };

    internal static bool CheckCore(string code, int cursor, int expandAllowed, Rule rule, SpecStore specStore, ReferenceStore referenceStore, out int newCursor, out Tree? ruleTree, out int expandLimit, out int expandedBy, out Slack slack, Optimizations optimizations, int depth = 0)
    {
        newCursor = cursor;
        expandLimit = 0;
        slack = Slack.None;
        switch (rule)
        {
            case Anchor anchor:
                // 0 width rule
                expandedBy = 0;
                if (anchor.Children.Length == 0)
                {
                    ruleTree = null;
                    return true;
                }
                bool res = GroupParser.CheckGroup(code, cursor, expandAllowed, anchor, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, depth, specStore, referenceStore, optimizations);
                if (res) {
                    Logger.Log($"Anchor matched: {anchor.Name} at cursor {cursor}->{newCursor} +{expandLimit}: ({ruleTree?.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                }
                return res;
            case Optional optional:
                Logger.Log("> " + optional.Name + " (" + optional.Value + ")", depth);
                var tempGroup = new Group((1, 1), false) // workaround to reuse the expansion code
                {
                    Children = [optional.Children[0]]
                };
                for (int i = 0; i < optional.Children.Length; i++)
                {
                    tempGroup.Name = optional.Name + "_" + i;
                    tempGroup.Children[0] = optional.Children[i]; // only child set here to avoid reallocation
                    if (GroupParser.CheckGroup(code, cursor, expandAllowed, tempGroup, out newCursor, out var optionalRuleTree, out expandLimit, out expandedBy, out slack, depth + 1, specStore, referenceStore, optimizations))
                    {
                        Logger.Log($"Optional matched without expansion: {optional.Name} ({optional.Value}) at cursor {cursor}, for {optional.Children[i].Name} ({optional.Children[i].Value}): ({optionalRuleTree?.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                        ruleTree = optionalRuleTree?.Children[0] ?? null; // unwrap the group
                        Logger.Log($"Ignored status: {optional.Ignored}", depth);
                        if (optional.Ignored && ruleTree.HasValue)
                        {
                            var ct = ruleTree.Value;
                            ct.Token.Ignored = true;
                            ruleTree = ct;
                        }
                        slack = WrapOptionalSlack(slack, optional.Ignored);
                        return true;
                    }
                    else
                    {
                        Logger.Log($"Optional not matched: {optional.Name} ({optional.Value}) at cursor {cursor} | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                    }
                }
                expandLimit = expandAllowed;
                newCursor = cursor;
                expandedBy = 0;
                slack = Slack.None;
                ruleTree = null;
                return false;
            case Simple simple:
                if (simple.Value == null)
                {
                    throw new Exception("Empty simple rule");
                }
                expandedBy = 0;
                slack = Slack.None;
                for (int i = 0; i < simple.Value.Length; i++)
                {
                    if (cursor + i >= code.Length || code[cursor + i] != simple.Value[i])
                    {
                        ruleTree = null;
                        return false;
                    }
                }
                newCursor += simple.Value.Length;
                if (simple.Children.Length > 0)
                {
                    // turn the simple token into a group
                    Group fakeGroup = new((1, 1), simple.Name, simple.Ignored, simple.Children)
                    {
                        UID = simple.UID // preserve UID for optimizations
                    };
                    bool groupSuccess = GroupParser.CheckGroup(code, newCursor, 0, fakeGroup, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, depth + 1, specStore, referenceStore, optimizations);
                    if (!groupSuccess) {
                        ruleTree = null;
                        return false; // if the group doesn't match, the simple doesn't match, even if the string does
                    }
                    if (ruleTree.HasValue)
                    {
                        // update token of the wider group to include simple
                        var rt = ruleTree.Value;
                        rt.Token.Value = code[cursor..newCursor] + rt.Token.Value;
                        rt.Token.Line = cursor;
                        ruleTree = rt;
                    }
                    slack = WrapSimpleSlack(slack, code, cursor);
                } else {
                    ruleTree = new Tree(new Token(simple.Name, simple.Value, cursor, newCursor, simple.Ignored), []);
                }
                Logger.Log($"Simple matched: {simple.Name} ({simple.Value}) at cursor {cursor}->{newCursor}: ({ruleTree?.Token.Value}) | ({code[cursor..Math.Min(cursor + 5, code.Length)]})", depth);
                return true;
            case Reserved reserved:
                if (reserved.Children.Length > 0)
                {
                    throw new Exception("Reserved rules with children are not supported in this version of the parser.");
                }
                expandedBy = 0;
                if (cursor >= code.Length)
                {
                    ruleTree = null;
                    return false;
                }
                switch (reserved.Name)
                {
                    case "space":
                        if (code[cursor] == ' ')
                        {
                            newCursor = cursor + 1;
                            ruleTree = new Tree(new Token("space", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                        ruleTree = null;
                        return false;
                    case "tab":
                        if (code[cursor] == '\t')
                        {
                            newCursor = cursor + 1;
                            ruleTree = new Tree(new Token("tab", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                        ruleTree = null;
                        return false;
                    case "newline":
                        //! a single raw line-break character; `\r\n` is two matches, repeat the token for a run
                        if (code[cursor] == '\n' || code[cursor] == '\r')
                        {
                            newCursor = cursor + 1;
                            ruleTree = new Tree(new Token("newline", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                        ruleTree = null;
                        return false;
                    case "whitespace":
                        {
                            //! greedy by default: `$whitespace:` backs the match, the default `\s+` eats the
                            //! whole run. A single-character regex (e.g. `[ \t]`) makes it non-greedy.
                            Checker whitespaceRgx = specStore.GetSpec("whitespace", @"\s+");
                            int whitespaceEnd = cursor;
                            while (whitespaceEnd < code.Length && whitespaceRgx.FullMatch(code[cursor..(whitespaceEnd + 1)]))
                            {
                                whitespaceEnd++;
                            }
                            if (whitespaceEnd == cursor)
                            {
                                ruleTree = null;
                                return false;
                            }
                            newCursor = whitespaceEnd;
                            ruleTree = new Tree(new Token("whitespace", code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                    case "name":
                        if (char.IsWhiteSpace(code[cursor]))
                        {
                            ruleTree = null;
                            return false;
                        }
                        Checker nameRgx = specStore.GetSpec("name", @"[^\s]*");
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
                        slack = new Slack(0, expandLimit);
                        Logger.Log($"Name regex match: {nameRgx} against '{code[cursor..(newCursor + expandLimit)]}' with expand limit {expandLimit}");
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
                        slack = new Slack(0, expandLimit);
                        return true;
                    case "digit":
                        Checker digitRgx = specStore.GetSpec("digit", @"[0-9]");
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
                            Checker customRgx = specStore.GetSpec(reserved.Name, @".");
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
                            slack = new Slack(0, expandLimit);
                            ruleTree = new Tree(new Token(reserved.Name, code[cursor..newCursor], cursor, newCursor, reserved.Ignored), []);
                            return true;
                        }
                        throw new NotSupportedException($"Reserved rule '{reserved.Name}' is not supported. If it's a custom spec, make sure it is defined.");
                }
            case Group group:
                return GroupParser.CheckGroup(code, cursor, expandAllowed, group, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, depth, specStore, referenceStore, optimizations);
            case Reference reference:
                if (reference.Children.Length > 0)
                {
                    throw new Exception("References with children are not supported in this version of the parser.");
                }
                var referenceRule = referenceStore.GetReference(reference.Name);
                Tree? ruleOutput;
                try
                {
                    RuntimeHelpers.EnsureSufficientExecutionStack();
                }
                catch (InsufficientExecutionStackException)
                {
                    throw new StackOverflowException("Stack overflow during parsing. Possible infinite recursion in grammar rules.");
                }
                expandedBy = 0;
                if (optimizations.IsInvalidated(referenceRule, cursor))
                {
                    ruleTree = null;
                    return false; // avoids doing the extra work of computing the rule again at this cursor
                }
                if (!AntiCompute.Studying(optimizations, referenceRule, cursor))
                {
                    Logger.Warning($"Detected circular recursion on rule {reference.Name} at cursor {cursor}, aborting this path to avoid infinite recursion.", depth);
                    ruleTree = null;
                    return false; // already studied this rule at this cursor, avoid infinite recursion
                }
                Logger.Log($"@> Reference {reference.Name} to {referenceRule.Name}", depth);
                var cacheCheckpoint = optimizations.Mark();
                bool success = Check(code, cursor, expandAllowed, referenceRule, specStore, referenceStore, out newCursor, out ruleOutput, out expandLimit, out expandedBy, out Slack innerSlack, optimizations, depth + 1);
                bool checkFailed = !success;
                slack = innerSlack;
                if (!success && reference.Repeat.Item1 == 0)
                {
                    success = true;
                    ruleOutput = null;
                    slack = Slack.None;
                }
                slack = WrapReferenceSlack(slack, referenceRule, reference);
                if (ruleOutput.HasValue)
                {
                    var val = ruleOutput.Value;
                    if (referenceRule.Ignored == true)
                        val.Token.Ignored = true;
                    if (reference.Value != null && !reference.Value.Contains('@'))
                        val.Token.Name = reference.Value;
                    ruleOutput = val;
                }
                if (checkFailed)
                {
                    optimizations.Rollback(cacheCheckpoint); // drop the failed attempt's speculative entries
                }
                AntiCompute.Finished(optimizations, referenceRule, cursor);
                if (checkFailed)
                {
                    optimizations.Invalidate(referenceRule, cursor);
                }
                Logger.Log($"{referenceRule.Name}{(ruleOutput != null && ruleOutput.Value.Token.Name != referenceRule.Name ? $" ({ruleOutput.Value.Token.Name})" : "")} {(success ? "matched" : "not matched")} at cursor {cursor}->{newCursor}", depth);
                ruleTree = ruleOutput;
                return success;
            case Lookahead lookahead:
                // zero-width rule
                // it isn't a regex lookahead, it looks forward for a specific string no matter what stands before it,
                // it's meant for grammar optimization, to avoid extra expensive processing if a simple lookup can disqualify
                // a rule ahead of time
                int endSearch = code.IndexOf('\n', cursor);
                int foundIndex;
                expandedBy = 0;
                if (lookahead.Optional)
                {
                    foundIndex = -1;
                    foreach(var child in lookahead.Children)
                    {
                        if (child.Value == null)
                        {
                            continue;
                        }
                        foundIndex = LookaheadLookup(code, cursor, endSearch == -1 ? code.Length : endSearch, lookahead, optimizations, depth);
                        if (foundIndex != -1) break;
                    }
                }
                else
                {
                    foundIndex = LookaheadLookup(code, cursor, endSearch == -1 ? code.Length : endSearch, lookahead, optimizations, depth);
                }
                Logger.Log($"> Lookahead {lookahead.Name} ({lookahead.Value}) on {code[cursor..(foundIndex == -1 ? code.Length : foundIndex)]}", depth);
                if (foundIndex != -1)
                {
                    ruleTree = null;
                    expandLimit = expandAllowed;
                    return true;
                }
                ruleTree = null;
                return false;
            default:
                throw new NotSupportedException($"Rule type {rule.GetType().Name} is not supported.");
        }
    }
    static int LookaheadLookup(string code, int startIndex, int endIndex, Rule lookahead, Optimizations optimizations, int depth)
    {
        int index = -1;
        while (index == -1 && startIndex < endIndex)
        {
            index = code.IndexOf(lookahead.Value!, startIndex, endIndex - startIndex);
            if (index == -1) return -1;
            if (optimizations.IsCached(lookahead, index))
            {
                startIndex = index + 1; // skip this match and look for the next one
                index = -1;
                continue;
            }
            optimizations.CacheLookahead(lookahead, index, depth);
        }
        return index;
    }
}