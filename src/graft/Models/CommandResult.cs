namespace Graft.Models;

internal sealed record CommandResult(bool IsSuccess, string StandardOutput, string StandardError, int ExitCode);
