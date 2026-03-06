using Graft.Output;
using Graft.Services;

namespace Graft.Handlers;

internal sealed class RemoveHandler
{
    private readonly RepositoryContextFactory _repositoryContextFactory;
    private readonly WorktreeService _worktreeService;
    private readonly ConsoleFormatter _formatter;

    public RemoveHandler(
        RepositoryContextFactory repositoryContextFactory,
        WorktreeService worktreeService,
        ConsoleFormatter formatter)
    {
        _repositoryContextFactory = repositoryContextFactory;
        _worktreeService = worktreeService;
        _formatter = formatter;
    }

    public async Task<int> HandleAsync(string target, bool force, CancellationToken ct)
    {
        var context = await _repositoryContextFactory.CreateAsync(ct: ct);
        if (context.IsFailure)
        {
            return context.ExitCode;
        }

        var result = await _worktreeService.RemoveAsync(context.RepositoryRoot!, target, force, ct: ct);
        if (!result.IsSuccess)
        {
            _formatter.WriteError(result.ErrorMessage!);
            return result.ExitCode;
        }

        _formatter.WriteSuccess($"Removed worktree: {result.Worktree!.Path}");
        return ExitCodes.Success;
    }
}
