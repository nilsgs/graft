# AGENTS.md

Project-specific instructions for working in `graft`.

## Scope

These instructions apply to the whole repository.

## Session Start

At the beginning of each session:

1. Read this file and [`README.md`](/C:/Users/niskut/source/repos/tools/graft/README.md).
2. Check the working tree with `git status --short`.
3. Assume the repo may already contain user changes. Do not revert unrelated edits.
4. Work from the repository root in Windows PowerShell unless the task clearly requires something else.

## Project Snapshot

- `graft` is a Windows-only CLI for managing Git worktrees.
- The app targets `.NET 10` via [`src/graft/graft.csproj`](/C:/Users/niskut/source/repos/tools/graft/src/graft/graft.csproj).
- Main dependencies are `System.CommandLine` and `Spectre.Console`.
- Managed worktrees live under `../.worktrees` relative to the current repository root.
- `graft` expects both `git` and `wt.exe` to be available on `PATH`.

## Architecture

- Entry point: [`src/graft/Program.cs`](/C:/Users/niskut/source/repos/tools/graft/src/graft/Program.cs)
- Command registration: [`src/graft/Commands/CommandFactory.cs`](/C:/Users/niskut/source/repos/tools/graft/src/graft/Commands/CommandFactory.cs)
- Command behavior lives in `Handlers/`.
- Git, terminal, repository, and worktree operations live in `Services/`.
- Data transfer/result types live in `Models/`.
- Console rendering and prompts live in [`src/graft/Output/ConsoleFormatter.cs`](/C:/Users/niskut/source/repos/tools/graft/src/graft/Output/ConsoleFormatter.cs).

Keep new code aligned with the existing split: command parsing in commands, orchestration in handlers/services, and plain result types in models.

## Build And Validation

Use the repo-local NuGet config when building:

```powershell
dotnet build .\src\graft\graft.csproj -nologo --configfile .\NuGet.config
```

For local installation and manual verification:

```powershell
.\scripts\publish.ps1
graft --help
graft --version
```

There is currently no test project in this repository. If code changes affect behavior, prefer at minimum:

1. Build successfully.
2. Run the relevant CLI command manually from inside a real Git repository.
3. Verify output and exit behavior for both success and failure paths when practical.

## Editing Guidance

- Follow the existing C# style: file-scoped namespaces, one primary type per file, explicit access modifiers, and `Async` suffixes on async methods.
- Preserve the current simple composition style in `Program.cs`; do not introduce a DI container unless the task justifies it.
- Keep Windows-specific behavior explicit. Do not make cross-platform assumptions unless the user asks for them.
- Prefer small, local changes over broad refactors.
- When changing CLI behavior, update [`README.md`](/C:/Users/niskut/source/repos/tools/graft/README.md) if usage or output expectations change.

## Safety Notes

- Be careful with commands that remove worktrees or branches. Avoid destructive Git operations unless the user explicitly wants them.
- `cleanup` and `remove --force` are user-impacting flows; preserve clear messaging and confirmation behavior.
- If you notice unrelated local changes in files you need to edit, read them carefully and work with them instead of overwriting them.
