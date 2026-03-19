using Graft.Models;
using Graft.Output;

namespace Graft.Services;

internal sealed class NavigateContextFactory
{
    private readonly GitService _gitService;
    private readonly ConsoleFormatter _formatter;

    public NavigateContextFactory(GitService gitService, ConsoleFormatter formatter)
    {
        _gitService = gitService;
        _formatter = formatter;
    }

    public async Task<NavigateContextResult> CreateAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Locating navigation context...");

        var currentDirectory = Directory.GetCurrentDirectory();
        var repositoryResult = await _gitService.RunAsync(currentDirectory, ct, "rev-parse", "--show-toplevel");
        if (repositoryResult.IsSuccess)
        {
            return new NavigateContextResult(
                false,
                ExitCodes.Success,
                NavigateLocationKind.Repository,
                repositoryResult.StandardOutput.Trim(),
                null);
        }

        var sharedRootPath = Path.Combine(currentDirectory, ".worktrees");
        if (Directory.Exists(sharedRootPath))
        {
            return new NavigateContextResult(
                false,
                ExitCodes.Success,
                NavigateLocationKind.SharedRoot,
                null,
                currentDirectory);
        }

        _formatter.WriteError("graft navigate must be run from inside an existing Git repository or from a folder that contains '.worktrees'.");
        return new NavigateContextResult(true, ExitCodes.NotInGitRepository, null, null, null);
    }
}
