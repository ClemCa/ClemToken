using System.Diagnostics;
using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Text;
namespace ClemToken
{
    class Serializer
    {
        //! 1: initial version
        //! 2: added references & anchors
        //! 3: implicit groups no longer supported
        //! 4: use of two-way serialization & generic marshaling
        //! 5: ignored for reserved
        //! 6: optionals
        //! 7: specs
        private const int Version = 6;
        public static string StringSerializeTree(Tree tree)
        {
            StringBuilder sb = new();
            StringSerializeTreeSub(tree, sb);
            return sb.ToString();
        }
        private static void StringSerializeTreeSub(Tree tree, StringBuilder stringBuilder, int depth = 0)
        {
            stringBuilder.AppendLine(new string(' ', depth * 2) + "(");
            stringBuilder.AppendLine(new string(' ', depth * 2 + 2) + "name: " + tree.Token.Name);
            stringBuilder.AppendLine(new string(' ', depth * 2 + 2) + "value: " + tree.Token.Value.Replace("\r\n", "\\n").Replace("\n", "\\n"));
            foreach (var child in tree.Children)
            {
                StringSerializeTreeSub(child, stringBuilder, depth + 1);
            }
            stringBuilder.AppendLine(new string(' ', depth * 2) + ")");
        }
        public static string DisplayRules(Rule[] rules)
        {
            StringBuilder sb = new();
            foreach (var rule in rules)
            {
                DisplayRule(rule, sb);
            }
            return sb.ToString();
        }
        public static void DisplayRule(Rule rule, StringBuilder stringBuilder, int depth = 0)
        {
            stringBuilder.AppendLine(new string(' ', depth * 2) + rule.Name + " (" + rule.GetType().Name + ";" + rule.Value + ";" + rule.Repeat + ")");
            foreach (var child in rule.Children)
            {
                DisplayRule(child, stringBuilder, depth + 1);
            }
        }
        public class ByteBuffer(byte[]? data = null, int initialCursor = 0, bool writeMode = false)
        {
            private int cursor = initialCursor;
            public bool WriteMode = writeMode;
            private List<byte> _buffer = data is not null ? [.. data] : [];
            public static bool IsLittleEndian { get => BitConverter.IsLittleEndian; }
            public bool Complete { get => cursor == _buffer.Count; }
            public void Add(byte value)
            {
                _buffer.Add(value);
            }
            public void Add(byte[] data)
            {
                if (!IsLittleEndian)
                {
                    Array.Reverse(data);
                }
                _buffer.AddRange(data);
            }
            public byte[] GetBytes(int count)
            {
                if (cursor + count > _buffer.Count)
                    throw new ArgumentException("Not enough bytes in buffer to read the requested amount.");
                var result = _buffer.GetRange(cursor, count);
                cursor += count;
                if (!IsLittleEndian)
                {
                    return [.. result.Reverse<byte>()]; // added type specification to use the Linq version
                }
                return [.. result];
            }
            public byte[] GetAllBytes()
            {
                return [.. _buffer];
            }
            public byte[] GetBytesUntil(byte terminator)
            {
                int start = cursor;
                while (cursor < _buffer.Count)
                {
                    byte b = _buffer[cursor++];
                    if (b == terminator)
                    {
                        break;
                    }
                }
                if (cursor == start + 1) return [];
                if (!IsLittleEndian)
                {
                    return [.. _buffer.Take(start..(cursor - 1)).Reverse()];
                }
                return [.. _buffer.Take(start..(cursor - 1))];
            }
        }
        public static byte[] Serialize(Rule[] rules, Spec[] specs)
        {
            ByteBuffer bytes = new(writeMode: true);
            Mirror((short)Version, bytes);
            ByteBuffer specBytesBuffer = new(writeMode: true);
            foreach (var spec in specs)
            {
                Console.WriteLine($"Serializing spec for target '{spec.Target}'");
                _ = MirrorSpec(spec, specBytesBuffer);
            }
            int specBytes = specBytesBuffer.GetAllBytes().Length;
            Mirror(specBytes, bytes);
            bytes.Add(specBytesBuffer.GetAllBytes());
            Console.WriteLine($"Serialized {specs.Length} specs.");
            for (int i = 0; i < rules.Length; i++)
            {
                Console.WriteLine($"Serializing rule {i + 1} of {rules.Length}");
                _ = MirrorRule(rules[i], bytes);
            }
            Console.WriteLine($"Serialized {rules.Length} rules.");
            return bytes.GetAllBytes();
        }
        public static (Rule[] rules, Spec[] specs) Deserialize(byte[] data)
        {
            if (data.Length < 2)
            {
                throw new Exception("Invalid serialization data");
            }
            List<Spec> specs = [];
            List<Rule> rules = [];
            ByteBuffer bytes = new(data);
            short version = Mirror((short)0, bytes);
            if (version != Version)
            {
                throw new Exception($"Incompatible serialization version, expected {Version}, got " + version);
            }
            int specBytes = Mirror(0, bytes);
            var specBuffer = new ByteBuffer(bytes.GetBytes(specBytes));
            while (!specBuffer.Complete)
            {
                specs.Add(MirrorSpec(new Spec(), specBuffer));
            }
            Console.WriteLine("All specs deserialized.");
            while (!bytes.Complete)
            {
                Console.WriteLine($"Rule {rules.Count + 1}");
                rules.Add(MirrorRule(new ProcessRule(), bytes));
            }
            Console.WriteLine("All rules deserialized.");
            return ([.. rules], [.. specs]);
        }
        public static Spec MirrorSpec(Spec spec, ByteBuffer bytes)
        {
            spec.Target = Mirror(spec.Target ?? "", bytes);
            spec.Definition = Mirror(spec.Definition ?? "", bytes);
            return spec;
        }
        public static Rule MirrorRule(Rule rule, ByteBuffer bytes)
        {
            int type = rule switch
            {
                Simple => 0,
                Reserved => 1,
                Group => 2,
                Reference => 3,
                Anchor => 4,
                Optional => 5,
                _ => -1
            };
            type = Mirror(type, bytes);
            switch (type)
            {
                case 0:
                    Simple s = rule is Simple simple ? simple : new();
                    s.Value = Mirror(s.Value ?? "", bytes);
                    rule = s;
                    break;
                case 1:
                    Reserved r = rule is Reserved reserved ? reserved : new();
                    r.Name = Mirror(r.Name ?? "", bytes);
                    if (bytes.WriteMode)
                        r.Value = $"[{r.Name}]";
                    r.Children = [];
                    r.Ignored = Mirror(r.Ignored, bytes);
                    return r;
                case 2:
                    Group g = rule is Group group ? group : new();
                    (int, int) repeat = g.Repeat;
                    repeat.Item1 = Mirror(repeat.Item1, bytes);
                    repeat.Item2 = Mirror(repeat.Item2, bytes);
                    g.Repeat = repeat;
                    g.Ignored = Mirror(g.Ignored, bytes);
                    rule = g;
                    break;
                case 3:
                    Reference refRule = rule is Reference reference ? reference : new();
                    refRule.Name = Mirror(refRule.Name, bytes);
                    refRule.Children = [];
                    return refRule;
                case 4:
                    Anchor a = rule is Anchor anchor ? anchor : new();
                    a.Name = Mirror(a.Name, bytes);
                    rule = a;
                    goto children; // anchor does not have extra data
                case 5:
                    Optional o = rule is Optional optional ? optional : new();
                    rule = o;
                    goto children; // optional does not have extra data
                default:
                    throw new Exception($"Unknown rule type: {rule.GetType().Name} ({type})");
            }
            rule.Name = Mirror(rule.Name ?? "", bytes);
        children:
            int children = rule.Children?.Length ?? 0;
            children = Mirror(children, bytes);
            rule.Children ??= new Rule[children];
            for (int i = 0; i < children; i++)
            {
                rule.Children[i] = MirrorRule(rule.Children[i] ?? new ProcessRule(), bytes);
            }
            return rule;
        }
        private static int Mirror(int value, ByteBuffer bytes)
        {
            if (bytes.WriteMode)
                bytes.Add(BitConverter.GetBytes(value));
            else
                value = BitConverter.ToInt32(bytes.GetBytes(4), 0);
            return value;
        }
        private static short Mirror(short value, ByteBuffer bytes)
        {
            if (bytes.WriteMode)
                bytes.Add(BitConverter.GetBytes(value));
            else
                value = BitConverter.ToInt16(bytes.GetBytes(2), 0);
            return value;
        }
        private static bool Mirror(bool value, ByteBuffer bytes)
        {
            if (bytes.WriteMode)
                bytes.Add(BitConverter.GetBytes(value));
            else
                value = BitConverter.ToBoolean(bytes.GetBytes(1), 0);
            return value;
        }
        private static string Mirror(string value, ByteBuffer bytes)
        {
            if (bytes.WriteMode)
            {
                bytes.Add(Encoding.UTF8.GetBytes(value ?? ""));
                bytes.Add(0); // null terminator
            }
            else
            {
                value = Encoding.UTF8.GetString(bytes.GetBytesUntil(0));
            }
            return value!;
        }
    }
}