using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using Altinn.Register.Contracts;
using Altinn.Register.Core;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.PartyImport.A2.Enrichers;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// An (optional) enrichment step for importing A2 parties.
/// </summary>
internal interface IA2PartyImportSagaEnrichmentStep
{
    /// <summary>
    /// Gets the (unique) name of the step.
    /// </summary>
    /// <remarks>
    /// This should <strong>NOT</strong> be changed, as it's persisted to the database and used to locate the step when processing messages.
    /// </remarks>
    public static abstract string StepName { get; }

    /// <summary>
    /// Determines whether the step is enabled based on the provided configuration.
    /// </summary>
    /// <param name="configuration"><see cref="IConfiguration"/>.</param>
    /// <returns>Whether or not the current step is enabled.</returns>
    public static virtual bool IsEnabled(IConfiguration configuration)
        => true;

    /// <summary>
    /// Determines whether the step should run for the given party.
    /// </summary>
    /// <param name="context">Context.</param>
    /// <returns>Whether or not the current step should run for the given party.</returns>
    public static abstract bool CanEnrich(A2PartyImportSagaEnrichmentCheckContext context);

    /// <summary>
    /// Executes the enrichment process for the specified party import saga context asynchronously.
    /// </summary>
    /// <param name="context">The context that provides the data and state required to perform the enrichment operation. Cannot be null.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/></param>
    public Task Run(A2PartyImportSagaEnrichmentRunContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context for <see cref="IA2PartyImportSagaEnrichmentStep.CanEnrich(A2PartyImportSagaEnrichmentCheckContext)"/>
/// </summary>
internal sealed class A2PartyImportSagaEnrichmentCheckContext
{
    /// <summary>
    /// Gets the unique identifier for the party.
    /// </summary>
    public required ImportPartyIdentifier PartyIdentifier { get; init; }

    /// <summary>
    /// Gets the party being evaluated for enrichment.
    /// </summary>
    public required PartyRecord Party { get; init; }
}

/// <summary>
/// Context for <see cref="IA2PartyImportSagaEnrichmentStep.Run(A2PartyImportSagaEnrichmentRunContext, CancellationToken)"/>
/// </summary>
internal sealed class A2PartyImportSagaEnrichmentRunContext
{
    /// <summary>
    /// Gets the unique identifier for the party.
    /// </summary>
    public required ImportPartyIdentifier PartyIdentifier { get; init; }

    /// <summary>
    /// Gets or sets the party being enriched.
    /// </summary>
    public required PartyRecord Party { get; set; }

    /// <summary>
    /// Gets or sets role-assignments from <see cref="Party"/>.
    /// </summary>
    public required Dictionary<ExternalRoleSource, PartyExternalRoleAssignmentsUpdate> RoleAssignments { get; init; }
}

/// <summary>
/// Import saga enricher.
/// </summary>
internal abstract class A2PartyImportSagaEnricher
{
    private static readonly Cache _cache = new();

    /// <summary>
    /// Gets the names of all enrichers that should run for a given context.
    /// </summary>
    /// <param name="configuration"><see cref="IConfiguration"/>, used for checking enricher feature-flags.</param>
    /// <param name="context"><see cref="A2PartyImportSagaEnrichmentCheckContext"/>.</param>
    /// <returns>An enumeration of enrichers to run for the given context. Should be run in order.</returns>
    public static IEnumerable<string> For(IConfiguration configuration, A2PartyImportSagaEnrichmentCheckContext context)
    {
        foreach (var enricher in _cache.Enrichers)
        {
            if (!enricher.IsEnabled(configuration))
            {
                continue;
            }

            if (enricher.CanEnrich(context))
            {
                yield return enricher.Name;
            }
        }
    }

    /// <summary>
    /// Retrieves the instance of the A2PartyImportSagaEnricher associated with the specified name.
    /// </summary>
    /// <param name="name">The name of the enricher to retrieve. This value cannot be null or empty.</param>
    /// <returns>The A2PartyImportSagaEnricher instance corresponding to the specified name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The enricher was not found.</exception>
    public static A2PartyImportSagaEnricher Get(string name)
    {
        if (!_cache.ByName.TryGetValue(name, out var enricher))
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(name), $"No enricher found with name '{name}'.");
        }

        return enricher;
    }

    private readonly string _name;

    /// <summary>
    /// Initializes a new instance of the A2PartyImportSagaEnricher class with the specified name.
    /// </summary>
    /// <param name="name">The name used to identify the instance of the A2PartyImportSagaEnricher. Cannot be null or empty.</param>
    protected A2PartyImportSagaEnricher(string name)
    {
        _name = name;
    }

    /// <summary>
    /// Gets the step name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Determines whether the step is enabled based on the provided configuration.
    /// </summary>
    /// <param name="configuration"><see cref="IConfiguration"/>.</param>
    /// <returns>Whether or not the current step is enabled.</returns>
    public abstract bool IsEnabled(IConfiguration configuration);

    /// <summary>
    /// Determines whether the step should run for the given party.
    /// </summary>
    /// <param name="context">Context.</param>
    /// <returns>Whether or not the current step should run for the given party.</returns>
    public abstract bool CanEnrich(A2PartyImportSagaEnrichmentCheckContext context);

    /// <summary>
    /// Executes the enrichment process for the specified party import saga context asynchronously.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="context">The context that provides the data and state required to perform the enrichment operation. Cannot be null.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/></param>
    public abstract Task Run(IServiceProvider services, A2PartyImportSagaEnrichmentRunContext context, CancellationToken cancellationToken);

    private sealed class Cache
    {
        internal FrozenDictionary<string, A2PartyImportSagaEnricher> ByName { get; }

        internal ImmutableArray<A2PartyImportSagaEnricher> Enrichers { get; }

        public Cache()
        {
            var builder = ImmutableArray.CreateBuilder<A2PartyImportSagaEnricher>();
            builder.Add(new Impl<A2PartyUserEnricher>());
            builder.Add(new Impl<CcrRoleAssignmentsFromA2Enricher>());
            builder.Add(new Impl<NprEnricher>());
            builder.Add(new Impl<SireEnricher>());

            Enrichers = builder.DrainToImmutable();
            ByName = Enrichers.ToFrozenDictionary(e => e.Name);
        }
    }

    private sealed class Impl<T>()
        : A2PartyImportSagaEnricher(T.StepName)
        where T : IA2PartyImportSagaEnrichmentStep
    {
        private static readonly ObjectFactory<T> _factory
            = ActivatorUtilities.CreateFactory<T>([]);

        public override bool IsEnabled(IConfiguration configuration)
            => T.IsEnabled(configuration);

        public override bool CanEnrich(A2PartyImportSagaEnrichmentCheckContext context)
            => T.CanEnrich(context);

        public override async Task Run(IServiceProvider services, A2PartyImportSagaEnrichmentRunContext context, CancellationToken cancellationToken)
        {
            using var activity = RegisterTelemetry.StartActivity(
                $"enrich {Name}",
                tags: [
                    new("party.identifier", context.PartyIdentifier.ToLogSafeString()),
                    new("enrichment.name", Name),
                ]);

            var step = _factory(services, []);
            try
            {
                await step.Run(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                switch (step)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;

                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
        }
    }
}
