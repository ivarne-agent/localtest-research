using System.Diagnostics;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents an optional value that may or may not be present.
/// </summary>
/// <typeparam name="T">The inner type.</typeparam>
[DebuggerDisplay("HasValue = {HasValue}, Value = {Value}")]
public readonly struct Optional<T>
    where T : notnull
{
    private readonly T _value;

    /// <summary>
    /// Gets whether the current <see cref="Optional{T}"/> has a value or not.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets the value of the current <see cref="Optional{T}"/> if present; otherwise, throws an <see cref="InvalidOperationException"/>.
    /// </summary>
    public T Value => HasValue ? _value : throw new InvalidOperationException("No value present.");

    private Optional(T value)
    {
        _value = value;
        HasValue = true;
    }

    /// <summary>
    /// Implicitly converts a value of type <typeparamref name="T"/> to an <see cref="Optional{T}"/> with that value.
    /// </summary>
    /// <param name="value">The value.</param>
    public static implicit operator Optional<T>(T value) => new(value);
}
