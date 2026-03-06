using System.Text;
using Graft.Models;
using Spectre.Console;

namespace Graft.Output;

internal sealed class ConsoleFormatter
{
    private readonly object _syncRoot = new();

    public void WriteError(string message)
    {
        WriteMarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }

    public void WriteWarning(string message)
    {
        WriteMarkupLine($"[yellow]Warning:[/] {Markup.Escape(message)}");
    }

    public void WriteSuccess(string message)
    {
        WriteMarkupLine($"[green]{Markup.Escape(message)}[/]");
    }

    public void WriteInfo(string message)
    {
        WriteMarkupLine(Markup.Escape(message));
    }

    public void WriteProgress(string message)
    {
        WriteMarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    public IProgress<string> CreateProgressReporter()
    {
        return new ProgressReporter(this);
    }

    public void WriteCreateSuccess(WorktreeInfo worktree)
    {
        WriteMarkupLine($"[green]Created worktree:[/] {Markup.Escape(worktree.Path)}");
        WriteMarkupLine($"Branch: [blue]{Markup.Escape(worktree.BranchName)}[/]");
    }

    public void WriteWorktrees(IReadOnlyList<WorktreeInfo> worktrees)
    {
        if (worktrees.Count == 0)
        {
            WriteInfo("No worktrees found.");
            return;
        }

        var table = new Table();
        table.AddColumn("Branch");
        table.AddColumn("Path");
        table.AddColumn("Managed");
        table.AddColumn("Status");

        foreach (var worktree in worktrees.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                Markup.Escape(worktree.BranchName),
                Markup.Escape(worktree.Path),
                worktree.IsManaged ? "yes" : "no",
                Markup.Escape(string.Join(", ", worktree.Statuses)));
        }

        lock (_syncRoot)
        {
            AnsiConsole.Write(table);
        }
    }

    public IReadOnlyList<WorktreeCandidate> PromptForCleanup(IReadOnlyList<WorktreeCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var prompt = new MultiSelectionPrompt<WorktreeCandidate>()
            .Title("Select worktrees to remove")
            .NotRequired()
            .UseConverter(candidate => $"{candidate.Worktree.BranchName} | {candidate.Reason} | {candidate.Worktree.Path}")
            .InstructionsText("[grey](Press <space> to toggle, <enter> to confirm)[/]");

        prompt.AddChoices(candidates.OrderBy(item => item.Worktree.Path, StringComparer.OrdinalIgnoreCase));
        lock (_syncRoot)
        {
            return AnsiConsole.Prompt(prompt);
        }
    }

    public bool ConfirmRemoval(int count)
    {
        lock (_syncRoot)
        {
            return AnsiConsole.Confirm($"Remove {count} worktree(s)?", false);
        }
    }

    private void WriteMarkupLine(string message)
    {
        lock (_syncRoot)
        {
            AnsiConsole.MarkupLine(message);
        }
    }

    private sealed class ProgressReporter : IProgress<string>
    {
        private readonly ConsoleFormatter _formatter;

        public ProgressReporter(ConsoleFormatter formatter)
        {
            _formatter = formatter;
        }

        public void Report(string value)
        {
            _formatter.WriteProgress(value);
        }
    }
}

