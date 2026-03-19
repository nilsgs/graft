namespace Graft.Models;

internal sealed record NavigateContextResult(
    bool IsFailure,
    int ExitCode,
    NavigateLocationKind? LocationKind,
    string? RepositoryRoot,
    string? SharedRootDirectory);
