using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Cryptography;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Calls the XML processor for CCR (Customer Contact Register) data and transforms the dto's to
/// to models usable for the db layer.
/// </summary>
public sealed partial class CcrService
{
    private readonly IUnitOfWorkManager _uowManager;
    private readonly ICcrXmlProcessor _ccrXmlProcessor;
    private readonly ICcrUpdateFederator _updateFederator;
    private readonly IExternalRoleDefinitionPersistence _roleMapper;
    private readonly ILocationLookupProvider _locationLookupProvider;
    private readonly ILogger<CcrService> _logger;
    private readonly IOptionsMonitor<CcrServiceSettings> _options;
    private readonly TimeProvider _timeProvider;
    private readonly CcrServiceMeters _meters;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrService"/> class.
    /// </summary>
    public CcrService(
        IUnitOfWorkManager uowManager,
        ICcrXmlProcessor ccrXmlProcessor,
        ICcrUpdateFederator updateFederator,
        IExternalRoleDefinitionPersistence roleMapper,
        ILocationLookupProvider locationLookupProvider,
        ILogger<CcrService> logger,
        IOptionsMonitor<CcrServiceSettings> options,
        TimeProvider timeProvider,
        IMetricsProvider metricsProvider)
    {
        _uowManager = uowManager;
        _ccrXmlProcessor = ccrXmlProcessor;
        _updateFederator = updateFederator;
        _logger = logger;
        _roleMapper = roleMapper;
        _locationLookupProvider = locationLookupProvider;
        _options = options;
        _timeProvider = timeProvider;
        _meters = metricsProvider.Get<CcrServiceMeters>();
    }

    /// <summary>
    /// Processes a CCR XML batch and upserts all party updates into the database.
    /// </summary>
    /// <param name="commandId">Idempotency disambiguation id for queueing. Together with the Org.PartyUuid refers to a unique upsert. Is a db requirement! </param>
    /// <param name="input">The raw CCR/ER XML byte sequence, without Soap envelope. Normally only one Org is in each XML, but we support several.</param>
    /// <param name="federate">A boolean indicating whether to federate the update.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public async Task<CcrUpdateResult> UpdateFromCcr(
        Guid commandId,
        ReadOnlySequence<byte> input,
        bool federate,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var updatedOrgs = ImmutableArray.CreateBuilder<Guid>();
        var updatedRoles = ImmutableArray.CreateBuilder<ExternalRoleAssignmentEvent>();

        ILocationLookup locationLookup = await _locationLookupProvider.GetLocationLookup(cancellationToken);
        IExternalRoleDefinitionLookup roleMap = await _roleMapper.GetRoleDefinitionLookup(cancellationToken);
        var updates = _ccrXmlProcessor.ProcessCcrXml(input, roleMap, locationLookup, cancellationToken);
        var dbUpdates = MapToDbUpdates(updates, now).ToList();

        {
            await using var uowOrg = await _uowManager.CreateAsync(activityName: "update parties from ccr xml", cancellationToken: cancellationToken);
            var parties = uowOrg.GetRequiredService<IPartyPersistence>();
            for (int i = 0; i < dbUpdates.Count; i++)
            {
                var dbUpdate = dbUpdates[i];
                Debug.Assert(dbUpdate.Org.OrganizationIdentifier.HasValue);
                using var activity = RegisterTelemetry.StartActivity(
                    name: "upsert party",
                    tags: [new("org.id", dbUpdate.Org.OrganizationIdentifier.Value)]);

                var result = await parties.UpsertParty(dbUpdate.Org, cancellationToken);
                result.EnsureSuccess();

                dbUpdates[i] = dbUpdate with { ResolvedOrgPartyUuid = result.Value.PartyUuid.Value };
                updatedOrgs.Add(result.Value.PartyUuid.Value);
            }

            await uowOrg.CommitAsync(cancellationToken);
        }

