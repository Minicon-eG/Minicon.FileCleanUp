using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;
using Xunit.Abstractions;

namespace Minicon.FileCleanUp.Tests;

public class ScaleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task One_hundred_thousand_candidates_are_counted_once_without_deletion_in_dry_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "scale-test");
        var now = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        var files = Enumerable.Range(0, 100_000).ToDictionary(
            i => Path.Combine(root, $"{i}.csv"),
            _ => new MockFileData("data") { LastWriteTime = now.AddYears(-1) });
        var fs = new MockFileSystem(files);
        var options = new CleanupOptions
        {
            MaxFilesToDeletePerRun = 100_001,
            Rules = [new()
            {
                Name = "Scale",
                RetentionDays = 90,
                Directories = [new()
                {
                    Root = root
                }

                ]
            }

            ]
        };
        var memoryBefore = GC.GetTotalMemory(true);
        var timer = Stopwatch.StartNew();
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(now)).RunAsync();
        output.WriteLine($"100000 in-memory files: {timer.Elapsed}; managed memory delta {GC.GetTotalMemory(false) - memoryBefore} bytes. Not an SMB throughput benchmark.");
        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.Equal(100_000, result.Statistics.CandidateFiles);
        Assert.Equal(400_000, result.Statistics.CandidateBytes);
        Assert.Equal(0, result.Statistics.FilesDeleted);
        Assert.Equal(100_000, fs.Directory.EnumerateFiles(root).Count());
    }
}
