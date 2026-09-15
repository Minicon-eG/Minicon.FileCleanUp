using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Minicon.FileCleanUp.Tests;

public class ConfigurationTests
{
    [Theory]
    [InlineData("14", "10", "14", "10", CleanupStatus.Succeeded)]
    [InlineData("30", "10", null, null, CleanupStatus.InvalidConfiguration)]
    [InlineData("14", "-1", null, null, CleanupStatus.InvalidConfiguration)]
    [InlineData("14", "10", "15", null, CleanupStatus.InvalidConfiguration)]
    [InlineData("14", "10", null, "20", CleanupStatus.InvalidConfiguration)]
    [InlineData("14", "10", null, null, CleanupStatus.Succeeded)]
    public async Task Legacy_global_names_are_supported_without_ignoring_conflicts(
        string minimumAge,
        string delay,
        string? modernMinimum,
        string? modernDelay,
        CleanupStatus expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-settings");
        var file = Path.Combine(root, "old.txt");
        var fs = new MockFileSystem();
        fs.AddFile(file, new MockFileData("old")
        {
            LastWriteTime = DateTimeOffset.Parse("2020-01-01T00:00:00Z")
        });
        var values = new Dictionary<string, string?>
        {
            ["FileCleanUp:DryRun"] = "true",
            ["FileCleanUp:DelayBetweenDelete"] = delay,
            ["FileCleanUp:MinimumAgeInDays"] = minimumAge,
            ["FileCleanUp:Rules:0:Name"] = "Legacy",
            ["FileCleanUp:Rules:0:RetentionDays"] = "14",
            ["FileCleanUp:Rules:0:Directories:0:Root"] = root
        };
        if (modernMinimum is not null)
        {
            values["FileCleanUp:MinimumRetentionDays"] = modernMinimum;
        }
        if (modernDelay is not null)
        {
            values["FileCleanUp:DeleteDelayMilliseconds"] = modernDelay;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddFileCleanUp(configuration.GetSection("FileCleanUp"));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IFileCleanUpService>().RunAsync();

        Assert.Equal(expected, result.Status);
        Assert.True(fs.File.Exists(file));
        Assert.Equal(0, result.Statistics.FilesDeleted);
        if (expected == CleanupStatus.Succeeded)
        {
            Assert.Equal(1, result.Statistics.CandidateFiles);
        }
    }

    [Fact]
    public async Task Host_configuration_is_bound_and_later_provider_wins()
    {
        var root = Path.Combine(Path.GetTempPath(), "minicon-config");
        var fs = new MockFileSystem();
        var file = Path.Combine(root, "old.csv");
        fs.AddFile(
            file,
            new MockFileData("old") { LastWriteTime = DateTimeOffset.Parse("2020-01-01T00:00:00Z") });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FileCleanUp:DryRun"] = "true", ["FileCleanUp:Rules:0:Name"] = "Test", ["FileCleanUp:Rules:0:RetentionDays"] = "90", ["FileCleanUp:Rules:0:Directories:0:Root"] = root }).AddInMemoryCollection(new Dictionary<string, string?> { ["FileCleanUp:DryRun"] = "false" }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse("2026-09-14T00:00:00Z")));
        services.AddFileCleanUp(config.GetSection("FileCleanUp"));
        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IFileCleanUpService>().RunAsync();
        Assert.False(fs.File.Exists(file));
        Assert.Equal(1, result.Statistics.FilesDeleted);
    }

    [Fact]
    public async Task Unknown_configuration_returns_invalid_result_instead_of_throwing_from_DI()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FileCleanUp:Typo"] = "true" }).Build();
        var services = new ServiceCollection().AddFileCleanUp(config.GetSection("FileCleanUp"));
        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IFileCleanUpService>().RunAsync();
        Assert.Equal(CleanupStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task Complete_settings_contract_can_be_bound()
    {
        var fs = new MockFileSystem();
        var root = Path.Combine(Path.GetTempPath(), "full-settings");
        fs.Directory.CreateDirectory(root);
        var values = new Dictionary<string, string?>
        {
            ["FileCleanUp:DryRun"] = "true",
            ["FileCleanUp:MinimumRetentionDays"] = "14",
            ["FileCleanUp:DeleteDelayMilliseconds"] = "0",
            ["FileCleanUp:MaxFilesToDeletePerRun"] = "5",
            ["FileCleanUp:Resilience:MaxAttempts"] = "1",
            ["FileCleanUp:Resilience:InitialDelaySeconds"] = "2",
            ["FileCleanUp:Resilience:MaxDelaySeconds"] = "30",
            ["FileCleanUp:Resilience:MaxRetryElapsedSecondsPerOperation"] = "120",
            ["FileCleanUp:Resilience:MaxRetryElapsedSecondsPerRun"] = "300",
            ["FileCleanUp:Rules:0:Name"] = "Test",
            ["FileCleanUp:Rules:0:RetentionDays"] = "90",
            ["FileCleanUp:Rules:0:Timestamp"] = "LastWriteTimeUtc",
            ["FileCleanUp:Rules:0:CheckDates"] = "CreationTimeUtc, LastWriteTimeUtc, LastAccessTimeUtc",
            ["FileCleanUp:Rules:0:Directories:0:Root"] = root
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddFileCleanUp(config.GetSection("FileCleanUp"));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            CleanupStatus.Succeeded,
            (await provider.GetRequiredService<IFileCleanUpService>().RunAsync()).Status);
    }
}
