using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Minicon.FileCleanUp.Tests;

public class LegacyProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Existing_rule_json_selects_wildcards_and_protects_named_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-provider");
        var fs = new MockFileSystem();
        var delete = Path.Combine(root, "R1", "Export", "old");
        var excluded = Path.Combine(root, "R1", "Export", "Eingang", "old.xml");
        AddOld(fs, delete);
        AddOld(fs, excluded);
        var json = JsonSerializer.Serialize(new
        {
            Name = "Exports",
            SourceDirectories = new[] { Path.Combine(root, "R*", "Export") },
            Filter = "*.*",
            MaxAgeInDays = 30,
            MaxAgeDateTime = (DateTime?)null,
            Recursive = true,
            RemoveEmptyDirectory = true,
            CheckDates = 7,
            IgnoreSubdirectories = Array.Empty<string>(),
            DeleteExclusions = new { Directories = new[] { "Eingang" }, Files = Array.Empty<string>() }
        });

        var result = await Run(fs, json);

        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.False(fs.File.Exists(delete));
        Assert.True(fs.File.Exists(excluded));
        Assert.Equal(1, result.Statistics.FilesDeleted);
    }

    [Fact]
    public async Task Shared_search_root_and_nonrecursive_parent_do_not_hide_archive_rule()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-overlap");
        var fs = new MockFileSystem();
        var incoming = Path.Combine(root, "R1", "Inbox", "old.txt");
        var archived = Path.Combine(root, "R1", "Inbox", "Archive", "old.txt");
        AddOld(fs, incoming);
        AddOld(fs, archived);
        string Rule(string name, string pattern, bool recursive) => JsonSerializer.Serialize(new
        {
            Name = name,
            SourceDirectories = new[] { Path.Combine(root, pattern) },
            MaxAgeInDays = 30,
            Recursive = recursive,
            CheckDates = 7
        });

        var result = await Run(fs, Rule("Inbox", "R*/Inbox", false), Rule("Archive", "R*/Inbox/Archive", true));

        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.False(fs.File.Exists(incoming));
        Assert.False(fs.File.Exists(archived));
        Assert.Equal(2, result.Statistics.FilesDeleted);
        Assert.Equal(1, result.RuleStatistics["Inbox"].FilesDeleted);
        Assert.Equal(1, result.RuleStatistics["Archive"].FilesDeleted);
    }

    [Fact]
    public async Task Legacy_path_wildcards_and_file_exclusions_preserve_matching_items()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-exclusions");
        var fs = new MockFileSystem();
        var ignored = Path.Combine(root, "R2S", "Export", "old.txt");
        var protectedFile = Path.Combine(root, "R1", "Export", "status.log");
        var deleted = Path.Combine(root, "R1", "Export", "old.txt");
        AddOld(fs, ignored);
        AddOld(fs, protectedFile);
        AddOld(fs, deleted);
        var json = JsonSerializer.Serialize(new
        {
            Name = "Exports",
            SourceDirectories = new[] { Path.Combine(root, "R*", "Export") },
            MaxAgeInDays = 30,
            Recursive = true,
            CheckDates = 7,
            IgnoreSubdirectories = new[] { Path.Combine(root, "R*S", "*") },
            DeleteExclusions = new { Files = new[] { "status.log" } }
        });

        var result = await Run(fs, json);

        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.True(fs.File.Exists(ignored));
        Assert.True(fs.File.Exists(protectedFile));
        Assert.False(fs.File.Exists(deleted));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"Name":"Unsupported","MaxAgeDateTime":"2020-01-01T00:00:00Z"}""")]
    [InlineData("""{"UnknownOption":true}""")]
    public async Task Invalid_later_rule_prevents_all_deletions(string invalidJson)
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-invalid");
        var fs = new MockFileSystem();
        var file = Path.Combine(root, "old.txt");
        AddOld(fs, file);
        var validJson = JsonSerializer.Serialize(new
        {
            Name = "Valid",
            SourceDirectory = root,
            MaxAgeInDays = 30
        });

        var result = await Run(fs, validJson, invalidJson);

        Assert.Equal(CleanupStatus.InvalidConfiguration, result.Status);
        Assert.True(fs.File.Exists(file));
    }

    [Fact]
    public async Task Conflicting_recursive_targets_prevent_all_deletions()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-conflict");
        var fs = new MockFileSystem();
        var file = Path.Combine(root, "R1", "Archive", "old.txt");
        AddOld(fs, file);
        string Rule(string name, string pattern) => JsonSerializer.Serialize(new
        {
            Name = name,
            SourceDirectories = new[] { Path.Combine(root, pattern) },
            MaxAgeInDays = 30,
            Recursive = true
        });

        var result = await Run(fs, Rule("Parent", "R*"), Rule("Child", "R*/Archive"));

        Assert.Equal(CleanupStatus.InvalidConfiguration, result.Status);
        Assert.True(fs.File.Exists(file));
    }

    [Theory]
    [InlineData("2026-06-01T00:00:00Z")]
    [InlineData("2026-06-01T02:00:00+02:00")]
    [InlineData("2026-06-01T00:00:00")]
    public async Task Fixed_cutoff_deletes_only_files_strictly_before_it(string cutoff)
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-cutoff");
        var fs = new MockFileSystem();
        var before = Path.Combine(root, "before.txt");
        var equal = Path.Combine(root, "equal.txt");
        var after = Path.Combine(root, "after.txt");
        var boundary = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        fs.AddFile(before, new MockFileData("old") { LastWriteTime = boundary.AddTicks(-1) });
        fs.AddFile(equal, new MockFileData("equal") { LastWriteTime = boundary });
        fs.AddFile(after, new MockFileData("new") { LastWriteTime = boundary.AddDays(1) });
        var json = JsonSerializer.Serialize(new
        {
            Name = "Fixed",
            SourceDirectory = root,
            MaxAgeDateTime = cutoff
        });

        var result = await Run(fs, json);

        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.False(fs.File.Exists(before));
        Assert.True(fs.File.Exists(equal));
        Assert.True(fs.File.Exists(after));
    }

    [Theory]
    [InlineData(null, "2026-09-10T00:00:00Z", "2026-09-05T00:00:00Z")]
    [InlineData(100, "2026-08-01T00:00:00Z", "2026-07-01T00:00:00Z")]
    [InlineData(30, "2026-06-01T00:00:00Z", "2026-07-01T00:00:00Z")]
    public async Task Fixed_date_and_age_limits_use_the_stricter_boundary(
        int? days, string cutoff, string timestamp)
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-combined");
        var fs = new MockFileSystem();
        var file = Path.Combine(root, "protected.txt");
        fs.AddFile(file, new MockFileData("data")
        {
            LastWriteTime = DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture)
        });
        var json = JsonSerializer.Serialize(new
        {
            Name = "Combined",
            SourceDirectory = root,
            MaxAgeInDays = days,
            MaxAgeDateTime = cutoff
        });

        var result = await Run(fs, json);

        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.True(fs.File.Exists(file));
        Assert.Equal(0, result.Statistics.FilesDeleted);
    }

    private static void AddOld(MockFileSystem fs, string path)
    {
        fs.AddFile(path, new MockFileData("data")
        {
            CreationTime = Now.AddDays(-200),
            LastWriteTime = Now.AddDays(-200),
            LastAccessTime = Now.AddDays(-200)
        });
    }

    private static async Task<CleanupResult> Run(MockFileSystem fs, params string[] rules)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileCleanUp:DryRun"] = "false",
            ["FileCleanUp:DelayBetweenDelete"] = "0",
            ["FileCleanUp:MinimumAgeInDays"] = "14"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddFileCleanUp(config.GetSection("FileCleanUp"),
            _ => Task.FromResult<IEnumerable<string>>(rules));
        using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IFileCleanUpService>().RunAsync();
    }
}
