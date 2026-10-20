#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Notifications.Localtest.Startup;
using Npgsql;

namespace Altinn.Notifications.Localtest.Capture;

/// <summary>
/// The message capture and its inboxes, in the <c>localtest</c> schema of notificationsdb. One inbox per e-mail address
/// or phone number (the exact string), holding the messages sent to it. A message's order and
/// outcome are not stored: they are the production notification row's, joined in on read, so the capture always shows
/// what the production status machine says. Written and read as <c>platform_notifications_admin</c> (the localtest
/// layer, not the production data source).
/// </summary>
public sealed class LocaltestCaptureStore(LocaltestAdminDatabase database)
{
    public const string Email = "email";
    public const string Sms = "sms";

    private const string MessageColumns = """
        c.id, c.captured_at, c.channel, c.kind, c.notification_id, c.inbox, c.recipient, c.sender, c.subject, c.body,
        c.content_type, c.attachments::text, o.alternateid, o.creatorname, o.sendersreference,
        COALESCE(e.result::text, s.result::text), COALESCE(e.resulttime, s.resulttime), oc.orderid
        """;

    private const string MessageJoins = """
        FROM localtest.capture c
        LEFT JOIN notifications.emailnotifications e ON c.channel = 'email' AND e.alternateid = c.notification_id
        LEFT JOIN notifications.smsnotifications s ON c.channel = 'sms' AND s.alternateid = c.notification_id
        LEFT JOIN notifications.orders o ON o._id = COALESCE(e._orderid, s._orderid)
        LEFT JOIN notifications.orderschain oc ON oc._id = o._orderchainid
        """;

    /// <summary>The inbox key: the exact string the message is addressed to. Nothing is normalised (no case folding, no country code).</summary>
    public static string NormalizeAddress(string channel, string address) => address;

    /// <summary>The channel of an address given in a URL: an e-mail address has an <c>@</c>.</summary>
    public static string ChannelOf(string address) => address.Contains('@', StringComparison.Ordinal) ? Email : Sms;

    public async Task<long> Capture(NewCapturedMessage message, CancellationToken cancellationToken)
    {
        var inbox = NormalizeAddress(message.Channel, message.Recipient);
        await using var command = database.DataSource.CreateCommand("""
            WITH ensure_inbox AS (
                INSERT INTO localtest.inbox (address, channel) VALUES ($1, $2) ON CONFLICT (address) DO NOTHING
            )
            INSERT INTO localtest.capture (channel, kind, notification_id, inbox, recipient, sender, subject, body, content_type, attachments)
            VALUES ($2, $3, $4, $1, $5, $6, $7, $8, $9, $10::jsonb)
            RETURNING id
            """);
        command.Parameters.AddWithValue(inbox);
        command.Parameters.AddWithValue(message.Channel);
        command.Parameters.AddWithValue(message.Kind);
        command.Parameters.AddWithValue(message.NotificationId);
        command.Parameters.AddWithValue(message.Recipient);
        command.Parameters.AddWithValue((object?)message.Sender ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)message.Subject ?? DBNull.Value);
        command.Parameters.AddWithValue(message.Body);
        command.Parameters.AddWithValue((object?)message.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(message.Attachments));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<InboxSummary>> ListInboxes(CancellationToken cancellationToken)
    {
        await using var command = database.DataSource.CreateCommand("""
            SELECT i.address, i.channel, i.receiving, i.created_at, count(c.id), max(c.captured_at)
            FROM localtest.inbox i LEFT JOIN localtest.capture c ON c.inbox = i.address
            GROUP BY i.address, i.channel, i.receiving, i.created_at
            ORDER BY i.created_at, i.address
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var inboxes = new List<InboxSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            inboxes.Add(new InboxSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return inboxes;
    }

    public async Task<InboxSummary?> GetInbox(string address, CancellationToken cancellationToken)
        => (await ListInboxes(cancellationToken)).FirstOrDefault(i => i.Address == address);

    public async Task<IReadOnlyList<CapturedMessage>> Messages(MessageFilter filter, CancellationToken cancellationToken)
    {
        await using var command = database.DataSource.CreateCommand($"""
            SELECT {MessageColumns}
            {MessageJoins}
            WHERE ($1::uuid IS NULL OR o.alternateid = $1 OR oc.orderid = $1)
              AND ($2::text IS NULL OR c.inbox = $2)
              AND ($3::text IS NULL OR c.channel = $3)
              AND c.id > $4
            ORDER BY c.id
            LIMIT $5
            """);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)filter.OrderId ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)filter.Inbox ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)filter.Channel ?? DBNull.Value);
        command.Parameters.AddWithValue(filter.After);
        command.Parameters.AddWithValue(filter.Limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var messages = new List<CapturedMessage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new CapturedMessage(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                JsonSerializer.Deserialize<List<CapturedAttachment>>(reader.GetString(11)) ?? [],
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                reader.IsDBNull(17) ? null : reader.GetGuid(17)));
        }

        return messages;
    }

    /// <summary>Empties the capture. The inboxes stay (they will hold the reception switch); the production tables are not touched.</summary>
    public async Task Reset(CancellationToken cancellationToken)
        => await database.Execute("TRUNCATE localtest.capture RESTART IDENTITY", cancellationToken);

    /// <summary>The production result of a notification, or null when its row does not exist (yet).</summary>
    public async Task<string?> NotificationResult(string channel, Guid notificationId, CancellationToken cancellationToken)
    {
        var table = channel == Email ? "notifications.emailnotifications" : "notifications.smsnotifications";
        await using var command = database.DataSource.CreateCommand($"SELECT result::text FROM {table} WHERE alternateid = $1");
        command.Parameters.AddWithValue(notificationId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }
}

public sealed record NewCapturedMessage(
    string Channel,
    string Kind,
    Guid NotificationId,
    string Recipient,
    string? Sender,
    string? Subject,
    string Body,
    string? ContentType,
    IReadOnlyList<CapturedAttachment> Attachments);

public sealed record CapturedAttachment(string Filename, string MimeType, string SasUrl);

public sealed record MessageFilter(Guid? OrderId, string? Inbox, string? Channel, long After, int Limit);

public sealed record InboxSummary(
    string Address,
    string Channel,
    bool Receiving,
    DateTimeOffset CreatedAt,
    long MessageCount,
    DateTimeOffset? LastMessageAt);

/// <summary>
/// One captured message. <c>Kind</c> is <c>queued</c> (the production queue path), <c>composed</c> (composed e-mail,
/// attachments by reference) or <c>instant</c> (posted to the Email or SMS host's instant endpoint). <c>OrderId</c>,
/// <c>OrderChainId</c> (the <c>notificationOrderId</c> of a <c>future/orders</c> response; <c>OrderId</c> is then the
/// shipment id), <c>Creator</c>, <c>SendersReference</c>, <c>Outcome</c> and <c>OutcomeAt</c> come from the production
/// tables and are null until the production code has written the notification (instant orders write it after the post).
/// </summary>
public sealed record CapturedMessage(
    long Id,
    DateTimeOffset CapturedAt,
    string Channel,
    string Kind,
    Guid NotificationId,
    string Address,
    string Recipient,
    string? Sender,
    string? Subject,
    string Body,
    string? ContentType,
    IReadOnlyList<CapturedAttachment> Attachments,
    Guid? OrderId,
    string? Creator,
    string? SendersReference,
    string? Outcome,
    DateTimeOffset? OutcomeAt,
    Guid? OrderChainId);
