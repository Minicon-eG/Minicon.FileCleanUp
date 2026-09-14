using System.IO.Abstractions;
namespace Minicon.FileCleanUp;

public interface ICleanupAuditRecoveryService
{
    IReadOnlyList<AuditRecord> Inspect();
    void Acknowledge(Guid operationId, string @operator, string reason);
}
public sealed class CleanupAuditRecoveryService(string directory, IFileSystem fileSystem, TimeProvider timeProvider) : ICleanupAuditRecoveryService
{
    public IReadOnlyList<AuditRecord> Inspect() { using var journal = new LocalAuditJournal(directory, fileSystem, timeProvider); return journal.GetPending(); }
    public void Acknowledge(Guid operationId, string @operator, string reason) { using var journal = new LocalAuditJournal(directory, fileSystem, timeProvider); journal.Acknowledge(operationId, @operator, reason); }
}
