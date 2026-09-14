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
    private FileSystemStream stream = null!;
    private readonly FileSystemStream sessionLock;
    private readonly string directory;
    private readonly long maxSegmentBytes;
    private readonly Dictionary<Guid, AuditRecord> pending = [];
    private string previousHash = new('0', 64);
    private long sequence;
    private bool poisoned;
    private readonly TimeProvider time;
    private readonly IFileSystem fileSystem;
    private readonly string checkpointPath;
    public LocalAuditJournal(string directory, IFileSystem fileSystem, TimeProvider? timeProvider = null, long maxSegmentBytes = 64 * 1024 * 1024)
    {
        if (maxSegmentBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxSegmentBytes));
        this.directory = directory;
        this.maxSegmentBytes = maxSegmentBytes;
        time = timeProvider ?? TimeProvider.System;
        this.fileSystem = fileSystem;
        checkpointPath = fileSystem.Path.Combine(directory, "audit.checkpoint");
        var path = fileSystem.Path.Combine(directory, "audit.journal");
        var marker = fileSystem.Path.Combine(directory, "audit.initialized");
        sessionLock = fileSystem.FileStream.New(fileSystem.Path.Combine(directory, "audit.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var initializedBefore = fileSystem.File.Exists(marker) || fileSystem.File.Exists(checkpointPath);
            stream = fileSystem.FileStream.New(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!fileSystem.File.Exists(marker))
            {
                using var initialized = fileSystem.FileStream.New(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                initialized.Write("Minicon.FileCleanUp audit v1"u8);
                initialized.Flush(flushToDisk: true);
            }
            var checkpoint = initializedBefore
                ? System.Text.Json.JsonSerializer.Deserialize<Checkpoint>(fileSystem.File.ReadAllText(checkpointPath)) ?? throw new IOException("Invalid audit checkpoint.")
                : new Checkpoint(0, previousHash);
            var checkpointVerified = checkpoint.Sequence == 0 && checkpoint.Hash == previousHash;
            foreach (var archive in fileSystem.Directory.GetFiles(directory, "audit.*.archive").Order(StringComparer.Ordinal))
            {
                using var input = fileSystem.FileStream.New(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
                Replay(input);
            }
            Replay(stream);
            void Replay(FileSystemStream input)
            {
                while (input.Position < input.Length)
                {
                    var lengthBytes = new byte[4]; input.ReadExactly(lengthBytes);
                    var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                    if (length is < 1 or > 16_777_216) throw new IOException("Invalid audit frame length.");
                    var bytes = new byte[length]; input.ReadExactly(bytes);
                    var frame = System.Text.Json.JsonSerializer.Deserialize<Frame>(bytes) ?? throw new IOException("Invalid audit frame.");
                    if (frame.Sequence != sequence + 1 || frame.PreviousHash != previousHash || frame.Hash != Hash(frame.Sequence, frame.PreviousHash, frame.Payload))
                        throw new IOException("Audit integrity check failed.");
                    var record = System.Text.Json.JsonSerializer.Deserialize<AuditRecord>(frame.Payload) ?? throw new IOException("Invalid audit record.");
                    Track(record); sequence = frame.Sequence; previousHash = frame.Hash;
                    if (sequence == checkpoint.Sequence) checkpointVerified = previousHash == checkpoint.Hash;
                }
            }
            if (!checkpointVerified) throw new IOException("Audit checkpoint does not match the journal; restore the complete audit directory.");
            if (!initializedBefore) WriteCheckpoint();
        }
        catch { stream?.Dispose(); sessionLock.Dispose(); throw; }
    }
    public void Append(AuditRecord record)
    {
        if (poisoned) throw new IOException("Journal requires recovery after a failed append.");
        var payload = System.Text.Json.JsonSerializer.Serialize(record);
        var hash = Hash(sequence + 1, previousHash, payload);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Frame(sequence + 1, previousHash, payload, hash));
        if (bytes.Length > 16_777_216) throw new ArgumentException("Audit record exceeds the 16 MiB frame limit.", nameof(record));
        try
        {
            if (stream.Length >= maxSegmentBytes)
            {
                stream.Dispose();
                var archive = fileSystem.Path.Combine(directory, $"audit.{sequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture)}.archive");
                fileSystem.File.Move(fileSystem.Path.Combine(directory, "audit.journal"), archive);
                stream = fileSystem.FileStream.New(fileSystem.Path.Combine(directory, "audit.journal"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }
            var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            stream.Write(length); stream.Write(bytes); stream.Flush(flushToDisk: true);
            Track(record); sequence++; previousHash = hash;
            WriteCheckpoint();
        }
        catch { poisoned = true; throw; }
    }
    public IReadOnlyList<AuditRecord> GetPending() => pending.Values.ToArray();
    private void Track(AuditRecord record)
    {
        if (record.Type == "DeleteIntent") pending[record.OperationId] = record;
        if (record.Type is "Deleted" or "Failed" or "SkippedAfterRevalidation" or "Acknowledged") pending.Remove(record.OperationId);
    }
    public void Acknowledge(Guid operationId, string @operator, string reason)
    {
        if (string.IsNullOrWhiteSpace(@operator) || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Operator and reason are required.");
        var pending = GetPending().Single(x => x.OperationId == operationId);
        Append(pending with { Type = "Acknowledged", Operator = @operator, Reason = reason, TimestampUtc = time.GetUtcNow() });
    }
    private void WriteCheckpoint()
    {
        using var checkpoint = fileSystem.FileStream.New(checkpointPath, FileMode.Create, FileAccess.Write, FileShare.None);
        checkpoint.Write(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Checkpoint(sequence, previousHash)));
        checkpoint.Flush(flushToDisk: true);
    }
    private sealed record Checkpoint(long Sequence, string Hash);
    private static string Hash(long sequence, string previous, string payload) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + previous + ":" + payload)));
    public void Dispose()
    {
        try { stream.Dispose(); }
        finally { sessionLock.Dispose(); }
    }
    private sealed record Frame(long Sequence, string PreviousHash, string Payload, string Hash);
}
