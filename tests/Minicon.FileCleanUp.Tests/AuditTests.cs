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
                journal.Append(new AuditRecord { Type = "DeleteIntent", OperationId = op, Path = "/data/old", IsDirectory = false });
            using (var journal = new LocalAuditJournal(dir, new FileSystem()))
            {
                Assert.Equal("/data/old", Assert.Single(journal.GetPending()).Path);
                journal.Acknowledge(op, "operator", "manually reviewed");
            }
            using var final = new LocalAuditJournal(dir, new FileSystem());
            Assert.Empty(final.GetPending());
        }
        finally { Directory.Delete(dir, true); }
    }
}
