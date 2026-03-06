namespace Graft.Models;

internal sealed record WorktreeInfo(
    string Path,
    string BranchName,
    string HeadCommit,
    bool IsBare,
    bool IsDetached,
    bool IsLocked,
    bool IsPrunable,
    bool IsManaged,
    IReadOnlyList<string> Statuses);
