using Altinn.Register.Core.UnitOfWork;

namespace Altinn.Register.Tests.UnitTests;

public class IUnitOfWorkHandleTests
{
    [Fact]
    public void ThrowIfCompleted_ThrowsObjectDisposed_WhenDisposed()
    {
        using var handle = new FakeUnitOfWorkHandle();
        ((IDisposable)handle).Dispose();

        Assert.Throws<ObjectDisposedException>(() => ((IUnitOfWorkHandle)handle).ThrowIfCompleted());
    }

    [Fact]
    public void ThrowIfCompleted_DoesNotThrow_WhenActive()
    {
        using var handle = new FakeUnitOfWorkHandle();

        ((IUnitOfWorkHandle)handle).ThrowIfCompleted();
    }

    [Fact]
    public void ThrowIfCompleted_ThrowsInvalidOperationException_WhenCommitted()
    {
        using var handle = new FakeUnitOfWorkHandle();
        handle.Status = UnitOfWorkStatus.Committed;

        Assert.Throws<InvalidOperationException>(() => ((IUnitOfWorkHandle)handle).ThrowIfCompleted());
    }

    [Fact]
    public void ThrowIfCompleted_ThrowsInvalidOperationException_WhenRolledBack()
    {
        using var handle = new FakeUnitOfWorkHandle();
        handle.Status = UnitOfWorkStatus.RolledBack;

        Assert.Throws<InvalidOperationException>(() => ((IUnitOfWorkHandle)handle).ThrowIfCompleted());
    }

    [Theory]

    [InlineData(UnitOfWorkStatus.Active, false)]
    [InlineData(UnitOfWorkStatus.Committed, true)]
    [InlineData(UnitOfWorkStatus.RolledBack, true)]
    [InlineData(UnitOfWorkStatus.Disposed, true)]
    public void IsCompleted(UnitOfWorkStatus status, bool expected)
    {
        using var handle = new FakeUnitOfWorkHandle();
        handle.Status = status;

        Assert.Equal(expected, ((IUnitOfWorkHandle)handle).IsCompleted);
    }

    private sealed class FakeUnitOfWorkHandle
        : IUnitOfWorkHandle
        , IDisposable
    {
        public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        public UnitOfWorkStatus Status { get; set; } = UnitOfWorkStatus.Active;

        UnitOfWorkStatus IUnitOfWorkHandle.Status
            => Status;

        CancellationToken IUnitOfWorkHandle.Token
            => throw new NotImplementedException();

        void IDisposable.Dispose()
        {
            Status = UnitOfWorkStatus.Disposed;
            CancellationTokenSource.Dispose();
        }
    }
}
