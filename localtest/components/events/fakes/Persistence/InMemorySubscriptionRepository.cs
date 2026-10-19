#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Events.Models;
using Altinn.Platform.Events.Repository;

namespace Altinn.Platform.Events.Localtest.Persistence;

/// <summary>
/// <see cref="SubscriptionRepository"/> on <see cref="LocaltestEventsStore"/>, following <c>find_subscription</c>,
/// <c>insert_subscription_v2</c>, <c>getsubscription_v3</c>, <c>getsubscriptions_v2</c>,
/// <c>getsubscriptionsbyconsumer_v3</c> and the two procedures. The production caching decorator still wraps it.
/// </summary>
public sealed class InMemorySubscriptionRepository(LocaltestEventsStore store) : ISubscriptionRepository
{
    public Task<Subscription> CreateSubscription(Subscription eventsSubscription)
    {
        var row = new Subscription
        {
            ResourceFilter = eventsSubscription.ResourceFilter.ToLower(),
            SourceFilter = eventsSubscription.SourceFilter is null ? null : new Uri(eventsSubscription.SourceFilter.AbsoluteUri),
            SubjectFilter = eventsSubscription.SubjectFilter,
            TypeFilter = eventsSubscription.TypeFilter,
            Consumer = eventsSubscription.Consumer,
            EndPoint = new Uri(eventsSubscription.EndPoint.AbsoluteUri),
            CreatedBy = eventsSubscription.CreatedBy,
            IncludeSubunits = eventsSubscription.IncludeSubunits,
        };
        return Task.FromResult(store.InsertSubscription(row));
    }

    public Task<Subscription> FindSubscription(Subscription eventsSubscription, CancellationToken cancellationToken)
    {
        string resourceFilter = eventsSubscription.ResourceFilter.ToLower();
        string? sourceFilter = eventsSubscription.SourceFilter?.AbsoluteUri;
        string endpoint = eventsSubscription.EndPoint.AbsoluteUri;

        var match = store.QuerySubscriptions(s =>
            s.ResourceFilter == resourceFilter
            && s.SourceFilter?.AbsoluteUri == sourceFilter
            && s.SubjectFilter == eventsSubscription.SubjectFilter
            && s.TypeFilter == eventsSubscription.TypeFilter
            && s.Consumer == eventsSubscription.Consumer
            && s.EndPoint.AbsoluteUri == endpoint
            && s.IncludeSubunits == eventsSubscription.IncludeSubunits).FirstOrDefault();
        return Task.FromResult(match!);
    }

    public Task<IReadOnlyList<Subscription>> GetSubscriptions(string resource, string subject, string eventType, CancellationToken cancellationToken)
    {
        subject ??= string.Empty;
        IReadOnlyList<Subscription> matches = store.QuerySubscriptions(s =>
            s.ResourceFilter == resource
            && (s.SubjectFilter is null || s.SubjectFilter == subject)
            && (s.TypeFilter is null || s.TypeFilter == eventType)
            && s.Validated).AsReadOnly();
        return Task.FromResult(matches);
    }

    public Task DeleteSubscription(int id)
    {
        store.DeleteSubscription(id);
        return Task.CompletedTask;
    }

    public Task SetValidSubscription(int id)
    {
        store.SetValidSubscription(id);
        return Task.CompletedTask;
    }

    public Task<Subscription> GetSubscription(int id)
        => Task.FromResult(store.QuerySubscriptions(s => s.Id == id).FirstOrDefault()!);

    public Task<List<Subscription>> GetSubscriptionsByConsumer(string consumer, bool includeInvalid)
        => Task.FromResult(store.QuerySubscriptions(s =>
            LocaltestEventsStore.Like(s.Consumer, consumer, ignoreCase: false) && (includeInvalid || s.Validated)));
}
