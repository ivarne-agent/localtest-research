using System.Text;
using System.Text.Json;
using Altinn.Correspondence.API.Auth;
using Altinn.Correspondence.Core.Models.Entities;
using Altinn.Correspondence.Core.Models.Enums;
using Altinn.Correspondence.Localtest.Auth;
using Altinn.Correspondence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Altinn.Correspondence.Localtest.Inspection;

/// <summary>
/// Read-only queries behind <c>correspondence/api/localtest/</c>: the production tables through the production EF context,
/// and Hangfire's own tables (schema <c>hangfire</c>) with plain SQL, since the job state is where a failing dependency shows
/// (every call downstream of <c>POST correspondence</c> runs in a Hangfire job).
/// </summary>
public sealed class LocaltestCorrespondenceQueries(
    ApplicationDbContext db,
    NpgsqlDataSource dataSource,
    LocaltestDialogTokenKeys dialogTokenKeys,
    IOptions<LocaltestCorrespondenceOptions> options)
{
    private const string UndefinedTable = "42P01";

    private string Platform => options.Value.PlatformEndpoint.TrimEnd('/') + "/";

    public async Task<CorrespondencePage?> List(
        Guid? after, string? resourceId, string? sender, string? recipient, CorrespondenceStatus? status, int limit, CancellationToken cancellationToken)
    {
        IQueryable<CorrespondenceEntity> query = db.Correspondences.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            query = query.Where(c => c.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(sender))
        {
            var number = WithoutPrefix(sender);
            query = query.Where(c => c.Sender.EndsWith(number));
        }

        if (!string.IsNullOrWhiteSpace(recipient))
        {
            var identifier = WithoutPrefix(recipient);
            query = query.Where(c => c.Recipient.EndsWith(identifier));
        }

        if (status is { } wanted)
        {
            query = query.Where(c => c.Statuses.OrderByDescending(s => s.StatusChanged).Select(s => (CorrespondenceStatus?)s.Status).FirstOrDefault() == wanted);
        }

        if (after is { } afterId)
        {
            var anchor = await db.Correspondences.AsNoTracking().Where(c => c.Id == afterId).Select(c => (DateTimeOffset?)c.Created).FirstOrDefaultAsync(cancellationToken);
            if (anchor is not { } created)
            {
                return null;
            }

            query = query.Where(c => EF.Functions.GreaterThan(ValueTuple.Create(c.Created, c.Id), ValueTuple.Create(created, afterId)));
        }

        var rows = await query
            .OrderBy(c => c.Created).ThenBy(c => c.Id)
            .Take(limit + 1)
            .Select(c => new CorrespondenceSummary(
                c.Id,
                c.ResourceId,
                c.Sender,
                c.Recipient,
                c.SendersReference,
                c.Content!.MessageTitle,
                c.Statuses.OrderByDescending(s => s.StatusChanged).Select(s => (CorrespondenceStatus?)s.Status).FirstOrDefault(),
                c.Statuses.OrderByDescending(s => s.StatusChanged).Select(s => (DateTimeOffset?)s.StatusChanged).FirstOrDefault(),
                c.ExternalReferences.Where(r => r.ReferenceType == ReferenceType.DialogportenDialogId).Select(r => r.ReferenceValue).FirstOrDefault(),
                c.ExternalReferences.Where(r => r.ReferenceType == ReferenceType.DialogportenTransmissionId).Select(r => r.ReferenceValue).FirstOrDefault(),
                c.Content!.Attachments.Count,
                c.Notifications.Count,
                c.Created,
                c.RequestedPublishTime,
                c.Published))
            .ToListAsync(cancellationToken);

        var next = rows.Count > limit ? rows[limit - 1].Id : (Guid?)null;
        return new CorrespondencePage([.. rows.Take(limit)], next);
    }

    public async Task<object?> Get(Guid id, CancellationToken cancellationToken)
    {
        var c = await db.Correspondences.AsNoTracking().AsSplitQuery()
            .Include(c => c.Statuses)
            .Include(c => c.Content).ThenInclude(content => content!.Attachments).ThenInclude(a => a.Attachment).ThenInclude(a => a!.Statuses)
            .Include(c => c.Notifications)
            .Include(c => c.ExternalReferences)
            .Include(c => c.ForwardingEvents)
            .Include(c => c.ReplyOptions)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (c is null)
        {
            return null;
        }

        var dialogId = c.ExternalReferences.FirstOrDefault(r => r.ReferenceType == ReferenceType.DialogportenDialogId)?.ReferenceValue;
        var statuses = c.Statuses.OrderBy(s => s.StatusChanged).ToList();
        return new
        {
            c.Id,
            c.ResourceId,
            c.Sender,
            c.Recipient,
            c.MessageSender,
            c.SendersReference,
            Status = statuses.LastOrDefault()?.Status,
            StatusChanged = statuses.LastOrDefault()?.StatusChanged,
            StatusHistory = statuses.Select(s => new { s.Status, s.StatusText, s.StatusChanged, s.PartyUuid }),
            c.Created,
            c.RequestedPublishTime,
            c.Published,
            c.DueDateTime,
            c.IsConfirmationNeeded,
            c.IsConfidential,
            c.AllowForwarding,
            c.IgnoreReservation,
            c.PartyId,
            c.PropertyList,
            Content = c.Content is null ? null : new
            {
                c.Content.Language,
                c.Content.MessageTitle,
                c.Content.MessageSummary,
                c.Content.MessageBody,
            },
            Attachments = (c.Content?.Attachments ?? []).Where(a => a.Attachment is not null).Select(a => new
            {
                a.AttachmentId,
                a.Attachment!.FileName,
                a.Attachment.DisplayName,
                a.Attachment.SendersReference,
                Status = a.Attachment.Statuses.OrderBy(s => s.StatusChanged).LastOrDefault()?.Status,
                StatusHistory = a.Attachment.Statuses.OrderBy(s => s.StatusChanged).Select(s => new { s.Status, s.StatusText, s.StatusChanged }),
                Size = a.Attachment.AttachmentSize,
                a.Attachment.Checksum,
                a.Attachment.DataLocationUrl,
                a.ExpirationTime,
                Blob = $"{Platform}correspondence/api/localtest/blobs/{a.AttachmentId:D}",
            }),
            Notifications = c.Notifications.OrderBy(n => n.Created).Select(n => new
            {
                n.Id,
                n.NotificationOrderId,
                n.ShipmentId,
                n.NotificationTemplate,
                n.NotificationChannel,
                n.IsReminder,
                n.RequestedSendTime,
                n.NotificationSent,
                n.NotificationAddress,
                n.NotificationOrderStatus,
                n.Created,
                Captured = (n.ShipmentId ?? n.NotificationOrderId) is { } orderId
                    ? $"{Platform}notifications/api/localtest/messages?orderId={orderId:D}"
                    : null,
            }),
            ExternalReferences = c.ExternalReferences.Select(r => new { r.ReferenceType, r.ReferenceValue }),
            Dialog = dialogId is null ? null : $"{Platform}dialogporten/api/v1/serviceowner/dialogs/{dialogId}",
            ReplyOptions = c.ReplyOptions.Select(r => new { r.LinkURL, r.LinkText }),
            ForwardingEvents = c.ForwardingEvents.OrderBy(f => f.ForwardedOnDate).Select(f => new
            {
                f.Id,
                f.ForwardedOnDate,
                f.ForwardedByPartyUuid,
                f.ForwardedToUserId,
                f.ForwardedToEmailAddress,
                f.ForwardingText,
                f.MailboxSupplier,
            }),
            Jobs = await Jobs(state: null, argument: c.Id.ToString("D"), limit: 200, cancellationToken),
        };
    }

    /// <summary>Hangfire jobs, newest first; <paramref name="argument"/> keeps the jobs whose arguments contain it.</summary>
    public async Task<List<JobView>> Jobs(string? state, string? argument, int limit, CancellationToken cancellationToken)
    {
        var sql = new StringBuilder("""
            SELECT j.id, j.statename, j.createdat, j.invocationdata::text, j.arguments::text, s.reason, s.createdat, s.data::text,
                   (SELECT p.value FROM hangfire.jobparameter p WHERE p.jobid = j.id AND p.name = 'RetryCount' LIMIT 1),
                   (SELECT f.data::text FROM hangfire.state f WHERE f.jobid = j.id AND f.name = 'Failed' ORDER BY f.id DESC LIMIT 1)
            FROM hangfire.job j
            LEFT JOIN hangfire.state s ON s.id = j.stateid
            WHERE true
            """);
        await using var command = dataSource.CreateCommand();
        if (!string.IsNullOrWhiteSpace(state))
        {
            sql.Append(" AND lower(j.statename) = lower(@state)");
            command.Parameters.AddWithValue("state", state);
        }

        if (!string.IsNullOrWhiteSpace(argument))
        {
            sql.Append(" AND (j.arguments::text LIKE @argument OR j.invocationdata::text LIKE @argument)");
            command.Parameters.AddWithValue("argument", $"%{argument}%");
        }

        sql.Append(" ORDER BY j.id DESC LIMIT @limit");
        command.Parameters.AddWithValue("limit", limit);
        command.CommandText = sql.ToString();

        var jobs = new List<JobView>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var (method, arguments) = Invocation(reader.GetString(3), reader.GetString(4));
                var stateData = Data(reader.IsDBNull(7) ? null : reader.GetString(7));
                var failure = Data(reader.IsDBNull(9) ? null : reader.GetString(9));
                jobs.Add(new JobView(
                    Convert.ToInt64(reader.GetValue(0)),
                    method,
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetDateTime(2),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    stateData.GetValueOrDefault("EnqueueAt"),
                    reader.IsDBNull(8) ? 0 : int.TryParse(reader.GetString(8).Trim('"'), out var retries) ? retries : 0,
                    failure.GetValueOrDefault("ExceptionType"),
                    failure.GetValueOrDefault("ExceptionMessage"),
                    arguments));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
        {
            // Hangfire has not created its schema yet.
        }

        return jobs;
    }

    public async Task<object> Status(CancellationToken cancellationToken)
    {
        var known = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var servers = new List<object>();
        try
        {
            await using var command = dataSource.CreateCommand("SELECT id, lastheartbeat FROM hangfire.server ORDER BY id");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                servers.Add(new { Id = reader.GetString(0), LastHeartbeat = reader.GetDateTime(1) });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
        {
        }

        return new
        {
            Migrations = new
            {
                Known = known.Count,
                Applied = applied.Count,
                Latest = applied.LastOrDefault(),
                Pending = known.Except(applied).ToList(),
            },
            HangfireServers = servers,
            DialogTokenKeys = new
            {
                Jwks = dialogTokenKeys.JwksUrl,
                Localtest = dialogTokenKeys.KeyIds,
                dialogTokenKeys.LastFetch,
                // Upstream's start-time cache (EdDsaSecurityKeysCacheService), no longer used for validation.
                UpstreamCache = EdDsaSecurityKeysCacheService.EdDsaSecurityKeys.Count,
            },
        };
    }

    public async Task<string?> AttachmentFileName(Guid id, CancellationToken cancellationToken)
        => await db.Attachments.AsNoTracking().Where(a => a.Id == id).Select(a => a.FileName).FirstOrDefaultAsync(cancellationToken);

    private static string WithoutPrefix(string value) => value[(value.LastIndexOf(':') + 1)..];

    private static (string Method, IReadOnlyList<string> Arguments) Invocation(string invocationData, string arguments)
    {
        var method = "?";
        try
        {
            using var invocation = JsonDocument.Parse(invocationData);
            var type = invocation.RootElement.TryGetProperty("Type", out var t) ? t.GetString() ?? "?" : "?";
            var name = invocation.RootElement.TryGetProperty("Method", out var m) ? m.GetString() ?? "?" : "?";
            method = $"{type.Split(',')[0].Split('.')[^1]}.{name}";
        }
        catch (JsonException)
        {
        }

        try
        {
            using var parsed = JsonDocument.Parse(arguments);
            return (method, [.. parsed.RootElement.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString() ?? "null" : a.GetRawText())]);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return (method, [arguments]);
        }
    }

    private static Dictionary<string, string?> Data(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.ValueKind == JsonValueKind.Object
                ? parsed.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed record CorrespondencePage(IReadOnlyList<CorrespondenceSummary> Items, Guid? Next);

public sealed record CorrespondenceSummary(
    Guid Id,
    string ResourceId,
    string Sender,
    string Recipient,
    string SendersReference,
    string? Title,
    CorrespondenceStatus? Status,
    DateTimeOffset? StatusChanged,
    string? DialogId,
    string? TransmissionId,
    int AttachmentCount,
    int NotificationCount,
    DateTimeOffset Created,
    DateTimeOffset RequestedPublishTime,
    DateTimeOffset? Published);

public sealed record JobView(
    long Id,
    string Method,
    string? State,
    string? Reason,
    DateTime CreatedAt,
    DateTime? StateAt,
    string? EnqueueAt,
    int RetryCount,
    string? ExceptionType,
    string? ExceptionMessage,
    IReadOnlyList<string> Arguments);
