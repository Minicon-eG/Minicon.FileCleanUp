namespace Minicon.FileCleanUp;

/// <summary>Every selected UTC timestamp must precede the retention cutoff.</summary>
[Flags]
public enum CheckDates
{
    None = 0,
    CreationTimeUtc = 1,
    LastWriteTimeUtc = 2,
    LastAccessTimeUtc = 4
}
