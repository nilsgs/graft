namespace Graft.Models;

internal sealed record ListWorktreesResult(bool IsSuccess, string? ErrorMessage, IReadOnlyList<WorktreeInfo>? Worktrees);
