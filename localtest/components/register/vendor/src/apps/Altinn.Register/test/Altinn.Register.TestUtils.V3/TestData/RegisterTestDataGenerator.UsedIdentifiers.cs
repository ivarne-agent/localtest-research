using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Contracts;
using CommunityToolkit.Diagnostics;
using Npgsql;

namespace Altinn.Register.TestUtils.TestData;

/// <summary>
/// Generator for test data.
/// </summary>
public sealed partial class RegisterTestDataGenerator
{
    private sealed class UsedIdentifiers
    {
        public static async Task<UsedIdentifiers> Fetch(NpgsqlDataSource db, CancellationToken cancellationToken = default)
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var orgIds = await ReadSet(
                conn,
                /*strpsql*/"""
                SELECT organization_identifier
                FROM register.party
                WHERE organization_identifier IS NOT NULL
                """,
                static r => OrganizationIdentifier.Parse(r.GetString(0)),
                cancellationToken);

            var persIds = await ReadSet(
                conn,
                /*strpsql*/"""
                SELECT person_identifier
                FROM register.party
                WHERE person_identifier IS NOT NULL
                """,
                static r => PersonIdentifier.Parse(r.GetString(0)),
                cancellationToken);

            var partyId = await ReadMax(
                conn,
                /*strpsql*/"""
                SELECT MAX(id)
                FROM register.party
                WHERE id < 1000000000
                """,
                cancellationToken);

            var userId = await ReadMax(
                conn,
                /*strpsql*/"""
                SELECT MAX(user_id)
                FROM register."user"
                WHERE user_id < 1000000000
                """,
                cancellationToken);

            await tx.RollbackAsync(cancellationToken);
            return new UsedIdentifiers(orgIds, persIds, partyId, userId);

            static async Task<ConcurrentSet<T>> ReadSet<T>(
                NpgsqlConnection conn,
                string sql,
                Func<NpgsqlDataReader, T> readValue,
                CancellationToken cancellationToken)
            {
                var set = new HashSet<T>();
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var value = readValue(reader);
                    set.Add(value);
                }

                return new(set);
            }

            static async Task<AtomicULong> ReadMax(
                NpgsqlConnection conn,
                string sql,
                CancellationToken cancellationToken)
            {
                await using var cmd = conn.CreateCommand(sql);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new AtomicULong(0);
                }

                if (await reader.IsDBNullAsync(0, cancellationToken))
                {
                    return new AtomicULong(0);
                }

