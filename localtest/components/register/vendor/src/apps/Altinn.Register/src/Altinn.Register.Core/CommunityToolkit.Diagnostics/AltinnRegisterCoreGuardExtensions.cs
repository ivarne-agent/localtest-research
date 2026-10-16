using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CommunityToolkit.Diagnostics;

/// <summary>
/// Extension methods for <see cref="Guard"/> and <see cref="ThrowHelper"/> in Altinn Register Core.
/// </summary>
public static class AltinnRegisterCoreGuardExtensions
{
    extension(ThrowHelper)
    {
        /// <summary>
        /// Throws a new <see cref="InvalidCastException"/>.
        /// </summary>
        /// <param name="message">The message to include in the exception.</param>
        /// <exception cref="InvalidCastException">Thrown with the specified parameter.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidCastException(string message)
            => throw new InvalidCastException(message);

        /// <summary>
        /// Throws a new <see cref="InvalidCastException"/>.
        /// </summary>
        /// <param name="message">The message to include in the exception.</param>
        /// <exception cref="InvalidCastException">Thrown with the specified parameter.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static T ThrowInvalidCastException<T>(string message)
            where T : allows ref struct
            => throw new InvalidCastException(message);

        /// <summary>
        /// Throws a new <see cref="UnreachableException"/> with the specified message. This method is intended to be used in code paths that are expected to be unreachable, and will throw an exception if executed.
        /// </summary>
        /// <param name="message">The message to include in the exception.</param>
        /// <exception cref="UnreachableException">Thrown with the specified message.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Unreachable(string message = "Reached code that should be unreachable.")
            => throw new UnreachableException(message);

        /// <summary>
        /// Throws a new <see cref="UnreachableException"/> with the specified message. This method is intended to be used in code paths that are expected to be unreachable, and will throw an exception if executed.
        /// </summary>
        /// <typeparam name="T">The type parameter for the method.</typeparam>
        /// <param name="message">The message to include in the exception.</param>
        /// <returns>Does not return a value.</returns>
        /// <exception cref="UnreachableException">Thrown with the specified message.</exception>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static T Unreachable<T>(string message = "Reached code that should be unreachable.")
            where T : allows ref struct
            => throw new UnreachableException(message);
    }
}
