namespace Minicon.FileCleanUp;

public sealed class DirectorySelector
{
    public bool MatchFullPath { get; set; }
    public SelectorKind Kind { get; set; }
    public string? Pattern { get; set; }
    public string Root { get; set; } = "";
}
