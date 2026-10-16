using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ClemToken;

//! Symbol count a rule can expand to. Infinity marks an unbounded high bound
//! (unbounded repeat or reference recursion).
public readonly record struct SymbolBound(long Low, long High)
{
    public const long Infinity = long.MaxValue;
    public bool InfiniteHigh => High >= Infinity;
    public bool InfiniteLow => Low >= Infinity;
    public override string ToString()
    {
        string lo = InfiniteLow ? "inf" : Low.ToString();
        string hi = InfiniteHigh ? "inf" : High.ToString();
        return $"{lo}..{hi}";
    }
}

public sealed class RuleGrammarStats
{
    public string Name { get; internal set; } = "";
    public string Type { get; internal set; } = "";
    public string? Value { get; internal set; }
    public SymbolBound Bound { get; internal set; }
    //! on a reference cycle, so its expansion can recurse forever
    public bool Recursive { get; internal set; }
    //! no terminating derivation (low bound is infinite)
    public bool Unproductive { get; internal set; }
}

public sealed class GrammarStats
{
    //! version of the JSON contract. Bump on a breaking change (renamed/removed/retyped field);
    //! additive fields keep the version, mirroring the syntax-token contract.
    public const int SchemaVersion = 1;

    public int RuleCount { get; internal set; }
    public int SpecCount { get; internal set; }
    public int SimpleCount { get; internal set; }
    public int ReservedCount { get; internal set; }
    public int GroupCount { get; internal set; }
    public int ReferenceCount { get; internal set; }
    public int AnchorCount { get; internal set; }
    public int OptionalCount { get; internal set; }
    public int LookaheadCount { get; internal set; }
    public int EmptyCount { get; internal set; }
    public int TotalNodes { get; internal set; }
    public IReadOnlyList<RuleGrammarStats> Rules { get; internal set; } = [];
    public SymbolBound Total { get; internal set; }

    public void WriteTo(TextWriter writer)
    {
        writer.WriteLine($"Rules: {RuleCount}   Specs: {SpecCount}");
        writer.WriteLine($"Nodes: {TotalNodes} (Simple {SimpleCount}, Reserved {ReservedCount}, Group {GroupCount}, Reference {ReferenceCount}, Anchor {AnchorCount}, Optional {OptionalCount}, Lookahead {LookaheadCount}, Empty {EmptyCount})");
        writer.WriteLine();
        int nameWidth = 8;
        foreach (var r in Rules)
        {
            nameWidth = Math.Max(nameWidth, Math.Min((r.Name ?? "").Length, 30));
        }
        writer.WriteLine($"{Pad("Rule", nameWidth)}  {"Type",-10}  {"Bound",-14}  {"Value",-24}  Flags");
        foreach (var r in Rules)
        {
            var flags = new List<string>();
            if (r.Recursive) flags.Add("recursive");
            if (r.Unproductive) flags.Add("unproductive");
            string value = r.Value == null ? "" : Short(r.Value, 24);
            writer.WriteLine($"{Pad(r.Name, nameWidth)}  {r.Type,-10}  {r.Bound.ToString(),-14}  {value,-24}  {string.Join(", ", flags)}");
        }
        writer.WriteLine($"{Pad("TOTAL", nameWidth)}  {"",-10}  {Total.ToString(),-14}");
    }

    internal static string Pad(string? text, int width)
    {
        text ??= "";
        if (text.Length > width)
        {
            return text[..Math.Max(0, width - 1)] + "~";
        }
        return text.PadRight(width);
    }

