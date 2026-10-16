using System.ComponentModel;
using System.Runtime.CompilerServices;
using Altinn.Register.Core.Utils;
using Npgsql;

namespace Altinn.Register.Persistence;

/// <summary>
/// Represents a command that can be executed as an enumerable.
/// </summary>
internal abstract class NpgsqlAsyncSideEffectEnumerable<T>
    : IAsyncSideEffectEnumerable<T>
{
    private readonly NpgsqlConnection _conn;
    private readonly string _commandText;
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlAsyncSideEffectEnumerable{T}"/> class.
    /// </summary>
    /// <param name="conn">The <see cref="NpgsqlConnection"/>.</param>
    /// <param name="commandText">The command text.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/>.</param>
    protected NpgsqlAsyncSideEffectEnumerable(
        NpgsqlConnection conn,
        string commandText,
        CancellationToken cancellationToken)
    {
        _conn = conn;
        _commandText = commandText;
        _cancellationToken = cancellationToken;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts = null;
        if (_cancellationToken != cancellationToken)
        {
            switch ((_cancellationToken.CanBeCanceled, cancellationToken.CanBeCanceled))
            {
                case (true, true):
                    // both can be cancelled, we need to combine them
                    cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);
                    cancellationToken = cts.Token;
                    break;

                case (true, false):
                    // only _cancellationToken can be cancelled, throw away the new one
                    cancellationToken = _cancellationToken;
                    break;

                default:
                    // only the new one (or none) can be cancelled, ignore _cancellationToken
                    break;
            }
        }

        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = _commandText;

            PrepareParameters(cmd.Parameters);
            await cmd.PrepareAsync(cancellationToken);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            await using var enumerator = Enumerate(reader, cancellationToken);
            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TaskAwaiter GetAwaiter()
        => ((Task)ExecuteNonQuery()).GetAwaiter();

    private async Task<int> ExecuteNonQuery()
    {
        var cancellationToken = _cancellationToken;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = _commandText;

        PrepareParameters(cmd.Parameters);
        await cmd.PrepareAsync(cancellationToken);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Prepares the parameters of the command.
    /// </summary>
    /// <param name="parameters">The <see cref="NpgsqlParameterCollection"/> to add parameters to.</param>
    protected virtual void PrepareParameters(NpgsqlParameterCollection parameters)
    {
    }

    /// <summary>
    /// Enumerates over a <see cref="NpgsqlDataReader"/> and yields <typeparamref name="T"/> items.
    /// </summary>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns></returns>
    protected abstract IAsyncEnumerator<T> Enumerate(NpgsqlDataReader reader, CancellationToken cancellationToken);
}
