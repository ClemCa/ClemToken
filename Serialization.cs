using System.Text;

class Serializer
{
    public static byte[] Serialize(Rule[] rules)
    {
        List<byte> data =
        [
            ..BitConverter.GetBytes((short)1), // version
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
        }
        else if (rule is Group group)
        {
            data.Add(2);
            // could do a single byte conversion, but this would be limiting repetitions to "only" 254 (offset by 1 for -1)
            data.AddRange(BitConverter.GetBytes(group.Repeat.Item1)); // Repeat
            data.AddRange(BitConverter.GetBytes(group.Repeat.Item2));
            data.Add(group.Ignored ? (byte)1 : (byte)0); // Ignored
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
        int childrenCount = BitConverter.ToInt32(data.AsSpan()[cursor..(cursor + 4)]);
        cursor += 4;
        rule.Name = name;
        rule.Children = new Rule[childrenCount];
        for (int i = 0; i < childrenCount; i++)
        {
            rule.Children[i] = DeserializeRule(data, ref cursor);
        }
        return rule;
    }
}