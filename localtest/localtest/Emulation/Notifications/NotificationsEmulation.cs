using System.Globalization;
using System.Text.Json.Nodes;
using Altinn.Register.Localtest.TestData;

namespace Localtest.Emulated;

/// <summary>
/// Notifications in minimal mode, under <c>notifications/api/</c>: e-mail, SMS and <c>future/orders</c> orders are
/// stored with their messages (recipients resolved from the test data: a person's e-mail and mobile number, an
/// organisation's notification addresses or its main unit's), answered with the ids app-lib reads, cancellable, and
/// reported <c>Delivered</c> at once. The Notifications component's inspection paths read the messages back:
/// <c>localtest/inboxes</c>, <c>localtest/inboxes/{address}</c>, <c>GET/DELETE localtest/messages</c>.
/// </summary>
public static class NotificationsEmulation
{
    public const string Service = "notifications";

    public static void Map(WebApplication app, string prefix)
    {
        var v1 = app.MapGroup(prefix + "/api/v1");

        v1.MapPost("/orders/email", async (HttpRequest request, TestDataStore store, NotificationLog log) =>
        {
            var order = await Body(request);
            var addresses = Recipients(order["recipients"]).SelectMany(r => r["emailAddress"]?.ToString() is { Length: > 0 } email
                ? [(email, email)]
                : Resolve(store.Current, r["nationalIdentityNumber"]?.ToString(), r["organizationNumber"]?.ToString() ?? r["organisationNumber"]?.ToString(), email: true)).ToList();
            var id = log.Add(Text(order, "sendersReference"), Creator(request), null, addresses.Select(a => new NewMessage("email", a.Address, a.Recipient, Text(order, "fromAddress"), Text(order, "subject"), Text(order, "body") ?? string.Empty, Text(order, "contentType"))));
            return Results.Text(new JsonObject { ["orderId"] = id.ToString() }.ToJsonString(), "application/json", statusCode: StatusCodes.Status202Accepted);
        });

        v1.MapPost("/orders/sms", async (HttpRequest request, TestDataStore store, NotificationLog log) =>
        {
            var order = await Body(request);
            var addresses = Recipients(order["recipients"]).SelectMany(r => r["mobileNumber"]?.ToString() is { Length: > 0 } mobile
                ? [(mobile, mobile)]
                : Resolve(store.Current, r["nationalIdentityNumber"]?.ToString(), r["organisationNumber"]?.ToString() ?? r["organizationNumber"]?.ToString(), email: false)).ToList();
            var id = log.Add(Text(order, "sendersReference"), Creator(request), null, addresses.Select(a => new NewMessage("sms", a.Address, a.Recipient, Text(order, "senderNumber"), null, Text(order, "body") ?? string.Empty, null)));
            return Results.Text(new JsonObject { ["orderId"] = id.ToString() }.ToJsonString(), "application/json", statusCode: StatusCodes.Status202Accepted);
        });

        v1.MapPost("/future/orders", async (HttpRequest request, TestDataStore store, NotificationLog log) =>
        {
            var order = await Body(request);
            var chain = Guid.NewGuid();
            var reference = Text(order, "sendersReference");
            var shipment = log.Add(reference, Creator(request), chain, FutureMessages(store.Current, order["recipient"] as JsonObject));
            var reminders = new JsonArray();
            foreach (var reminder in Recipients(order["reminders"]))
            {
                var reminderId = log.Add(Text(reminder, "sendersReference"), Creator(request), chain, FutureMessages(store.Current, reminder["recipient"] as JsonObject));
                reminders.Add((JsonNode)new JsonObject { ["shipmentId"] = reminderId.ToString(), ["sendersReference"] = Text(reminder, "sendersReference") });
            }

            var response = new JsonObject
            {
                ["notificationOrderId"] = chain.ToString(),
                ["notification"] = new JsonObject { ["shipmentId"] = shipment.ToString(), ["sendersReference"] = reference },
                ["reminders"] = reminders,
            };
            return Results.Text(response.ToJsonString(), "application/json", statusCode: StatusCodes.Status201Created);
        });

        v1.MapPut("/orders/{orderId:guid}/cancel", (Guid orderId, NotificationLog log) =>
            log.Cancel(orderId) ? Results.Ok() : Results.NotFound());

        v1.MapGet("/orders/{orderId:guid}", (Guid orderId, NotificationLog log) => OrderStatus(log, orderId));
        v1.MapGet("/orders/{orderId:guid}/status", (Guid orderId, NotificationLog log) => OrderStatus(log, orderId));
        v1.MapGet("/orders/{orderId:guid}/notifications/email", (Guid orderId, NotificationLog log) => Notifications(log, orderId, "email"));
        v1.MapGet("/orders/{orderId:guid}/notifications/sms", (Guid orderId, NotificationLog log) => Notifications(log, orderId, "sms"));
        v1.MapGet("/future/shipment/{shipmentId:guid}", (Guid shipmentId, NotificationLog log) =>
        {
            var messages = log.Messages.Where(m => m.OrderId == shipmentId).ToList();
            if (messages.Count == 0 && !log.Exists(shipmentId))
            {
                return Results.NotFound();
            }

            var cancelled = log.IsCancelled(shipmentId);
            var shipment = new JsonObject
            {
                ["shipmentId"] = shipmentId.ToString(),
                ["sendersReference"] = messages.FirstOrDefault()?.SendersReference,
                ["type"] = "Notification",
                ["status"] = cancelled ? "Order_Cancelled" : "Order_Completed",
                ["lastUpdate"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                ["recipients"] = new JsonArray([.. messages.Select(m => (JsonNode)new JsonObject
                {
                    ["type"] = m.Channel == "email" ? "Email" : "SMS",
                    ["destination"] = m.Address,
                    ["status"] = m.Channel == "email" ? "Email_Delivered" : "SMS_Delivered",
                    ["lastUpdate"] = m.CapturedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                })]),
            };
            return Results.Text(shipment.ToJsonString(), "application/json");
        });

        var localtest = app.MapGroup(prefix + "/api/localtest");

        localtest.MapGet("/inboxes", (NotificationLog log) =>
            Results.Text(new JsonObject { ["items"] = new JsonArray([.. log.Inboxes().Select(i => (JsonNode)i)]) }.ToJsonString(), "application/json"));

        localtest.MapGet("/inboxes/{address}", (string address, NotificationLog log) =>
        {
            var inbox = log.Inboxes().FirstOrDefault(i => string.Equals(i["address"]?.ToString(), address, StringComparison.OrdinalIgnoreCase));
            return inbox is null
                ? Results.NotFound()
                : Results.Text(new JsonObject
                {
                    ["inbox"] = inbox,
                    ["messages"] = new JsonArray([.. log.Messages.Where(m => string.Equals(m.Address, address, StringComparison.OrdinalIgnoreCase)).Select(m => (JsonNode)m.ToJson())]),
                }.ToJsonString(), "application/json");
        });

        localtest.MapGet("/messages", (Guid? orderId, string? channel, long? after, int? limit, NotificationLog log) =>
        {
            if (channel is not (null or "email" or "sms"))
            {
                return Results.BadRequest("channel must be email or sms.");
            }

            var messages = log.Messages
                .Where(m => orderId is null || m.OrderId == orderId || m.OrderChainId == orderId)
                .Where(m => channel is null || m.Channel == channel)
                .Where(m => m.Id > (after ?? 0))
                .Take(Math.Clamp(limit ?? 200, 1, 1000))
                .ToList();
            return Results.Text(new JsonObject
            {
                ["items"] = new JsonArray([.. messages.Select(m => (JsonNode)m.ToJson())]),
                ["next"] = messages.Count > 0 ? messages[^1].Id : after ?? 0,
            }.ToJsonString(), "application/json");
        });

        localtest.MapDelete("/messages", (NotificationLog log) =>
        {
            log.Clear();
            return Results.NoContent();
        });
    }

    private static async Task<JsonObject> Body(HttpRequest request)
        => await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted) as JsonObject ?? [];

