namespace ClemToken
{
    public struct DecidingResult
    {
        public DecidingType Type;
        public string Message;
        public int Priority;
        public int Cursor;
        public DecidingResult(DecidingType type, string message, int priority, int cursor)
        {
            Type = type;
            Message = message;
            Priority = priority;
            Cursor = cursor;
        }
    }
    public enum DecidingType
    {
        Fail,
        Match,
        PartialMatch,
        Skip,
        Open
    }
    class Decider(int cursor = 0)
    {
        private int _cursor = cursor;
        private Decider? _subDecider = null;
        public void Set(int cursor = 0)
        {
            _cursor = cursor;
        }
        public DecidingResult CheckRule(Rule rule, string stream)
        {
            if (rule.IsReserved)
            {
                switch (rule.Name)
                {
                    case "name":
                        if (!char.IsWhiteSpace(stream[_cursor]))
                        {
                            _cursor++;
                            if (_cursor >= stream.Length && !char.IsWhiteSpace(stream[_cursor]))
                            {
                                return new DecidingResult(DecidingType.PartialMatch, "", 10, _cursor);
                            }
                            else
                            {
                                return new DecidingResult(DecidingType.Match, "", 20, _cursor);
                            }
                        }
                        else
                        {
                            return new DecidingResult(DecidingType.Fail, "Expected name", 0, _cursor);
                        }
                    default:
                        return new DecidingResult(DecidingType.Fail, $"Unimplemented reserved rule: {rule.Name}", 9999, _cursor);
                }
            }
            switch (rule)
            {
                case Anchor:
                    return new DecidingResult(DecidingType.Skip, "", 1, _cursor);
                case Group group:
                    if(_subDecider != null)
                        _subDecider.Set(_cursor);
                    else
                        _subDecider = new Decider(_cursor);
                    var res = _subDecider.CheckRule(group.Children[_cursor], stream);
                    switch (res.Type)
                    {
                        case DecidingType.Match:
                            _cursor++;
                            if (_cursor < group.Children.Count())
                            {
                                return new DecidingResult(DecidingType.PartialMatch, "", res.Priority + 5, _cursor);
                            }
                            else
                            {
                                return new DecidingResult(DecidingType.Match, "", res.Priority + 10, _cursor);
                            }
                        case DecidingType.PartialMatch:
                            return new DecidingResult(DecidingType.PartialMatch, "", res.Priority + 2, _cursor);
                        case DecidingType.Fail:
                            if (rule.Repeat.Item1 == 0)
                            {
                                return new DecidingResult(DecidingType.Skip, "", 1, _cursor);
                            }
                            return new DecidingResult(DecidingType.Fail, $"Expected {group.Name}", 0, _cursor);
                        case DecidingType.Skip:
                            if (_cursor < group.Children.Count())
                            {
                                return new DecidingResult(DecidingType.PartialMatch, "", res.Priority + 1, _cursor);
                            }
                            else
                            {
                                return new DecidingResult(DecidingType.Match, "", res.Priority + 2, _cursor);
                            }
                        case DecidingType.Open:
                            return new DecidingResult(DecidingType.Open, group.Name, 5, _cursor);
                        default:
                            return new DecidingResult(DecidingType.Fail, $"Unimplemented group rule type for rule {rule.Name}", 9999, _cursor);
                    }
                case Reference:
                    return new DecidingResult(DecidingType.Open, rule.Name, 2, _cursor);
                case Simple simple:
                    if (simple.Value?[0] == stream[_cursor])
                    {
                        _cursor++;
                        if (_cursor < simple.Value.Length)
                        {
                            return new DecidingResult(DecidingType.PartialMatch, "", 10, _cursor);
                        }
                        else
                        {
                            return new DecidingResult(DecidingType.Match, "", 20, _cursor);
                        }
                    }
                    else
                    {
                        return new DecidingResult(DecidingType.Fail, $"Expected {simple.Value}", 0, _cursor);
                    }
                default:
                    return new DecidingResult(DecidingType.Fail, $"Unimplemented rule type for rule {rule.Name}", 9999, _cursor);
            }
        }
    }
    class ReferenceStore
    {
        private Dictionary<string, Rule> _references = new();
        public Rule GetReference(string name)
        {
            if (_references.TryGetValue(name, out var rule))
            {
                return rule;
            }
            throw new KeyNotFoundException($"Reference '{name}' not found.");
        }
        public void AddIfReferenceable(Rule rule)
        {
            if (rule is not Anchor)
            {
                return; // only anchors can be added as references
            }
            if (_references.ContainsKey(rule.Name))
            {
                throw new ArgumentException($"Reference '{rule.Name}' already exists.");
            }
            _references[rule.Name] = rule;
        }
    }
    class ParserV2
    {
        public static Tree Parse(string code, Rule[] rules, ReferenceStore referenceStore)
        {
            Tree tree = new Tree(new Token("Root", "", 0, 0), new List<Tree>());
            List<(Rule, DecidingResult)> shortlist = [];
            List<DecidingResult> errors = [];
            Decider decider = new();
            int line = 1;
            int column = 1;
            void Progress(ref int target, int cursor)
            {
                while (target < cursor)
                {
                    if (code[target] != '\n')
                        column++;
                    else
                    {
                        line++;
                        column = 1;
                    }
                    target++;
                }
            }
            for (int i = 0; i < code.Length;)
            {
                shortlist.Clear();
                errors.Clear();
                // shortlist match
                decider.Set(i);
                foreach (var rule in rules)
                {
                    var result = decider.CheckRule(rule, code);
                    if (result.Type == DecidingType.Fail)
                        errors.Add(result);
                    else if (result.Type != DecidingType.Skip)
                        shortlist.Add((rule, result));
                }
                if (shortlist.Count > 0)
                {
                    var mostFit = shortlist.MaxBy(x => x.Item2.Priority);
                    string value = code[i..mostFit.Item2.Cursor];
                    if (mostFit.Item2.Type == DecidingType.Open)
                    {
                        try
                        {
                            var rule = referenceStore.GetReference(mostFit.Item1.Name);
                            decider.Set(i);
                            var result = decider.CheckRule(rule, code);
                            if (result.Type == DecidingType.Fail)
                            {
                                errors.Add(result);
                            }
                            else if (result.Type != DecidingType.Skip)
                            {
                                shortlist.Add((rule, result));
                            }
                            Progress(ref i, result.Cursor + 1);
                        }
                        catch (ArgumentNullException)
                        {
                            errors.Add(new DecidingResult(DecidingType.Fail, $"Referenced rule '{mostFit.Item1.Name}' not found", 9999, i));
                            Progress(ref i, i + 1); // move at least one character forward
                            continue;
                        }
                        continue;
                    }
                    Progress(ref i, mostFit.Item2.Cursor + 1);
                    switch (mostFit.Item1)
                    {
                        case Anchor:
                            // do nothing
                            break;
                        case Group group:
                            var childTree = Parse(code[i..], group.Children, referenceStore);
                            tree.Children.Add(childTree);
                            break;
                        case Reference reference:
                            throw new NotImplementedException($"Unhandled reference: {reference.Name}"); // should've been handled earlier
                        case Simple simple:
                            tree.Children.Add(new Tree(new Token(simple.Name, value, 0, 0), new List<Tree>()));
                            break;
                        default:
                            throw new NotImplementedException($"Unimplemented rule type: {mostFit.Item1.GetType()}");
                    }
                    continue;
                }
                // else if
                if (char.IsWhiteSpace(code[i]))
                {
                    Progress(ref i, i + 1);
                    continue;
                }
                // else most fit error
                if (errors.Count > 0)
                {
                    var error = errors.MinBy(x => x.Priority);
                    tree.Children.Add(new Tree(new Token("Error", error.Message, line, column), new List<Tree>()));
                }
                else
                {
                    tree.Children.Add(new Tree(new Token("Error", "Unexpected character", line, column), new List<Tree>()));
                }
            }
            return tree;
        }
    }
}