# graft

graft is a small Windows CLI for working with Git worktrees from inside an existing repository.

It creates worktrees under a shared `../.worktrees` folder, opens the new worktree in a Windows Terminal tab, and helps clean up old worktrees.

## Requirements

- Windows
- Git available on `PATH`
- Windows Terminal available as `wt.exe`
- `graft.exe` installed locally

## Install

Publish and install `graft` locally:

```powershell
.\scripts\publish.ps1
```

That installs the executable to:

```text
%USERPROFILE%\bin\graft.exe
```

In PowerShell, that path is:

```powershell
Join-Path $env:USERPROFILE "bin\graft.exe"
```

If `%USERPROFILE%\bin` is on your `PATH`, you can run it as:

```powershell
graft
```

## Important behavior

- `graft` only works when run from inside an existing Git repository.
- All managed worktrees are created under `../.worktrees` relative to the repo root.
- Worktree folders use this naming pattern:

```text
../.worktrees/{repo-token}--{branch-slug}--{hash8}
```

- `repo-token` is the last segment of the repo folder name split on `.`.
  - Example: `This.Is.A.LongRepo` becomes `LongRepo`
- If the branch already exists locally, `graft` uses it.
- If the branch exists only as `origin/<branch>`, `graft` creates a local tracking branch.
- If the branch does not exist, `graft` creates it from the current `HEAD`.

## Commands

Show built-in help:

```powershell
graft --help
```

Short command aliases:

- `c` for `create`
- `l` for `list`
- `r` for `remove`
- `x` for `cleanup`
- `p` for `prune`

### Create

Create a worktree for a branch and open it in a new Windows Terminal tab:

```powershell
graft create feature/my-branch
```

Short form:

```powershell
graft c feature/my-branch
```

Behavior:
- creates `../.worktrees` if it does not exist
- creates or reuses the branch as needed
- creates the worktree folder
- opens a new Windows Terminal tab in that folder

Example output:

```text
Created worktree: <parent-of-repo>\.worktrees\LongRepo--feature-my-branch--1a2b3c4d
Branch: feature/my-branch
```

### List

List worktrees for the current repository:

```powershell
graft list
```

Short form:

```powershell
graft l
```

This shows:
- branch
- path
- whether the worktree is managed by `graft`
- status flags reported by Git

### Remove

Remove a specific worktree by branch name or path:

```powershell
graft remove feature/my-branch
```

Short form:

```powershell
graft r feature/my-branch
```

or:

```powershell
graft remove ..\.worktrees\LongRepo--feature-my-branch--1a2b3c4d
```

Force removal for dirty or locked worktrees:

```powershell
graft remove feature/my-branch --force
```

### Cleanup

Interactively remove obsolete managed worktrees:

```powershell
graft cleanup
```

Short form:

```powershell
graft x
```

`graft cleanup` focuses on managed worktrees under `../.worktrees` and surfaces candidates such as:
- missing path
- clean
- gone upstream
- merged into current `HEAD`

### Prune

Prune stale Git worktree metadata:

```powershell
graft prune
```

Short form:

```powershell
graft p
```

Use this when a worktree folder was deleted manually and Git still has stale metadata for it.

## Typical workflow

Create a branch worktree:

```powershell
graft create feature/new-api
```

See what exists:

```powershell
graft list
```

Remove it when done:

```powershell
graft remove feature/new-api
```

Or clean up several old worktrees interactively:

```powershell
graft cleanup
```

## Notes

- If `wt.exe` is not available, worktree creation still succeeds and `graft` prints a warning.
- `graft` does not work outside a Git repository.
- `graft` manages only the worktrees for the repository you are currently inside.
