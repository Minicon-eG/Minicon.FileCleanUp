using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Minicon.FileCleanUp;

internal sealed class CleanupRun(
    CleanupOptions options,
    IFileSystem fs,
    TimeProvider clock,
    ICleanupAuditJournal? providedJournal,
    ILogger? logger,
    IRetryJitter jitter,
    CancellationToken token,
    Func<string, ICleanupAuditJournal> createJournal)
{
    private readonly CleanupResult result = new();
    private readonly HashSet<string> visited = new(ConfigurationValidator.Comparer);
    private readonly HashSet<string> protectedRoots = new(ConfigurationValidator.Comparer);
    private readonly HashSet<string> virtuallyRemoved = new(ConfigurationValidator.Comparer);
    private readonly HashSet<string> incomplete = new(ConfigurationValidator.Comparer);
    private readonly List<AuditRecord> pending = [];
    private readonly HashSet<string> unavailableScopes = new(ConfigurationValidator.Comparer);
    private ICleanupAuditJournal? journal;
    private bool auditFailed;
    private long runStart;
    private double retrySeconds;
    private bool attemptedMutation;
    private CleanupRuleOptions? currentRule;
    private DirectorySelector? currentSelector;
    internal async Task<CleanupResult> Execute()
    {
        result.StartedUtc = clock.GetUtcNow();
        runStart = clock.GetTimestamp();
        Event("CleanupStarted", 1000);
        try
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ConfigurationValidator.Validate(options, fs);
            }
            catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
            {
                result.Status = CleanupStatus.InvalidConfiguration;
                Event("ConfigurationInvalid", 3002, ex);
                return result;
            }

            foreach (var rule in options.Rules)
            {
                _ = RetentionPolicy.Cutoff(result.StartedUtc, rule);
            }

            result.ConfigFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(options)));
            Event("ConfigurationValidated", 1001);
            if (options.Audit.Mode == AuditMode.Required)
            {
                if (providedJournal is null)
                {
                    var directory = options.Audit.JournalDirectory ?? throw new ArgumentException("Audit.JournalDirectory is required.");
                    EnsureSafePath(directory);
                    journal = createJournal(directory);
                }
                else
                {
                    journal = providedJournal;
                }

                pending.AddRange(journal.GetPending());
                Audit(new() { Type = "RunStarted", RunId = result.RunId, TimestampUtc = clock.GetUtcNow(), Reason = result.ConfigFingerprint });
            }

            var targets = await ResolveTargets();
            if (TargetsConflict(targets))
            {
                result.Status = CleanupStatus.InvalidConfiguration;
                Event("ConfigurationInvalid", 3002, new ArgumentException("Resolved cleanup targets overlap."));
                return result;
            }

            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                currentRule = target.Rule;
                currentSelector = target.Selector;
                var scope = StorageScope(target.Path);
                if (unavailableScopes.Contains(scope))
                {
                    Error(target.Path, new IOException("Storage scope unavailable."));
                    continue;
                }

                Event("RuleStarted", 1100, path: target.Path);
                try
                {
                    await ProcessTarget(target);
                }
                catch (AuditUnavailableException)
                {
                    throw;
                }
                catch (RetryBudgetException)
                {
                    throw;
                }
                catch (IOException ex)
                {
                    Error(target.Path, ex);
                    if (IsTransient(ex))
                    {
                        unavailableScopes.Add(scope);
                        Event("StorageScopeUnavailable", 3203, ex, target.Path);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Error(target.Path, ex);
                }

                Event("RuleCompleted", 1199, path: target.Path);
            }
        }
        catch (LimitReachedException)
        {
            result.LimitReached = true;
            if (result.Status == CleanupStatus.Succeeded)
            {
                result.Status = CleanupStatus.Limited;
            }

            Event("CleanupLimitReached", 3100);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result.Status = CleanupStatus.Cancelled;
            Event("CleanupCancelled", 3101);
        }
        catch (AuditUnavailableException ex)
        {
            auditFailed = true;
            result.Status = CleanupStatus.Failed;
            Count(s => s.ErrorCount++);
            Event("AuditUnavailable", 3301, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            result.Status = CleanupStatus.Failed;
            Count(s => s.ErrorCount++);
            Event("CleanupFailed", 3999, ex);
        }
        finally
        {
            result.CompletedUtc = clock.GetUtcNow();
            result.Duration = clock.GetElapsedTime(runStart);
            result.IsPartial = result.Status != CleanupStatus.Succeeded;
            if (journal is not null
                && !auditFailed)
            {
                try
                {
                    Audit(new() { Type = "RunCompleted", RunId = result.RunId, TimestampUtc = clock.GetUtcNow(), Reason = JsonSerializer.Serialize(result) });
                }
                catch (AuditUnavailableException ex)
                {
                    result.Status = CleanupStatus.Failed;
                    result.IsPartial = true;
                    Event("AuditUnavailable", 3301, ex);
                }
            }

            if (providedJournal is null)
            {
                journal?.Dispose();
            }

            Event("CleanupCompleted", 1999);
        }

        return result;
    }

    private async Task<List<Target>> ResolveTargets()
    {
        var targets = new List<Target>();
        foreach (var rule in options.Rules)
        {
            foreach (var selector in rule.Directories)
            {
                currentRule = rule;
                currentSelector = selector;
                selector.Root = fs.Path.TrimEndingDirectorySeparator(fs.Path.GetFullPath(selector.Root));
                await Read(
                    () =>
                {
                    EnsureSafePath(selector.Root);
                    return true;
                },
                    selector.Root,
                    missingRoot: true);
                var found = 0;
                var queue = new Stack<string>();
                if (selector.Kind == SelectorKind.Path)
                {
                    Add(selector.Root);
                }
                else
                {
                    queue.Push(selector.Root);
                    while (queue.TryPop(out var directory))
                    {
                        foreach (var child in await Read(() => fs.Directory.EnumerateDirectories(directory).ToArray(), directory))
                        {
                            if (Excluded(rule, selector, child)
                                || await IsLink(child))
                            {
                                continue;
                            }

                            if (DirectoryPatterns.Matches(selector, fs.Path.GetRelativePath(selector.Root, child)))
                            {
                                Add(child);
                            }

                            if (DirectoryPatterns.MayContainTarget(selector, fs.Path.GetRelativePath(selector.Root, child)))
                            {
                                queue.Push(child);
                            }
                        }
                    }
                }

                Event(
                    found == 0 ? "DirectorySelectionEmpty" : "DirectorySelectionCompleted",
                    found == 0 ? 1011 : 1012,
                    path: selector.Root);
                void Add(string path)
                {
                    if (Excluded(rule, selector, path))
                    {
                        return;
                    }

                    found++;
                    if (targets.Any(t => t.Rule == rule
                        && ConfigurationValidator.Comparer.Equals(t.Path, path)))
                    {
                        return;
                    }

                    if (targets.Count >= 10000)
                    {
                        throw new IOException("Directory selection exceeded 10000 targets.");
                    }

                    targets.Add(new(rule, selector, path));
                    protectedRoots.Add(path);
                    Event("DirectorySelected", 1010, path: path);
                }
            }
        }

        return targets;
    }

    private static bool TargetsConflict(List<Target> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            for (var j = i + 1; j < targets.Count; j++)
            {
                var left = targets[i];
                var right = targets[j];
                if (left.Rule == right.Rule)
                {
                    continue;
                }

                if (ConfigurationValidator.Comparer.Equals(left.Path, right.Path)
                    || (left.Rule.Recursive && ConfigurationValidator.Contains(left.Path, right.Path))
                    || (right.Rule.Recursive && ConfigurationValidator.Contains(right.Path, left.Path)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task ProcessTarget(Target target)
    {
        var stack = new Stack<(string Path, bool Finish)>();
        stack.Push((target.Path, false));
        while (stack.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            if (entry.Finish)
            {
                await RemoveEmpty(entry.Path, target);
                continue;
            }

            if (Blocked(entry.Path, directory: true))
            {
                Skip(entry.Path, "AuditPending", true);
                continue;
            }

            if (!visited.Add(entry.Path))
            {
                continue;
            }

            try
            {
                await Read(
                    () =>
                {
                    EnsureSafePath(entry.Path);
                    return true;
                },
                    entry.Path);
                // Materialize one complete directory before changing any of its entries.
                var files = await Read(() => fs.Directory.EnumerateFiles(entry.Path).ToArray(), entry.Path);
                var children = target.Rule.Recursive ? await Read(() => fs.Directory.EnumerateDirectories(entry.Path).ToArray(), entry.Path) : [];
                Count(s => s.DirectoriesScanned++);
                if (target.Rule.RemoveEmptyDirectories)
                {
                    stack.Push((entry.Path, true));
                }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await ProcessFile(file, target);
                    }
                    catch (AuditUnavailableException)
                    {
                        throw;
                    }
                    catch (RetryBudgetException)
                    {
                        throw;
                    }
                    catch (IOException ex) when (!IsTransient(ex))
                    {
                        Error(file, ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Error(file, ex);
                    }
                }

                foreach (var child in children)
                {
                    if (Excluded(target.Rule, target.Selector, child)
                        || await IsLink(child))
                    {
                        continue;
                    }

                    stack.Push((child, false));
                }
            }
            catch (AuditUnavailableException)
            {
                throw;
            }
            catch (RetryBudgetException)
            {
                throw;
            }
            catch (IOException ex) when (!IsTransient(ex))
            {
                Error(entry.Path, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Error(entry.Path, ex);
            }
        }
    }

    private FileTimestampSnapshot ReadTimestamps(string path, CheckDates selection)
    {
        // Always track writes for mutation revalidation, even when retention uses other dates.
        var lastWrite = fs.File.GetLastWriteTimeUtc(path);
        DateTime? creation = selection.HasFlag(CheckDates.CreationTimeUtc)
            ? fs.File.GetCreationTimeUtc(path)
            : null;
        DateTime? lastAccess = selection.HasFlag(CheckDates.LastAccessTimeUtc)
            ? fs.File.GetLastAccessTimeUtc(path)
            : null;

        return new FileTimestampSnapshot(creation, lastWrite, lastAccess);
    }

    private async Task ProcessFile(string path, Target target)
    {
        Count(s => s.FilesScanned++);
        if (Blocked(path, false))
        {
            Skip(path, "AuditPending");
            return;
        }

        var name = fs.Path.GetFileName(path);
        bool Match(string pattern) => DirectoryPatterns.Matches(new() { Kind = SelectorKind.Glob, Pattern = pattern }, name);
        if (!target.Rule.IncludePatterns.Any(Match)
            || target.Rule.ExcludePatterns.Any(Match))
        {
            Skip(path, "Excluded");
            return;
        }

        var attributes = await Read(() => fs.File.GetAttributes(path), path);
        if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
        {
            Skip(path, "ReadOnlyOrReparsePoint");
            return;
        }

        var observed = await Read(() => ReadTimestamps(path, target.Rule.CheckDates), path);
        var cutoff = RetentionPolicy.Cutoff(result.StartedUtc, target.Rule);
        if (!RetentionPolicy.IsExpired(observed.LatestSelected(target.Rule.CheckDates), cutoff))
        {
            Skip(path, "TooRecent");
            return;
        }

        var bytes = await Read(() => fs.FileInfo.New(path).Length, path);
        Count(s => s.CandidateFiles++);
        Count(s => s.CandidateBytes += bytes);
        var intent = new AuditRecord
        {
            Type = "DeleteIntent",
            RunId = result.RunId,
            OperationId = Guid.NewGuid(),
            Path = path,
            RuleName = target.Rule.Name,
            Reason = "OlderThanRetention",
            EvaluatedTimestampUtc = observed.LatestSelected(target.Rule.CheckDates),
            CheckedDates = target.Rule.CheckDates,
            EvaluatedCreationTimeUtc = observed.CreationTimeUtc,
            EvaluatedLastWriteTimeUtc = observed.LastWriteTimeUtc,
            EvaluatedLastAccessTimeUtc = observed.LastAccessTimeUtc,
            CutoffUtc = cutoff,
            FileSizeBytes = bytes,
            TimestampUtc = clock.GetUtcNow()
        };
        if (options.DryRun)
        {
            virtuallyRemoved.Add(path);
            Audit(intent with { Type = "WouldDelete" });
            Event("FileWouldBeDeleted", 2002, path: path, record: intent);
            CheckLimit();
            return;
        }

        await DelayMutation();
        await Read(
            () =>
        {
            EnsureSafePath(fs.Path.GetDirectoryName(path)!);
            return true;
        },
            path);
        var updated = await Read(() => ReadTimestamps(path, target.Rule.CheckDates), path);
        if (updated != observed)
        {
            Skip(path, "ChangedSinceEvaluation");
            return;
        }

        Audit(intent);
        // Revalidate again after journal persistence, which may itself take time.
        await Read(
            () =>
        {
            EnsureSafePath(fs.Path.GetDirectoryName(path)!);
            return true;
        },
            path);
        updated = await Read(() => ReadTimestamps(path, target.Rule.CheckDates), path);
        attributes = await Read(() => fs.File.GetAttributes(path), path);
        if (updated != observed
            || (await Read(() => fs.FileInfo.New(path).Length, path) != bytes)
            || (attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
        {
            Audit(intent with { Type = "SkippedAfterRevalidation" });
            Skip(path, "ChangedSinceEvaluation");
            return;
        }

        Event("FileDeleteStarted", 2000, path: path, record: intent);
        Count(s => s.DeleteAttempts++);
        try
        {
            var attempt = 0;
            var deleted = await Read(
                () =>
            {
                token.ThrowIfCancellationRequested();
                if (attempt++ > 0)
                {
                    EnsureSafePath(fs.Path.GetDirectoryName(path)!);
                    if (ReadTimestamps(path, target.Rule.CheckDates) != observed
                        || (fs.FileInfo.New(path).Length != bytes)
                        || (fs.File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                    {
                        Audit(intent with { Type = "SkippedAfterRevalidation" });
                        return false;
                    }

                    intent = intent with
                    {
                        AttemptNumber = attempt
                    };
                    Audit(intent);
                    Event("FileDeleteStarted", 2000, path: path, record: intent);
                    Count(s => s.DeleteAttempts++);
                }

                fs.File.Delete(path);
                return true;
            },
                path,
                mutation: true);
            if (!deleted)
            {
                Skip(path, "ChangedSinceEvaluation");
                return;
            }
        }
        catch (AuditUnavailableException)
        {
            throw;
        }
        catch (RetryBudgetException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Audit(intent with { Type = "Failed" });
            Error(path, ex);
            return;
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            Audit(intent with { Type = "Failed" });
            Error(path, ex);
            return;
        }
        catch (IOException ex)
        {
            // A failed response does not prove that the remote delete did not happen.
            pending.Add(intent);
            Count(s => s.UnknownDeletionOutcomes++);
            incomplete.Add(path);
            Audit(intent with { Type = "OutcomeUnknown" });
            Error(path, ex);
            Event("DeletionOutcomeUnknown", 3204, ex, path, intent);
            CheckLimit();
            return;
        }

        try
        {
            Audit(intent with { Type = "Deleted" });
        }
        catch (AuditUnavailableException)
        {
            Count(s => s.UnknownDeletionOutcomes++);
            throw;
        }

        virtuallyRemoved.Add(path);
        Count(s => s.FilesDeleted++);
        Count(s => s.DeletedFileBytes += bytes);
        Event("FileDeleted", 2001, path: path, record: intent);
        CheckLimit();
    }

    private async Task RemoveEmpty(string path, Target target)
    {
        if (protectedRoots.Contains(path)
            || Blocked(path, true)
            || pending.Any(p => ConfigurationValidator.Contains(path, p.Path))
            || incomplete.Any(p => ConfigurationValidator.Contains(path, p)))
        {
            return;
        }

        var entries = await Read(() => fs.Directory.EnumerateFileSystemEntries(path).ToArray(), path);
        if (entries.Any(p => !options.DryRun
            || !virtuallyRemoved.Contains(p)))
        {
            return;
        }

        var intent = new AuditRecord
        {
            Type = "DeleteIntent",
            RunId = result.RunId,
            OperationId = Guid.NewGuid(),
            Path = path,
            IsDirectory = true,
            RuleName = target.Rule.Name,
            Reason = entries.Length == 0 ? "AlreadyEmpty" : "EmptyAfterCleanup",
            TimestampUtc = clock.GetUtcNow()
        };
        if (options.DryRun)
        {
            virtuallyRemoved.Add(path);
            Count(s => s.DirectoriesWouldBeDeleted++);
            Audit(intent with { Type = "WouldDelete" });
            Event("DirectoryWouldBeDeleted", 2102, path: path, record: intent);
            return;
        }

        await DelayMutation();
        await Read(() =>
        {
            EnsureSafePath(path);
            return true;
        }, path);
        Audit(intent);
        try
        {
            token.ThrowIfCancellationRequested();
            await Read(
                () =>
            {
                EnsureSafePath(path);
                return true;
            },
                path);
            if ((await Read(() => fs.Directory.EnumerateFileSystemEntries(path).ToArray(), path)).Length != 0)
            {
                Audit(intent with { Type = "SkippedAfterRevalidation" });
                Skip(path, "ChangedSinceEvaluation", true);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Audit(intent with { Type = "SkippedAfterRevalidation" });
            throw;
        }

        Event("DirectoryDeleteStarted", 2100, path: path, record: intent);
        try
        {
            fs.Directory.Delete(path, false);
        }
        catch (UnauthorizedAccessException ex)
        {
            Audit(intent with { Type = "Failed" });
            Error(path, ex);
            return;
        }
        catch (IOException ex)
        {
            pending.Add(intent);
            Audit(intent with { Type = "OutcomeUnknown" });
            Error(path, ex);
            Event("DeletionOutcomeUnknown", 3204, ex, path, intent);
            return;
        }

        Audit(intent with { Type = "Deleted" });
        Count(s => s.DirectoriesDeleted++);
        Event("DirectoryDeleted", 2101, path: path, record: intent);
    }

    private bool Blocked(string path, bool directory) => pending.Any(p => p.IsDirectory ? ConfigurationValidator.Contains(p.Path, path) : ConfigurationValidator.Comparer.Equals(p.Path, path));
    private bool Excluded(CleanupRuleOptions rule, DirectorySelector selector, string path)
    {
        if (!ConfigurationValidator.Contains(selector.Root, path))
        {
            throw new IOException("Path escaped the search root.");
        }

        var relative = fs.Path.GetRelativePath(selector.Root, path);
        for (var i = 0; i < rule.ExcludeDirectories.Count; i++)
        {
            var exclusion = rule.ExcludeDirectories[i];
            var candidate = exclusion.MatchFullPath ? path : relative;
            if (DirectoryPatterns.Matches(exclusion, candidate)
                || (exclusion.MatchFullPath && DirectoryPatterns.Matches(exclusion, candidate + "/")))
            {
                Event("DirectorySkipped", 2103, path: path, reason: "Excluded", exclusionIndex: i);
                return true;
            }
        }

        return false;
    }

    private Task<bool> IsLink(string path) => Read(() => (fs.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0, path);
    private void EnsureSafePath(string path)
    {
        for (var current = fs.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = fs.Path.GetDirectoryName(current))
        {
            if ((fs.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Reparse point in path: " + current);
            }
        }
    }

    private async Task DelayMutation()
    {
        token.ThrowIfCancellationRequested();
        if (attemptedMutation
            && options.DeleteDelayMilliseconds > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(options.DeleteDelayMilliseconds), clock, token);
        }

        attemptedMutation = true;
    }

    private void CheckLimit()
    {
        if ((options.DryRun ? result.Statistics.CandidateFiles : result.Statistics.FilesDeleted + result.Statistics.UnknownDeletionOutcomes) >= options.MaxFilesToDeletePerRun)
        {
            throw new LimitReachedException();
        }
    }

    private async Task<T> Read<T>(Func<T> operation, string path, bool missingRoot = false, bool mutation = false)
    {
        long? started = null;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (retrySeconds + (started.HasValue ? clock.GetElapsedTime(started.Value).TotalSeconds : 0) >= options.Resilience.MaxRetryElapsedSecondsPerRun)
                {
                    throw new RetryBudgetException();
                }

                try
                {
                    var value = operation();
                    if (started.HasValue)
                    {
                        Event(
                            "FileSystemRecoverySucceeded",
                            3201,
                            path: path,
                            attemptNumber: attempt,
                            retryElapsedSeconds: clock.GetElapsedTime(started.Value).TotalSeconds);
                    }

                    return value;
                }
                catch (IOException ex) when (mutation ? (ex.HResult & 0xffff) is 32 or 33 : IsTransient(ex)
                    || (missingRoot
    && ex is FileNotFoundException or DirectoryNotFoundException))
                {
                    started ??= clock.GetTimestamp();
                    var elapsed = clock.GetElapsedTime(started.Value).TotalSeconds;
                    var remaining = Math.Min(
                        options.Resilience.MaxRetryElapsedSecondsPerOperation - elapsed,
                        options.Resilience.MaxRetryElapsedSecondsPerRun - retrySeconds - elapsed);
                    if (attempt >= options.Resilience.MaxAttempts
                        || remaining <= 0)
                    {
                        Event(
                            "FileSystemRetryExhausted",
                            3202,
                            ex,
                            path,
                            attemptNumber: attempt,
                            retryElapsedSeconds: elapsed);
                        throw;
                    }

                    var delay = Math.Min(
                        remaining,
                        Math.Min(
    options.Resilience.MaxDelaySeconds,
    options.Resilience.InitialDelaySeconds * Math.Pow(2, Math.Min(attempt - 1, 30)) * jitter.NextFactor()));
                    Count(s => s.RetryCount++);
                    Event(
                        "FileSystemRetryScheduled",
                        3200,
                        ex,
                        path,
                        attemptNumber: attempt,
                        retryDelaySeconds: delay,
                        retryElapsedSeconds: elapsed);
                    await Task.Delay(TimeSpan.FromSeconds(delay), clock, token);
                    if (clock.GetElapsedTime(started.Value).TotalSeconds >= options.Resilience.MaxRetryElapsedSecondsPerOperation)
                    {
                        throw;
                    }
                }
            }
        }
        finally
        {
            if (started.HasValue)
            {
                retrySeconds += clock.GetElapsedTime(started.Value).TotalSeconds;
            }
        }
    }

    private static bool IsTransient(IOException ex) => (ex.HResult & 0xffff) is 21 or 53 or 64 or 67 or 121 or 1231 or 1232 or 1236 or 1460;
    private string StorageScope(string path)
    {
        if (path.StartsWith("\\\\"))
        {
            return string.Join('\\', path.Split('\\', StringSplitOptions.RemoveEmptyEntries).Take(2));
        }

        return fs.Path.GetPathRoot(path) ?? path;
    }

    private void Audit(AuditRecord record)
    {
        if (options.Audit.Mode != AuditMode.Required)
        {
            return;
        }

        try
        {
            (journal ?? throw new IOException("Audit journal unavailable.")).Append(record with { TimestampUtc = clock.GetUtcNow() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            auditFailed = true;
            throw new AuditUnavailableException(ex);
        }
    }

    private void Skip(string path, string reason, bool directory = false)
    {
        Count(s => s.SkippedByReason[reason] = s.SkippedByReason.GetValueOrDefault(reason) + 1);
        if (reason == "AuditPending")
        {
            incomplete.Add(path);
            result.Status = CleanupStatus.CompletedWithErrors;
        }

        Event(
            directory ? "DirectorySkipped" : "FileSkipped",
            directory ? 2103 : 2003,
            path: path,
            reason: reason);
    }

    private void Count(Action<CleanupStatistics> update)
    {
        update(result.Statistics);
        if (currentRule is null)
        {
            return;
        }

        if (!result.RuleStatistics.TryGetValue(currentRule.Name, out var statistics))
        {
            result.RuleStatistics[currentRule.Name] = statistics = new();
        }

        update(statistics);
    }

    private void Error(string path, Exception ex)
    {
        result.Status = CleanupStatus.CompletedWithErrors;
        Count(s => s.ErrorCount++);
        incomplete.Add(path);
        Event("FileSystemOperationFailed", 3000, ex, path);
    }

    private void Event(
        string type,
        int id,
        Exception? error = null,
        string? path = null,
        AuditRecord? record = null,
        string? reason = null,
        int? attemptNumber = null,
        double? retryDelaySeconds = null,
        double? retryElapsedSeconds = null,
        int? exclusionIndex = null)
    {
        if (logger is null)
        {
            return;
        }

        var properties = new Dictionary<string, object?>
        {
            ["{OriginalFormat}"] = "{EventType}: {Path}",
            ["EventType"] = type,
            ["EventSchemaVersion"] = 1,
            ["RunId"] = result.RunId,
            ["DryRun"] = options.DryRun,
            ["Path"] = path,
            ["RuleName"] = currentRule?.Name,
            ["SearchRoot"] = currentSelector?.Root,
            ["SelectorKind"] = currentSelector?.Kind.ToString(),
            ["SelectorPattern"] = currentSelector?.Pattern,
            ["OperationId"] = record?.OperationId,
            ["AttemptNumber"] = attemptNumber ?? record?.AttemptNumber,
            ["EventTimestampUtc"] = clock.GetUtcNow(),
            ["RunStartedUtc"] = result.StartedUtc,
            ["SelectorIndex"] = currentSelector is null ? null : currentRule?.Directories.IndexOf(currentSelector),
            ["ExclusionIndex"] = exclusionIndex,
            ["RetryDelaySeconds"] = retryDelaySeconds,
            ["RetryElapsedSeconds"] = retryElapsedSeconds,
            ["MaxAttempts"] = options.Resilience?.MaxAttempts,
            ["UnknownDeletionOutcomes"] = result.Statistics.UnknownDeletionOutcomes,
            ["DirectoriesDeleted"] = result.Statistics.DirectoriesDeleted,
            ["CandidateBytes"] = result.Statistics.CandidateBytes,
            ["DeletedFileBytes"] = result.Statistics.DeletedFileBytes,
            ["ReasonCode"] = reason ?? record?.Reason,
            ["EvaluatedTimestampUtc"] = record?.EvaluatedTimestampUtc,
            ["CutoffUtc"] = record?.CutoffUtc,
            ["FileSizeBytes"] = record?.FileSizeBytes,
            ["TimestampBasis"] = currentRule?.CheckDates.ToString(),
            ["EvaluatedCreationTimeUtc"] = record?.EvaluatedCreationTimeUtc,
            ["EvaluatedLastWriteTimeUtc"] = record?.EvaluatedLastWriteTimeUtc,
            ["EvaluatedLastAccessTimeUtc"] = record?.EvaluatedLastAccessTimeUtc,
            ["RetentionDays"] = currentRule?.RetentionDays,
            ["Status"] = result.Status.ToString(),
            ["CandidateFiles"] = result.Statistics.CandidateFiles,
            ["FilesDeleted"] = result.Statistics.FilesDeleted,
            ["ErrorCount"] = result.Statistics.ErrorCount,
            ["RetryCount"] = result.Statistics.RetryCount,
            ["Duration"] = result.Duration,
            ["AuditMode"] = options.Audit?.Mode.ToString(),
            ["ConfigFingerprint"] = result.ConfigFingerprint,
            ["NativeErrorCode"] = error is IOException io ? io.HResult & 0xffff : null
        };
        var level = type is "FileSkipped" or "DirectorySkipped" ? LogLevel.Debug : error is not null ? LogLevel.Error : LogLevel.Information;
        if (type is "FileSystemRetryScheduled" or "DirectorySelectionEmpty" or "CleanupLimitReached")
        {
            level = LogLevel.Warning;
        }

        if (type == "CleanupCompleted"
            && result.Status is CleanupStatus.Failed or CleanupStatus.CompletedWithErrors or CleanupStatus.InvalidConfiguration)
        {
            level = LogLevel.Error;
        }

        logger.Log(
            level,
            new EventId(id, type),
            properties,
            error,
            (state, _) => $"{state["EventType"]}: {state["Path"]}");
    }

    private sealed record Target(CleanupRuleOptions Rule, DirectorySelector Selector, string Path);
    private sealed class LimitReachedException : Exception;
    private sealed class AuditUnavailableException(Exception inner) : IOException("Audit persistence failed.", inner);
    private sealed class RetryBudgetException : IOException
    {
        public RetryBudgetException() : base("Global retry budget exhausted.")
        {
        }
    }
}
