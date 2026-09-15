using System.IO.Abstractions;

namespace Minicon.FileCleanUp;

internal static class ConfigurationValidator
{
    internal static void Validate(CleanupOptions options, IFileSystem fs)
    {
        if (options.ProgressIntervalSeconds is < 0 or > 86400
            || options.MinimumRetentionDays < 1
            || options.MaxFilesToDeletePerRun < 1
            || options.DeleteDelayMilliseconds < 0
            || options.Resilience.MaxAttempts < 1
            || options.Resilience.InitialDelaySeconds < 1
            || options.Resilience.MaxDelaySeconds < options.Resilience.InitialDelaySeconds
            || options.Resilience.MaxRetryElapsedSecondsPerOperation < 1
            || options.Resilience.MaxRetryElapsedSecondsPerRun < 1
            || !Enum.IsDefined(options.Audit.Mode))
        {
            throw new ArgumentException("Invalid global options.");
        }

        if (options.Rules.Count == 0)
        {
            throw new ArgumentException("At least one rule is required.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<(string Path, string Rule, SelectorKind Kind)>();
        foreach (var rule in options.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name)
                || !names.Add(rule.Name)
                || rule.RetentionDays < options.MinimumRetentionDays
                || rule.Directories.Count == 0)
            {
                throw new ArgumentException("Invalid rule name, retention or directories.");
            }

            if (rule.RemoveEmptyDirectories
                && !rule.Recursive)
            {
                throw new ArgumentException("RemoveEmptyDirectories requires Recursive.");
            }

            const CheckDates supportedDates = CheckDates.CreationTimeUtc
                | CheckDates.LastWriteTimeUtc
                | CheckDates.LastAccessTimeUtc;
            if (rule.CheckDates == CheckDates.None || (rule.CheckDates & ~supportedDates) != 0)
            {
                throw new ArgumentException("CheckDates must select at least one supported timestamp.");
            }

            if (rule.Timestamp != "LastWriteTimeUtc"
                || rule.IncludePatterns.Count == 0)
            {
                throw new ArgumentException("Invalid timestamp or patterns.");
            }

            foreach (var pattern in rule.IncludePatterns.Concat(rule.ExcludePatterns))
            {
                if (string.IsNullOrWhiteSpace(pattern)
                    || pattern.Contains('/')
                    || pattern.Contains('\\'))
                {
                    throw new ArgumentException("File patterns must be names.");
                }
            }

            foreach (var selector in rule.Directories)
            {
                if (!fs.Path.IsPathFullyQualified(selector.Root))
                {
                    throw new ArgumentException("Root must be absolute.");
                }

                var root = fs.Path.GetFullPath(selector.Root).TrimEnd(fs.Path.DirectorySeparatorChar);
                if (root == fs.Path.GetPathRoot(selector.Root)!.TrimEnd(fs.Path.DirectorySeparatorChar))
                {
                    throw new ArgumentException("Volume/share roots are forbidden.");
                }

                foreach (var protectedPath in options.ProtectedDirectories)
                {
                    if (!fs.Path.IsPathFullyQualified(protectedPath))
                    {
                        throw new ArgumentException("ProtectedDirectories must be absolute.");
                    }

                    var canonical = fs.Path.TrimEndingDirectorySeparator(fs.Path.GetFullPath(protectedPath));
                    for (var current = canonical; !string.IsNullOrEmpty(current); current = fs.Path.GetDirectoryName(current))
                    {
                        try
                        {
                            if ((fs.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                            {
                                throw new ArgumentException("Protected host paths must not contain reparse points.");
                            }
                        }
                        catch (FileNotFoundException)
                        {
                        }
                        catch (DirectoryNotFoundException)
                        {
                        }
                    }

                    if (Contains(root, canonical)
                        || Contains(canonical, root))
                    {
                        throw new ArgumentException("Search roots must not overlap protected host directories.");
                    }
                }

                foreach (var existing in roots)
                {
                    if (existing.Kind == SelectorKind.Path && selector.Kind == SelectorKind.Path
                        && (Contains(existing.Path, root)
                        || Contains(root, existing.Path))
                        && !(existing.Rule == rule.Name
    && existing.Path == root))
                    {
                        throw new ArgumentException("Overlapping search roots.");
                    }
                }

                if (options.Audit.JournalDirectory is { } audit)
                {
                    if (!fs.Path.IsPathFullyQualified(audit)
                        || audit.StartsWith("\\\\")
                        || Contains(root, fs.Path.GetFullPath(audit)))
                    {
                        throw new ArgumentException("Journal must be local and outside search roots.");
                    }
                }

                roots.Add((root, rule.Name, selector.Kind));
                ValidateSelector(selector, false);
            }

            foreach (var selector in rule.ExcludeDirectories)
            {
                ValidateSelector(selector, true);
            }
        }
    }

    internal static bool Contains(string root, string path) => string.Equals(root, path, Comparison)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison);
    internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void ValidateSelector(DirectorySelector selector, bool exclusion)
    {
        if (selector.PreserveDirectoryOnly && !exclusion)
        {
            throw new ArgumentException("Directory preservation is only supported for exclusions.");
        }

        if (selector.MatchFullPath && (!exclusion || selector.Kind != SelectorKind.Regex))
        {
            throw new ArgumentException("Full-path matching is only supported for regex exclusions.");
        }

        if (!Enum.IsDefined(selector.Kind))
        {
            throw new ArgumentException("Unknown selector kind.");
        }

        if (exclusion
            && !string.IsNullOrEmpty(selector.Root))
        {
            throw new ArgumentException("Exclusions cannot define Root.");
        }

        if (!exclusion
            && selector.Kind == SelectorKind.Path)
        {
            if (selector.Pattern != null)
            {
                throw new ArgumentException("Path target cannot define Pattern.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(selector.Pattern))
        {
            throw new ArgumentException("Pattern required.");
        }

        if (selector.Kind != SelectorKind.Regex
            && (selector.Pattern.StartsWith('/')
    || selector.Pattern.StartsWith('\\')
    || selector.Pattern.Contains(':')
    || selector.Pattern.Replace('\\', '/').Split('/').Contains("..")))
        {
            throw new ArgumentException("Pattern must stay inside the root.");
        }

        if (selector.Kind == SelectorKind.Glob
            && selector.Pattern.Replace('\\', '/').Split('/').Any(segment => segment.Contains("**")
    && segment != "**"))
        {
            throw new ArgumentException("Double star must be a complete directory segment.");
        }

        _ = DirectoryPatterns.Matches(selector, "validation");
    }
}
