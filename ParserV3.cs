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

    //! global spacing mode: when true, implicit whitespace skipping only consumes horizontal
    //! whitespace, so a line break is significant. A line-oriented grammar (e.g. the DSL) uses this
    //! to keep a rule body from running past the end of its line. Set from the `$spacing` spec.
    public bool HorizontalSpacing;

    // whether `c` may be skipped as inter-token whitespace under the current spacing mode
    public bool IsSkippableWhitespace(char c)
        => char.IsWhiteSpace(c) && !(HorizontalSpacing && (c == '\n' || c == '\r'));

    //! furthest cursor any successful match reached during the current top-level match attempt. Error
    //! recovery uses it as the end of a failed region: a lower-priority root whose match stops short of
    //! it is only a fragment of a higher-priority rule that got further, so the whole region becomes one
    //! Error instead of a run of tiny matches. Speculative failures report nothing, so a whitespace or
    //! comment stretch probed and abandoned cannot inflate the region. Reset at the start of every Match.
    public int FurthestReached;
    public void ReportReached(int cursor)
    {
        if (cursor > FurthestReached)
        {
            FurthestReached = cursor;
        }
    }

    //! Retroactive cache journal
    // A demanded repeat re-run happens after the demanded node already returned, so the caches it
    // wrote during the lower-demand attempt are still live. Rolling back to a checkpoint taken
    // before that attempt (then recomputing at the higher demand) is what keeps them from shadowing
    // the new attempt. Entries are only final once DefinitiveProgression commits them.
    public readonly record struct Checkpoint(int Invalidated, int Lookahead, int Stack);

    private List<((string UID, int Cursor, bool Explicit, int Expand) key, bool hadPrevious, int previousDemand)> _invalidatedJournal = [];
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
        List<(string UID, int Cursor, bool Explicit, int Expand)> staleInvalidated = [];
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
    //! context signals that make a failure non-reusable: a recursion-guard abort, or a lookahead-cache
    //! read/write, both make a rule's outcome depend on parse state rather than position alone
    public int GuardAborts;
    public int LookaheadWrites;
    public int LookaheadSkips;

    //! Preemptive invalidation
    // Stores the *minimal* demand at which (rule, cursor) is known to fail. Failure is monotone in
    // demand (requiring more passes is strictly harder), so a failure recorded at demand d is safely
    // reusable for any demand >= d. The spacing mode is part of the key: a rule that inherits spacing
    // matches differently under an explicit vs implicit scope, so a failure only counts in the same one.
    private Dictionary<(string UID, int Cursor, bool Explicit, int Expand), int> _invalidated = new();
    private (string UID, int Cursor, bool Explicit, int Expand) InvalidationKey(Rule rule, int cursor, int expandAllowed) => (rule.UID, cursor, ExplicitSpacing, expandAllowed);
    public void Invalidate(Rule rule, int cursor, int expandAllowed, int demand = 0)
    {
        var key = InvalidationKey(rule, cursor, expandAllowed);
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
    public void InvalidatePermanent(Rule rule, int cursor, int expandAllowed, int demand = 0)
    {
        var key = InvalidationKey(rule, cursor, expandAllowed);
        if (_invalidated.TryGetValue(key, out var existing) && existing <= demand)
        {
            return;
        }
        _invalidated[key] = demand;
    }
    public bool IsInvalidated(Rule rule, int cursor, int expandAllowed, int demand = 0)
    {
        return _invalidated.TryGetValue(InvalidationKey(rule, cursor, expandAllowed), out var failingDemand) && failingDemand <= demand;
    }
    //! Lookahead uniqueness
    private List<(string stack, (string UID, int foundIndex) signature)> _lookaheadCache = [];
    public void CacheLookahead(Rule lookahead, int foundIndex, int depth)
    {
        string key = StackKey();
        Logger.Log($"Caching lookahead: {lookahead.Name} at index {foundIndex} with stack {key}", depth);
        _lookaheadJournal.Add((_lookaheadCache.Count, (key, (lookahead.UID, foundIndex)), true));
        _lookaheadCache.Add((key, (lookahead.UID, foundIndex)));
        LookaheadWrites++;
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
    public abstract bool IsMatch(string text, int start);
    public abstract int GetMatch(string text, int start);
    public abstract int GetMatchLength(string text);
    //! length of the match when it is required to start at index 0 of `text`, or -1 when nothing
    //! matches there. A plain unanchored probe assumes a one-character minimum, so a spec whose
    //! shortest match is longer (e.g. `\\[\[\]()]`) would never be attempted
    public abstract int MatchAtStartLength(ReadOnlySpan<char> text);
}

class RegexChecker(string regexString) : Checker
{
    private Regex _regex = new(regexString);
    public override int MatchAtStartLength(ReadOnlySpan<char> text)
    {
        //! the enumerator yields the leftmost match without materializing it, so an index of 0 means
        //! a match starts exactly at the cursor (a match there would always be the leftmost)
        foreach (var match in _regex.EnumerateMatches(text))
        {
            return match.Index == 0 ? match.Length : -1;
        }
        return -1;
    }
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

    public override bool IsMatch(string text, int start)
    {
        return _regex.IsMatch(text, start);
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
    public override int GetMatch(string text, int start)
    {
        var match = _regex.Match(text, start);
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
    public override int MatchAtStartLength(ReadOnlySpan<char> text)
    {
        if (text.Length < checkString.Length)
            return -1;
        for (int i = 0; i < checkString.Length; i++)
        {
            if (text[i] != checkString[i])
                return -1;
        }
        return checkString.Length;
    }
    public override bool IsMatch(string text)
    {
        return text.StartsWith(checkString);
    }
    public override bool IsMatch(string text, int start)
    {
        return text.IndexOf(checkString, start) >= 0;
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
    public override int GetMatch(string text, int start)
    {
        return text.IndexOf(checkString, start);
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
            optimizations.GuardAborts++;
            return false;
        }
        if (optimizations.SearchStack.Contains((rule.UID, cursor)))
        {
            var stackList = optimizations.SearchStack.ToList(); // top-first order
            if (stackList.Count((v) => v.Item1 == rule.UID && v.Item2 == cursor) > recursion_repeat_limit)
            {
                optimizations.GuardAborts++;
                return false;
            }
            int matchIndex = stackList.FindIndex(entry => entry.Item1 == rule.UID && entry.Item2 == cursor);
            List<(string, int)> sequence = stackList.Take(matchIndex).ToList();
            if (sequence.Count == 0)
            {
                optimizations.GuardAborts++;
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
                optimizations.GuardAborts++;
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
        //! comment cleaning is length-preserving, so offsets survive it; resolve coordinates against
        //! the original source so multi-line comments don't collapse the line numbers after them
        int[] lineStarts = BuildLineStarts(code);
        string spacingMode = specStore.GetSpecDefinition("spacing", "implicit").Trim();
        Optimizations optimizations = new()
        {
            Metrics = metrics,
            ExplicitSpacing = spacingMode.Equals("explicit", StringComparison.OrdinalIgnoreCase),
            HorizontalSpacing = spacingMode.Equals("horizontal", StringComparison.OrdinalIgnoreCase)
        };
        AntiCompute.ApplySpecs(specStore);
        try
        {
            return Parse(cleanedCode, rules, specStore, referenceStore, optimizations, lineStarts);
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
        int cursor = 0;
        while (cursor < result.Length)
        {
            while (cursor < result.Length && (result[cursor] == '\r' || result[cursor] == '\n'))
            {
                cursor++; // skip newlines
            }
            if (cursor >= result.Length)
            {
                break;
            }

            int lineStart = cursor;
            int lineEnd = result.AsSpan(lineStart).IndexOfAny(['\r', '\n']);
            lineEnd = lineEnd == -1 ? result.Length : lineStart + lineEnd;
            string line = new(result, lineStart, lineEnd - lineStart);
            int searchFrom = 0;

            while (true)
            {
                Checker? earliestMatcher = null;
                int earliestIndex = int.MaxValue;
                bool lineMatch = false;
                foreach (var checker in commentCheckers)
                {
                    //! `line` starts at the real beginning of the line, so a `^` in the
                    //! comment spec means line start instead of the current scan offset.
                    int commentStart = FindUnescaped(checker, line, result, lineStart, escapeCheckers, searchFrom);
                    if (commentStart < 0 || commentStart >= earliestIndex)
                    {
                        continue;
                    }
                    earliestMatcher = checker;
                    earliestIndex = commentStart;
                    lineMatch = true;
                }
                foreach (var checker in beginCheckers)
                {
                    if (checker is RegexChecker)
                    {
                        throw new Exception("In the grammar it's a simple?");
                    }
                    int beginMatch = FindUnescaped(checker, line, result, lineStart, escapeCheckers, searchFrom);
                    if (beginMatch < 0 || beginMatch >= earliestIndex)
                    {
                        continue;
                    }
                    earliestMatcher = checker;
                    earliestIndex = beginMatch;
                    lineMatch = false;
                }

                if (earliestMatcher == null)
                {
                    cursor = lineEnd;
                    break;
                }

                if (lineMatch)
                {
                    for (int j = lineStart + earliestIndex; j < lineEnd; j++)
                    {
                        result[j] = ' ';
                    }
                    cursor = lineEnd;
                    break;
                }

                // multiline
                int k = lineStart + earliestIndex;
                Checker? earliestEndChecker = null;
                string rest = new(result, k, result.Length - k);
                int earliestEndIndex = int.MaxValue;
                foreach (var checker in endCheckers)
                {
                    int endMatch = FindUnescaped(checker, rest, result, k, escapeCheckers);
                    if (endMatch < 0 || endMatch >= earliestEndIndex)
                    {
                        continue;
                    }
                    earliestEndChecker = checker;
                    earliestEndIndex = endMatch;
                }
                int limit = earliestEndChecker != null
                    ? k + earliestEndIndex + earliestEndChecker.GetMatchLength(rest)
                    : result.Length;
                for (; k < limit; k++)
                {
                    result[k] = ' ';
                }
                cursor = limit;
                if (cursor >= lineEnd)
                {
                    break;
                }

                //! the block comment ended on this line; keep the real line start so a
                //! later `//` is not mistaken for a line-leading comment
                line = new(result, lineStart, lineEnd - lineStart);
                searchFrom = cursor - lineStart;
            }
        }
        return new string(result);
    }

    //! first match of `checker` in `text` that is not escaped, or -1. `offset` is
    //! the source position of `text[0]`, used to inspect the text before the match.
    static int FindUnescaped(Checker checker, string text, char[] source, int offset, List<Checker> escapeCheckers, int searchFrom = 0)
    {
        int start = searchFrom;
        while (start <= text.Length)
        {
            if (!checker.IsMatch(text, start))
            {
                return -1;
            }
            int index = checker.GetMatch(text, start);
            if (index < 0)
            {
                return -1;
            }
            if (!SearchForEscape(source[offset..(offset + index)], escapeCheckers))
            {
                return index;
            }
            start = index + 1;
        }
        return -1;
    }
    // offsets of the first character of each line; index 0 is always 0 and one entry is added after
    // every '\n', so a binary search yields the 1-based (line, column) of any offset
    internal static int[] BuildLineStarts(string code)
    {
        List<int> starts = [0];
        for (int i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }
        return [.. starts];
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
            target.Token.Value = code[target.Token.Start..(target.Token.Start + target.Token.Value.Length + by)];
            target.Token.End = target.Token.Start + target.Token.Value.Length;
            Logger.Log($"Expanded node by {by} to: \"{target.Token.Value}\" (size {target.Token.Value.Length})", 1);
            return target;
        }
        var lastNode = target.Children[^1];
        target.Children[^1] = ExpandLastNode(lastNode, code, by);
        target.Token.Value = code[target.Token.Start..(target.Token.Start + target.Token.Value.Length + by)];
        target.Token.End = target.Token.Start + target.Token.Value.Length;
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
                    rt.Token.Start = line;
                    rt.Token.End = r.Cursor;
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
    public static Tree Parse(string code, Rule[] rules, SpecStore specStore, ReferenceStore referenceStore, Optimizations optimizations, int[]? lineStarts = null)
    {
        var tree = new Tree(new Token("Root", "", 0, 0, false), []);
        lineStarts ??= BuildLineStarts(code);
        int cursor = 0;
        //! resolve any character offset to a 1-based (line, column). Indices are not visited in
        //! increasing order (a parent is addressed after its children) and comment cleaning keeps
        //! offsets but not lines, so a binary search over precomputed line starts is both correct
        //! and order-independent.
        (int, int) GetCoordinates(int index)
        {
            if (index < 0) index = 0;
            else if (index > code.Length) index = code.Length;
            int lo = 0;
            int hi = lineStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (lineStarts[mid] <= index) lo = mid;
                else hi = mid - 1;
            }
            return (lo + 1, index - lineStarts[lo] + 1);
        }
        Tree WithCoordinates(Tree tree)
        {
            for (int i = 0; i < tree.Children.Count; i++)
            {
                tree.Children[i] = WithCoordinates(tree.Children[i]);
            }
            var (line, column) = GetCoordinates(tree.Token.Start);
            var (endLine, endColumn) = GetCoordinates(tree.Token.End);
            tree.Token.Line = line;
            tree.Token.Column = column;
            tree.Token.EndLine = endLine;
            tree.Token.EndColumn = endColumn;
            return tree;
        }
        //! record one Error covering [start, end); merge it with an immediately preceding Error so a failed
        //! region becomes a single token rather than a run of one-character ones
        void AddErrorSpan(int start, int end)
        {
            if (end <= start)
            {
                end = start + 1;
            }
            if (end > code.Length)
            {
                end = code.Length;
            }
            if (tree.Children.Count > 0)
            {
                var last = tree.Children[^1];
                if (last.Token.Name == "Error" && last.Token.End == start)
                {
                    last.Token.Value = code[last.Token.Start..end];
                    last.Token.End = end;
                    tree.Children[^1] = last;
                    return;
                }
            }
            tree.Children.Add(new Tree(new Token("Error", code[start..end], start, end, false), []));
        }
        int expandLimit = 0;
        while (cursor < code.Length)
        {
            if (!optimizations.ExplicitSpacing && optimizations.IsSkippableWhitespace(code[cursor]))
            {
                // Skip whitespace in-between rules
                cursor++;
                continue;
            }
            if (Match(code, cursor, rules, specStore, referenceStore, out Tree? foundTree, out int newCursor, out int matchExpandLimit, out Slack matchSlack, optimizations))
            {
                Tree? currentTree = foundTree;
                int matchedCursor = newCursor;
                //! a name-like token can still absorb the rest of its run; take it now instead of letting a
                //! following root match split it into one-character fragments
                if (matchExpandLimit > 0 && currentTree.HasValue)
                {
                    currentTree = ExpandLastNode(currentTree.Value, code, matchExpandLimit);
                    matchedCursor += matchExpandLimit;
                    matchExpandLimit = 0;
                }
                int matchedExpandLimit = matchExpandLimit;
                //! consume the match's slack one step at a time: at top level nothing follows to demand
                //! it, so this loop drives the continuation until nothing more can be consumed. It runs
                //! before the fragment test below so a lazy match that can still reach that far is kept.
                var slackCheckpoint = optimizations.Mark();
                Slack lastSlack = matchSlack;
                while (lastSlack.More != null)
                {
                    var extensionCheckpoint = optimizations.Mark();
                    var extended = lastSlack.More();
                    if (extended.Ok && extended.Tree.HasValue && extended.Cursor > matchedCursor)
                    {
                        matchedCursor = extended.Cursor;
                        currentTree = extended.Tree;
                        matchedExpandLimit = extended.ExpandLimit;
                        lastSlack = extended.Slack;
                    }
                    else
                    {
                        optimizations.Rollback(extensionCheckpoint);
                        break;
                    }
                }
                //! a higher-priority rule consumed further before failing: this match is only a fragment
                //! of it, so report the whole failed region as one Error and skip past it instead
                if (matchedCursor < optimizations.FurthestReached)
                {
                    optimizations.Rollback(slackCheckpoint);
                    AddErrorSpan(cursor, optimizations.FurthestReached);
                    cursor = optimizations.FurthestReached;
                    expandLimit = 0;
                    optimizations.DefinitiveProgression(cursor);
                    continue;
                }
                cursor = matchedCursor;
                expandLimit = matchedExpandLimit;
                optimizations.DefinitiveProgression(cursor);
                if (currentTree != null)
                {
                    tree.Children.Add(currentTree.Value);
                }
            }
            else
            {
                if (!optimizations.ExplicitSpacing && optimizations.IsSkippableWhitespace(code[cursor]))
                {
                    expandLimit = 0; // Reset expand limit on whitespace
                    while (cursor < code.Length && optimizations.IsSkippableWhitespace(code[cursor]))
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
                    int failureEnd = optimizations.FurthestReached > cursor ? optimizations.FurthestReached : cursor + 1;
                    AddErrorSpan(cursor, failureEnd);
                    cursor = failureEnd; // skip the whole failed region
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
        optimizations.FurthestReached = cursor; // reset the reached region for this position
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
                //! Check keeps only the reached region of failing rules, so FurthestReached here is how
                //! far a higher-priority rule got before this one matched
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
        //! a successful rule's speculative dead ends must not extend the reached region: keep what a
        //! rule consumed only when the rule itself fails, so FurthestReached follows the failing path
        int savedReached = optimizations.FurthestReached;
        try
        {
            var metrics = optimizations.Metrics;
            bool matched = false;
            if (metrics == null)
            {
                matched = CheckCore(code, cursor, expandAllowed, rule, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, optimizations, depth);
            }
            else
            {
                var frame = metrics.Enter(rule);
                try
                {
                    matched = CheckCore(code, cursor, expandAllowed, rule, specStore, referenceStore, out newCursor, out ruleTree, out expandLimit, out expandedBy, out slack, optimizations, depth);
                }
                finally
                {
                    metrics.Exit(frame, matched);
                }
            }
            if (matched)
            {
                optimizations.FurthestReached = savedReached;
            }
            return matched;
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
                    Logger.Log($"Anchor matched: {anchor.Name} at cursor {cursor}->{newCursor} +{expandLimit}: ({ruleTree?.Token.Value}) | ({code.SafeSlice(cursor, cursor + 5)})", depth);
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
                        Logger.Log($"Optional matched without expansion: {optional.Name} ({optional.Value}) at cursor {cursor}, for {optional.Children[i].Name} ({optional.Children[i].Value}): ({optionalRuleTree?.Token.Value}) | ({code.SafeSlice(cursor, cursor + 5)})", depth);
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
                        Logger.Log($"Optional not matched: {optional.Name} ({optional.Value}) at cursor {cursor} | ({code.SafeSlice(cursor, cursor + 5)})", depth);
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
                        rt.Token.Start = cursor;
                        rt.Token.End = newCursor;
                        ruleTree = rt;
                    }
                    slack = WrapSimpleSlack(slack, code, cursor);
                } else {
                    ruleTree = new Tree(new Token(simple.Name, simple.Value, cursor, newCursor, simple.Ignored), []);
                }
                Logger.Log($"Simple matched: {simple.Name} ({simple.Value}) at cursor {cursor}->{newCursor}: ({ruleTree?.Token.Value}) | ({code.SafeSlice(cursor, cursor + 5)})", depth);
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
                        Logger.Log($"Name regex match: {nameRgx} against '{code.SafeSlice(cursor, newCursor + expandLimit)}' with expand limit {expandLimit}");
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
                            int matched = customRgx.MatchAtStartLength(code.AsSpan(cursor));
                            if (matched <= 0)
                            {
                                ruleTree = null;
                                return false;
                            }
                            int maxCursor = cursor + matched;
                            //! an anchored match is greedy, but a shorter alternation can win (e.g. `a|ab`),
                            //! so keep growing while the whole prefix still matches
                            while (maxCursor < code.Length && customRgx.FullMatch(code[cursor..(maxCursor + 1)]))
                            {
                                maxCursor++;
                            }
                            //! custom specs take their maximal match up front. Unlike the built-in `name`
                            //! (which starts at one char and is extended by a demanding sibling), a custom
                            //! regex is a self-contained shape, so a terminal use still consumes the whole
                            //! token instead of leaking its tail into the next match.
                            newCursor = maxCursor;
                            expandLimit = 0;
                            slack = new Slack(0, 0);
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
                if (optimizations.IsInvalidated(referenceRule, cursor, expandAllowed))
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
                int guardAbortsBefore = optimizations.GuardAborts;
                int lookaheadWritesBefore = optimizations.LookaheadWrites;
                int lookaheadSkipsBefore = optimizations.LookaheadSkips;
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
                    if (optimizations.GuardAborts == guardAbortsBefore
                        && optimizations.LookaheadWrites == lookaheadWritesBefore
                        && optimizations.LookaheadSkips == lookaheadSkipsBefore)
                    {
                        optimizations.InvalidatePermanent(referenceRule, cursor, expandAllowed);
                    }
                    else
                    {
                        optimizations.Invalidate(referenceRule, cursor, expandAllowed);
                    }
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
                        foundIndex = LookaheadLookup(code, cursor, endSearch == -1 ? code.Length : endSearch, lookahead, child.Value, optimizations, depth);
                        if (foundIndex != -1) break;
                    }
                }
                else
                {
                    foundIndex = LookaheadLookup(code, cursor, endSearch == -1 ? code.Length : endSearch, lookahead, optimizations, depth);
                }
                Logger.Log($"> Lookahead {lookahead.Name} ({lookahead.Value}) on {code.SafeSlice(cursor, foundIndex == -1 ? code.Length : foundIndex)}", depth);
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
    //! an optional lookahead holds each alternative in its children, so the searched literal differs
    //! from the joined Value; the parent is kept as the cache identity while the value is passed in
    static int LookaheadLookup(string code, int startIndex, int endIndex, Rule lookahead, Optimizations optimizations, int depth)
        => LookaheadLookup(code, startIndex, endIndex, lookahead, lookahead.Value!, optimizations, depth);
    static int LookaheadLookup(string code, int startIndex, int endIndex, Rule lookahead, string value, Optimizations optimizations, int depth)
    {
        int index = -1;
        while (index == -1 && startIndex < endIndex)
        {
            index = code.IndexOf(value, startIndex, endIndex - startIndex);
            if (index == -1) return -1;
            if (optimizations.IsCached(lookahead, index))
            {
                optimizations.LookaheadSkips++;
                startIndex = index + 1; // skip this match and look for the next one
                index = -1;
                continue;
            }
            optimizations.CacheLookahead(lookahead, index, depth);
        }
        return index;
    }
}