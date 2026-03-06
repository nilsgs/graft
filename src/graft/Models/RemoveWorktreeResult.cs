namespace Graft.Models;

internal sealed record RemoveWorktreeResult(bool IsSuccess, int ExitCode, string? ErrorMessage, WorktreeInfo? Worktree);
