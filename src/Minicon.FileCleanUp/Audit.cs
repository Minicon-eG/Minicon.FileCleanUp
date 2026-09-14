using System.IO.Abstractions;
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
public interface ICleanupAuditJournal : IDisposable
{
    void Append(AuditRecord record);
    IReadOnlyList<AuditRecord> GetPending();
    void Acknowledge(Guid operationId, string @operator, string reason);
}
public sealed class LocalAuditJournal : ICleanupAuditJournal
{
    private readonly FileSystemStream stream;
    private readonly List<AuditRecord> records = [];
    private string previousHash = new('0', 64);
    private long sequence;
    private bool poisoned;
    private readonly TimeProvider time;
    public LocalAuditJournal(string directory, IFileSystem fileSystem, TimeProvider? timeProvider = null)
    {
        time = timeProvider ?? TimeProvider.System;
        stream = fileSystem.FileStream.New(fileSystem.Path.Combine(directory, "audit.journal"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            while (stream.Position < stream.Length)
            {
                var lengthBytes = new byte[4]; stream.ReadExactly(lengthBytes);
                var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length is < 1 or > 16_777_216) throw new IOException("Invalid audit frame length.");
                var bytes = new byte[length]; stream.ReadExactly(bytes);
                var frame = System.Text.Json.JsonSerializer.Deserialize<Frame>(bytes) ?? throw new IOException("Invalid audit frame.");
                if (frame.Sequence != sequence + 1 || frame.PreviousHash != previousHash || frame.Hash != Hash(frame.Sequence, frame.PreviousHash, frame.Payload))
                    throw new IOException("Audit integrity check failed.");
                var record = System.Text.Json.JsonSerializer.Deserialize<AuditRecord>(frame.Payload) ?? throw new IOException("Invalid audit record.");
                records.Add(record); sequence = frame.Sequence; previousHash = frame.Hash;
            }
        }
        catch { stream.Dispose(); throw; }
    }
    public void Append(AuditRecord record)
    {
        if (poisoned) throw new IOException("Journal requires recovery after a failed append.");
        var payload = System.Text.Json.JsonSerializer.Serialize(record);
        var hash = Hash(sequence + 1, previousHash, payload);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Frame(sequence + 1, previousHash, payload, hash));
        try
        {
            var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            stream.Write(length); stream.Write(bytes); stream.Flush(flushToDisk: true);
            records.Add(record); sequence++; previousHash = hash;
        }
        catch { poisoned = true; throw; }
    }
    public IReadOnlyList<AuditRecord> GetPending()
    {
        var pending = new Dictionary<Guid, AuditRecord>();
        foreach (var record in records)
        {
            if (record.Type == "DeleteIntent") pending[record.OperationId] = record;
            if (record.Type is "Deleted" or "Failed" or "SkippedAfterRevalidation" or "Acknowledged") pending.Remove(record.OperationId);
        }
        return pending.Values.ToArray();
    }
    public void Acknowledge(Guid operationId, string @operator, string reason)
    {
        if (string.IsNullOrWhiteSpace(@operator) || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Operator and reason are required.");
        var pending = GetPending().Single(x => x.OperationId == operationId);
        Append(pending with { Type = "Acknowledged", Operator = @operator, Reason = reason, TimestampUtc = time.GetUtcNow() });
    }
    private static string Hash(long sequence, string previous, string payload) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sequence + ":" + previous + ":" + payload)));
    public void Dispose() => stream.Dispose();
    private sealed record Frame(long Sequence, string PreviousHash, string Payload, string Hash);
}
