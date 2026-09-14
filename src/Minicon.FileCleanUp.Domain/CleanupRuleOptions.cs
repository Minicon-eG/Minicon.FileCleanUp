namespace Minicon.FileCleanUp;

public sealed class CleanupRuleOptions
{
    public string Timestamp { get; set; } = "LastWriteTimeUtc";
    public List<string> IncludePatterns { get; set; } = ["*"];
    public List<string> ExcludePatterns { get; set; } = [];
    public bool RemoveEmptyDirectories { get; set; }
    public bool Recursive { get; set; }
    public List<DirectorySelector> ExcludeDirectories { get; set; } = [];
    public string Name { get; set; } = "";
    public int RetentionDays { get; set; }
    public List<DirectorySelector> Directories { get; set; } = [];
}
