using Graft.Output;
using Graft.Services;

namespace Graft.Handlers;

internal sealed class PruneHandler
{
    private readonly RepositoryContextFactory _repositoryContextFactory;
    private readonly WorktreeService _worktreeService;
    private readonly ConsoleFormatter _formatter;

    public PruneHandler(
        RepositoryContextFactory repositoryContextFactory,
        WorktreeService worktreeService,
        ConsoleFormatter formatter)
    {
        _repositoryContextFactory = repositoryContextFactory;
        _worktreeService = worktreeService;
        _formatter = formatter;
    }

    public async Task<int> HandleAsync(CancellationToken ct)
    {
        var progress = _formatter.CreateProgressReporter();
        var context = await _repositoryContextFactory.CreateAsync(progress, ct);
        if (context.IsFailure)
        {
            return context.ExitCode;
        }

        var result = await _worktreeService.PruneAsync(context.RepositoryRoot!, progress, ct);
        if (!result.IsSuccess)
        {
            _formatter.WriteError(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim());
            return ExitCodes.GitFailure;
        }

        _formatter.WriteSuccess("Pruned stale Git worktree metadata.");
        return ExitCodes.Success;
    }
}
