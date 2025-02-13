using System.Text;
namespace ClemToken
{
    class Serializer
    {
        public static string StringSerializeTree(Tree tree)
        {
            StringBuilder sb = new();
            StringSerializeTreeSub(tree, sb);
            return sb.ToString();
        }
        private static void StringSerializeTreeSub(Tree tree, StringBuilder stringBuilder) {
            stringBuilder.AppendLine("(");
            stringBuilder.AppendLine("name: " + tree.Token.Name);
            stringBuilder.AppendLine("value: " + tree.Token.Value);
            foreach (var child in tree.Children)
            {
                StringSerializeTreeSub(child, stringBuilder);
            }
            stringBuilder.AppendLine(")");
        }
        public static byte[] Serialize(Rule[] rules)
        {
            List<byte> data =
            [
                ..BitConverter.GetBytes((short)2), // version (2: added references & anchors)
            ];
            foreach (var rule in rules)
            {
                SerializeRule(rule, ref data);
            }
            return [..data];
        }

        public static Rule[] Deserialize(byte[] data)
        {
            if (data.Length < 2)
            {
                throw new Exception("Invalid serialization data");
            }
            List<Rule> rules = new();
            int version = BitConverter.ToInt16(data.AsSpan()[0..2]);
            if (version != 1)
            {
                throw new Exception("Incompatible serialization version, expected 1, got " + version);
            }
            int i = 2;
            while (i < data.Length)
            {
                rules.Add(DeserializeRule(data, ref i));
            }
            return [..rules];
        }


        public static void SerializeRule(Rule rule, ref List<byte> data)
        {
            if (rule is Simple simple)
            {
                data.Add(0); // Simple
                data.AddRange(Encoding.UTF8.GetBytes(simple.Value ?? "")); // Value
                data.Add(0); // null terminator
            }
            else if (rule is Reserved reserved)
            {
                data.Add(1); // Reserved
                data.AddRange(Encoding.UTF8.GetBytes(reserved.Value ?? "")); // Value
                data.Add(0); // null terminator
                return;
            }
            else if (rule is Group group)
            {
                data.Add(2);
                // could do a single byte conversion, but this would be limiting repetitions to "only" 254 (offset by 1 for -1)
                data.AddRange(BitConverter.GetBytes(group.Repeat.Item1)); // Repeat
                data.AddRange(BitConverter.GetBytes(group.Repeat.Item2));
                data.Add(group.Ignored ? (byte)1 : (byte)0); // Ignored
            }
            else if (rule is Reference)
            {
                data.Add(3); // Reference
                // no need, name is already serialized
            } else if (rule is Anchor)
            {
                data.Add(4); // Anchor
                // name is already serialized
            }
            else
            {
                throw new Exception($"Rule type ({rule.GetType().Name}²) cannot be serialized, was there a generator error?");
            }
            data.AddRange(Encoding.UTF8.GetBytes(rule.Name));
            data.Add(0); // null terminator
            data.AddRange(BitConverter.GetBytes(rule.Children.Length));
            foreach (var child in rule.Children)
            {
                SerializeRule(child, ref data);
            }
        }

        public static Rule DeserializeRule(byte[] data, ref int cursor)
        {
            byte type = data[cursor];
            cursor++;
            Rule rule;
            switch (type)
            {
                case 0: // Simple
                    { // to isolate the scope

                        string value = "";
                        while (data[cursor] != 0)
                        {
                            value += (char)data[cursor];
                            cursor++;
                        }
                        cursor++;
                        rule = new Simple(value);
                        break;
                    }
                case 1: // Reserved
                    {

                        string value = "";
                        while (data[cursor] != 0)
                        {
                            value += (char)data[cursor];
                            cursor++;
                        }
                        cursor++;
                        rule = new Reserved(value);
                        break;
                    }
                case 2: // Group
                    {
                        int repeat1 = BitConverter.ToInt32(data.AsSpan()[cursor..(cursor + 4)]);
                        cursor += 4;
                        int repeat2 = BitConverter.ToInt32(data.AsSpan()[cursor..(cursor + 4)]);
                        cursor += 4;
                        bool ignored = data[cursor] == 1;
                        cursor++;
                        rule = new Group((repeat1, repeat2), ignored);
                        break;
                    }
                case 3: // Reference
                    {
                        string refName = "";
                        while (data[cursor] != 0)
                        {
                            refName += (char)data[cursor];
                            cursor++;
                        }
                        cursor++;
                        rule = new Reference("@"+refName);
                        return rule;
                    }
                case 4: // Anchor
                    {
                        string anchorName = "";
                        while (data[cursor] != 0) // we don't want the name to be read after the constructor
                        {
                            anchorName += (char)data[cursor];
                            cursor++;
                        }
                        cursor++;
                        rule = new Anchor(anchorName);
                        goto children; // anchor does not have extra data
                    }
                default:
                    throw new Exception("Invalid rule type encountered during deserialization: " + type);
            }
            string name = "";
            while (data[cursor] != 0)
            {
                name += (char)data[cursor];
                cursor++;
            }
            cursor++;
            rule.Name = name;
            children:
            int childrenCount = BitConverter.ToInt32(data.AsSpan()[cursor..(cursor + 4)]);
            cursor += 4;
            rule.Children = new Rule[childrenCount];
            for (int i = 0; i < childrenCount; i++)
            {
                rule.Children[i] = DeserializeRule(data, ref cursor);
            }
            return rule;
        }
    }
}