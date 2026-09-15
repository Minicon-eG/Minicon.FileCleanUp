using Microsoft.Extensions.Time.Testing;
using System.IO.Abstractions.TestingHelpers;

namespace Minicon.FileCleanUp.Tests;

public class TimestampSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(4, true)]
    [InlineData(3, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public async Task Every_selected_timestamp_must_be_older_than_cutoff(int selection, bool shouldDelete)
    {
        var root = Path.Combine(Path.GetTempPath(), "timestamp-selection");
        var file = Path.Combine(root, "file.txt");
        var fs = new MockFileSystem();
        fs.AddFile(file, new MockFileData("data")
        {
            CreationTime = Now.AddDays(-200),
            LastWriteTime = Now.AddDays(-1),
            LastAccessTime = Now.AddDays(-200)
        });
        var options = new CleanupOptions
        {
            DryRun = false,
            Rules = [new CleanupRuleOptions
            {
                Name = "Dates",
                RetentionDays = 100,
                CheckDates = (CheckDates)selection,
                Directories = [new DirectorySelector { Root = root }]
            }]
        };

        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();

        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.Equal(shouldDelete, !fs.File.Exists(file));
        Assert.Equal(shouldDelete ? 1 : 0, result.Statistics.FilesDeleted);
    }
    [Theory]
    [InlineData(1, -100, false)]
    [InlineData(2, -100, false)]
    [InlineData(4, -100, false)]
    [InlineData(7, -101, true)]
    [InlineData(0, -101, false)]
    [InlineData(8, -101, false)]
    public async Task Cutoff_is_exclusive_and_invalid_selections_never_delete(int selection, int age, bool candidate)
    {
        var root = Path.Combine(Path.GetTempPath(), "timestamp-boundary");
        var file = Path.Combine(root, "file.txt");
        var fs = new MockFileSystem();
        fs.AddFile(file, new MockFileData("data")
        {
            CreationTime = Now.AddDays(age),
            LastWriteTime = Now.AddDays(age),
            LastAccessTime = Now.AddDays(age)
        });
        var options = new CleanupOptions
        {
            Rules = [new CleanupRuleOptions
            {
                Name = "Dates",
                RetentionDays = 100,
                CheckDates = (CheckDates)selection,
                Directories = [new DirectorySelector { Root = root }]
            }]
        };

        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();

        Assert.True(fs.File.Exists(file));
        Assert.Equal(candidate ? 1 : 0, result.Statistics.CandidateFiles);
        Assert.Equal(selection is 0 or 8 ? CleanupStatus.InvalidConfiguration : CleanupStatus.Succeeded, result.Status);
    }

    [Theory]
    [InlineData(CheckDates.CreationTimeUtc)]
    [InlineData(CheckDates.LastAccessTimeUtc)]
    public async Task Selected_timestamp_changed_during_audit_prevents_deletion(CheckDates changedDate)
    {
        var root = Path.Combine(Path.GetTempPath(), "timestamp-revalidation");
        var file = Path.Combine(root, "file.txt");
        var fs = new MockFileSystem();
        fs.AddFile(file, new MockFileData("data")
        {
            CreationTime = Now.AddDays(-200),
            LastWriteTime = Now.AddDays(-200),
            LastAccessTime = Now.AddDays(-200)
        });
        var journal = new ChangingJournal(() =>
        {
            if (changedDate == CheckDates.CreationTimeUtc)
            {
                fs.File.SetCreationTimeUtc(file, Now.UtcDateTime);
            }
            else
            {
                fs.File.SetLastAccessTimeUtc(file, Now.UtcDateTime);
            }
        });
        var options = new CleanupOptions
        {
            DryRun = false,
            Audit = new AuditOptions { Mode = AuditMode.Required },
            Rules = [new CleanupRuleOptions
            {
                Name = "Dates",
                RetentionDays = 100,
                CheckDates = (CheckDates)7,
                Directories = [new DirectorySelector { Root = root }]
            }]
        };

        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now), journal).RunAsync();

        Assert.True(fs.File.Exists(file));
        Assert.Equal(0, result.Statistics.FilesDeleted);
        Assert.Equal(1, result.Statistics.SkippedByReason["ChangedSinceEvaluation"]);
        var intent = Assert.Single(journal.Records, record => record.Type == "DeleteIntent");
        Assert.Equal((CheckDates)7, intent.CheckedDates);
        Assert.Equal(Now.AddDays(-200), intent.EvaluatedCreationTimeUtc);
        Assert.Equal(Now.AddDays(-200), intent.EvaluatedLastAccessTimeUtc);
    }

    private sealed class ChangingJournal(Action change) : ICleanupAuditJournal
    {
        public List<AuditRecord> Records { get; } = [];

        public void Append(AuditRecord record)
        {
            Records.Add(record);
            if (record.Type == "DeleteIntent")
            {
                change();
            }
        }

        public IReadOnlyList<AuditRecord> GetPending() => [];
        public void Acknowledge(Guid operationId, string @operator, string reason) { }
        public void Dispose() { }
    }

}
