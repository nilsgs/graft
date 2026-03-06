using System.CommandLine;
using Graft.Handlers;

namespace Graft.Commands;

internal static class CommandFactory
{
    public static RootCommand CreateRootCommand(
        CreateHandler createHandler,
        ListHandler listHandler,
        RemoveHandler removeHandler,
        CleanupHandler cleanupHandler,
        PruneHandler pruneHandler)
    {
        var rootCommand = new RootCommand("Manage Git worktrees from inside an existing repository.");
        rootCommand.Options.Add(new VersionOption("--version", "-v"));

        rootCommand.Subcommands.Add(CreateCreateCommand(createHandler));
        rootCommand.Subcommands.Add(CreateListCommand(listHandler));
        rootCommand.Subcommands.Add(CreateRemoveCommand(removeHandler));
        rootCommand.Subcommands.Add(CreateCleanupCommand(cleanupHandler));
        rootCommand.Subcommands.Add(CreatePruneCommand(pruneHandler));

        return rootCommand;
    }

    private static Command CreateCreateCommand(CreateHandler handler)
    {
        var branchArgument = new Argument<string>("branch")
        {
            Description = "Branch name for the new worktree."
        };

        var command = new Command("create", "Create a worktree for a branch and open it in Windows Terminal.");
        command.Aliases.Add("c");
        command.Arguments.Add(branchArgument);
        command.SetAction(async (parseResult, ct) =>
        {
            var branchName = parseResult.GetValue(branchArgument)!;
            return await handler.HandleAsync(branchName, ct);
        });
        return command;
    }

    private static Command CreateListCommand(ListHandler handler)
    {
        var command = new Command("list", "List worktrees for the current repository.");
        command.Aliases.Add("l");
        command.SetAction(async (_, ct) => await handler.HandleAsync(ct));
        return command;
    }

    private static Command CreateRemoveCommand(RemoveHandler handler)
    {
        var targetArgument = new Argument<string>("target")
        {
            Description = "Branch name or worktree path to remove."
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Force removal of dirty or locked worktrees."
        };

        var command = new Command("remove", "Remove a specific worktree.");
        command.Aliases.Add("r");
        command.Arguments.Add(targetArgument);
        command.Options.Add(forceOption);
        command.SetAction(async (parseResult, ct) =>
        {
            var target = parseResult.GetValue(targetArgument)!;
            var force = parseResult.GetValue(forceOption);
            return await handler.HandleAsync(target, force, ct);
        });
        return command;
    }

    private static Command CreateCleanupCommand(CleanupHandler handler)
    {
        var command = new Command("cleanup", "Interactively remove obsolete managed worktrees.");
        command.Aliases.Add("x");
        command.SetAction(async (_, ct) => await handler.HandleAsync(ct));
        return command;
    }

    private static Command CreatePruneCommand(PruneHandler handler)
    {
        var command = new Command("prune", "Prune stale Git worktree metadata.");
        command.Aliases.Add("p");
        command.SetAction(async (_, ct) => await handler.HandleAsync(ct));
        return command;
    }
}
