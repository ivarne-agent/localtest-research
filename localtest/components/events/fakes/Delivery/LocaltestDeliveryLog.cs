#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Events.Localtest.Changes;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Events.Localtest.Delivery;

/// <summary>Writes each webhook POST to <c>localtest.delivery</c> (created by <c>fakes/Startup/prepare.sql</c>).</summary>
public sealed class LocaltestDeliveryLog(NpgsqlDataSource dataSource, LocaltestChanges changes)
{
    private const string InsertSql = @"insert into localtest.delivery
        (event_id, event_type, kind, endpoint, mode, request_headers, request_body, response_status, response_body, duration_ms, error)
        values (@event_id, @event_type, @kind, @endpoint, @mode, @request_headers, @request_body, @response_status, @response_body, @duration_ms, @error)
        returning id";

    public async Task Record(DeliveryRecord delivery, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(InsertSql);
        command.Parameters.AddWithValue("event_id", (object?)delivery.EventId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_type", (object?)delivery.EventType ?? DBNull.Value);
        command.Parameters.AddWithValue("kind", delivery.Kind);
        command.Parameters.AddWithValue("endpoint", delivery.Endpoint);
        command.Parameters.AddWithValue("mode", delivery.Mode);
        command.Parameters.AddWithValue("request_headers", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(delivery.RequestHeaders));
        command.Parameters.AddWithValue("request_body", delivery.RequestBody);
        command.Parameters.AddWithValue("response_status", (object?)delivery.ResponseStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("response_body", (object?)delivery.ResponseBody ?? DBNull.Value);
        command.Parameters.AddWithValue("duration_ms", delivery.DurationMs);
        command.Parameters.AddWithValue("error", (object?)delivery.Error ?? DBNull.Value);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        changes.Raise("delivery", "created", id?.ToString());
    }
}

/// <summary>One outgoing webhook POST, as the capture handler saw it.</summary>
public sealed record DeliveryRecord(
    string? EventId,
    string? EventType,
    string Kind,
    string Endpoint,
    string Mode,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string RequestBody,
    int? ResponseStatus,
    string? ResponseBody,
    long DurationMs,
    string? Error);
