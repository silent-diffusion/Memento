using System.Text.Json;
using Memento.AI.Local;
using Memento.Core.Workers;

namespace Memento.AI.Tests.Local;

/// <summary>A progress or device line of the wrong shape is dropped, not thrown out of the worker read loop.</summary>
public sealed class WorkerReplyMappingTests
{
    [Fact]
    public void AMisshapenProgressObjectIsLeftOut()
    {
        var line = new WorkerReply
        {
            Type = WorkerMessageTypes.Progress,
            Percent = 40,
            LlmProgress = JsonDocument.Parse("""{"phase":5,"promptIndex":"first","promptCount":[]}""").RootElement.Clone(),
        };

        var local = WorkerLocalLlmJobClient.ToLocal(line);

        Assert.NotNull(local);
        Assert.Equal(40, local.Percent);
        Assert.Null(local.LlmProgress);
    }

    [Fact]
    public void AMisshapenDeviceObjectIsLeftOut()
    {
        var line = new WorkerReply
        {
            Type = WorkerMessageTypes.Device,
            LlmDevice = JsonDocument.Parse("""{"backend":{"nested":true},"gpuLayers":"many"}""").RootElement.Clone(),
        };

        var local = WorkerLocalLlmJobClient.ToLocal(line);

        Assert.NotNull(local);
        Assert.Null(local.LlmDevice);
    }

    [Fact]
    public void AWellFormedProgressObjectIsKept()
    {
        var line = new WorkerReply
        {
            Type = WorkerMessageTypes.Progress,
            LlmProgress = JsonSerializer.SerializeToElement(new LocalLlmProgress(LocalLlmProgress.Generating, 1, 2, "Hi", 3), LocalLlmJsonContext.Default.LocalLlmProgress),
        };

        Assert.Equal(new LocalLlmProgress(LocalLlmProgress.Generating, 1, 2, "Hi", 3), WorkerLocalLlmJobClient.ToLocal(line)!.LlmProgress);
    }
}
