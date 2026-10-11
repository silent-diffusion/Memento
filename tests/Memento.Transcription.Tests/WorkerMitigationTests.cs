using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Memento.Transcription.Tests;

/// <summary>
/// The real Memento.Worker.exe forbids itself DLLs from network shares before it reads a job (security audit 2, SA2-22).
/// It can still start programs: forbidding that broke the Vulkan driver (see ProcessMitigations).
/// </summary>
public sealed class WorkerMitigationTests
{
    [Fact]
    public async Task TheWorkerCannotLoadDllsFromNetworkShares()
    {
        if (WorkerBuild.Executable is not { } exe)
        {
            return; // Not built in this run.
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var worker = Process.Start(info)!;
        try
        {
            // The ready line comes after the limit is applied.
            var ready = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Contains("\"ready\"", ready, StringComparison.Ordinal);

            Assert.True(GetProcessMitigationPolicy(worker.Handle, 10, out var images, sizeof(uint)));
            Assert.Equal(1u, images & 1u);
        }
        finally
        {
            worker.Kill();
        }
    }

#pragma warning disable SYSLIB1054 // A test-only call with an out uint; no unsafe code in this project.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint buffer, nuint length);
#pragma warning restore SYSLIB1054
}