    private static IEnumerable<JsonObject> Recipients(JsonNode? list) => list is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string? Text(JsonObject node, string name) => node[name]?.ToString();

    /// <summary>The app that ordered, from its platform access token (<c>urn:altinn:app</c>), when there is one.</summary>
    private static string? Creator(HttpRequest request)
        => Jwt.Payload(request.Headers["PlatformAccessToken"].ToString()) is { } claims ? Jwt.Claim(claims, "urn:altinn:app") : null;

    /// <summary>A person's or an organisation's addresses for one channel, from the test data.</summary>
    private static IEnumerable<(string Address, string Recipient)> Resolve(TestDataSnapshot data, string? ssn, string? orgNumber, bool email)
    {
        if (!string.IsNullOrEmpty(ssn) && data.BySsn.GetValueOrDefault(ssn) is { Person: { } person })
        {
            var address = email ? person.Email : person.MobileNumber;
            return string.IsNullOrEmpty(address) ? [] : [(address, ssn)];
        }

        if (!string.IsNullOrEmpty(orgNumber) && data.ByOrgNumber.GetValueOrDefault(orgNumber) is { Org: { } org } party)
        {
            var addresses = Addresses(org, email);
            if (addresses.Count == 0 && party.ParentPartyId is { } parent && data.ByPartyId.GetValueOrDefault(parent)?.Org is { } main)
            {
                addresses = Addresses(main, email);
            }

            return addresses.Select(a => (a, orgNumber));
        }

        return [];
    }

