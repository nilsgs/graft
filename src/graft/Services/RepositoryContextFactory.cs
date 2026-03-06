using Graft.Models;
using Graft.Output;

namespace Graft.Services;

internal sealed class RepositoryContextFactory
{
    private readonly GitService _gitService;
    private readonly ConsoleFormatter _formatter;

    public RepositoryContextFactory(GitService gitService, ConsoleFormatter formatter)
    {
        _gitService = gitService;
        _formatter = formatter;
    }

    public async Task<RepositoryContextResult> CreateAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Locating repository root...");

        var currentDirectory = Directory.GetCurrentDirectory();
        var result = await _gitService.RunAsync(currentDirectory, ct, "rev-parse", "--show-toplevel");
        if (!result.IsSuccess)
        {
            _formatter.WriteError("graft must be run from inside an existing Git repository.");
            return new RepositoryContextResult(true, ExitCodes.NotInGitRepository, null);
        }

        return new RepositoryContextResult(false, ExitCodes.Success, result.StandardOutput.Trim());
    }
}
