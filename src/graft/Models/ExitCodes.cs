namespace Graft;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int NotInGitRepository = 2;
    public const int GitFailure = 3;
    public const int RefusedRemoval = 4;
}
