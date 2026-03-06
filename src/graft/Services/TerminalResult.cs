namespace Graft.Services;

internal sealed record TerminalResult(bool IsSuccess, string? ErrorMessage)
{
    public static TerminalResult Success() => new(true, null);

    public static TerminalResult Failure(string errorMessage) => new(false, errorMessage);
}
