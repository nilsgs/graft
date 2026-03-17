using Graft.Output;
using Graft.Services;
using Graft.Models;

namespace Graft.Handlers;

internal sealed class CreateHandler
{
    private readonly RepositoryContextFactory _repositoryContextFactory;
    private readonly WorktreeService _worktreeService;
    private readonly TerminalService _terminalService;
    private readonly ConsoleFormatter _formatter;

    public CreateHandler(
        RepositoryContextFactory repositoryContextFactory,
        WorktreeService worktreeService,
        TerminalService terminalService,
        ConsoleFormatter formatter)
    {
        _repositoryContextFactory = repositoryContextFactory;
        _worktreeService = worktreeService;
        _terminalService = terminalService;
        _formatter = formatter;
    }

    public async Task<int> HandleAsync(string branchName, CreateBranchBase branchBase, CancellationToken ct)
    {
        var progress = _formatter.CreateProgressReporter();
        var context = await _repositoryContextFactory.CreateAsync(progress, ct);
        if (context.IsFailure)
        {
            return context.ExitCode;
        }

        var result = await _worktreeService.CreateAsync(context.RepositoryRoot!, branchName, branchBase, progress, ct);
        if (!result.IsSuccess)
        {
            _formatter.WriteError(result.ErrorMessage!);
            return ExitCodes.GitFailure;
        }

        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
        {
            _formatter.WriteWarning(result.WarningMessage);
        }

        _formatter.WriteCreateSuccess(result.Worktree!);

        progress.Report("Opening Windows Terminal tab...");
        var terminalResult = _terminalService.OpenTab(result.Worktree!.Path);
        if (!terminalResult.IsSuccess)
        {
            _formatter.WriteWarning(terminalResult.ErrorMessage!);
        }

        return ExitCodes.Success;
    }
}
