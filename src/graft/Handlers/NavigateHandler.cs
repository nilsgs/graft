using Graft.Models;
using Graft.Output;
using Graft.Services;

namespace Graft.Handlers;

internal sealed class NavigateHandler
{
    private readonly NavigateContextFactory _navigateContextFactory;
    private readonly WorktreeService _worktreeService;
    private readonly TerminalService _terminalService;
    private readonly ConsoleFormatter _formatter;

    public NavigateHandler(
        NavigateContextFactory navigateContextFactory,
        WorktreeService worktreeService,
        TerminalService terminalService,
        ConsoleFormatter formatter)
    {
        _navigateContextFactory = navigateContextFactory;
        _worktreeService = worktreeService;
        _terminalService = terminalService;
        _formatter = formatter;
    }

    public async Task<int> HandleAsync(bool openTerminal, CancellationToken ct)
    {
        var progress = _formatter.CreateProgressReporter();
        var context = await _navigateContextFactory.CreateAsync(progress, ct);
        if (context.IsFailure)
        {
            return context.ExitCode;
        }

        var result = context.LocationKind switch
        {
            NavigateLocationKind.Repository => await _worktreeService.ListAsync(context.RepositoryRoot!, progress, ct),
            NavigateLocationKind.SharedRoot => await _worktreeService.ListManagedFromSharedRootAsync(context.SharedRootDirectory!, progress, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(context.LocationKind), context.LocationKind, null)
        };

        if (!result.IsSuccess)
        {
            _formatter.WriteError(result.ErrorMessage!);
            return ExitCodes.GitFailure;
        }

        if (result.Worktrees!.Count == 0)
        {
            _formatter.WriteInfo("No worktrees found.");
            return ExitCodes.Success;
        }

        WorktreeInfo selectedWorktree;
        try
        {
            selectedWorktree = _formatter.PromptForNavigation(result.Worktrees);
        }
        catch (InvalidOperationException ex)
        {
            _formatter.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }

        if (!openTerminal)
        {
            _formatter.WriteSelectedWorktreePath(selectedWorktree.Path);
            return ExitCodes.Success;
        }

        progress.Report("Opening Windows Terminal tab...");
        var terminalResult = _terminalService.OpenTab(selectedWorktree.Path);
        if (!terminalResult.IsSuccess)
        {
            _formatter.WriteWarning(terminalResult.ErrorMessage!);
        }

        return ExitCodes.Success;
    }
}
