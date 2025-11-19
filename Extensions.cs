using System.Text.RegularExpressions;

public static class Extensions
{
    extension(Regex regex)
    {
        public bool FullMatch(string input)
        {
            var match = regex.Match(input);
            return match.Success && match.Length == input.Length;
        }
    }
}