    internal static string Short(string? text, int width)
    {
        text ??= "";
        return text.Length > width ? text[..Math.Max(0, width - 1)] + "~" : text;
    }

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"schema\": \"clemtoken.grammarStats\",\n");
        sb.Append($"  \"version\": {SchemaVersion},\n");
        sb.Append($"  \"ruleCount\": {RuleCount},\n");
        sb.Append($"  \"specCount\": {SpecCount},\n");
        sb.Append($"  \"counts\": {{ \"simple\": {SimpleCount}, \"reserved\": {ReservedCount}, \"group\": {GroupCount}, \"reference\": {ReferenceCount}, \"anchor\": {AnchorCount}, \"optional\": {OptionalCount}, \"lookahead\": {LookaheadCount}, \"empty\": {EmptyCount}, \"nodes\": {TotalNodes} }},\n");
        sb.Append($"  \"total\": {BoundJson(Total)},\n");
        sb.Append("  \"rules\": [\n");
        for (int i = 0; i < Rules.Count; i++)
        {
            var r = Rules[i];
            sb.Append("    {");
            sb.Append($" \"name\": \"{StatFormat.Json(r.Name)}\", \"type\": \"{StatFormat.Json(r.Type)}\", \"value\": {(r.Value == null ? "null" : "\"" + StatFormat.Json(r.Value) + "\"")}, ");
            sb.Append($"\"low\": {Num(r.Bound.Low)}, \"high\": {Num(r.Bound.High)}, \"recursive\": {(r.Recursive ? "true" : "false")}, \"unproductive\": {(r.Unproductive ? "true" : "false")} }}");
            sb.Append(i < Rules.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string BoundJson(SymbolBound b) => $"{{ \"low\": {Num(b.Low)}, \"high\": {Num(b.High)} }}";
    private static string Num(long value) => value >= SymbolBound.Infinity ? "null" : value.ToString();
}

public sealed class RuleMetrics
{
    //! position in ParseMetrics.Rules; the stable identity a parse-tree edge points at
    public int Index { get; internal set; }
    public string Name { get; internal set; } = "";
    public string Type { get; internal set; } = "";
    public string? Value { get; internal set; }
    public long Executions { get; internal set; }
    public long Matches { get; internal set; }
    public long Failures { get; internal set; }
    //! inclusive over every execution span of this rule (nested rules included)
    public long AllocatedBytes { get; internal set; }
    //! exclusive: this rule minus its direct children
    public long SelfAllocatedBytes { get; internal set; }
    public TimeSpan TotalTime { get; internal set; }
    public TimeSpan SelfTime { get; internal set; }
}

//! One node of the merged call tree a flame graph lays out. Occurrences that share the same call
//! path (same ancestors, same rule) merge into one node, so a node's children are exactly the calls
//! made inside it and a child's inclusive figures never exceed its parent's. `Rule` indexes
//! ParseMetrics.Rules; the root has -1.
public sealed class CallNode
{
    public int Index { get; internal set; }
    //! parent node index, or -1 for the root
    public int Parent { get; internal set; }
    //! rule index into ParseMetrics.Rules, or -1 for the synthetic root
    public int Rule { get; internal set; }
    public long Executions { get; internal set; }
    public long Matches { get; internal set; }
    public long Failures { get; internal set; }
    //! inclusive over this node's whole subtree
    public long AllocatedBytes { get; internal set; }
    public TimeSpan TotalTime { get; internal set; }
    //! raw stopwatch ticks, resolved to TotalTime at Build; not serialized
    internal long Ticks;
}

public sealed class ParseMetrics
{
    //! version of the JSON contract. Bump on a breaking change (renamed/removed/retyped field);
    //! additive fields keep the version, mirroring the syntax-token contract.
    public const int SchemaVersion = 1;

    //! version of the `parseTree` contract (`-stats parse tree json`), which carries the call tree
    public const int TreeSchemaVersion = 1;

    public long TotalAllocatedBytes { get; internal set; }
    public TimeSpan TotalElapsed { get; internal set; }
    //! allocations outside any rule (source cleaning, coordinate assignment, tree cleanup, ...)
    public long UnattributedAllocatedBytes { get; internal set; }
    public TimeSpan UnattributedTime { get; internal set; }
    public IReadOnlyList<RuleMetrics> Rules { get; internal set; } = [];
    //! merged call tree for the flame graph (`-stats parse tree json`); node 0 is the root
    public IReadOnlyList<CallNode> Tree { get; internal set; } = [];

