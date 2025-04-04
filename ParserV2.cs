namespace ClemToken
{
    public struct DecidingResult {
        public DecidingType Type;
        public string Message;
        public int Priority;
        public DecidingResult(DecidingType type, string message, int priority) {
            Type = type;
            Message = message;
            Priority = 0;
        }
    }
    public enum DecidingType {
        Fail,
        Match,
        PartialMatch,
        Skip,
        Open
    }
    public class Decider {
        private int _cursor = 0;
        private Decider? _subDecider = null;
        void Reset() {
            _cursor = 0;
        }// TODO higher logic that uses deciders
        DecidingResult CheckRule(Rule rule, string stream) {
            if(rule.IsReserved) {
                switch(rule.Name) {
                    case "name":
                        if(!char.IsWhiteSpace(stream[_cursor])) {
                            _cursor++;
                            if(_cursor >= stream.Length && !char.IsWhiteSpace(stream[_cursor])) {
                                return new DecidingResult(DecidingType.PartialMatch, "", 0);
                            } else {
                                return new DecidingResult(DecidingType.Match, "", 0);
                            }
                        } else {
                            return new DecidingResult(DecidingType.Fail, "Expected name", 0);
                        }
                    default:
                        return new DecidingResult(DecidingType.Fail, $"Unimplemented reserved rule: {rule.Name}", 9999);
                }
            }
            switch(rule) {
                case Anchor:
                    return new DecidingResult(DecidingType.Skip, "", 0);
                case Group group:
                    _subDecider ??= new Decider();
                    var res = _subDecider.CheckRule(group.Children[_cursor], stream);
                    switch(res.Type) {
                        case DecidingType.Match:
                            _cursor++;
                            if(_cursor < group.Children.Count()) {
                                return new DecidingResult(DecidingType.PartialMatch, "", 0);
                            } else {
                                return new DecidingResult(DecidingType.Match, "", 0);
                            }
                        case DecidingType.PartialMatch:
                            return new DecidingResult(DecidingType.PartialMatch, "", 0);
                        case DecidingType.Fail:
                            // TODO find some kind of logic for max repeat, probably higher up the chain we could after one success create a copy with a floor & ceiling 1 lower and rince and repeat
                            if(rule.Repeat.Item1 == 0) {
                                return new DecidingResult(DecidingType.Skip, "", 0);
                            }
                            return new DecidingResult(DecidingType.Fail, $"Expected {group.Name}", 0);
                        case DecidingType.Skip:
                            if(_cursor < group.Children.Count()) {
                                return new DecidingResult(DecidingType.PartialMatch, "", 0);
                            } else {
                                return new DecidingResult(DecidingType.Match, "", 0);
                            }
                        case DecidingType.Open:
                            return new DecidingResult(DecidingType.Open, group.Name, 0);
                        default:
                            return new DecidingResult(DecidingType.Fail, $"Unimplemented group rule type for rule {rule.Name}", 9999);
                    }
                case Reference:
                    return new DecidingResult(DecidingType.Open, rule.Name, 0);
                case Simple simple:
                    if(simple.Value?[_cursor] == stream[0]) {
                        _cursor++;
                        if(_cursor < simple.Value.Length) {
                            return new DecidingResult(DecidingType.PartialMatch, "", 0);
                        } else {
                            return new DecidingResult(DecidingType.Match, "", 0);
                        }
                    } else {
                        return new DecidingResult(DecidingType.Fail, $"Expected {simple.Value}", 0);
                    }
                default:
                    return new DecidingResult(DecidingType.Fail, $"Unimplemented rule type for rule {rule.Name}", 9999);
            }
        }
    }
    class ParserV2
    {
        public static Tree Parse(string code, Rule[] rules)
        {
            Tree tree = new Tree(new Token("Root", "", 0, 0), new List<Tree>());
            List<Rule> shortlist = new();
            for (int i = 0; i < code.Length;)
            {
                // shortlist match
                // else if
                if (char.IsWhiteSpace(code[i]))
                {
                    i++;
                    continue;
                }
                // else most fit error
            }
            return tree;
        }
    }
}