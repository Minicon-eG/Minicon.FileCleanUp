namespace Minicon.FileCleanUp;

public sealed class DirectorySelector
{
    /// <summary>Protect only this directory from deletion, allowing traversal and cleanup inside it.</summary>
    public bool PreserveDirectoryOnly { get; set; }
    public bool MatchFullPath { get; set; }
    public SelectorKind Kind { get; set; }
    public string? Pattern { get; set; }
    public string Root { get; set; } = "";
}
