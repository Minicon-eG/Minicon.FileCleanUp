namespace Minicon.FileCleanUp;

public sealed class AuditOptions
{
    public AuditMode Mode { get; set; }
    public string? JournalDirectory { get; set; }
}
