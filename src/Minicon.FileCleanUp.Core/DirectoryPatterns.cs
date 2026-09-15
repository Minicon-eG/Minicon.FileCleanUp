using System.Text;
using System.Text.RegularExpressions;

namespace Minicon.FileCleanUp;

internal static class DirectoryPatterns
{
    internal static bool Matches(DirectorySelector selector, string relative)
    {
        relative = relative.Replace('\\', '/');
        var pattern = selector.Pattern ?? "";
        if (selector.Kind == SelectorKind.Path)
        {
            return string.Equals(relative, pattern.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        var expression = selector.Kind == SelectorKind.Regex ? pattern : Glob(pattern.Replace('\\', '/'));
        return Regex.IsMatch(
            relative,
            "\\A(?:" + expression + ")\\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    internal static bool MayContainTarget(DirectorySelector selector, string relative)
    {
        if (selector.Kind != SelectorKind.Glob)
        {
            return true;
        }

        var pattern = (selector.Pattern ?? "").Replace('\\', '/').Split('/');
        if (pattern.Contains("**"))
        {
            return true;
        }

        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length >= pattern.Length)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            if (!Matches(new DirectorySelector { Kind = SelectorKind.Glob, Pattern = pattern[i] }, parts[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string Glob(string pattern)
    {
        var result = new StringBuilder();
        var parts = pattern.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "**")
            {
                if (i == parts.Length - 1)
                {
                    if (i > 0
                        && result.Length > 0
                        && result[^1] == '/')
                    {
                        result.Length--;
                    }

                    result.Append(i > 0 ? "(?:/.*)?" : ".*");
                }
                else
                {
                    result.Append("(?:[^/]+/)*");
                }
            }
            else
            {
                result.Append(Regex.Escape(parts[i]).Replace("\\*", "[^/]*").Replace("\\?", "[^/]"));
                if (i < parts.Length - 1)
                {
                    result.Append('/');
                }
            }
        }

        return result.ToString();
    }
}
