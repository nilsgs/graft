using System.Security.Cryptography;
using System.Text;

namespace Graft.Services;

internal sealed class WorktreePathService
{
    private const int MaxManagedWorktreePathLength = 140;
    private const int MinimumBranchSlugLength = 1;

    public string GetManagedRoot(string repositoryRoot)
    {
        var repositoryParent = Directory.GetParent(repositoryRoot)
            ?? throw new InvalidOperationException("Repository root has no parent directory.");

        return Path.Combine(repositoryParent.FullName, ".worktrees");
    }

    public string GetManagedWorktreePath(string repositoryRoot, string branchName)
    {
        var managedRoot = GetManagedRoot(repositoryRoot);
        var repoName = new DirectoryInfo(repositoryRoot).Name;
        var repoToken = GetRepositoryToken(repoName);
        var branchSlug = GetBranchSlug(branchName);
        var hash = GetHash(branchName);
        var maxBranchSlugLength = GetMaxBranchSlugLength(managedRoot, repoToken, hash);
        var truncatedBranchSlug = branchSlug.Length <= maxBranchSlugLength
            ? branchSlug
            : branchSlug[..maxBranchSlugLength];

        return Path.Combine(managedRoot, BuildFolderName(repoToken, truncatedBranchSlug, hash));
    }

    public bool IsManagedPath(string repositoryRoot, string path)
    {
        var managedRoot = EnsureTrailingSeparator(Path.GetFullPath(GetManagedRoot(repositoryRoot)));
        var candidate = EnsureTrailingSeparator(Path.GetFullPath(path));
        return candidate.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepositoryToken(string repositoryName)
    {
        var parts = repositoryName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? repositoryName : parts[^1];
    }

    private static string BuildFolderName(string repoToken, string branchSlug, string hash)
    {
        return $"{repoToken}--{branchSlug}--{hash}";
    }

    private static string GetBranchSlug(string branchName)
    {
        var invalid = Path.GetInvalidFileNameChars().Append('/').Append('\\').ToHashSet();
        var builder = new StringBuilder(branchName.Length);
        var previousDash = false;

        foreach (var character in branchName)
        {
            if (invalid.Contains(character) || char.IsWhiteSpace(character))
            {
                if (!previousDash)
                {
                    builder.Append('-');
                    previousDash = true;
                }

                continue;
            }

            if (character == '-')
            {
                if (previousDash)
                {
                    continue;
                }

                builder.Append(character);
                previousDash = true;
                continue;
            }

            builder.Append(character);
            previousDash = false;
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "branch" : slug;
    }

    private static string GetHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes[..4]);
    }

    private static int GetMaxBranchSlugLength(string managedRoot, string repoToken, string hash)
    {
        var managedRootPrefixLength = EnsureTrailingSeparator(Path.GetFullPath(managedRoot)).Length;
        var availableFolderNameLength = MaxManagedWorktreePathLength - managedRootPrefixLength;
        var fixedFolderNameLength = BuildFolderName(repoToken, string.Empty, hash).Length;
        var maxBranchSlugLength = availableFolderNameLength - fixedFolderNameLength;

        if (maxBranchSlugLength < MinimumBranchSlugLength)
        {
            throw new InvalidOperationException(
                $"Managed worktree root '{managedRoot}' is too deep to create a safe path within {MaxManagedWorktreePathLength} characters.");
        }

        return maxBranchSlugLength;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