                var longValue = await reader.GetFieldValueAsync<long>(0);
                return new(checked((ulong)longValue));
            }
        }

        private readonly ConcurrentSet<OrganizationIdentifier> _orgIds;
        private readonly ConcurrentSet<PersonIdentifier> _persIds;
        private readonly AtomicULong _partyId;
        private readonly AtomicULong _userId;

        private UsedIdentifiers(
            ConcurrentSet<OrganizationIdentifier> orgIds,
            ConcurrentSet<PersonIdentifier> persIds,
            AtomicULong partyId,
            AtomicULong userId)
        {
            _orgIds = orgIds;
            _persIds = persIds;
            _partyId = partyId;
            _userId = userId;
        }

        public uint GetNextPartyId()
            => checked((uint)_partyId.Next());

        public uint GetNextUserId()
            => checked((uint)_userId.Next());

        public IReadOnlyList<uint> GetNextUserIds(int count)
        {
            var result = new uint[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = GetNextUserId();
            }

            return result;
        }

        public OrganizationIdentifier GetNewOrgNumber(SharedDeterministicRandom rng)
        {
            return _orgIds.Gen(rng, static rng =>
            {
                Span<char> s = stackalloc char[9];
                Vector128<ushort> weights = Vector128.Create((ushort)3, 2, 7, 6, 5, 4, 3, 2);

                while (true)
                {
                    // 8 digit random number
                    var random = rng.Next(10_000_000, 99_999_999);
                    Debug.Assert(random.TryFormat(s, out var written, provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 8);

                    ReadOnlySpan<ushort> chars = MemoryMarshal.Cast<char, ushort>(s);

                    Vector128<ushort> zeroDigit = Vector128.Create('0', '0', '0', '0', '0', '0', '0', '0');
                    Vector128<ushort> charsVec = Vector128.Create(chars);

                    var sum = Vector128.Sum((charsVec - zeroDigit) * weights);

                    var ctrlDigit = 11 - (sum % 11);
                    if (ctrlDigit == 11)
                    {
                        ctrlDigit = 0;
                    }

                    if (ctrlDigit == 10)
                    {
                        continue;
                    }

                    Debug.Assert(ctrlDigit is >= 0 and <= 9, $"ctrlDigit was {ctrlDigit}");
                    s[8] = (char)('0' + ctrlDigit);

                    return OrganizationIdentifier.Parse(new string(s));
                }
            });
        }

        public PersonIdentifier GetNewPersonIdentifier(SharedDeterministicRandom rng, DateOnly birthDate, bool isDNumber)
        {
            return _persIds.Gen(rng, (birthDate, isDNumber), static (rng, data) =>
            {
                var (dateComp, isDNumber) = data;
                Vector256<ushort> k1weights = Vector256.Create((ushort)3, 7, 6, 1, 8, 9, 4, 5, 2, 0, 0, 0, 0, 0, 0, 0);
                Vector256<ushort> k2weights = Vector256.Create((ushort)5, 4, 3, 2, 7, 6, 5, 4, 3, 2, 0, 0, 0, 0, 0, 0);
                Span<ushort> k1_candidates = stackalloc ushort[4];

                var dayOffset = isDNumber ? 40 : 0;
                int written;

                while (true)
                {
                    var individualNumber = rng.Next(0, 1000);
                    Span<char> s = stackalloc char[11];
                    s.Fill('0');

                    var day = dateComp.Day + dayOffset;
                    var month = dateComp.Month;
                    var year = dateComp.Year % 100;

                    Debug.Assert(day.TryFormat(s, out written, "D2", provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 2);

                    Debug.Assert(month.TryFormat(s.Slice(2), out written, "D2", provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 2);

                    Debug.Assert(year.TryFormat(s.Slice(4), out written, "D2", provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 2);

                    Debug.Assert(individualNumber.TryFormat(s.Slice(6), out written, "D3", provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 3);

                    Vector256<ushort> digits = CreateVector(s);

                    var k1c_base = (ushort)(Vector256.Sum(digits * k1weights) % 11);
                    var k1c_1 = (ushort)((11 - k1c_base) % 11);
                    var k1c_2 = (ushort)((12 - k1c_base) % 11);
                    var k1c_3 = (ushort)((13 - k1c_base) % 11);
                    var k1c_4 = (ushort)((14 - k1c_base) % 11);

                    var idx = 0;
                    AddIfValid(k1_candidates, ref idx, k1c_1);
                    AddIfValid(k1_candidates, ref idx, k1c_2);
                    AddIfValid(k1_candidates, ref idx, k1c_3);
                    AddIfValid(k1_candidates, ref idx, k1c_4);

                    var k1 = k1_candidates[rng.Next(0, idx)];
                    Debug.Assert(k1.TryFormat(s.Slice(9), out written, "D1", provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 1);

                    digits = CreateVector(s);
                    var k2 = (ushort)((11 - (Vector256.Sum(digits * k2weights) % 11)) % 11);

                    if (k2 == 10)
                    {
                        continue;
                    }

                    Debug.Assert(k2.TryFormat(s.Slice(10), out written, "D1", provider: CultureInfo.InvariantCulture));
                    Debug.Assert(written == 1);

                    if (!PersonIdentifier.TryParse(s, provider: null, out var result))
                    {
                        Assert.Fail($"Generated illegal person identifier: {new string(s)}");
                    }

                    return result;
                }

                static void AddIfValid(Span<ushort> candidates, ref int idx, ushort value)
                {
                    if (value != 10)
                    {
                        candidates[idx++] = value;
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                static Vector256<ushort> CreateVector(ReadOnlySpan<char> s)
                {
                    Debug.Assert(s.Length == 11);

                    Span<ushort> c = stackalloc ushort[16];
                    c.Clear(); // zero out the vector
                    MemoryMarshal.Cast<char, ushort>(s).CopyTo(c);

                    var chars = Vector256.Create<ushort>(c);
                    var zeros = Vector256.Create('0', '0', '0', '0', '0', '0', '0', '0', '0', '0', '0', 0, 0, 0, 0, 0);

                    return chars - zeros;
                }
            });
        }

        private sealed class ConcurrentSet<T>
        {
            private readonly Lock _lock = new();
            private readonly HashSet<T> _set;

            public ConcurrentSet(HashSet<T> set)
            {
                _set = set;
            }

            public T Gen(SharedDeterministicRandom rng, Func<SharedDeterministicRandom, T> generator)
            {
                Guard.IsNotNull(generator);

                T newValue;
                bool added;
                do
                {
                    newValue = generator(rng);

                    lock (_lock)
                    {
                        added = _set.Add(newValue);
                    }
                }
                while (!added);

                return newValue;
            }

            public T Gen<TData>(SharedDeterministicRandom rng, TData data, Func<SharedDeterministicRandom, TData, T> generator)
            {
                Guard.IsNotNull(generator);

                T newValue;
                bool added;

                do
                {
                    newValue = generator(rng, data);

                    lock (_lock)
                    {
                        added = _set.Add(newValue);
                    }
                }
                while (!added);

                return newValue;
            }
        }

        private sealed class AtomicULong
        {
            private ulong _prev;

            public AtomicULong(ulong maxUsed)
            {
                _prev = maxUsed;
            }

            public ulong Next()
                => Interlocked.Increment(ref _prev);
        }
    }

    private sealed class SharedDeterministicRandom(int seed)
    {
        private readonly Lock _lock = new();
        private readonly Random _random = new(seed);

        /// <inheritdoc cref="Random.Next(int, int)"/>
        internal int Next(int minValue, int maxValue)
        {
            lock (_lock)
            {
                return _random.Next(minValue, maxValue);
            }
        }

        /// <summary>
        /// Returns a random enum value of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <returns>A random <typeparamref name="T"/>.</returns>
        internal T Next<T>()
            where T : struct, Enum
        {
            var values = Enum.GetValues<T>();
            var index = Next(0, values.Length);
            return values[index];
        }

        internal T Next<T>(IReadOnlyList<T> values)
        {
            var index = Next(0, values.Count);
            return values[index];
        }

        internal bool NextBool(double chance)
        {
            lock (_lock)
            {
                return _random.NextDouble() <= chance;
            }
        }

        /// <summary>
        /// Generates a new <see cref="System.Guid"/>.
        /// </summary>
        /// <returns>A UUID.</returns>
        /// <remarks>
        /// Currently not deterministic.
        /// </remarks>
        internal Guid Guid()
        {
            return System.Guid.NewGuid();
        }
    }
}
