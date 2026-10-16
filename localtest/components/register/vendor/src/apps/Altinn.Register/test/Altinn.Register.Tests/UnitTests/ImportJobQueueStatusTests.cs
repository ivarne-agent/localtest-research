using Altinn.Register.Core.ImportJobs;

namespace Altinn.Register.Tests.UnitTests;

public class ImportJobQueueStatusTests
{
    [Fact]
    public void SourceMaxNull()
    {
        var status = new ImportJobQueueStatus
        {
            EnqueuedMax = 42,
            SourceMax = null,
        };

        status.SourceMax.ShouldBeNull();
    }

    [Fact]
    public void SourceMaxZero()
    {
        var status = new ImportJobQueueStatus
        {
            EnqueuedMax = 42,
            SourceMax = 0,
        };

        status.SourceMax.ShouldBe(0UL);
    }

    [Fact]
    public void SourceMaxPositive()
    {
        var status = new ImportJobQueueStatus
        {
            EnqueuedMax = 42,
            SourceMax = 42,
        };
        status.SourceMax.ShouldBe(42UL);
    }
}
