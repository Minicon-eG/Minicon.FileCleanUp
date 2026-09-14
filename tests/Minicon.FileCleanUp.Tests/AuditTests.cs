using System.IO.Abstractions;

namespace Minicon.FileCleanUp.Tests;

public class AuditTests
{
    [Fact]
    public void Unfinished_intent_is_recovered_and_can_be_acknowledged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "minicon-audit-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var op = Guid.NewGuid();
            using (var journal = new LocalAuditJournal(dir, new FileSystem()))
            {
                journal.Append(new AuditRecord { Type = "DeleteIntent", OperationId = op, Path = "/data/old", IsDirectory = false });
            }

            using (var journal = new LocalAuditJournal(dir, new FileSystem()))
            {
                Assert.Equal("/data/old", Assert.Single(journal.GetPending()).Path);
                journal.Acknowledge(op, "operator", "manually reviewed");
            }

            using var final = new LocalAuditJournal(dir, new FileSystem());
            Assert.Empty(final.GetPending());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Removing_an_initialized_journal_must_not_silently_reset_pending_protection()
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var dir = Path.Combine(Path.GetTempPath(), "audit-loss");
        fs.Directory.CreateDirectory(dir);
        using (var journal = new LocalAuditJournal(dir, fs))
        {
            journal.Append(new() { Type = "DeleteIntent", OperationId = Guid.NewGuid(), Path = "old.csv" });
        }

        fs.File.Delete(Path.Combine(dir, "audit.journal"));
        Assert.ThrowsAny<IOException>(() => new LocalAuditJournal(dir, fs));
    }

    [Fact]
    public void Truncation_at_a_complete_frame_boundary_is_detected()
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var dir = Path.Combine(Path.GetTempPath(), "audit-truncated");
        fs.Directory.CreateDirectory(dir);
        using (var journal = new LocalAuditJournal(dir, fs))
        {
            journal.Append(new() { Type = "DeleteIntent", OperationId = Guid.NewGuid(), Path = "old.csv" });
        }

        fs.File.WriteAllBytes(Path.Combine(dir, "audit.journal"), []);
        Assert.ThrowsAny<IOException>(() => new LocalAuditJournal(dir, fs));
    }

    [Fact]
    public void Rotation_preserves_pending_operations_across_segments_and_detects_a_missing_segment()
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var dir = Path.Combine(Path.GetTempPath(), "audit-rotation");
        fs.Directory.CreateDirectory(dir);
        var pending = Guid.NewGuid();
        using (var journal = new LocalAuditJournal(dir, fs, maxSegmentBytes: 1024))
        {
            journal.Append(new() { Type = "DeleteIntent", OperationId = pending, Path = "keep-pending" });
            for (var i = 0; i < 20; i++)
            {
                journal.Append(new() { Type = "RunStarted", Reason = new string('x', 300) });
            }
        }

        Assert.NotEmpty(fs.Directory.GetFiles(dir, "*.archive"));
        using (var journal = new LocalAuditJournal(dir, fs))
        {
            Assert.Equal(pending, Assert.Single(journal.GetPending()).OperationId);
        }

        fs.File.Delete(fs.Directory.GetFiles(dir, "*.archive").Order().First());
        Assert.ThrowsAny<IOException>(() => new LocalAuditJournal(dir, fs));
    }

    [Fact]
    public void Oversized_record_is_rejected_without_damaging_the_journal()
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var dir = Path.Combine(Path.GetTempPath(), "audit-large");
        fs.Directory.CreateDirectory(dir);
        using (var journal = new LocalAuditJournal(dir, fs))
        {
            Assert.Throws<ArgumentException>(() => journal.Append(new() { Type = "RunStarted", Reason = new string('x', 16_777_216) }));
            journal.Append(new() { Type = "DeleteIntent", OperationId = Guid.NewGuid(), Path = "old.csv" });
        }

        using var reopened = new LocalAuditJournal(dir, fs);
        Assert.Single(reopened.GetPending());
    }

    [Fact]
    public void Concurrent_callers_do_not_corrupt_a_journal_session()
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var dir = Path.Combine(Path.GetTempPath(), "audit-concurrent");
        fs.Directory.CreateDirectory(dir);
        using (var journal = new LocalAuditJournal(dir, fs))
        {
            Parallel.For(
                0,
                200,
                i => journal.Append(new() { Type = "DeleteIntent", OperationId = Guid.NewGuid(), Path = $"{i}.csv" }));
        }

        using var reopened = new LocalAuditJournal(dir, fs);
        Assert.Equal(200, reopened.GetPending().Count);
    }
}
