namespace Minicon.FileCleanUp;

public sealed record AuditRecord
{
    public string Type { get; init; } = "";
    public Guid OperationId { get; init; }
    public Guid RunId { get; init; }
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
    public int AttemptNumber { get; init; } = 1;
    public string? RuleName { get; init; }
    public DateTimeOffset? CutoffUtc { get; init; }
    public DateTimeOffset? EvaluatedTimestampUtc { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? Operator { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
}
