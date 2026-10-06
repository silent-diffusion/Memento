using System.Diagnostics;
using Memento.Audio.Sources;

namespace Memento.Audio.Tests.Sources;

/// <summary>
/// Memento's own WebView2 processes (Review plays audio from one) are never offered as a source, but other programs
/// Memento started, such as a browser opened from a link, still are.
/// </summary>
public sealed class ProcessTreeTests
{
    [Fact]
    public void AChildProcessIsInOurTreeButOnlyWebView2CountsAsOurOwn()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 5 127.0.0.1 > nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.True(ProcessInfoNative.IsInTreeOf(child.Id, Environment.ProcessId));
            Assert.Equal(Environment.ProcessId, ProcessInfoNative.ParentProcessId(child.Id));
            Assert.True(AudioSourceEnumerator.IsOwnProcess(Environment.ProcessId));
            Assert.False(AudioSourceEnumerator.IsOwnProcess(child.Id), "a program we started that is not WebView2 stays recordable");

            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (explorer is not null)
            {
                Assert.False(ProcessInfoNative.IsInTreeOf(explorer.Id, Environment.ProcessId));
                Assert.False(AudioSourceEnumerator.IsOwnProcess(explorer.Id));
            }
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }
}