        {
            await using var uowRoles = await _uowManager.CreateAsync(activityName: "update roles from ccr xml", cancellationToken: cancellationToken);
            var roles = uowRoles.GetRequiredService<IPartyExternalRolePersistence>();
            foreach (CcrDbUpdate dbUpdate in dbUpdates)
            {
                if (dbUpdate.RolesUpdate is null)
                {
                    continue;
                }

                Debug.Assert(dbUpdate.Org.OrganizationIdentifier.HasValue);
                Debug.Assert(dbUpdate.ResolvedOrgPartyUuid.HasValue);
                using var activity = RegisterTelemetry.StartActivity(
                    name: "upsert roles",
                    tags: [new("org.id", dbUpdate.Org.OrganizationIdentifier.Value)]);

                var events = roles.UpsertExternalRolesFromPartyBySource(
                    commandId: commandId,
                    partyUuid: dbUpdate.ResolvedOrgPartyUuid.Value,
                    roleSource: ExternalRoleSource.CentralCoordinatingRegister,
                    update: dbUpdate.RolesUpdate,
                    cancellationToken: cancellationToken);

                await foreach (var e in events)
                {
                    updatedRoles.Add(e);
                }
            }

            await uowRoles.CommitAsync(cancellationToken);
        }

        if (federate)
        {
            using var activity = RegisterTelemetry.StartActivity(name: "federate changes");

            // federate the updates to other systems after we've successfully updated our own database, but ignore any failures as this is just for test environments
            await _updateFederator.FederateUpdates(input, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _meters.PartyUpdates.Add(updatedOrgs.Count);
        _meters.RoleUpdates.Add(updatedRoles.Count);
        return new CcrUpdateResult
        {
            UpdatedOrganizationPartyUuids = updatedOrgs.ToImmutableValueArray(),
            RoleAssignmentEvents = updatedRoles.ToImmutableValueArray(),
        };
    }

    /// <summary>
    /// Authorizes a CCR client based on the provided credentials and source IP address.
    /// </summary>
    /// <param name="userName">The username of the CCR client.</param>
    /// <param name="password">The password of the CCR client.</param>
    /// <param name="sourceIp">The source IP address of the CCR client.</param>
    /// <param name="settings">The settings of the authorized CCR client, if authorization is successful.</param>
    /// <returns><see langword="true"/> if the client is authorized; otherwise, <see langword="false"/>.</returns>
    public bool AuthorizeCcrClient(
        string userName,
        string password,
        IPAddress sourceIp,
        [NotNullWhen(true)] out CcrClientIdentitySettings? settings)
    {
        if (!_options.CurrentValue.Clients.TryGetValue(userName, out settings))
        {
            Log.UnknownCcrClient(_logger, userName, sourceIp);
            return false;
        }

        if (settings.PasswordHash is null
            || !PasswordHash.Validate(userName, password, settings.PasswordHash))
        {
            Log.InvalidCcrClientCredentials(_logger, userName, sourceIp);
            return false;
        }

        if (!settings.AllowedSourceNetworks.Any(network => network.Contains(sourceIp)))
        {
            Log.InvalidCcrClientSourceIp(_logger, userName, sourceIp);
            return false;
        }

        return true;
    }

    private static IEnumerable<CcrDbUpdate> MapToDbUpdates(IEnumerable<CcrOrganizationUpdate> updates, DateTimeOffset now)
    {
        foreach (var update in updates)
        {
            CcrDbUpdate dbUpdate = new(MapOrganization(update, now), MapRolesUpdate(update), null);
            yield return dbUpdate;
        }
    }

    private static PartyExternalRoleAssignmentsUpdate? MapRolesUpdate(CcrOrganizationUpdate update)
    {
        if (update.RoleUpdates is null)
        {
            return null;
        }

        PartyExternalRoleAssignmentsUpdate result;

        if (update.IsFirstRegistration)
        {
            result = new PartyExternalRoleAssignmentsUpdate.Full
            {
                Assignments = update.RoleUpdates?.RoleAssignments.
                    Select(a => new PartyExternalRoleAssignment
                    {
                        ExternalRoleIdentifier = a.RoleCode,
                        ToParty = MapToPartyRef(a),
                    }).ToImmutableValueArray() ?? []
            };
        }
        else
        {
            result = new PartyExternalRoleAssignmentsUpdate.Patch
            {
                AbsentByIdentifier = update.RoleUpdates?.BulkRemoveRoleAssignments.
                    Select(b => b.RoleCode).ToImmutableValueArray() ?? [],

                Absent = update.RoleUpdates?.RemoveRoleAssignments.
                    Select(r => new PartyExternalRoleAssignment
                    {
                        ExternalRoleIdentifier = r.RoleCode,
                        ToParty = MapToPartyRef(r),
                    }).ToImmutableValueArray() ?? [],

                Present = update.RoleUpdates?.RoleAssignments.
                    Select(a => new PartyExternalRoleAssignment
                    {
                        ExternalRoleIdentifier = a.RoleCode,
                        ToParty = MapToPartyRef(a),
                    }).ToImmutableValueArray() ?? []
            };
        }

        return result;
    }

    private static PartyExternalRoleAssignmentPartyRef MapToPartyRef(CcrRoleAssignment assignment)
    {
        if (assignment.IsToOrganization)
        {
            return new PartyExternalRoleAssignmentPartyRef.Organization
            {
                OrganizationIdentifier = assignment.RoleOrganizationNumber,
            };
        }

        if (assignment.IsToPerson)
        {
            return new PartyExternalRoleAssignmentPartyRef.Person
            {
                PersonIdentifier = assignment.RolePersonalIdentifier,
                Name = assignment.PersonName,
                MailingAddress = assignment.MailingAddress,
            };
        }

        ThrowHelper.ThrowArgumentException(nameof(assignment), "Role assignment must be to either a person or an organization, but was neither.");
        return null!; // Unreachable, but required for compilation
    }

    private static OrganizationRecord MapOrganization(CcrOrganizationUpdate model, DateTimeOffset now)
    {
        return new OrganizationRecord
        {
            PartyUuid = FieldValue.Unset,
            OwnerUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            OrganizationIdentifier = model.OrganizationIdentifier,
            PersonIdentifier = FieldValue.Null,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(model.OrganizationIdentifier),
            UserIds = PartyHistoricalAggregate<uint>.Empty,
            Usernames = PartyHistoricalAggregate<string>.Empty,
            CreatedAt = now,
            ModifiedAt = now,
            VersionId = FieldValue.Unset,
            ParentOrganizationUuid = FieldValue.Unset,

            Source = OrganizationSource.CentralCoordinatingRegister,
            IsDeleted = model.IsDeleted,
            DeletedAt = FieldValue.From(model.DeletedAt).Select(ToNorwegianDateTimeOffset),
            DisplayName = model.DisplayName,
            UnitType = model.UnitType,
            UnitStatus = model.UnitStatus,
            TelephoneNumber = model.TelephoneNumber,
            MobileNumber = model.MobileNumber,
            FaxNumber = model.FaxNumber,
            EmailAddress = model.EmailAddress,
            InternetAddress = model.InternetAddress,
            MailingAddress = model.MailingAddress,
            BusinessAddress = model.BusinessAddress,
        };
    }

    private static readonly TimeZoneInfo _norwegianTimeZone
        = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private static DateTimeOffset ToNorwegianDateTimeOffset(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, _norwegianTimeZone.GetUtcOffset(local));
    }