    public void WriteTo(TextWriter writer)
    {
        int nameWidth = 8;
        foreach (var r in Rules)
        {
            nameWidth = Math.Max(nameWidth, Math.Min((r.Name ?? "").Length, 30));
        }
        writer.WriteLine($"{GrammarStats.Pad("Rule", nameWidth)}  {"Type",-10}  {"Exec",9}  {"Match",9}  {"Fail",9}  {"Alloc (incl / self)",-24}  {"Time (incl / self)",-22}  Value");
        foreach (var r in Rules)
        {
            string alloc = StatFormat.Bytes(r.AllocatedBytes) + " / " + StatFormat.Bytes(r.SelfAllocatedBytes);
            string time = StatFormat.Duration(r.TotalTime) + " / " + StatFormat.Duration(r.SelfTime);
            string value = r.Value == null ? "" : GrammarStats.Short(r.Value, 24);
            writer.WriteLine($"{GrammarStats.Pad(r.Name ?? "", nameWidth)}  {r.Type,-10}  {r.Executions,9}  {r.Matches,9}  {r.Failures,9}  {alloc,-24}  {time,-22}  {value}");
        }
        writer.WriteLine($"{GrammarStats.Pad("Other (non-rule)", nameWidth)}  {"",-10}  {"",9}  {"",9}  {"",9}  {StatFormat.Bytes(UnattributedAllocatedBytes),-24}  {StatFormat.Duration(UnattributedTime),-22}");
        writer.WriteLine($"{GrammarStats.Pad("TOTAL", nameWidth)}  {"",-10}  {"",9}  {"",9}  {"",9}  {StatFormat.Bytes(TotalAllocatedBytes),-24}  {StatFormat.Duration(TotalElapsed),-22}");
    }

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"schema\": \"clemtoken.parseMetrics\",\n");
        sb.Append($"  \"version\": {SchemaVersion},\n");
        sb.Append($"  \"totalAllocatedBytes\": {TotalAllocatedBytes},\n");
        sb.Append($"  \"totalElapsedMs\": {TotalElapsed.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)},\n");
        sb.Append($"  \"unattributedAllocatedBytes\": {UnattributedAllocatedBytes},\n");
        sb.Append($"  \"unattributedElapsedMs\": {UnattributedTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)},\n");
        sb.Append("  \"rules\": [\n");
        for (int i = 0; i < Rules.Count; i++)
        {
            var r = Rules[i];
            sb.Append("    {");
            sb.Append($" \"name\": \"{StatFormat.Json(r.Name)}\", \"type\": \"{StatFormat.Json(r.Type)}\", \"value\": {(r.Value == null ? "null" : "\"" + StatFormat.Json(r.Value) + "\"")}, ");
            sb.Append($"\"executions\": {r.Executions}, \"matches\": {r.Matches}, \"failures\": {r.Failures}, ");
            sb.Append($"\"allocatedBytes\": {r.AllocatedBytes}, \"selfAllocatedBytes\": {r.SelfAllocatedBytes}, ");
            sb.Append($"\"totalMs\": {r.TotalTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}, \"selfMs\": {r.SelfTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} }}");
            sb.Append(i < Rules.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    //! `parseTree` payload: the flat table plus the merged call tree a flame graph lays out.
    //! Kept a separate schema so the plain `parseMetrics` contract is unchanged.
    public string ToTreeJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"schema\": \"clemtoken.parseTree\",\n");
        sb.Append($"  \"version\": {TreeSchemaVersion},\n");
        sb.Append($"  \"totalAllocatedBytes\": {TotalAllocatedBytes},\n");
        sb.Append($"  \"totalElapsedMs\": {TotalElapsed.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)},\n");
        sb.Append($"  \"unattributedAllocatedBytes\": {UnattributedAllocatedBytes},\n");
        sb.Append($"  \"unattributedElapsedMs\": {UnattributedTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)},\n");
        sb.Append("  \"rules\": [\n");
        for (int i = 0; i < Rules.Count; i++)
        {
            var r = Rules[i];
            sb.Append("    {");
            sb.Append($" \"id\": {i}, \"name\": \"{StatFormat.Json(r.Name)}\", \"type\": \"{StatFormat.Json(r.Type)}\", \"value\": {(r.Value == null ? "null" : "\"" + StatFormat.Json(r.Value) + "\"")}, ");
            sb.Append($"\"executions\": {r.Executions}, \"matches\": {r.Matches}, \"failures\": {r.Failures}, ");
            sb.Append($"\"allocatedBytes\": {r.AllocatedBytes}, \"selfAllocatedBytes\": {r.SelfAllocatedBytes}, ");
            sb.Append($"\"totalMs\": {r.TotalTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}, \"selfMs\": {r.SelfTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} }}");
            sb.Append(i < Rules.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n");
        sb.Append("  \"tree\": [\n");
        for (int i = 0; i < Tree.Count; i++)
        {
            var n = Tree[i];
            sb.Append("    {");
            sb.Append($" \"id\": {i}, \"parent\": {n.Parent}, \"rule\": {n.Rule}, ");
            sb.Append($"\"executions\": {n.Executions}, \"matches\": {n.Matches}, \"failures\": {n.Failures}, ");
            sb.Append($"\"allocatedBytes\": {n.AllocatedBytes}, ");
            sb.Append($"\"totalMs\": {n.TotalTime.TotalMilliseconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} }}");
            sb.Append(i < Tree.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }
}

internal static class StatFormat
{
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        } while (value >= 1024 && unit < units.Length - 1);
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Duration(TimeSpan time)
    {
        double ms = time.TotalMilliseconds;
        if (ms >= 1000) return (ms / 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s";
        if (ms >= 1) return ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " ms";
        return time.TotalMicroseconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " us";
    }

    //! complete JSON string escaping: quotes, backslashes, every control character, and lone
    //! surrogates (written as the replacement character). Token values carry arbitrary source text,
    //! so anything less can emit invalid JSON or throw on a UTF-8 write.
    public static string Json(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        StringBuilder sb = new(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        sb.Append(c).Append(text[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        sb.Append("\\ufffd");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}

//! Static analysis of a grammar: per-rule symbol-count bounds plus construct counts. No input needed.
internal static class GrammarAnalyzer
{
    private const long INF = SymbolBound.Infinity;

    public static GrammarStats Analyze(Rule[] rules, Spec[] specs)
    {
        int n = rules.Length;
        var anchors = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            if (rules[i] is Anchor anchorRule && !anchors.ContainsKey(anchorRule.Name))
            {
                anchors[anchorRule.Name] = i;
            }
        }

        //! references only ever resolve to top-level anchors (ReferenceStore adds anchors only),
        //! so the recursion graph lives entirely over the top-level rule array
        var edges = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            edges[i] = [];
            CollectReferences(rules[i], anchors, edges[i]);
        }

        var recursive = new bool[n];
        for (int i = 0; i < n; i++)
        {
            recursive[i] = CanReach(edges, i, i);
        }

        //! low bound: least-symbol derivation, solved downward from infinity. A rule with no
        //! terminating derivation stays infinite (unproductive).
        var low = new long[n];
        for (int i = 0; i < n; i++) low[i] = INF;
        //! allocated up front so reference lookups can index both arrays in either pass (only the
        //! component relevant to the current pass is read)
        var high = new long[n];
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < n; i++)
            {
                long value = BoundNode(rules[i], low, high, anchors).Low;
                if (value < low[i])
                {
                    low[i] = value;
                    changed = true;
                }
            }
        }

        //! high bound: greatest-symbol derivation, solved upward from zero. Recursive rules are
        //! pinned to infinity so pure cycles terminate.
        bool changedHigh = true;
        while (changedHigh)
        {
            changedHigh = false;
            for (int i = 0; i < n; i++)
            {
                long value = recursive[i] ? INF : BoundNode(rules[i], low, high, anchors).High;
                if (value > high[i])
                {
                    high[i] = value;
                    changedHigh = true;
                }
            }
        }

        var stats = new GrammarStats
        {
            RuleCount = n,
            SpecCount = specs.Length
        };
        int simple = 0, reserved = 0, group = 0, reference = 0, anchor = 0, optional = 0, lookahead = 0, empty = 0, nodes = 0;
        var ruleStats = new List<RuleGrammarStats>(n);
        for (int i = 0; i < n; i++)
        {
            CountNodes(rules[i], ref simple, ref reserved, ref group, ref reference, ref anchor, ref optional, ref lookahead, ref empty, ref nodes);
            ruleStats.Add(new RuleGrammarStats
            {
                Name = string.IsNullOrEmpty(rules[i].Name) ? (rules[i].Value ?? rules[i].GetType().Name) : rules[i].Name,
                Type = rules[i].GetType().Name,
                Value = rules[i].Value,
                Bound = new SymbolBound(low[i], high[i]),
                Recursive = recursive[i],
                Unproductive = low[i] >= INF
            });
        }
        long totalLow = 0, totalHigh = 0;
        for (int i = 0; i < n; i++)
        {
            totalLow = Add(totalLow, low[i]);
            totalHigh = Add(totalHigh, high[i]);
        }
        stats.SimpleCount = simple;
        stats.ReservedCount = reserved;
        stats.GroupCount = group;
        stats.ReferenceCount = reference;
        stats.AnchorCount = anchor;
        stats.OptionalCount = optional;
        stats.LookaheadCount = lookahead;
        stats.EmptyCount = empty;
        stats.TotalNodes = nodes;
        stats.Rules = ruleStats;
        stats.Total = new SymbolBound(totalLow, totalHigh);
        return stats;
    }

    private static void CollectReferences(Rule rule, Dictionary<string, int> anchors, List<int> into)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        if (rule is Reference r)
        {
            if (anchors.TryGetValue(r.Name, out int index))
            {
                into.Add(index);
            }
        }
        foreach (var child in rule.Children)
        {
            CollectReferences(child, anchors, into);
        }
    }

    private static bool CanReach(List<int>[] edges, int start, int target)
    {
        var visited = new bool[edges.Length];
        var stack = new Stack<int>();
        foreach (var next in edges[start]) stack.Push(next);
        while (stack.Count > 0)
        {
            int node = stack.Pop();
            if (node == target) return true;
            if (visited[node]) continue;
            visited[node] = true;
            foreach (var next in edges[node]) stack.Push(next);
        }
        return false;
    }

    //! symbols a rule expands to. Leaf = 1, sequence sums, repeat multiplies, optional floors at 0,
    //! lookahead is zero-width, reference defers to the referenced rule.
    private static SymbolBound BoundNode(Rule rule, long[] low, long[] high, Dictionary<string, int> anchors)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        switch (rule)
        {
            case Lookahead:
            case Empty:
                return new SymbolBound(0, 0);
            case Reserved:
                return new SymbolBound(1, 1);
            case Simple:
            {
                long l = 1, h = 1;
                foreach (var child in rule.Children)
                {
                    var b = BoundNode(child, low, high, anchors);
                    l = Add(l, b.Low);
                    h = Add(h, b.High);
                }
                return new SymbolBound(l, h);
            }
            case Reference r:
                if (anchors.TryGetValue(r.Name, out int index))
                {
                    return new SymbolBound(low[index], high[index]);
                }
                return new SymbolBound(0, 0);
            case Optional:
            {
                long h = 0;
                foreach (var child in rule.Children)
                {
                    h = Math.Max(h, BoundNode(child, low, high, anchors).High);
                }
                return new SymbolBound(0, h);
            }
            case Anchor:
            case Group:
            default:
            {
                long l = 0, h = 0;
                foreach (var child in rule.Children)
                {
                    var b = BoundNode(child, low, high, anchors);
                    l = Add(l, b.Low);
                    h = Add(h, b.High);
                }
                if (rule is Group group)
                {
                    return Scale(group.Repeat, l, h);
                }
                return new SymbolBound(l, h);
            }
        }
    }

    private static SymbolBound Scale((int, int) repeat, long rawLow, long rawHigh)
    {
        int min = repeat.Item1;
        int max = repeat.Item2;
        long low = Mul(min, rawLow);
        long high = max == -1 ? (rawHigh == 0 ? 0 : INF) : Mul(max, rawHigh);
        return new SymbolBound(low, high);
    }

    private static long Add(long a, long b)
    {
        if (a >= INF || b >= INF) return INF;
        long sum = a + b;
        return sum < 0 ? INF : sum;
    }

    private static long Mul(long a, long b)
    {
        if (a == 0 || b == 0) return 0;
        if (a >= INF || b >= INF) return INF;
        if (a > INF / b) return INF;
        return a * b;
    }

    private static void CountNodes(Rule rule, ref int simple, ref int reserved, ref int group, ref int reference, ref int anchor, ref int optional, ref int lookahead, ref int empty, ref int nodes)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        nodes++;
        switch (rule)
        {
            case Simple: simple++; break;
            case Reserved: reserved++; break;
            case Group: group++; break;
            case Reference: reference++; break;
            case Anchor: anchor++; break;
            case Optional: optional++; break;
            case Lookahead: lookahead++; break;
            case Empty: empty++; break;
        }
        foreach (var child in rule.Children)
        {
            CountNodes(child, ref simple, ref reserved, ref group, ref reference, ref anchor, ref optional, ref lookahead, ref empty, ref nodes);
        }
    }
}

