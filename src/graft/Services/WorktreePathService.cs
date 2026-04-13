using System.Security.Cryptography;
using System.Text;

namespace Graft.Services;

internal sealed class WorktreePathService
{
    private const int RepoHashLength = 8;
    private const int BranchHashLength = 8;

    public string GetManagedRoot()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".graft", "worktrees");
    }

    public string GetManagedWorktreePath(string repositoryRoot, string branchName)
    {
        var managedRoot = GetManagedRoot();
        var repoName = new DirectoryInfo(repositoryRoot).Name;
        var repoHash = GetHash(repoName, RepoHashLength);
        var branchHash = GetHash(branchName, BranchHashLength);

        return Path.Combine(managedRoot, BuildFolderName(repoHash, branchHash));
    }

    public bool IsManagedPath(string path)
    {
        var managedRoot = EnsureTrailingSeparator(Path.GetFullPath(GetManagedRoot()));
        var candidate = EnsureTrailingSeparator(Path.GetFullPath(path));
        return candidate.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFolderName(string repoHash, string branchHash)
    {
        return $"{repoHash}{branchHash}";
    }

    private static string GetHash(string value, int hexLength)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes)[..hexLength];
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
