using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;

namespace Minicon.FileCleanUp.Tests;

public class RecoveryTests
{
    [Fact]
    public void Recovery_service_inspects_and_acknowledges_a_pending_operation()
    {
        var fs = new MockFileSystem();
        var dir = Path.Combine(Path.GetTempPath(), "recovery");
        fs.Directory.CreateDirectory(dir);
        var operation = Guid.NewGuid();
        using (var journal = new LocalAuditJournal(dir, fs))
        {
            journal.Append(new() { Type = "DeleteIntent", Path = "data/file", OperationId = operation });
        }

        var recovery = new CleanupAuditRecoveryService(dir, fs, new FakeTimeProvider());
        Assert.Equal(operation, Assert.Single(recovery.Inspect()).OperationId);
        recovery.Acknowledge(operation, "operator", "checked against backup");
        Assert.Empty(recovery.Inspect());
    }
}