//! Runtime attribution of allocations/executions to rules during a single parse. Threaded through
//! Optimizations so concurrent parses stay independent. Enter/Exit nest; each rule's self figure is
//! its inclusive span minus the direct children that ran inside it.
internal sealed class MetricsCollector
{
    internal sealed class Frame
    {
        public Rule Rule = null!;
        //! the call-tree node this execution landed on, or null once the node cap is hit
        public CallNode? Node;
        public long StartAlloc;
        public long StartTs;
        public long ChildAlloc;
        public long ChildTs;
    }

    //! call-tree nodes are bounded so a pathological grammar cannot blow up the payload without
    //! limit; past the cap deeper calls simply fold into their parent's self figures.
    private const int MaxNodes = 200000;

    private readonly Stack<Frame> _stack = new();
    //! keyed by rule identity: only top-level rules get a UID, nested children keep a null one
    private readonly Dictionary<Rule, RuleMetrics> _byRule = new();
    private readonly List<RuleMetrics> _order = [];
    //! the merged call tree, node 0 is the root; `_children` finds a node by (parent, rule)
    private readonly List<CallNode> _nodes = [];
    private readonly Dictionary<CallNode, Dictionary<Rule, CallNode>> _children = new();
    private readonly CallNode _root = new() { Index = 0, Parent = -1, Rule = -1 };
    private long _startAlloc;
    private long _startTs;
    private long _rootAlloc;
    private long _rootTs;

