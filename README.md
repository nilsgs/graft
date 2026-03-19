# graft

graft is a small Windows CLI for working with Git worktrees from inside an existing repository and for navigating managed worktrees from a shared root folder.

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

or from Bash:

```bash
./scripts/publish.sh
```

The script auto-detects the local Windows architecture and publishes `win-x64` or `win-arm64` accordingly.
You can override it explicitly:

```powershell
.\scripts\publish.ps1 -RuntimeIdentifier win-arm64
```

```bash
./scripts/publish.sh --runtime-identifier win-arm64
```

The current app version is defined centrally in `Directory.Build.props`.
For local prerelease builds, you can add a suffix during publish:

```powershell
.\scripts\publish.ps1 -VersionSuffix dev
```

```bash
./scripts/publish.sh --version-suffix dev
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

- `create`, `list`, `remove`, `cleanup`, and `prune` work only when run from inside an existing Git repository.
- `navigate` also works from the directory that contains the shared `.worktrees` folder.
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
- `graft create --from-local-main` creates a new branch from local `main`, or `origin/main` if local `main` is not available.
- `graft create --from-origin-main` creates a new branch from `origin/main` only.
- If the target branch already exists, `--from-local-main` and `--from-origin-main` are ignored and `graft` prints a warning.

## Commands

Show built-in help:

```powershell
graft --help
```

Show the installed version:

```powershell
graft --version
```

Run the smoke validation harness:

```powershell
.\scripts\validate.ps1
```

or from Bash:

```bash
./scripts/validate.sh
```

Short command aliases:

- `c` for `create`
- `n` for `navigate`
- `l` for `list`
- `r` for `remove`
- `x` for `cleanup`
- `p` for `prune`

### Create

Create a worktree for a branch and open it in a new Windows Terminal tab:

```powershell
graft create feature/my-branch
```

Create a new branch from `main`:

```powershell
graft create feature/my-branch --from-local-main
```

Create a new branch from `origin/main` explicitly:

```powershell
graft create feature/my-branch --from-origin-main
```

Short form:

```powershell
graft c feature/my-branch
```

Behavior:
- creates `../.worktrees` if it does not exist
- creates or reuses the branch as needed
- supports `--from-local-main` / `-l` for new branches from `main`
- supports `--from-origin-main` / `-o` for new branches from `origin/main`
- creates the worktree folder
- opens a new Windows Terminal tab in that folder

Example output:

```text
Created worktree: <parent-of-repo>\.worktrees\LongRepo--feature-my-branch--1a2b3c4d
Branch: feature/my-branch
```

### Navigate

Select a worktree and open it in a new Windows Terminal tab:

```powershell
graft navigate
```

Short form:

```powershell
graft n
```

You can run this:
- from inside a Git repository, where it shows that repository's worktrees
- from the directory that contains `.worktrees`, where it shows direct managed worktree folders under `.worktrees`

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

For non-interactive cleanup:

```powershell
graft cleanup --all --yes
```

- `--all` selects all current cleanup candidates
- `--yes` skips the confirmation prompt

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

## Validation

Run the built-in smoke harness after behavioral changes:

```powershell
.\scripts\validate.ps1
```

or from Bash:

```bash
./scripts/validate.sh
```

What it covers:
- `create` from `HEAD`, `main`, and `origin/main`
- `navigate` from both repository mode and shared-root mode
- `list`
- `remove`
- `cleanup --all --yes`
- `prune`

The harness builds `graft`, creates disposable Git repositories, and intentionally runs without `wt.exe`, so the Windows Terminal warning is expected during validation runs.
Keep `scripts/validate.ps1` and `scripts/validate.sh` behaviorally aligned when changing validation coverage or expectations.

## Notes

- If `wt.exe` is not available, worktree creation still succeeds and `graft` prints a warning.
- `navigate` also works from a directory that contains `.worktrees`.
- `graft` manages only the worktrees for the repository you are currently inside.
