namespace Minicon.FileCleanUp;

internal sealed record FileTimestampSnapshot(
    DateTime? CreationTimeUtc,
    DateTime LastWriteTimeUtc,
    DateTime? LastAccessTimeUtc)
{
    internal DateTime LatestSelected(CheckDates selection)
    {
        var latest = DateTime.MinValue;

        if (selection.HasFlag(CheckDates.CreationTimeUtc))
        {
            latest = CreationTimeUtc!.Value;
        }

        if (selection.HasFlag(CheckDates.LastWriteTimeUtc) && LastWriteTimeUtc > latest)
        {
            latest = LastWriteTimeUtc;
        }

        if (selection.HasFlag(CheckDates.LastAccessTimeUtc) && LastAccessTimeUtc!.Value > latest)
        {
            latest = LastAccessTimeUtc.Value;
        }

        return latest;
    }
}
