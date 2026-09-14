namespace Minicon.FileCleanUp;

public sealed class ResilienceOptions
{
    public int MaxAttempts { get; set; } = 6;
    public int InitialDelaySeconds { get; set; } = 2;
    public int MaxDelaySeconds { get; set; } = 30;
    public int MaxRetryElapsedSecondsPerOperation { get; set; } = 120;
    public int MaxRetryElapsedSecondsPerRun { get; set; } = 300;
}
