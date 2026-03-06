namespace Graft.Models;

internal sealed record CreateWorktreeResult(bool IsSuccess, string? ErrorMessage, WorktreeInfo? Worktree);
