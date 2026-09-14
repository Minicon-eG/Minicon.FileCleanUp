using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Minicon.FileCleanUp;

/// <summary>Runs one cleanup using an isolated options snapshot and injectable system boundaries.</summary>
public sealed class FileCleanUpService(
    CleanupOptions options,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ICleanupAuditJournal? auditJournal = null,
    ILogger<FileCleanUpService>? logger = null,
    IRetryJitter? retryJitter = null) : IFileCleanUpService
{
    public Task<CleanupResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var snapshot = JsonSerializer.Deserialize<CleanupOptions>(JsonSerializer.Serialize(options))!;
        return new CleanupRun(snapshot, fileSystem, timeProvider, auditJournal, logger, retryJitter ?? new RandomRetryJitter(), cancellationToken, directory => new LocalAuditJournal(directory, fileSystem, timeProvider)).Execute();
    }
}

