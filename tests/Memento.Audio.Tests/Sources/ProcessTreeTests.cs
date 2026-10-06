using System.Diagnostics;
using Memento.Audio.Sources;

namespace Memento.Audio.Tests.Sources;

/// <summary>Memento's own processes (WebView2 plays Review's audio from a child process) are never offered as a source.</summary>
public sealed class ProcessTreeTests
{
    [Fact]
    public void AChildProcessIsInOurTreeAndAnUnrelatedOneIsNot()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 5 127.0.0.1 > nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.True(ProcessInfoNative.IsInTreeOf(child.Id, Environment.ProcessId));
            Assert.True(ProcessInfoNative.IsInTreeOf(Environment.ProcessId, Environment.ProcessId));
            Assert.Equal(Environment.ProcessId, ProcessInfoNative.ParentProcessId(child.Id));

            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (explorer is not null)
            {
                Assert.False(ProcessInfoNative.IsInTreeOf(explorer.Id, Environment.ProcessId));
            }
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }
}
