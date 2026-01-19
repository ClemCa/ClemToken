namespace ClemToken
{
    public class Navigator
    {
        class Page
        {
            public Tree Tree;
            public int status;
            public Page(Tree tree, int status)
            {
                Tree = tree;
                this.status = status;
            }
            public static implicit operator Page((Tree, int) page)
            {
                return new Page(page.Item1, page.Item2);
            }
        }
        private List<Page> History;
        private Tree Tree
        {
            get => History[^1].Tree;
            set => History[^1].Tree = value;
        }
        private int status
        {
            get => History[^1].status;
            set => History[^1].status = value;
        }
        internal Navigator(Tree tree)
        {
            History = [(tree, 0)];
        }
        public int Children => Tree.Children.Count;
        public bool Has(string name)
        {
            foreach (var child in Tree.Children)
            {
                if (child.Token.Name == name)
                {
                    return true;
                }
            }
            return false;
        }
        public bool GoTo(string name)
        {
            foreach (var child in Tree.Children)
            {
                if (child.Token.Name == name)
                {
                    History.Add((child, 0));
                    return true;
                }
            }
            return false;
        }
        public bool Enter()
        {
            if (status < Tree.Children.Count)
            {
                History.Add((Tree.Children[status], 0));
                return true;
            }
            return false;
        }
        public bool Back(int count = 1)
        {
            if (count >= History.Count)
            {
                return false;
            }
            while (count-- > 0)
            {
                History.RemoveAt(History.Count - 1);
            }
            return true;
        }
        public bool GoToRelative(int offset)
        {
            if (status + offset < Tree.Children.Count && status + offset >= 0)
            {
                History.Add((Tree.Children[status + offset], 0));
                return true;
            }
            return false;
        }
        public bool GoToNext(string name)
        {
            for (int i = status; i < Tree.Children.Count; i++)
            {
                if (Tree.Children[i].Token.Name == name)
                {
                    History.Add((Tree.Children[i], 0));
                    return true;
                }
            }
            return false;
        }
        public bool Is(string name)
        {
            return Tree.Token.Name == name;
        }
        public bool Is(string name, string value)
        {
            return Tree.Token.Name == name && Tree.Token.Value == value;
        }
        public void First()
        {
            status = 0;
        }
        public void Last()
        {
            status = Tree.Children.Count - 1;
        }
        public bool Previous()
        {
            if (status <= 0)
            {
                return false;
            }
            status--;
            return true;
        }
        public bool Next()
        {
            if (status >= Tree.Children.Count - 1)
            {
                return false;
            }
            status++;
            return true;
        }
        public bool PreviousSibling()
        {
            if (History.Count < 2)
            {
                return false;
            }
            Back();
            if (Previous()) return true;
            Enter();
            return false;
        }
        public bool NextSibling()
        {
            if (History.Count < 2)
            {
                return false;
            }
            Back();
            if (Next()) return true;
            Enter();
            return false;
        }
        public bool IsLast()
        {
            return status >= Tree.Children.Count - 1;
        }
        public bool IsFirst()
        {
            return status == 0;
        }
        public bool NextIs(string name, string? value = null)
        {
            if (status + 1 >= Tree.Children.Count)
            {
                return false;
            }
            return Tree.Children[status + 1].Token.Name == name && (value == null || Tree.Children[status + 1].Token.Value == value);
        }
        public bool HasLeft(string name, string? value = null, int count = 1)
        {
            for (int i = status + 1; i < Tree.Children.Count; i++)
            {
                if (Tree.Children[i].Token.Name == name && value == null || Tree.Children[i].Token.Value == value)
                {
                    if (count-- == 0) return true;
                }
            }
            return false;
        }
        public bool HasPassed(string name, string? value = null, int count = 1)
        {
            for (int i = status - 1; i >= 0; i--)
            {
                if (Tree.Children[i].Token.Name == name && value == null || Tree.Children[i].Token.Value == value)
                {
                    if (count-- == 0) return true;
                }
            }
            return false;
        }
        public Token Current()
        {
            return Tree.Children[status].Token;
        }
        public Tree CurrentTree()
        {
            return Tree.Children[status];
        }
    }
}