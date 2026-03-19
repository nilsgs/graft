using Graft.Commands;
using Graft.Handlers;
using Graft.Output;
using Graft.Services;

namespace Graft;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var formatter = new ConsoleFormatter();
        var gitService = new GitService();
        var worktreePathService = new WorktreePathService();
        var terminalService = new TerminalService();
        var repositoryContextFactory = new RepositoryContextFactory(gitService, formatter);
        var navigateContextFactory = new NavigateContextFactory(gitService, formatter);
        var worktreeService = new WorktreeService(gitService, worktreePathService, formatter);

        var rootCommand = CommandFactory.CreateRootCommand(
            new CreateHandler(repositoryContextFactory, worktreeService, terminalService, formatter),
            new NavigateHandler(navigateContextFactory, worktreeService, terminalService, formatter),
            new ListHandler(repositoryContextFactory, worktreeService, formatter),
            new RemoveHandler(repositoryContextFactory, worktreeService, formatter),
            new CleanupHandler(repositoryContextFactory, worktreeService, formatter),
            new PruneHandler(repositoryContextFactory, worktreeService, formatter));

        return await rootCommand.Parse(args).InvokeAsync();
    }
}
