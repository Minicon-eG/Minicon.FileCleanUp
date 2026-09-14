using System.IO.Abstractions;
using Microsoft.Extensions.Time.Testing;
namespace Minicon.FileCleanUp.Tests;

public class RealFileSystemTests
{
    [Fact]
    public async Task Real_file_system_deletes_only_old_files_and_preserves_root()
    {
        // Only this test-owned directory is ever deleted by this test.
        var work = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var root = Path.Combine(work, "data"); var journalPath = Path.Combine(work, "journal");
            Directory.CreateDirectory(root); Directory.CreateDirectory(journalPath);
            for (var i = 0; i < 100; i++)
            {
                var file = Path.Combine(root, $"old-{i}.csv"); await File.WriteAllTextAsync(file, "old");
                File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }
            var recent = Path.Combine(root, "recent.csv"); await File.WriteAllTextAsync(recent, "keep");
            var now = new DateTimeOffset(2026, 9, 14, 2, 0, 0, TimeSpan.Zero); File.SetLastWriteTimeUtc(recent, now.UtcDateTime);
            var options = new CleanupOptions
            {
                DryRun = false,
                Audit = new() { Mode = AuditMode.Required, JournalDirectory = journalPath },
                Rules = [new() { Name = "Real", RetentionDays = 90, Recursive = true, RemoveEmptyDirectories = true, Directories = [new() { Root = root }] }]
            };
            var result = await new FileCleanUpService(options, new FileSystem(), new FakeTimeProvider(now)).RunAsync();
            Assert.Equal(CleanupStatus.Succeeded, result.Status);
            Assert.Equal(100, result.Statistics.FilesDeleted);
            Assert.Equal(recent, Assert.Single(Directory.GetFiles(root)));
            using var journal = new LocalAuditJournal(journalPath, new FileSystem()); Assert.Empty(journal.GetPending());
        }
        finally { Directory.Delete(work, true); }
    }
}
