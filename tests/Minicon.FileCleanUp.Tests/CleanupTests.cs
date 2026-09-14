using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;
namespace Minicon.FileCleanUp.Tests;

public class CleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 2, 0, 0, TimeSpan.Zero);
    private static string Root => Path.Combine(Path.GetTempPath(), "minicon-test");
    private static string FilePath => Path.Combine(Root, "old.csv");
    private static MockFileSystem Files()
    {
        var fs = new MockFileSystem();
        fs.AddFile(FilePath, new MockFileData("old") { LastWriteTime = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero) });
        return fs;
    }
    private static CleanupOptions Options(bool dryRun = true) => new()
    {
        DryRun = dryRun,
        Rules = [new() { Name = "Exports", RetentionDays = 90, Directories = [new() { Root = Root }] }]
    };

    [Fact]
    public async Task Dry_run_preserves_old_file_and_reports_candidate()
    {
        var fs = Files();
        var service = new FileCleanUpService(Options(), fs, new FakeTimeProvider(Now));
        var result = await service.RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.Equal(1, result.Statistics.CandidateFiles);
        Assert.Equal(0, result.Statistics.FilesDeleted);
    }
    [Fact]
    public async Task Real_run_deletes_old_file_but_keeps_exact_cutoff()
    {
        var fs = Files();
        var boundary = Path.Combine(Root, "boundary.csv");
        fs.AddFile(boundary, new MockFileData("keep") { LastWriteTime = new DateTimeOffset(2026, 6, 16, 2, 0, 0, TimeSpan.Zero) });
        var result = await new FileCleanUpService(Options(false), fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.False(fs.File.Exists(FilePath));
        Assert.True(fs.File.Exists(boundary));
        Assert.Equal(1, result.Statistics.FilesDeleted);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task Invalid_retention_prevents_all_deletions(int days)
    {
        var fs = Files();
        var options = Options(false);
        options.Rules.Add(new() { Name = "Invalid", RetentionDays = days, Directories = [new() { Root = Root }] });
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.Equal(CleanupStatus.InvalidConfiguration, result.Status);
    }
    [Theory]
    [InlineData(SelectorKind.Glob, "**/Exports", "**/Protected")]
    [InlineData(SelectorKind.Regex, "(?:.*/)?Exports", "(?:.*/)?Protected")]
    public async Task Directory_selectors_find_targets_and_exclusions_protect_subtrees(SelectorKind kind, string include, string exclude)
    {
        var fs = Files();
        var target = Path.Combine(Root, "Tenant", "Exports", "old.csv");
        var protectedFile = Path.Combine(Root, "Tenant", "Exports", "Protected", "old.csv");
        fs.AddFile(target, new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        fs.AddFile(protectedFile, new MockFileData("protected") { LastWriteTime = Now.AddYears(-1) });
        var options = Options(false);
        options.Rules[0].Recursive = true;
        options.Rules[0].Directories[0].Kind = kind;
        options.Rules[0].Directories[0].Pattern = include;
        options.Rules[0].ExcludeDirectories = [new() { Kind = kind, Pattern = exclude }];
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.False(fs.File.Exists(target));
        Assert.True(fs.File.Exists(protectedFile));
        Assert.True(fs.File.Exists(FilePath));
        Assert.Equal(1, result.Statistics.FilesDeleted);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_directories_are_predicted_or_removed_but_root_is_retained(bool dryRun)
    {
        var fs = Files();
        var child = Path.Combine(Root, "child");
        fs.AddFile(Path.Combine(child, "old.csv"), new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        var options = Options(dryRun);
        options.Rules[0].Recursive = true;
        options.Rules[0].RemoveEmptyDirectories = true;
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.True(fs.Directory.Exists(Root));
        Assert.Equal(dryRun, fs.Directory.Exists(child));
        Assert.Equal(dryRun ? 1 : 0, result.Statistics.DirectoriesWouldBeDeleted);
        Assert.Equal(dryRun ? 0 : 1, result.Statistics.DirectoriesDeleted);
    }
    [Fact]
    public async Task File_filters_and_readonly_attributes_preserve_ineligible_files()
    {
        var fs = Files();
        var keep = Path.Combine(Root, "keep.csv");
        var other = Path.Combine(Root, "other.txt");
        fs.AddFile(keep, new MockFileData("keep") { LastWriteTime = Now.AddYears(-1) });
        fs.AddFile(other, new MockFileData("other") { LastWriteTime = Now.AddYears(-1) });
        fs.File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        var options = Options(false);
        options.Rules[0].IncludePatterns = ["*.csv"];
        options.Rules[0].ExcludePatterns = ["keep.*"];
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.True(fs.File.Exists(keep));
        Assert.True(fs.File.Exists(other));
        Assert.Equal(0, result.Statistics.FilesDeleted);
    }
    [Theory]
    [InlineData("root")]
    [InlineData("relative")]
    [InlineData("overlap")]
    [InlineData("regex")]
    [InlineData("contradiction")]
    public async Task Invalid_target_configuration_is_rejected_before_deletion(string scenario)
    {
        var fs = Files(); var options = Options(false);
        var rule = options.Rules[0];
        switch (scenario)
        {
            case "root": rule.Directories[0].Root = Path.GetPathRoot(Root)!; break;
            case "relative": rule.Directories[0].Root = "relative"; break;
            case "overlap": options.Rules.Add(new() { Name = "Other", RetentionDays = 90, Directories = [new() { Root = Root }] }); break;
            case "regex": rule.ExcludeDirectories = [new() { Kind = SelectorKind.Regex, Pattern = "[" }]; break;
            case "contradiction": rule.RemoveEmptyDirectories = true; break;
        }
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.Equal(CleanupStatus.InvalidConfiguration, result.Status);
        Assert.True(fs.File.Exists(FilePath));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_stops_at_limit_or_cancellation(bool cancelled)
    {
        var fs = Files();
        fs.AddFile(Path.Combine(Root, "second.csv"), new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        var options = Options(false); options.MaxFilesToDeletePerRun = 1;
        using var cts = new CancellationTokenSource();
        if (cancelled) cts.Cancel();
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync(cts.Token);
        Assert.Equal(cancelled ? CleanupStatus.Cancelled : CleanupStatus.Limited, result.Status);
        Assert.Equal(cancelled ? 0 : 1, result.Statistics.FilesDeleted);
    }
    [Fact]
    public async Task Temporary_listing_failure_recovers_without_duplicate_candidates()
    {
        var fs = Files();
        var directory = new Moq.Mock<System.IO.Abstractions.IDirectory>();
        directory.SetupSequence(d => d.EnumerateFiles(Root))
            .Throws(new IOException("network unavailable", unchecked((int)0x80070040)))
            .Returns(fs.Directory.EnumerateFiles(Root));
        directory.Setup(d => d.EnumerateDirectories(Moq.It.IsAny<string>())).Returns((string p) => fs.Directory.EnumerateDirectories(p));
        var proxy = new Moq.Mock<System.IO.Abstractions.IFileSystem>();
        proxy.SetupGet(f => f.Directory).Returns(directory.Object);
        proxy.SetupGet(f => f.File).Returns(fs.File);
        proxy.SetupGet(f => f.Path).Returns(fs.Path);
        var clock = new FakeTimeProvider(Now);
        var task = new FileCleanUpService(Options(false), proxy.Object, clock).RunAsync();
        for (var i = 0; i < 10000 && !task.IsCompleted; i++) { clock.Advance(TimeSpan.FromMilliseconds(10)); await Task.Yield(); }
        Assert.True(task.IsCompleted);
        var result = await task;
        Assert.False(fs.File.Exists(FilePath));
        Assert.Equal(1, result.Statistics.CandidateFiles);
        Assert.Equal(1, result.Statistics.RetryCount);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unrecoverable_listing_returns_error_without_deletion(bool transient)
    {
        var fs = Files();
        var directory = new Moq.Mock<System.IO.Abstractions.IDirectory>();
        directory.Setup(d => d.EnumerateFiles(Root)).Throws(transient
            ? new IOException("offline", unchecked((int)0x80070040)) : new UnauthorizedAccessException("denied"));
        var proxy = new Moq.Mock<System.IO.Abstractions.IFileSystem>();
        proxy.SetupGet(f => f.Directory).Returns(directory.Object);
        proxy.SetupGet(f => f.File).Returns(fs.File);
        proxy.SetupGet(f => f.Path).Returns(fs.Path);
        var clock = new FakeTimeProvider(Now);
        var task = new FileCleanUpService(Options(false), proxy.Object, clock).RunAsync();
        for (var i = 0; i < 10000 && !task.IsCompleted; i++) { clock.Advance(TimeSpan.FromMilliseconds(10)); await Task.Yield(); }
        Assert.True(task.IsCompleted);
        var result = await task;
        Assert.Equal(CleanupStatus.CompletedWithErrors, result.Status);
        Assert.True(fs.File.Exists(FilePath));
        Assert.Equal(transient ? 5 : 0, result.Statistics.RetryCount);
    }
    [Fact]
    public async Task Audit_intent_failure_prevents_deletion()
    {
        var fs = Files(); var options = Options(false); options.Audit.Mode = AuditMode.Required;
        var journal = new Moq.Mock<ICleanupAuditJournal>();
        journal.Setup(j => j.GetPending()).Returns([]);
        journal.Setup(j => j.Append(Moq.It.Is<AuditRecord>(r => r.Type == "DeleteIntent"))).Throws(new IOException("disk full"));
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now), journal.Object).RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.Equal(CleanupStatus.Failed, result.Status);
    }
    [Fact]
    public async Task Restart_skips_unresolved_path_and_cleans_other_files_with_complete_audit()
    {
        var fs = Files(); var options = Options(false); options.Audit.Mode = AuditMode.Required;
        var other = Path.Combine(Root, "other.csv"); fs.AddFile(other, new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        var dir = Path.Combine(Path.GetTempPath(), "minicon-journal-" + Guid.NewGuid());
        fs.Directory.CreateDirectory(dir);
        using var journal = new LocalAuditJournal(dir, fs);
        journal.Append(new() { Type = "DeleteIntent", Path = FilePath, OperationId = Guid.NewGuid() });
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now), journal).RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.False(fs.File.Exists(other));
        Assert.Equal(FilePath, Assert.Single(journal.GetPending()).Path);
        Assert.Equal(CleanupStatus.CompletedWithErrors, result.Status);
    }
    [Fact]
    public async Task Deletion_event_explains_which_file_was_deleted_and_why()
    {
        var logger = new RecordingLogger<FileCleanUpService>();
        var result = await new FileCleanUpService(Options(false), Files(), new FakeTimeProvider(Now), logger: logger).RunAsync();
        var deleted = Assert.Single(logger.Events, e => Equals(e.GetValueOrDefault("EventType"), "FileDeleted"));
        Assert.Equal(FilePath, deleted["Path"]);
        Assert.Equal("OlderThanRetention", deleted["ReasonCode"]);
        Assert.Equal("Exports", deleted["RuleName"]);
        Assert.Equal(new DateTimeOffset(2026, 6, 16, 2, 0, 0, TimeSpan.Zero), deleted["CutoffUtc"]);
        Assert.Contains(logger.Events, e => Equals(e.GetValueOrDefault("EventType"), "CleanupCompleted"));
    }
    [Fact]
    public async Task Reparse_directory_is_not_traversed()
    {
        var fs = Files(); var options = Options(false); options.Rules[0].Recursive = true;
        var linked = Path.Combine(Root, "linked");
        var file = Path.Combine(linked, "old.csv");
        fs.AddFile(file, new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        fs.File.SetAttributes(linked, FileAttributes.Directory | FileAttributes.ReparsePoint);
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.True(fs.File.Exists(file));
        Assert.False(fs.File.Exists(FilePath));
    }
    [Fact]
    public async Task File_changed_after_audit_intent_is_preserved()
    {
        var fs = Files(); var options = Options(false); options.Audit.Mode = AuditMode.Required;
        var journal = new Moq.Mock<ICleanupAuditJournal>(); journal.Setup(j => j.GetPending()).Returns([]);
        journal.Setup(j => j.Append(Moq.It.Is<AuditRecord>(r => r.Type == "DeleteIntent")))
            .Callback(() => fs.File.SetLastWriteTimeUtc(FilePath, Now.UtcDateTime));
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now), journal.Object).RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.Equal(0, result.Statistics.FilesDeleted);
    }
    [Fact]
    public async Task Required_mode_records_completed_file_and_directory_operations()
    {
        var fs = Files(); var options = Options(false); options.Audit.Mode = AuditMode.Required;
        options.Rules[0].Recursive = true; options.Rules[0].RemoveEmptyDirectories = true;
        fs.Directory.CreateDirectory(Path.Combine(Root, "empty"));
        var records = new List<AuditRecord>();
        var journal = new Moq.Mock<ICleanupAuditJournal>(); journal.Setup(j => j.GetPending()).Returns([]);
        journal.Setup(j => j.Append(Moq.It.IsAny<AuditRecord>())).Callback<AuditRecord>(records.Add);
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now), journal.Object).RunAsync();
        Assert.Equal(2, records.Count(r => r.Type == "DeleteIntent"));
        Assert.Equal(2, records.Count(r => r.Type == "Deleted"));
        Assert.Contains(records, r => r.Type == "RunCompleted");
        Assert.All(records.Where(r => r.Type == "DeleteIntent"), intent => Assert.Contains(records, r => r.Type == "Deleted" && r.OperationId == intent.OperationId));
    }
    [Fact]
    public async Task Network_failure_during_delete_is_not_retried_and_is_reported_as_uncertain()
    {
        var fs = Files(); var options = Options(false); options.Audit.Mode = AuditMode.Required;
        var file = new Moq.Mock<System.IO.Abstractions.IFile>();
        file.Setup(f => f.GetAttributes(Moq.It.IsAny<string>())).Returns((string p) => fs.File.GetAttributes(p));
        file.Setup(f => f.GetLastWriteTimeUtc(Moq.It.IsAny<string>())).Returns((string p) => fs.File.GetLastWriteTimeUtc(p));
        file.Setup(f => f.Delete(FilePath)).Callback(() => fs.File.Delete(FilePath)).Throws(new IOException("response lost", unchecked((int)0x80070040)));
        var proxy = new Moq.Mock<System.IO.Abstractions.IFileSystem>();
        proxy.SetupGet(f => f.File).Returns(file.Object); proxy.SetupGet(f => f.Directory).Returns(fs.Directory); proxy.SetupGet(f => f.Path).Returns(fs.Path); proxy.SetupGet(f => f.FileInfo).Returns(fs.FileInfo);
        var records = new List<AuditRecord>(); var journal = new Moq.Mock<ICleanupAuditJournal>(); journal.Setup(j => j.GetPending()).Returns([]);
        journal.Setup(j => j.Append(Moq.It.IsAny<AuditRecord>())).Callback<AuditRecord>(records.Add);
        var result = await new FileCleanUpService(options, proxy.Object, new FakeTimeProvider(Now), journal.Object).RunAsync();
        Assert.Equal(1, result.Statistics.UnknownDeletionOutcomes);
        Assert.Equal(0, result.Statistics.FilesDeleted);
        Assert.Contains(records, r => r.Type == "OutcomeUnknown");
        Assert.DoesNotContain(records, r => r.Type == "Deleted");
    }
    [Fact]
    public async Task Required_mode_opens_configured_journal_and_completes_it()
    {
        var fs = Files(); var options = Options(false); options.Audit.Mode = AuditMode.Required;
        options.Audit.JournalDirectory = Path.Combine(Path.GetTempPath(), "minicon-audit-configured");
        fs.Directory.CreateDirectory(options.Audit.JournalDirectory);
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        using var journal = new LocalAuditJournal(options.Audit.JournalDirectory, fs);
        Assert.Empty(journal.GetPending());
        Assert.False(fs.File.Exists(FilePath));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_run_has_correlated_start_completion_and_statistics(bool cancelled)
    {
        var logger = new RecordingLogger<FileCleanUpService>();
        var result = await new FileCleanUpService(Options(), Files(), new FakeTimeProvider(Now), logger: logger).RunAsync(new CancellationToken(cancelled));
        var started = Assert.Single(logger.Events, e => Equals(e.GetValueOrDefault("EventType"), "CleanupStarted"));
        var completed = Assert.Single(logger.Events, e => Equals(e.GetValueOrDefault("EventType"), "CleanupCompleted"));
        Assert.Equal(started["RunId"], completed["RunId"]);
        Assert.NotEqual(Guid.Empty, started["RunId"]);
        Assert.Equal(result.Statistics.CandidateFiles, completed["CandidateFiles"]);
    }
    [Fact]
    public async Task Missing_later_target_prevents_any_early_deletion()
    {
        var fs = Files(); var options = Options(false); options.Resilience.MaxAttempts = 1;
        options.Rules[0].Directories.Add(new() { Root = Root + "-missing" });
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.True(fs.File.Exists(FilePath));
        Assert.NotEqual(CleanupStatus.Succeeded, result.Status);
    }
    [Fact]
    public async Task Configured_retry_limit_is_honored()
    {
        var fs = Files(); var options = Options(false); options.Resilience.MaxAttempts = 1;
        var directory = new Moq.Mock<System.IO.Abstractions.IDirectory>();
        directory.Setup(d => d.EnumerateFiles(Root)).Throws(new IOException("offline", unchecked((int)0x80070040)));
        var proxy = new Moq.Mock<System.IO.Abstractions.IFileSystem>(); proxy.SetupGet(f => f.Directory).Returns(directory.Object); proxy.SetupGet(f => f.File).Returns(fs.File); proxy.SetupGet(f => f.Path).Returns(fs.Path);
        using var cts = new CancellationTokenSource();
        var task = new FileCleanUpService(options, proxy.Object, new FakeTimeProvider(Now)).RunAsync(cts.Token);
        var completedWithoutWaiting = task.IsCompleted;
        if (!completedWithoutWaiting) cts.Cancel();
        var result = await task;
        Assert.True(completedWithoutWaiting);
        Assert.Equal(0, result.Statistics.RetryCount);
    }
    [Theory]
    [InlineData("minimum")]
    [InlineData("limit")]
    [InlineData("retry")]
    [InlineData("timestamp")]
    [InlineData("empty-patterns")]
    [InlineData("audit-in-target")]
    public async Task Unsafe_global_settings_prevent_deletion(string kind)
    {
        var fs = Files(); var options = Options(false);
        switch (kind)
        {
            case "minimum": options.MinimumRetentionDays = 100; break;
            case "limit": options.MaxFilesToDeletePerRun = 0; break;
            case "retry": options.Resilience.MaxAttempts = 0; break;
            case "timestamp": options.Rules[0].Timestamp = "LastAccessTimeUtc"; break;
            case "empty-patterns": options.Rules[0].IncludePatterns = []; break;
            case "audit-in-target": options.Audit.Mode = AuditMode.Required; options.Audit.JournalDirectory = Root; break;
        }
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.Equal(CleanupStatus.InvalidConfiguration, result.Status);
        Assert.True(fs.File.Exists(FilePath));
    }
    [Fact]
    public async Task Temporary_metadata_failure_is_retried_too()
    {
        var fs = Files(); var file = new Moq.Mock<System.IO.Abstractions.IFile>();
        file.Setup(f => f.GetAttributes(Moq.It.IsAny<string>())).Returns((string p) => fs.File.GetAttributes(p));
        file.SetupSequence(f => f.GetLastWriteTimeUtc(FilePath)).Throws(new IOException("offline", unchecked((int)0x80070040)))
            .Returns(fs.File.GetLastWriteTimeUtc(FilePath)).Returns(fs.File.GetLastWriteTimeUtc(FilePath));
        var proxy = new Moq.Mock<System.IO.Abstractions.IFileSystem>(); proxy.SetupGet(f => f.Directory).Returns(fs.Directory); proxy.SetupGet(f => f.File).Returns(file.Object); proxy.SetupGet(f => f.Path).Returns(fs.Path); proxy.SetupGet(f => f.FileInfo).Returns(fs.FileInfo);
        var clock = new FakeTimeProvider(Now);
        var task = new FileCleanUpService(Options(), proxy.Object, clock).RunAsync();
        for (var i = 0; i < 10000 && !task.IsCompleted; i++) { clock.Advance(TimeSpan.FromMilliseconds(10)); await Task.Yield(); }
        Assert.True(task.IsCompleted);
        var result = await task;
        Assert.Equal(CleanupStatus.Succeeded, result.Status);
        Assert.Equal(1, result.Statistics.CandidateFiles);
        Assert.Equal(1, result.Statistics.RetryCount);
    }
    [Fact]
    public async Task Configured_delete_delay_is_cancellable_between_files()
    {
        var fs = Files(); fs.AddFile(Path.Combine(Root, "next.csv"), new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        var options = Options(false); options.DeleteDelayMilliseconds = 1000;
        using var cts = new CancellationTokenSource();
        var task = new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync(cts.Token);
        cts.Cancel();
        var result = await task;
        Assert.Equal(CleanupStatus.Cancelled, result.Status);
        Assert.Equal(1, result.Statistics.FilesDeleted);
    }
    [Fact]
    public async Task Temporary_sharing_violation_is_retried_after_revalidation()
    {
        var fs = Files(); var file = new Moq.Mock<System.IO.Abstractions.IFile>();
        file.Setup(f => f.GetAttributes(Moq.It.IsAny<string>())).Returns((string p) => fs.File.GetAttributes(p));
        file.Setup(f => f.GetLastWriteTimeUtc(Moq.It.IsAny<string>())).Returns((string p) => fs.File.GetLastWriteTimeUtc(p));
        var attempts = 0;
        file.Setup(f => f.Delete(FilePath)).Callback(() => { if (++attempts == 1) throw new IOException("sharing violation", unchecked((int)0x80070020)); fs.File.Delete(FilePath); });
        var proxy = new Moq.Mock<System.IO.Abstractions.IFileSystem>(); proxy.SetupGet(f => f.File).Returns(file.Object); proxy.SetupGet(f => f.Directory).Returns(fs.Directory); proxy.SetupGet(f => f.Path).Returns(fs.Path); proxy.SetupGet(f => f.FileInfo).Returns(fs.FileInfo);
        var clock = new FakeTimeProvider(Now);
        var task = new FileCleanUpService(Options(false), proxy.Object, clock).RunAsync();
        for (var i = 0; i < 10000 && !task.IsCompleted; i++) { clock.Advance(TimeSpan.FromMilliseconds(10)); await Task.Yield(); }
        Assert.True(task.IsCompleted); var result = await task;
        Assert.False(fs.File.Exists(FilePath));
        Assert.Equal(1, result.Statistics.FilesDeleted);
        Assert.Equal(1, result.Statistics.RetryCount);
        Assert.Equal(0, result.Statistics.UnknownDeletionOutcomes);
    }
    [Fact]
    public async Task Statistics_are_separate_for_each_rule()
    {
        var fs = Files(); var options = Options(); var otherRoot = Root + "-other";
        fs.AddFile(Path.Combine(otherRoot, "old.csv"), new MockFileData("old") { LastWriteTime = Now.AddYears(-1) });
        options.Rules.Add(new() { Name = "Other", RetentionDays = 90, Directories = [new() { Root = otherRoot }] });
        var result = await new FileCleanUpService(options, fs, new FakeTimeProvider(Now)).RunAsync();
        Assert.Equal(2, result.Statistics.CandidateFiles);
        Assert.Equal(1, result.RuleStatistics["Exports"].CandidateFiles);
        Assert.Equal(1, result.RuleStatistics["Other"].CandidateFiles);
    }
}