    private static List<string> Addresses(TestOrg org, bool email)
        => [.. org.NotificationAddresses.Select(a => email ? a.Email : a.MobileNumber).OfType<string>().Where(a => a.Length > 0)];

    /// <summary>The messages of one <c>future/orders</c> recipient: a direct e-mail or SMS, or a person or organisation by channel schema.</summary>
    private static IEnumerable<NewMessage> FutureMessages(TestDataSnapshot data, JsonObject? recipient)
    {
        if (recipient is null)
        {
            yield break;
        }

        if (recipient["recipientEmail"] is JsonObject direct && direct["emailAddress"]?.ToString() is { Length: > 0 } emailAddress)
        {
            yield return Email(emailAddress, emailAddress, direct["emailSettings"] as JsonObject);
        }

        if (recipient["recipientSms"] is JsonObject sms && sms["phoneNumber"]?.ToString() is { Length: > 0 } phone)
        {
            yield return Sms(phone, phone, sms["smsSettings"] as JsonObject);
        }

        foreach (var (key, ssnKey, orgKey) in new[] { ("recipientPerson", "nationalIdentityNumber", (string?)null), ("recipientOrganization", (string?)null, "orgNumber") })
        {
            if (recipient[key] is not JsonObject party)
            {
                continue;
            }

            var ssn = ssnKey is null ? null : party[ssnKey]?.ToString();
            var orgNumber = orgKey is null ? null : party[orgKey]?.ToString();
            var schema = party["channelSchema"]?.ToString() ?? "EmailPreferred";
            var emails = Resolve(data, ssn, orgNumber, email: true).ToList();
            var mobiles = Resolve(data, ssn, orgNumber, email: false).ToList();
            var (useEmail, useSms) = schema switch
            {
                "0" or "Email" => (true, false),
                "1" or "Sms" => (false, true),
                "3" or "SmsPreferred" => (mobiles.Count == 0, mobiles.Count > 0),
                "4" or "EmailAndSms" => (true, true),
                _ => (emails.Count > 0, emails.Count == 0),
            };
            if (useEmail)
            {
                foreach (var (address, who) in emails)
                {
                    yield return Email(address, who, party["emailSettings"] as JsonObject);
                }
            }

            if (useSms)
            {
                foreach (var (address, who) in mobiles)
                {
                    yield return Sms(address, who, party["smsSettings"] as JsonObject);
                }
            }
        }
    }

    private static NewMessage Email(string address, string recipient, JsonObject? settings)
        => new("email", address, recipient, settings?["senderEmailAddress"]?.ToString(), settings?["subject"]?.ToString(), settings?["body"]?.ToString() ?? string.Empty, settings?["contentType"]?.ToString());

    private static NewMessage Sms(string address, string recipient, JsonObject? settings)
        => new("sms", address, recipient, settings?["sender"]?.ToString(), null, settings?["body"]?.ToString() ?? string.Empty, null);

    private static IResult OrderStatus(NotificationLog log, Guid orderId)
    {
        var messages = log.Messages.Where(m => m.OrderId == orderId).ToList();
        if (messages.Count == 0 && !log.Exists(orderId))
        {
            return Results.NotFound();
        }

        var cancelled = log.IsCancelled(orderId);
        JsonObject Summary(string channel) => new()
        {
            ["generated"] = messages.Count(m => m.Channel == channel),
            ["succeeded"] = cancelled ? 0 : messages.Count(m => m.Channel == channel),
        };
        var status = new JsonObject
        {
            ["id"] = orderId.ToString(),
            ["sendersReference"] = messages.FirstOrDefault()?.SendersReference,
            ["creator"] = messages.FirstOrDefault()?.Creator,
            ["notificationChannel"] = messages.Select(m => m.Channel).Distinct().Count() > 1 ? "EmailAndSms" : messages.FirstOrDefault()?.Channel == "sms" ? "Sms" : "Email",
            ["processingStatus"] = new JsonObject
            {
                ["status"] = cancelled ? "Cancelled" : "Completed",
                ["description"] = cancelled ? "Order processing was stopped due to order being cancelled." : "Order processing is completed. All notifications have been generated.",
                ["lastUpdate"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            },
            ["notificationsStatusSummary"] = new JsonObject { ["email"] = Summary("email"), ["sms"] = Summary("sms") },
        };
        return Results.Text(status.ToJsonString(), "application/json");
    }

    private static IResult Notifications(NotificationLog log, Guid orderId, string channel)
    {
        var messages = log.Messages.Where(m => m.OrderId == orderId && m.Channel == channel).ToList();
        if (messages.Count == 0 && !log.Exists(orderId))
        {
            return Results.NotFound();
        }

        var result = new JsonObject
        {
            ["orderId"] = orderId.ToString(),
            ["sendersReference"] = messages.FirstOrDefault()?.SendersReference,
            ["generated"] = messages.Count,
            ["succeeded"] = messages.Count,
            ["notifications"] = new JsonArray([.. messages.Select(m => (JsonNode)new JsonObject
            {
                ["id"] = m.NotificationId.ToString(),
                ["succeeded"] = true,
                ["recipient"] = channel == "email" ? new JsonObject { ["emailAddress"] = m.Address } : new JsonObject { ["mobileNumber"] = m.Address },
                ["sendStatus"] = new JsonObject { ["status"] = "Delivered", ["lastUpdate"] = m.CapturedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) },
            })]),
        };
        return Results.Text(result.ToJsonString(), "application/json");
    }
}