    public MetricsCollector()
    {
        _nodes.Add(_root);
    }

    public void Start()
    {
        _startAlloc = GC.GetAllocatedBytesForCurrentThread();
        _startTs = Stopwatch.GetTimestamp();
    }

    internal Frame Enter(Rule rule)
    {
        var frame = new Frame
        {
            Rule = rule,
            StartAlloc = GC.GetAllocatedBytesForCurrentThread(),
            StartTs = Stopwatch.GetTimestamp()
        };
        //! merge by (parent node, rule): the parent node already encodes the whole call path, so
        //! occurrences reached the same way share a node and the children stay context-correct
        var parent = _stack.Count > 0 ? _stack.Peek().Node : _root;
        frame.Node = parent == null ? null : ChildNode(parent, rule);
        if (frame.Node != null)
        {
            frame.Node.Executions++;
        }
        ForRule(rule).Executions++;
        _stack.Push(frame);
        return frame;
    }

    private CallNode? ChildNode(CallNode parent, Rule rule)
    {
        if (!_children.TryGetValue(parent, out var byRule))
        {
            byRule = new Dictionary<Rule, CallNode>();
            _children[parent] = byRule;
        }
        if (byRule.TryGetValue(rule, out var node))
        {
            return node;
        }
        if (_nodes.Count >= MaxNodes)
        {
            return null;
        }
        node = new CallNode { Index = _nodes.Count, Parent = parent.Index, Rule = ForRule(rule).Index };
        byRule[rule] = node;
        _nodes.Add(node);
        return node;
    }

