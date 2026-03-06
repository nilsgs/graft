namespace Graft.Models;

internal sealed record RepositoryContextResult(bool IsFailure, int ExitCode, string? RepositoryRoot);
