using Graft.Output;
using Graft.Services;

namespace Graft.Handlers;

internal sealed class CleanupHandler
{
    private readonly RepositoryContextFactory _repositoryContextFactory;
    private readonly WorktreeService _worktreeService;
    private readonly ConsoleFormatter _formatter;

    public CleanupHandler(
        RepositoryContextFactory repositoryContextFactory,
        WorktreeService worktreeService,
        ConsoleFormatter formatter)
    {
        _repositoryContextFactory = repositoryContextFactory;
        _worktreeService = worktreeService;
        _formatter = formatter;
    }

    public async Task<int> HandleAsync(bool selectAll, bool assumeYes, CancellationToken ct)
    {
        var progress = _formatter.CreateProgressReporter();
        var context = await _repositoryContextFactory.CreateAsync(progress, ct);
        if (context.IsFailure)
        {
            return context.ExitCode;
        }

        var result = await _worktreeService.CleanupAsync(context.RepositoryRoot!, selectAll, assumeYes, progress, ct);
        if (!result.IsSuccess)
        {
            _formatter.WriteError(result.Message!);
            return result.ExitCode;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            _formatter.WriteInfo(result.Message);
        }

        foreach (var worktree in result.RemovedWorktrees)
        {
            _formatter.WriteSuccess($"Removed worktree: {worktree.Path}");
        }

        return ExitCodes.Success;
    }
}
