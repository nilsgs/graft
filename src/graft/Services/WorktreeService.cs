using System.Collections.Concurrent;
using Graft.Models;
using Graft.Output;

namespace Graft.Services;

internal sealed class WorktreeService
{
    private readonly GitService _gitService;
    private readonly WorktreePathService _worktreePathService;
    private readonly ConsoleFormatter _formatter;

    public WorktreeService(GitService gitService, WorktreePathService worktreePathService, ConsoleFormatter formatter)
    {
        _gitService = gitService;
        _worktreePathService = worktreePathService;
        _formatter = formatter;
    }

    public async Task<CreateWorktreeResult> CreateAsync(
        string repositoryRoot,
        string branchName,
        CreateBranchBase branchBase,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Preparing managed worktree for '{branchName}'...");

        var targetPath = _worktreePathService.GetManagedWorktreePath(repositoryRoot, branchName);
        var managedRoot = _worktreePathService.GetManagedRoot(repositoryRoot);

        try
        {
            Directory.CreateDirectory(managedRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CreateWorktreeResult(false, $"Could not prepare managed worktree directory '{managedRoot}': {ex.Message}", null, null);
        }

        try
        {
            if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                return new CreateWorktreeResult(false, $"Target path already exists: {targetPath}", null, null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CreateWorktreeResult(false, $"Could not inspect target path '{targetPath}': {ex.Message}", null, null);
        }

        var branchResolution = await ResolveBranchAsync(repositoryRoot, branchName, progress, ct);
        var warningMessage = GetIgnoredBaseWarning(branchResolution, branchName, branchBase);

        var createBaseResult = await ResolveCreateBaseAsync(repositoryRoot, branchBase, ct);
        if (!createBaseResult.IsSuccess)
        {
            return new CreateWorktreeResult(false, createBaseResult.ErrorMessage, null, null);
        }

        string[] createArgs = branchResolution switch
        {
            BranchResolution.ExistingLocal => ["worktree", "add", targetPath, branchName],
            BranchResolution.ExistingRemote => ["worktree", "add", "--track", "-b", branchName, targetPath, $"origin/{branchName}"],
            _ => ["worktree", "add", "-b", branchName, targetPath, createBaseResult.GitRef!]
        };

        progress?.Report(GetCreateProgressMessage(branchResolution, branchName, createBaseResult.DisplayName!));

        var result = await _gitService.RunAsync(repositoryRoot, ct, createArgs);
        if (!result.IsSuccess)
        {
            return new CreateWorktreeResult(false, GetErrorMessage(result), null, null);
        }

        return new CreateWorktreeResult(true, null, CreateWorktreeInfo(targetPath, branchName), warningMessage);
    }

    public async Task<ListWorktreesResult> ListAsync(
        string repositoryRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Reading Git worktrees...");

        var result = await _gitService.RunAsync(repositoryRoot, ct, "worktree", "list", "--porcelain");
        if (!result.IsSuccess)
        {
            return new ListWorktreesResult(false, GetErrorMessage(result), null);
        }

        return new ListWorktreesResult(true, null, ParseWorktrees(repositoryRoot, result.StandardOutput));
    }

    public async Task<RemoveWorktreeResult> RemoveAsync(
        string repositoryRoot,
        string target,
        bool force,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Resolving worktree '{target}'...");

        var listResult = await ListAsync(repositoryRoot, null, ct);
        if (!listResult.IsSuccess)
        {
            return new RemoveWorktreeResult(false, ExitCodes.GitFailure, listResult.ErrorMessage, null);
        }

        var worktree = FindWorktree(listResult.Worktrees!, target);
        if (worktree is null)
        {
            return new RemoveWorktreeResult(false, ExitCodes.RefusedRemoval, $"Worktree not found: {target}", null);
        }

        var arguments = new List<string> { "worktree", "remove" };
        if (force)
        {
            arguments.Add("--force");
        }

        arguments.Add(worktree.Path);
        progress?.Report($"Removing worktree '{worktree.BranchName}'...");

        var result = await _gitService.RunAsync(repositoryRoot, ct, [.. arguments]);
        if (!result.IsSuccess)
        {
            return new RemoveWorktreeResult(
                false,
                force ? ExitCodes.GitFailure : ExitCodes.RefusedRemoval,
                GetErrorMessage(result),
                null);
        }

        return new RemoveWorktreeResult(true, ExitCodes.Success, null, worktree);
    }

    public async Task<CleanupResult> CleanupAsync(
        string repositoryRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var listResult = await ListAsync(repositoryRoot, null, ct);
        if (!listResult.IsSuccess)
        {
            return new CleanupResult(false, ExitCodes.GitFailure, listResult.ErrorMessage, []);
        }

        progress?.Report("Inspecting managed worktrees for cleanup candidates...");
        var candidates = await GetCleanupCandidatesAsync(repositoryRoot, listResult.Worktrees!, progress, ct);
        if (candidates.Count == 0)
        {
            return new CleanupResult(true, ExitCodes.Success, "No removable managed worktrees found.", []);
        }

        var selectedCandidates = _formatter.PromptForCleanup(candidates);
        if (selectedCandidates.Count == 0)
        {
            return new CleanupResult(true, ExitCodes.Success, "No worktrees selected.", []);
        }

        if (!_formatter.ConfirmRemoval(selectedCandidates.Count))
        {
            return new CleanupResult(true, ExitCodes.Success, "Cleanup cancelled.", []);
        }

        var removedWorktrees = new List<WorktreeInfo>();
        foreach (var candidate in selectedCandidates)
        {
            progress?.Report($"Removing '{candidate.Worktree.BranchName}'...");
            var removeResult = await RemoveAsync(repositoryRoot, candidate.Worktree.Path, true, null, ct);
            if (!removeResult.IsSuccess)
            {
                return new CleanupResult(false, removeResult.ExitCode, removeResult.ErrorMessage, removedWorktrees);
            }

            removedWorktrees.Add(candidate.Worktree);
        }

        return new CleanupResult(true, ExitCodes.Success, null, removedWorktrees);
    }

    public async Task<CommandResult> PruneAsync(
        string repositoryRoot,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Pruning stale Git worktree metadata...");
        return await _gitService.RunAsync(repositoryRoot, ct, "worktree", "prune");
    }

    private async Task<BranchResolution> ResolveBranchAsync(
        string repositoryRoot,
        string branchName,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Resolving branch '{branchName}'...");

        var localResultTask = _gitService.RunAsync(repositoryRoot, ct, "show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
        var remoteResultTask = _gitService.RunAsync(repositoryRoot, ct, "show-ref", "--verify", "--quiet", $"refs/remotes/origin/{branchName}");
        await Task.WhenAll(localResultTask, remoteResultTask);

        var localResult = await localResultTask;
        if (localResult.IsSuccess)
        {
            return BranchResolution.ExistingLocal;
        }

        var remoteResult = await remoteResultTask;
        if (remoteResult.IsSuccess)
        {
            return BranchResolution.ExistingRemote;
        }

        return BranchResolution.NewBranch;
    }

    private async Task<(bool IsSuccess, string? GitRef, string? DisplayName, string? ErrorMessage)> ResolveCreateBaseAsync(
        string repositoryRoot,
        CreateBranchBase branchBase,
        CancellationToken ct)
    {
        switch (branchBase)
        {
            case CreateBranchBase.CurrentHead:
                return (true, "HEAD", "HEAD", null);

            case CreateBranchBase.Main:
            {
                var localMainExists = await GitRefExistsAsync(repositoryRoot, "refs/heads/main", ct);
                if (localMainExists)
                {
                    return (true, "main", "main", null);
                }

                var originMainExists = await GitRefExistsAsync(repositoryRoot, "refs/remotes/origin/main", ct);
                if (originMainExists)
                {
                    return (true, "origin/main", "origin/main", null);
                }

                return (false, null, null, "Could not find local 'main' or 'origin/main' to create the new branch from.");
            }

            case CreateBranchBase.OriginMain:
            {
                var originMainExists = await GitRefExistsAsync(repositoryRoot, "refs/remotes/origin/main", ct);
                return originMainExists
                    ? (true, "origin/main", "origin/main", null)
                    : (false, null, null, "Could not find 'origin/main' to create the new branch from.");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(branchBase), branchBase, null);
        }
    }

    private async Task<IReadOnlyList<WorktreeCandidate>> GetCleanupCandidatesAsync(
        string repositoryRoot,
        IReadOnlyList<WorktreeInfo> worktrees,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var mergedBranches = await GetMergedBranchesAsync(repositoryRoot, ct);
        var managedWorktrees = worktrees.Where(item => item.IsManaged).ToArray();
        if (managedWorktrees.Length == 0)
        {
            return [];
        }

        progress?.Report($"Inspecting {managedWorktrees.Length} managed worktree(s)...");

        var candidates = new ConcurrentBag<WorktreeCandidate>();
        var processedCount = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
        };

        await Parallel.ForEachAsync(managedWorktrees, parallelOptions, async (worktree, token) =>
        {
            var candidate = await GetCleanupCandidateAsync(repositoryRoot, worktree, mergedBranches, token);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }

            var currentCount = Interlocked.Increment(ref processedCount);
            if (progress is not null && ShouldReportInspectionProgress(currentCount, managedWorktrees.Length))
            {
                progress.Report($"Inspected {currentCount}/{managedWorktrees.Length} managed worktree(s)...");
            }
        });

        return candidates
            .OrderBy(item => item.Worktree.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<HashSet<string>> GetMergedBranchesAsync(string repositoryRoot, CancellationToken ct)
    {
        var result = await _gitService.RunAsync(repositoryRoot, ct, "branch", "--merged", "HEAD");
        if (!result.IsSuccess)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('*').Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<WorktreeCandidate?> GetCleanupCandidateAsync(
        string repositoryRoot,
        WorktreeInfo worktree,
        IReadOnlySet<string> mergedBranches,
        CancellationToken ct)
    {
        var reasons = new List<string>();

        if (!Directory.Exists(worktree.Path))
        {
            reasons.Add("missing path");
        }
        else
        {
            var statusResult = await _gitService.RunAsync(worktree.Path, ct, "status", "--short");
            if (statusResult.IsSuccess && string.IsNullOrWhiteSpace(statusResult.StandardOutput))
            {
                reasons.Add("clean");
            }
        }

        if (!string.IsNullOrWhiteSpace(worktree.BranchName) && worktree.BranchName != "(detached)")
        {
            var upstreamResult = await _gitService.RunAsync(repositoryRoot, ct, "rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{worktree.BranchName}");
            if (!upstreamResult.IsSuccess)
            {
                reasons.Add("gone upstream");
            }

            if (mergedBranches.Contains(worktree.BranchName))
            {
                reasons.Add("merged into current HEAD");
            }
        }

        return reasons.Count == 0
            ? null
            : new WorktreeCandidate(worktree, string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<WorktreeInfo> ParseWorktrees(string repositoryRoot, string output)
    {
        var normalized = output.Replace("\r\n", "\n");
        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var worktrees = new List<WorktreeInfo>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var path = string.Empty;
            var branchName = "(detached)";
            var headCommit = string.Empty;
            var isBare = false;
            var isDetached = false;
            var isLocked = false;
            var isPrunable = false;
            var statuses = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("worktree ", StringComparison.Ordinal))
                {
                    path = line["worktree ".Length..];
                    continue;
                }

                if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                {
                    headCommit = line["HEAD ".Length..];
                    continue;
                }

                if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                {
                    branchName = line["branch refs/heads/".Length..];
                    continue;
                }

                switch (line)
                {
                    case "bare":
                        isBare = true;
                        statuses.Add("bare");
                        break;
                    case "detached":
                        isDetached = true;
                        statuses.Add("detached");
                        break;
                    case "locked":
                        isLocked = true;
                        statuses.Add("locked");
                        break;
                    case "prunable":
                        isPrunable = true;
                        statuses.Add("prunable");
                        break;
                }
            }

            if (statuses.Count == 0)
            {
                statuses.Add("ok");
            }

            worktrees.Add(new WorktreeInfo(
                path,
                branchName,
                headCommit,
                isBare,
                isDetached,
                isLocked,
                isPrunable,
                !string.IsNullOrWhiteSpace(path) && _worktreePathService.IsManagedPath(repositoryRoot, path),
                statuses));
        }

        return worktrees;
    }

    private static WorktreeInfo? FindWorktree(IReadOnlyList<WorktreeInfo> worktrees, string target)
    {
        var targetPath = Path.GetFullPath(target);
        return worktrees.FirstOrDefault(worktree =>
            worktree.BranchName.Equals(target, StringComparison.OrdinalIgnoreCase)
            || PathsEqual(worktree.Path, target)
            || PathsEqual(worktree.Path, targetPath));
    }

    private static bool PathsEqual(string left, string right)
    {
        return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static WorktreeInfo CreateWorktreeInfo(string targetPath, string branchName)
    {
        return new WorktreeInfo(targetPath, branchName, string.Empty, false, false, false, false, true, ["ok", "created"]);
    }

    private async Task<bool> GitRefExistsAsync(string repositoryRoot, string gitRef, CancellationToken ct)
    {
        var result = await _gitService.RunAsync(repositoryRoot, ct, "show-ref", "--verify", "--quiet", gitRef);
        return result.IsSuccess;
    }

    private static string GetCreateProgressMessage(BranchResolution branchResolution, string branchName, string baseDisplayName)
    {
        return branchResolution switch
        {
            BranchResolution.ExistingLocal => $"Creating worktree from local branch '{branchName}'...",
            BranchResolution.ExistingRemote => $"Creating tracking worktree from 'origin/{branchName}'...",
            _ => $"Creating new branch '{branchName}' from {baseDisplayName}..."
        };
    }

    private static string? GetIgnoredBaseWarning(BranchResolution branchResolution, string branchName, CreateBranchBase branchBase)
    {
        if (branchResolution is BranchResolution.NewBranch || branchBase is CreateBranchBase.CurrentHead)
        {
            return null;
        }

        var flagName = branchBase == CreateBranchBase.Main
            ? "--from-main"
            : "--from-origin-main";

        return $"Ignoring {flagName} because branch '{branchName}' already exists.";
    }

    private static string GetErrorMessage(CommandResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return message.Trim();
    }

    private static bool ShouldReportInspectionProgress(int processedCount, int totalCount)
    {
        return processedCount == totalCount || processedCount % 5 == 0;
    }
}

