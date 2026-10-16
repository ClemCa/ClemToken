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

    extension(string text)
    {
        //! preview slice for diagnostics: clamps to the string and keeps the bounds off a split
        //! surrogate pair, so a preview never carries an unpaired surrogate. Without this a log
        //! built from `code[cursor..cursor+N]` can end halfway through a character (e.g. the halves
        //! of an emoji) and a later UTF-8 write of the log rejects the whole string
        public string SafeSlice(int start, int end)
        {
            start = Math.Clamp(start, 0, text.Length);
            end = Math.Clamp(end, start, text.Length);
            if (start < text.Length && char.IsLowSurrogate(text[start]))
            {
                start++;
            }
            if (end > start && char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }
            return end > start ? text[start..end] : "";
        }
    }
}