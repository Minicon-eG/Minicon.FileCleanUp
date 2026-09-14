namespace Minicon.FileCleanUp;

public interface IFileCleanUpService
{
    Task<CleanupResult> RunAsync(CancellationToken cancellationToken = default);
}
