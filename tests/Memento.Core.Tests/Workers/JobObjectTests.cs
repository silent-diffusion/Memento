using Memento.Core.Workers.Interop;

namespace Memento.Core.Tests.Workers;

public sealed class JobObjectTests
{
    [Fact]
    public void TheJobEndsItsProcessesWithMementoAndOnAnUnhandledException()
    {
        using var job = new JobObject();

        var flags = job.QueryLimitFlags();

        Assert.Equal(JobObject.JobObjectLimitKillOnJobClose, flags & JobObject.JobObjectLimitKillOnJobClose);
        Assert.Equal(JobObject.JobObjectLimitDieOnUnhandledException, flags & JobObject.JobObjectLimitDieOnUnhandledException);

        // No memory limit (JOB_OBJECT_LIMIT_PROCESS_MEMORY 0x100, JOB_OBJECT_LIMIT_JOB_MEMORY 0x200).
        Assert.Equal(0u, flags & 0x300u);
    }
}
