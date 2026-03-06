using System.Diagnostics;
using Graft.Models;

namespace Graft.Services;

internal sealed class GitService
{
    public async Task<CommandResult> RunAsync(string workingDirectory, CancellationToken ct, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new CommandResult(process.ExitCode == 0, await stdoutTask, await stderrTask, process.ExitCode);
    }
}
