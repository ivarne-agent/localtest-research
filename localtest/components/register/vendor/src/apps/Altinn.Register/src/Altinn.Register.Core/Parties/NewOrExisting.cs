namespace Altinn.Register.Core.Parties;

/// <summary>
/// Provides factory methods for creating instances of <see cref="NewOrExisting{T}"/> to represent values that can be either new or existing.
/// </summary>
public static class NewOrExisting
{
    /// <summary>
    /// Creates a new instance of <see cref="NewOrExisting{T}"/> with the specified value marked as new.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="NewOrExisting{T}"/> instance marked as new.</returns>
    public static NewOrExisting<T> New<T>(T value)
        => NewOrExisting<T>.New(value);

    /// <summary>
    /// Creates a new instance of <see cref="NewOrExisting{T}"/> with the specified value marked as existing.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="NewOrExisting{T}"/> instance marked as existing.</returns>
    public static NewOrExisting<T> Existing<T>(T value)
        => NewOrExisting<T>.Existing(value);
}

/// <summary>
/// Represents a value that can be either new or existing, along with a flag indicating whether it's new.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="IsNew">Indicates whether the value is new.</param>
public readonly record struct NewOrExisting<T>(T Value, bool IsNew)
{
    /// <summary>
    /// Creates a new instance of <see cref="NewOrExisting{T}"/> with the specified value marked as new.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="NewOrExisting{T}"/> instance marked as new.</returns>
    public static NewOrExisting<T> New(T value) => new(value, true);

    /// <summary>
    /// Creates a new instance of <see cref="NewOrExisting{T}"/> with the specified value marked as existing.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="NewOrExisting{T}"/> instance marked as existing.</returns>
    public static NewOrExisting<T> Existing(T value) => new(value, false);
}