    internal void Exit(Frame frame, bool matched)
    {
        long endAlloc = GC.GetAllocatedBytesForCurrentThread();
        long endTs = Stopwatch.GetTimestamp();
        _stack.Pop();
        long inclusiveAlloc = endAlloc - frame.StartAlloc;
        long inclusiveTs = endTs - frame.StartTs;
        var metrics = ForRule(frame.Rule);
        metrics.AllocatedBytes += inclusiveAlloc;
        metrics.SelfAllocatedBytes += inclusiveAlloc - frame.ChildAlloc;
        metrics.TotalTime += TicksToTime(inclusiveTs);
        metrics.SelfTime += TicksToTime(inclusiveTs - frame.ChildTs);
        if (matched) metrics.Matches++;
        else metrics.Failures++;
        if (frame.Node != null)
        {
            frame.Node.AllocatedBytes += inclusiveAlloc;
            frame.Node.Ticks += inclusiveTs;
            if (matched) frame.Node.Matches++;
            else frame.Node.Failures++;
        }
        if (_stack.Count > 0)
        {
            var parent = _stack.Peek();
            parent.ChildAlloc += inclusiveAlloc;
            parent.ChildTs += inclusiveTs;
        }
        else
        {
            _rootAlloc += inclusiveAlloc;
            _rootTs += inclusiveTs;
        }
    }

    private RuleMetrics ForRule(Rule rule)
    {
        if (_byRule.TryGetValue(rule, out var metrics))
        {
            return metrics;
        }
        string name = string.IsNullOrEmpty(rule.Name) ? (rule.Value ?? rule.GetType().Name) : rule.Name;
        metrics = new RuleMetrics { Index = _order.Count, Name = name, Type = rule.GetType().Name, Value = rule.Value };
        _byRule[rule] = metrics;
        _order.Add(metrics);
        return metrics;
    }

    public ParseMetrics Build()
    {
        long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - _startAlloc;
        long totalTs = Stopwatch.GetTimestamp() - _startTs;
        foreach (var node in _nodes)
        {
            node.TotalTime = TicksToTime(node.Ticks);
        }
        return new ParseMetrics
        {
            Rules = _order,
            Tree = _nodes,
            TotalAllocatedBytes = totalAlloc,
            TotalElapsed = TicksToTime(totalTs),
            UnattributedAllocatedBytes = Math.Max(0, totalAlloc - _rootAlloc),
            UnattributedTime = TicksToTime(Math.Max(0, totalTs - _rootTs))
        };
    }

    private static TimeSpan TicksToTime(long ticks) => TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
}
