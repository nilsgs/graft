using System.CommandLine;
using Graft.Handlers;
using Graft.Models;

namespace Graft.Commands;

internal static class CommandFactory
{
    public static RootCommand CreateRootCommand(
        CreateHandler createHandler,
        NavigateHandler navigateHandler,
        ListHandler listHandler,
        RemoveHandler removeHandler,
        CleanupHandler cleanupHandler,
        PruneHandler pruneHandler)
    {
        var rootCommand = new RootCommand("Manage Git worktrees and navigate to worktrees from a shared root.");

        var o = rootCommand.Options.OfType<VersionOption>().First();
        o.Aliases.Add("-v");

        rootCommand.Subcommands.Add(CreateCreateCommand(createHandler));
        rootCommand.Subcommands.Add(CreateNavigateCommand(navigateHandler));
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

        var fromLocalMainOption = new Option<bool>("--from-local-main", "-l")
        {
            Description = "Create a new branch from local main, or origin/main if local main does not exist."
        };

        var fromOriginMainOption = new Option<bool>("--from-origin-main", "-o")
        {
            Description = "Create a new branch from origin/main."
        };

        var command = new Command("create", "Create a worktree for a branch and open it in Windows Terminal.");
        command.Aliases.Add("c");
        command.Arguments.Add(branchArgument);
        command.Options.Add(fromLocalMainOption);
        command.Options.Add(fromOriginMainOption);
        command.Validators.Add(result =>
        {
            if (result.GetValue(fromLocalMainOption) && result.GetValue(fromOriginMainOption))
            {
                result.AddError("--from-local-main and --from-origin-main cannot be used together.");
            }
        });
        command.SetAction(async (parseResult, ct) =>
        {
            var branchName = parseResult.GetValue(branchArgument)!;
            var branchBase = GetCreateBranchBase(
                parseResult.GetValue(fromLocalMainOption),
                parseResult.GetValue(fromOriginMainOption));
            return await handler.HandleAsync(branchName, branchBase, ct);
        });
        return command;
    }

    private static Command CreateNavigateCommand(NavigateHandler handler)
    {
        var command = new Command("navigate", "Select a worktree and open it in Windows Terminal from a repo or shared root.");
        command.Aliases.Add("n");
        command.SetAction(async (_, ct) => await handler.HandleAsync(ct));
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
        var allOption = new Option<bool>("--all")
        {
            Description = "Select all cleanup candidates without the interactive prompt."
        };

        var yesOption = new Option<bool>("--yes")
        {
            Description = "Skip the cleanup confirmation prompt."
        };
        
        var command = new Command("cleanup", "Interactively remove obsolete managed worktrees.");
        command.Aliases.Add("x");
        command.Options.Add(allOption);
        command.Options.Add(yesOption);
        command.SetAction(async (parseResult, ct) =>
        {
            var selectAll = parseResult.GetValue(allOption);
            var assumeYes = parseResult.GetValue(yesOption);
            return await handler.HandleAsync(selectAll, assumeYes, ct);
        });

        return command;
    }

    private static Command CreatePruneCommand(PruneHandler handler)
    {
        var command = new Command("prune", "Prune stale Git worktree metadata.");
        command.Aliases.Add("p");
        command.SetAction(async (_, ct) => await handler.HandleAsync(ct));
        return command;
    }

    private static CreateBranchBase GetCreateBranchBase(bool fromMain, bool fromOriginMain)
    {
        if (fromOriginMain)
        {
            return CreateBranchBase.OriginMain;
        }

        return fromMain
            ? CreateBranchBase.Main
            : CreateBranchBase.CurrentHead;
    }
}
