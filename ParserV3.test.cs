using ClemToken;

class ParserV3Tester
{
    public static void Main(string[] _)
    {
        // Test0();
        // Test1();
        // Test2();
        // Test3();
        // Test4();
        // Test5();
        // Test6();
    }
    private static void Test0()
    {
        var res = ParserV3.Parse("test", [
            new Simple("test"),
        ], new ReferenceStore());
        Console.WriteLine(Stringify(res));
    }
    private static void Test1()
    {
        var res = ParserV3.Parse("a b c", [
            new Group((1, 1), "Group Test", false, [
                new Simple("a"),
                new Simple("b"),
                new Simple("c")
            ]),
        ], new ReferenceStore());
        Console.WriteLine(Stringify(res));
    }
    private static void Test2()
    {
        var res = ParserV3.Parse("var a = 5;", [
            new Group((1, 1), "Declaration", false, [
                new Simple("var"),
                new Reserved("[name]"),
                new Simple("="),
                new Reserved("[name]"),
                new Simple(";")
            ]),
        ], new ReferenceStore());
        Console.WriteLine(Stringify(res));
    }
    private static void Test3()
    {
        var res = ParserV3.Parse("var a = 5; var b = 10;", [
            new Group((1, 1), "Declaration", false, [
                new Simple("var"),
                new Reserved("[name]"),
                new Simple("="),
                new Reserved("[name]"),
                new Simple(";")
            ]),
        ], new ReferenceStore());
        Console.WriteLine(Stringify(res));
    }
    private static void Test4()
    {
        var res = ParserV3.Parse("var a = 5ffsfs;", [
            new Group((1, 1), "Declaration", true, [
                new Simple("var"),
                new Reserved("[name]"),
                new Simple("="),
                new Reserved("[name]"),
                new Simple(";")
            ]),
        ], new ReferenceStore());
        Console.WriteLine(Stringify(res));
    }
    private static void Test5()
    {
        Anchor number = new("@number:", [
            new Reserved("[name]"),
        ]);
        Anchor identifier = new("@identifier:", [
            new Reserved("[name]"),
        ]);
        ReferenceStore preloadedRules = new ReferenceStore();
        preloadedRules.AddIfReferenceable(number);
        preloadedRules.AddIfReferenceable(identifier);
        var res = ParserV3.Parse("var a = 50;", [
            new Group((1, 1), "Declaration", false, [
                new Simple("var"),
                new Reference("@identifier"),
                new Simple("="),
                new Reference("@number"),
                new Simple(";")
            ]),
        ], preloadedRules);
        Console.WriteLine(Stringify(res));
    }
    private static void Test6()
    {
        Anchor number = new("@number:", [
            new Reserved("[name]", true),
        ]);
        Anchor identifier = new("@identifier:", [
            new Reserved("[name]", true),
        ]);
        ReferenceStore preloadedRules = new ReferenceStore();
        preloadedRules.AddIfReferenceable(number);
        preloadedRules.AddIfReferenceable(identifier);
        var res = ParserV3.Parse("var a = 50;", [
            new Group((1, 1), "Declaration", false, [
                new Simple("var"),
                new Reference("@identifier"),
                new Simple("="),
                new Reference("@number"),
                new Simple(";")
            ]),
        ], preloadedRules);
        Console.WriteLine(Stringify(res));
    }
    private static string Stringify(Tree tree, int depth = 0)
    {
        static string Indent(int d) => new string(' ', d * 2);
        return Indent(depth) + tree.Token.Name + " {\n" +
            Indent(depth + 1) + "Value: " + tree.Token.Value + "\n" +
            (
                tree.Children.Count == 0 ? ""
                : (
                Indent(depth + 1) + "Children: [\n" +
                string.Join(",\n", tree.Children.Select(c => Stringify(c, depth + 1))) +
                "\n" + Indent(depth + 1) + "]\n"
                )
            ) + Indent(depth) + "}";
    }
}