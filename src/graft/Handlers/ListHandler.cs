using Graft.Output;
using Graft.Services;

namespace Graft.Handlers;

internal sealed class ListHandler
{
    private readonly RepositoryContextFactory _repositoryContextFactory;
    private readonly WorktreeService _worktreeService;
    private readonly ConsoleFormatter _formatter;

    public ListHandler(
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
        var context = await _repositoryContextFactory.CreateAsync(ct: ct);
        if (context.IsFailure)
        {
            return context.ExitCode;
        }

        var result = await _worktreeService.ListAsync(context.RepositoryRoot!, ct: ct);
        if (!result.IsSuccess)
        {
            _formatter.WriteError(result.ErrorMessage!);
            return ExitCodes.GitFailure;
        }

        _formatter.WriteWorktrees(result.Worktrees!);
        return ExitCodes.Success;
    }
}
