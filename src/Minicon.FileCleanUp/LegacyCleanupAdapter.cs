using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Minicon.FileCleanUp;

internal static class LegacyCleanupAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<CheckDates>() }
    };

    internal static CleanupOptions Convert(CleanupOptions options, IEnumerable<string> ruleJson, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(ruleJson);
        if (options.Rules.Count != 0)
        {
            throw new ArgumentException("Do not combine modern Rules with a legacy rule provider.");
        }

        foreach (var json in ruleJson)
        {
            var legacy = JsonSerializer.Deserialize<LegacyRule>(json, JsonOptions)
                ?? throw new ArgumentException("A legacy rule must not be null.");
            if (legacy.MaxAgeDateTime is null && legacy.MaxAgeInDays is null)
            {
                throw new ArgumentException("MaxAgeInDays or MaxAgeDateTime is required.");
            }

            var sources = legacy.SourceDirectories ?? (legacy.SourceDirectory is { } singleSource ? [singleSource] : []);
            var rule = new CleanupRuleOptions
            {
                Name = legacy.Name,
                RetentionDays = legacy.MaxAgeInDays ?? options.MinimumRetentionDays,
                MaxAgeDateTime = legacy.MaxAgeDateTime is { } date
                    ? new DateTimeOffset(date.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                        : date).ToUniversalTime()
                    : null,
                CheckDates = legacy.CheckDates,
                Recursive = legacy.Recursive,
                RemoveEmptyDirectories = legacy.RemoveEmptyDirectory,
                IncludePatterns = [legacy.Filter == "*.*" ? "*" : legacy.Filter],
                ExcludePatterns = legacy.DeleteExclusions?.Files?.ToList() ?? []
            };
            foreach (var source in sources)
            {
                rule.Directories.Add(Source(source, fs));
            }

            var exclusions = (legacy.IgnoreSubdirectories ?? [])
                .Select(pattern => (Pattern: pattern, PreserveOnly: false))
                .Concat((legacy.DeleteExclusions?.Directories ?? [])
                    .Select(pattern => (Pattern: pattern, PreserveOnly: true)));
            foreach (var entry in exclusions)
            {
                var exclusion = entry.Pattern;
                if (string.IsNullOrWhiteSpace(exclusion))
                {
                    throw new ArgumentException("An exclusion must not be empty.");
                }

                var path = exclusion.Replace('\\', '/').TrimEnd('/');
                var expression = Regex.Escape(path).Replace("\\*", ".*").Replace("\\?", "[^/]");
                // Bare names match at every directory depth. Paths match from the full path root.
                if (!path.Contains('/'))
                {
                    expression = "(?:.*/)?" + expression;
                }

                rule.ExcludeDirectories.Add(new DirectorySelector
                {
                    Kind = SelectorKind.Regex,
                    MatchFullPath = true,
                    PreserveDirectoryOnly = entry.PreserveOnly,
                    Pattern = entry.PreserveOnly ? expression : expression + "(?:/.*)?"
                });
            }

            options.Rules.Add(rule);
        }

        return options;
    }

    private static DirectorySelector Source(string path, IFileSystem fs)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Source directory must not be empty.");
        }

        var normalized = path.Replace('\\', fs.Path.DirectorySeparatorChar).Replace('/', fs.Path.DirectorySeparatorChar);
        var wildcard = normalized.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            return new DirectorySelector { Root = normalized };
        }

        var separator = normalized.LastIndexOf(fs.Path.DirectorySeparatorChar, wildcard);
        if (separator <= 0)
        {
            throw new ArgumentException("Wildcard sources require a fixed absolute parent directory.");
        }

        return new DirectorySelector
        {
            Kind = SelectorKind.Glob,
            Root = normalized[..separator],
            Pattern = normalized[(separator + 1)..]
        };
    }

    private sealed class LegacyRule
    {
        public string Name { get; set; } = "";
        public string? SourceDirectory { get; set; }
        public string[]? SourceDirectories { get; set; }
        public string Filter { get; set; } = "*.*";
        public int? MaxAgeInDays { get; set; }
        public DateTime? MaxAgeDateTime { get; set; }
        public bool RemoveEmptyDirectory { get; set; }
        public bool Recursive { get; set; }
        public CheckDates CheckDates { get; set; } = CheckDates.LastWriteTimeUtc;
        public string[]? IgnoreSubdirectories { get; set; }
        public Exclusions? DeleteExclusions { get; set; }
    }

    private sealed class Exclusions
    {
        public string[]? Files { get; set; }
        public string[]? Directories { get; set; }
    }
}