    private record struct CcrDbUpdate(OrganizationRecord Org, PartyExternalRoleAssignmentsUpdate? RolesUpdate, Guid? ResolvedOrgPartyUuid);

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Warning, "Unknown CCR client with username '{UserName}' attempted to authenticate from IP address '{SourceIp}'.")]
        public static partial void UnknownCcrClient(ILogger logger, string userName, IPAddress sourceIp);

        [LoggerMessage(2, LogLevel.Warning, "CCR client with username '{UserName}' provided invalid credentials when attempting to authenticate from IP address '{SourceIp}'.")]
        public static partial void InvalidCcrClientCredentials(ILogger logger, string userName, IPAddress sourceIp);

        [LoggerMessage(3, LogLevel.Warning, "CCR client with username '{UserName}' attempted to authenticate from unauthorized IP address '{SourceIp}'.")]
        public static partial void InvalidCcrClientSourceIp(ILogger logger, string userName, IPAddress sourceIp);
    }

    private sealed class CcrServiceMeters(Meter meter)
        : IMetrics<CcrServiceMeters>
    {
        /// <summary>
        /// Gets a counter for the number of parties updated from CCR updates.
        /// </summary>
        public Counter<int> PartyUpdates { get; }
            = meter.CreateCounter<int>("altinn.register.ccr.party-updates", description: "The number of parties updated from CCR updates.");

        /// <summary>
        /// Gets a counter for the number of role assignments updated from CCR updates.
        /// </summary>
        public Counter<int> RoleUpdates { get; }
            = meter.CreateCounter<int>("altinn.register.ccr.role-updates", description: "The number of role assignments updated from CCR updates.");

        public static CcrServiceMeters Create(Meter meter)
            => new(meter);
    }
}
