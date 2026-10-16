namespace Altinn.Register.Tests.Utils;

internal sealed class AtomicBool(bool initialValue = false)
{
    private const byte FALSE = 0;
    private const byte TRUE = 1;

    private byte _value = initialValue ? TRUE : FALSE;

    public void Set(bool value)
    {
        Interlocked.Exchange(ref _value, value ? TRUE : FALSE);
    }

    public bool Value
        => Volatile.Read(ref _value) == TRUE;
}
