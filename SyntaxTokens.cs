using System.Text;

namespace ClemToken;

// one highlightable terminal of a parsed source file. `Rule` is the nearest enclosing grammar rule
// (the highlight-kind taxonomy: the same terminal means different things under different rules);
// `Leaf` is the terminal's own rule/spec name. Positions are 0-based and LSP-shaped
// (`Line`/`Character` in UTF-16 units) so a consumer can build a range without re-deriving
// coordinates from absolute offsets.
internal readonly record struct SyntaxToken(string? Rule, string Leaf, string Value, int Line, int Character, int Length);

// Parser-driven token stream for the editor/extension contract. Pure: no input- or run-specific state.
internal static class SyntaxTokens
{
    //! version of the JSON contract. Bump on a breaking change (renamed/removed/retyped field);
    //! additive fields keep the version.
    public const int SchemaVersion = 1;

    public static List<SyntaxToken> Collect(Tree root, Rule[] rules)
    {
        // the grammar's rule names are the taxonomy; a tree node is a "rule" iff its name is one of
        // them, which is how an intermediate terminal (a spec like `word`) is told from its context
        HashSet<string> ruleNames = new(rules.Length);
        foreach (var rule in rules)
        {
            ruleNames.Add(rule.Name);
        }
        List<SyntaxToken> tokens = [];
        List<string> path = [];
        Walk(root, ruleNames, path, tokens);
        return tokens;
    }

    // depth-first, source order. Terminals are emitted; the nearest enclosing rule rides along.
    static void Walk(Tree tree, HashSet<string> ruleNames, List<string> path, List<SyntaxToken> tokens)
    {
        if (tree.Children.Count == 0)
        {
            tokens.Add(new SyntaxToken(
                path.Count > 0 ? path[^1] : null,
                tree.Token.Name,
                tree.Token.Value,
                tree.Token.Line - 1,
                tree.Token.Column - 1,
                tree.Token.End - tree.Token.Start));
            return;
        }
        bool declared = ruleNames.Contains(tree.Token.Name);
        if (declared)
        {
            path.Add(tree.Token.Name);
        }
        foreach (var child in tree.Children)
        {
            Walk(child, ruleNames, path, tokens);
        }
        if (declared)
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    public static string ToJson(IReadOnlyList<SyntaxToken> tokens)
    {
        StringBuilder sb = new();
        sb.Append("{\n");
        sb.Append("  \"schema\": \"clemtoken.tokens\",\n");
        sb.Append("  \"version\": ").Append(SchemaVersion).Append(",\n");
        sb.Append("  \"tokens\": [");
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    { \"rule\": ");
            sb.Append(t.Rule == null ? "null" : "\"" + StatFormat.Json(t.Rule) + "\"");
            sb.Append(", \"leaf\": \"").Append(StatFormat.Json(t.Leaf)).Append('"');
            sb.Append(", \"value\": \"").Append(StatFormat.Json(t.Value)).Append('"');
            sb.Append(", \"line\": ").Append(t.Line);
            sb.Append(", \"character\": ").Append(t.Character);
            sb.Append(", \"length\": ").Append(t.Length);
            sb.Append(" }");
        }
        sb.Append(tokens.Count == 0 ? "]\n}\n" : "\n  ]\n}\n");
        return sb.ToString();
    }
}
