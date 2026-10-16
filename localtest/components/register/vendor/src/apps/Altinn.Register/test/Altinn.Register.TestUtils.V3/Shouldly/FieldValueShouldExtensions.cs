using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Altinn.Authorization.ModelUtils;

namespace Shouldly;

[DebuggerStepThrough]
[ShouldlyMethods]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class FieldValueShouldExtensions
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBeNull<T>(this FieldValue<T> actual, string? customMessage = null)
        where T : notnull
    {
        // https://github.com/shouldly/shouldly/issues/1092
        actual.AssertAwesomely(static v => v.IsNull, FieldValue<T>.Null, actual, customMessage);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBeUnset<T>(this FieldValue<T> actual, string? customMessage = null)
        where T : notnull
    {
        actual.AssertAwesomely(static v => v.IsUnset, actual, FieldValue<T>.Unset, customMessage);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T ShouldHaveValue<T>(this FieldValue<T> actual, string? customMessage = null)
        where T : notnull
    {
        actual.AssertAwesomely(static v => v.HasValue, actual, FieldValue<T>.Unset, customMessage);
        return actual.Value!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBe<T>(this FieldValue<T> actual, T? expected, string? customMessage = null)
        where T : notnull
    {
        if (expected is null)
        {
            actual.AssertAwesomely(v => v.IsNull, actual, expected, customMessage);
        }
        else
        {
            actual.AssertAwesomely(v => v.HasValue && v.Value.Equals(expected), actual, expected, customMessage);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBe<T>(this FieldValue<T> actual, FieldValue<T> expected, string? customMessage = null)
        where T : notnull
    {
        actual.AssertAwesomely(v => v.Equals(expected), actual, expected, customMessage);
    }
}
