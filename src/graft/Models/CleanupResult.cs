namespace Graft.Models;

internal sealed record CleanupResult(bool IsSuccess, int ExitCode, string? Message, IReadOnlyList<WorktreeInfo> RemovedWorktrees);
