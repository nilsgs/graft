# AGENTS.md

Project-specific instructions for working in `graft`.

## Scope

These instructions apply to the whole repository.

## Session Start

At the beginning of each session:

1. Read this file and [`README.md`](README.md).
2. Check the working tree with `git status --short`.
3. Assume the repo may already contain user changes. Do not revert unrelated edits.
4. Work from the repository root in the shell that matches the current environment.
5. Use PowerShell-oriented scripts from PowerShell and Bash-oriented scripts from Bash/WSL/Git Bash.

## Project Snapshot

- `graft` is a Windows-only CLI for managing Git worktrees.
- The app targets `.NET 10` via [`src/graft/graft.csproj`](src/graft/graft.csproj).
- Main dependencies are `System.CommandLine` and `Spectre.Console`.
- Managed worktrees live under `../.worktrees` relative to the current repository root.
- `graft` expects both `git` and `wt.exe` to be available on `PATH`.

## Architecture

- Entry point: [`src/graft/Program.cs`](src/graft/Program.cs)
- Command registration: [`src/graft/Commands/CommandFactory.cs`](src/graft/Commands/CommandFactory.cs)
- Command behavior lives in `Handlers/`.
- Git, terminal, repository, and worktree operations live in `Services/`.
- Data transfer/result types live in `Models/`.
- Console rendering and prompts live in [`src/graft/Output/ConsoleFormatter.cs`](src/graft/Output/ConsoleFormatter.cs).

Keep new code aligned with the existing split: command parsing in commands, orchestration in handlers/services, and plain result types in models.

## Build And Validation

Use the repo-local NuGet config when building:

```powershell
dotnet build .\src\graft\graft.csproj -nologo --configfile .\NuGet.config
```

Use the smoke harness first for behavioral validation. Pick the script that matches the current shell environment:

```powershell
.\scripts\validate.ps1
```

```bash
./scripts/validate.sh
```

The harness:
- builds the CLI
- runs it against disposable Git repositories
- covers create/navigate/list/remove/cleanup/prune flows
- intentionally runs without `wt.exe`, so that warning is expected during harness runs
- exists in both `scripts/validate.ps1` and `scripts/validate.sh`; keep them behaviorally aligned when making validation changes

For local installation checks:

```powershell
.\scripts\publish.ps1
graft --help
graft --version
```

There is currently no test project in this repository. If code changes affect behavior, prefer at minimum:

1. Build successfully.
2. Run `.\scripts\validate.ps1` in PowerShell or `./scripts/validate.sh` in Bash.
3. Fall back to manual CLI verification only for scenarios the harness does not cover.

## Editing Guidance

- Follow the existing C# style: file-scoped namespaces, one primary type per file, explicit access modifiers, and `Async` suffixes on async methods.
- Preserve the current simple composition style in `Program.cs`; do not introduce a DI container unless the task justifies it.
- Keep Windows-specific behavior explicit. Do not make cross-platform assumptions unless the user asks for them.
- Prefer small, local changes over broad refactors.
- When changing CLI behavior, update [`README.md`](README.md) if usage or output expectations change.

## Safety Notes

- Be careful with commands that remove worktrees or branches. Avoid destructive Git operations unless the user explicitly wants them.
- `cleanup` and `remove --force` are user-impacting flows; preserve clear messaging and confirmation behavior.
- If you notice unrelated local changes in files you need to edit, read them carefully and work with them instead of overwriting them.
