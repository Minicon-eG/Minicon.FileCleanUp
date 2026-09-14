namespace Minicon.FileCleanUp;

public interface ICleanupAuditJournal : IDisposable
{
    void Append(AuditRecord record);
    IReadOnlyList<AuditRecord> GetPending();
    void Acknowledge(Guid operationId, string @operator, string reason);
}
