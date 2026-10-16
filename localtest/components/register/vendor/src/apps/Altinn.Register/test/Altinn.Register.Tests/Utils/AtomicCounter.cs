namespace Altinn.Register.Tests.Utils;

internal sealed class AtomicCounter
{
    private uint _value = 0;

    public void Increment()
    {
        Interlocked.Increment(ref _value);
    }

    public uint Value
        => Volatile.Read(ref _value);
}
