using System.Data;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ServiceDefaults.Leases;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Persistence.Leases;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence.Tests.Leases;

public class PostgresqlLeaseProviderTests
    : DatabaseTestBase
{
    protected override bool SeedData => false;

    private PostgresqlLeaseProvider? _provider;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _provider = GetRequiredService<PostgresqlLeaseProvider>();
    }

    private PostgresqlLeaseProvider Provider
        => _provider!;

    [Fact]
    public async Task Acquire_New()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);

        var result = await Provider.TryAcquireLease(id, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeTrue();
            result.Lease.ShouldNotBeNull();
            result.Lease.LeaseId.ShouldBe(id);
            result.Lease.Expires.ShouldBe(now + duration);
            result.Lease.Token.ShouldNotBe(Guid.Empty);
            result.Expires.ShouldBe(now + duration);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: result.Lease.Token,
            expectedExpires: now + duration,
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Acquire_New_WithCondition()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ifNotAcquiredFor = TimeSpan.FromMinutes(5);

        var result = await Provider.TryAcquireLease(id, duration, ifNotAcquiredFor, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeTrue();
            result.Lease.ShouldNotBeNull();
            result.Lease.LeaseId.ShouldBe(id);
            result.Lease.Expires.ShouldBe(now + duration);
            result.Lease.Token.ShouldNotBe(Guid.Empty);
            result.Expires.ShouldBe(now + duration);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: result.Lease.Token,
            expectedExpires: now + duration,
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Acquire_Expired()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);

        await WriteLease(id);

        var result = await Provider.TryAcquireLease(id, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeTrue();
            result.Lease.ShouldNotBeNull();
            result.Lease.LeaseId.ShouldBe(id);
            result.Lease.Expires.ShouldBe(now + duration);
            result.Lease.Token.ShouldNotBe(Guid.Empty);
            result.Expires.ShouldBe(now + duration);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: result.Lease.Token,
            expectedExpires: now + duration,
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Acquire_Expired_WithUnmetCondition()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ifNotAcquiredFor = TimeSpan.FromMinutes(5);

        var oldToken = Guid.NewGuid();
        await WriteLease(id, token: oldToken, acquired: now, released: now);

        var result = await Provider.TryAcquireLease(id, duration, ifNotAcquiredFor, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeFalse();
            result.Lease.ShouldBeNull();
            result.Expires.ShouldBe(DateTimeOffset.MinValue);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBe(now);
        }

        await CheckDatabase(
            id,
            expectedToken: oldToken,
            expectedExpires: DateTimeOffset.MinValue,
            expectedAcquired: now,
            expectedReleased: now);
    }

    [Fact]
    public async Task Acquire_Expired_WithMetCondition()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var then = now - TimeSpan.FromMinutes(10);
        var duration = TimeSpan.FromMinutes(1);
        var ifNotAcquiredFor = TimeSpan.FromMinutes(5);

        await WriteLease(id, acquired: then, released: then);

        var result = await Provider.TryAcquireLease(id, duration, ifNotAcquiredFor, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeTrue();
            result.Lease.ShouldNotBeNull();
            result.Lease.LeaseId.ShouldBe(id);
            result.Lease.Expires.ShouldBe(now + duration);
            result.Lease.Token.ShouldNotBe(Guid.Empty);
            result.Expires.ShouldBe(now + duration);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: result.Lease.Token,
            expectedExpires: now + duration,
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Acquire_Held()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);

        var oldToken = Guid.NewGuid();
        await WriteLease(id, token: oldToken, expires: now + TimeSpan.FromMinutes(5), acquired: now, released: FieldValue.Null);

        var result = await Provider.TryAcquireLease(id, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeFalse();
            result.Lease.ShouldBeNull();
            result.Expires.ShouldBe(now + TimeSpan.FromMinutes(5));
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: oldToken,
            expectedExpires: now + TimeSpan.FromMinutes(5),
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Renew_New()
    {
        var id = "test";
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), TimeProvider.GetUtcNow() + duration);

        var result = await Provider.TryRenewLease(ticket, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeFalse();
            result.Expires.ShouldBe(DateTimeOffset.MinValue);
            result.LastAcquiredAt.ShouldBeNull();
            result.LastReleasedAt.ShouldBeNull();
        }

        // check that db is stil empty
        await using var uow = await GetRequiredService<IUnitOfWorkManager>().CreateAsync(CancellationToken);
        var conn = uow.GetRequiredService<NpgsqlConnection>();
        await using var cmd = conn.CreateCommand(
            /*strpsql*/"""
            SELECT id, token, expires, acquired, released
            FROM register.lease
            WHERE id = @id
            """);

        cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult, CancellationToken);
        var read = await reader.ReadAsync(CancellationToken);

        read.ShouldBeFalse();
    }

    [Fact]
    public async Task Renew_WrongToken()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        var oldToken = Guid.NewGuid();
        await WriteLease(id, token: oldToken, expires: now + duration, acquired: now, released: FieldValue.Null);

        var result = await Provider.TryRenewLease(ticket, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeFalse();
            result.Expires.ShouldBe(now + duration);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: oldToken,
            expectedExpires: now + duration,
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Renew_Expired()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        await WriteLease(id, token: ticket.Token, expires: now - duration, acquired: now - (duration * 2), released: FieldValue.Null);

        var result = await Provider.TryRenewLease(ticket, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeFalse();
            result.Expires.ShouldBe(now - duration);
            result.LastAcquiredAt.ShouldBe(now - (duration * 2));
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: ticket.Token,
            expectedExpires: now - duration,
            expectedAcquired: now - (duration * 2),
            expectedReleased: null);
    }

    [Fact]
    public async Task Renew_Released()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        var oldToken = Guid.NewGuid();
        await WriteLease(id, token: oldToken, expires: DateTimeOffset.MinValue, acquired: now, released: now);

        var result = await Provider.TryRenewLease(ticket, duration, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeFalse();
            result.Expires.ShouldBe(DateTimeOffset.MinValue);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: oldToken,
            expectedExpires: DateTimeOffset.MinValue,
            expectedAcquired: now,
            expectedReleased: now);
    }

    [Fact]
    public async Task Renew_Active()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        await WriteLease(id, token: ticket.Token, expires: ticket.Expires, acquired: now, released: FieldValue.Null);

        var result = await Provider.TryRenewLease(ticket, duration * 2, cancellationToken: CancellationToken);
        {
            result.IsLeaseAcquired.ShouldBeTrue();
            result.Lease.ShouldNotBeNull();
            result.Lease.LeaseId.ShouldBe(id);
            result.Lease.Expires.ShouldBe(now + (duration * 2));
            result.Lease.Token.ShouldNotBe(Guid.Empty);
            result.Expires.ShouldBe(now + (duration * 2));
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: result.Lease.Token,
            expectedExpires: now + (duration * 2),
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Release_New()
    {
        var id = "test";
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), TimeProvider.GetUtcNow() + duration);

        var result = await Provider.ReleaseLease(ticket, cancellationToken: CancellationToken);
        {
            result.IsReleased.ShouldBeFalse();
            result.Expires.ShouldBe(DateTimeOffset.MinValue);
            result.LastAcquiredAt.ShouldBeNull();
            result.LastReleasedAt.ShouldBeNull();
        }

        // check that db is stil empty
        await using var uow = await GetRequiredService<IUnitOfWorkManager>().CreateAsync(CancellationToken);
        var conn = uow.GetRequiredService<NpgsqlConnection>();
        await using var cmd = conn.CreateCommand(
            /*strpsql*/"""
            SELECT id, token, expires, acquired, released
            FROM register.lease
            WHERE id = @id
            """);

        cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult, CancellationToken);
        var read = await reader.ReadAsync(CancellationToken);

        read.ShouldBeFalse();
    }

    [Fact]
    public async Task Release_WrongToken()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        var oldToken = Guid.NewGuid();
        await WriteLease(id, token: oldToken, expires: now + duration, acquired: now, released: FieldValue.Null);

        var result = await Provider.ReleaseLease(ticket, cancellationToken: CancellationToken);
        {
            result.IsReleased.ShouldBeFalse();
            result.Expires.ShouldBe(now + duration);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: oldToken,
            expectedExpires: now + duration,
            expectedAcquired: now,
            expectedReleased: null);
    }

    [Fact]
    public async Task Release_Expired()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        await WriteLease(id, token: ticket.Token, expires: now - duration, acquired: now - (duration * 2), released: FieldValue.Null);

        var result = await Provider.ReleaseLease(ticket, cancellationToken: CancellationToken);
        {
            result.IsReleased.ShouldBeFalse();
            result.Expires.ShouldBe(now - duration);
            result.LastAcquiredAt.ShouldBe(now - (duration * 2));
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: ticket.Token,
            expectedExpires: now - duration,
            expectedAcquired: now - (duration * 2),
            expectedReleased: null);
    }

    [Fact]
    public async Task Release_Released()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        var oldToken = Guid.NewGuid();
        await WriteLease(id, token: oldToken, expires: DateTimeOffset.MinValue, acquired: now, released: now);

        var result = await Provider.ReleaseLease(ticket, cancellationToken: CancellationToken);
        {
            result.IsReleased.ShouldBeFalse();
            result.Expires.ShouldBe(DateTimeOffset.MinValue);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBeNull();
        }

        await CheckDatabase(
            id,
            expectedToken: oldToken,
            expectedExpires: DateTimeOffset.MinValue,
            expectedAcquired: now,
            expectedReleased: now);
    }

    [Fact]
    public async Task Release_Active()
    {
        var id = "test";
        var now = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromMinutes(1);
        var ticket = new LeaseTicket(id, Guid.NewGuid(), now + duration);

        await WriteLease(id, token: ticket.Token, expires: ticket.Expires, acquired: now, released: FieldValue.Null);

        var result = await Provider.ReleaseLease(ticket, cancellationToken: CancellationToken);
        {
            result.IsReleased.ShouldBeTrue();
            result.Expires.ShouldBe(DateTimeOffset.MinValue);
            result.LastAcquiredAt.ShouldBe(now);
            result.LastReleasedAt.ShouldBe(now);
        }

        await CheckDatabase(
            id,
            expectedToken: null,
            expectedExpires: DateTimeOffset.MinValue,
            expectedAcquired: now,
            expectedReleased: now);
    }

    private async Task WriteLease(
        string id,
        FieldValue<Guid> token = default,
        FieldValue<DateTimeOffset> expires = default,
        FieldValue<DateTimeOffset> acquired = default,
        FieldValue<DateTimeOffset> released = default)
    {
        await using var uow = await GetRequiredService<IUnitOfWorkManager>().CreateAsync(CancellationToken);
        var conn = uow.GetRequiredService<NpgsqlConnection>();
        await using var cmd = conn.CreateCommand(
            /*strpsql*/"""
            INSERT INTO register.lease (id, token, expires, acquired, released)
            VALUES (@id, @token, @expires, @acquired, @released)
            ON CONFLICT (id) DO UPDATE SET
                token = EXCLUDED.token,
                expires = EXCLUDED.expires,
                acquired = EXCLUDED.acquired,
                released = EXCLUDED.released
            """);

        var now = TimeProvider.GetUtcNow();
        cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;
        cmd.Parameters.Add<Guid>("token", NpgsqlDbType.Uuid).TypedValue = token.OrDefault(Guid.NewGuid());
        cmd.Parameters.Add<DateTimeOffset>("expires", NpgsqlDbType.TimestampTz).TypedValue = expires.OrDefault(DateTimeOffset.MinValue);
        cmd.Parameters.Add<DateTimeOffset?>("acquired", NpgsqlDbType.TimestampTz).TypedValue = acquired switch { { HasValue: true, Value: var value } => value, { IsNull: true } => null, _ => now };
        cmd.Parameters.Add<DateTimeOffset?>("released", NpgsqlDbType.TimestampTz).TypedValue = released switch { { HasValue: true, Value: var value } => value, { IsNull: true } => null, _ => now };

        await cmd.PrepareAsync(CancellationToken);
        await cmd.ExecuteNonQueryAsync(CancellationToken);
        await uow.CommitAsync(CancellationToken);
    }

    private async Task CheckDatabase(
        string id,
        Guid? expectedToken,
        DateTimeOffset expectedExpires,
        DateTimeOffset? expectedAcquired,
        DateTimeOffset? expectedReleased)
    {
        await using var uow = await GetRequiredService<IUnitOfWorkManager>().CreateAsync(CancellationToken);
        var conn = uow.GetRequiredService<NpgsqlConnection>();
        await using var cmd = conn.CreateCommand(
            /*strpsql*/"""
            SELECT id, token, expires, acquired, released
            FROM register.lease
            WHERE id = @id
            """);

        cmd.Parameters.Add<string>("id", NpgsqlDbType.Text).TypedValue = id;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SingleResult, CancellationToken);
        var read = await reader.ReadAsync(CancellationToken);
        read.ShouldBeTrue();

        (await reader.GetFieldValueAsync<string>("id", CancellationToken)).ShouldBe(id);
        (await reader.GetFieldValueAsync<DateTimeOffset>("expires", CancellationToken)).ShouldBe(expectedExpires);
        (await reader.GetFieldValueAsync<DateTimeOffset?>("acquired", CancellationToken)).ShouldBe(expectedAcquired);
        (await reader.GetFieldValueAsync<DateTimeOffset?>("released", CancellationToken)).ShouldBe(expectedReleased);

        if (expectedToken.HasValue)
        {
            (await reader.GetFieldValueAsync<Guid>("token", CancellationToken)).ShouldBe(expectedToken.Value);
        }
    }
}
