using System.ComponentModel;
using System.Diagnostics;

namespace Graft.Services;

internal sealed class TerminalService
{
    public TerminalResult OpenTab(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("nt");
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo);
            return process is null
                ? TerminalResult.Failure("Created the worktree, but Windows Terminal did not start.")
                : TerminalResult.Success();
        }
        catch (Win32Exception)
        {
            return TerminalResult.Failure($"Created the worktree, but wt.exe was not found. Open it manually at {path}");
        }
        catch (Exception ex)
        {
            return TerminalResult.Failure($"Created the worktree, but Windows Terminal could not be opened: {ex.Message}");
        }
    }
}