/// <summary>A message to store: one channel, one address.</summary>
public sealed record NewMessage(string Channel, string Address, string Recipient, string? Sender, string? Subject, string Body, string? ContentType);

/// <summary>A stored message, in the Notifications component's inspection shape.</summary>
public sealed record StoredMessage(
    long Id,
    Guid OrderId,
    Guid? OrderChainId,
    Guid NotificationId,
    string Channel,
    string Address,
    string Recipient,
    string? Sender,
    string? Subject,
    string Body,
    string? ContentType,
    string? Creator,
    string? SendersReference,
    DateTimeOffset CapturedAt)
{
    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["channel"] = Channel,
        ["kind"] = "notification",
        ["address"] = Address,
        ["recipient"] = Recipient,
        ["sender"] = Sender,
        ["subject"] = Subject,
        ["body"] = Body,
        ["contentType"] = ContentType,
        ["attachments"] = new JsonArray(),
        ["orderId"] = OrderId.ToString(),
        ["orderChainId"] = OrderChainId?.ToString(),
        ["notificationId"] = NotificationId.ToString(),
        ["creator"] = Creator,
        ["sendersReference"] = SendersReference,
        ["capturedAt"] = CapturedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        ["outcome"] = "Delivered",
        ["outcomeAt"] = CapturedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
    };
}

/// <summary>The orders and their messages (the last 1,000 messages), in memory.</summary>
public sealed class NotificationLog
{
    public const int Capacity = 1000;

    private readonly Lock _lock = new();
    private readonly LinkedList<StoredMessage> _messages = new();
    private readonly HashSet<Guid> _orders = [];
    private readonly HashSet<Guid> _cancelled = [];
    private long _sequence;

    public IReadOnlyList<StoredMessage> Messages
    {
        get
        {
            lock (_lock)
            {
                return [.. _messages];
            }
        }
    }

    /// <summary>Stores an order and its messages; the order's id.</summary>
    public Guid Add(string? sendersReference, string? creator, Guid? chain, IEnumerable<NewMessage> messages)
    {
        var orderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var list = messages.ToList();
        lock (_lock)
        {
            _orders.Add(orderId);
            foreach (var m in list)
            {
                _messages.AddLast(new StoredMessage(++_sequence, orderId, chain, Guid.NewGuid(), m.Channel, m.Address, m.Recipient, m.Sender, m.Subject, m.Body, m.ContentType, creator, sendersReference, now));
            }

            while (_messages.Count > Capacity)
            {
                _messages.RemoveFirst();
            }
        }

        return orderId;
    }

    public bool Exists(Guid orderId)
    {
        lock (_lock)
        {
            return _orders.Contains(orderId);
        }
    }

    public bool Cancel(Guid orderId)
    {
        lock (_lock)
        {
            return _orders.Contains(orderId) && (_cancelled.Add(orderId) || true);
        }
    }

    public bool IsCancelled(Guid orderId)
    {
        lock (_lock)
        {
            return _cancelled.Contains(orderId);
        }
    }

    /// <summary>One inbox per address messages went to, as the component's inspection API lists them.</summary>
    public IEnumerable<JsonObject> Inboxes()
        => Messages.GroupBy(m => (m.Channel, m.Address)).Select(g => new JsonObject
        {
            ["address"] = g.Key.Address,
            ["channel"] = g.Key.Channel,
            ["receiving"] = true,
            ["messageCount"] = g.Count(),
            ["createdAt"] = g.First().CapturedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["lastMessageAt"] = g.Last().CapturedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        });

    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
        }
    }
}
