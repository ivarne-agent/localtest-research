using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using CommunityToolkit.Diagnostics;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to lookup a v1 party based on a pre-parsed identifier.
/// </summary>
public readonly record struct LookupV1PartyRequest
    : IRequest<V1Models.Party>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LookupV1PartyRequest"/> struct for organization lookup.
    /// </summary>
    /// <param name="value">The organization identifier.</param>
    public LookupV1PartyRequest(OrganizationIdentifier value)
    {
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LookupV1PartyRequest"/> struct for person lookup.
    /// </summary>
    /// <param name="value">The person identifier.</param>
    public LookupV1PartyRequest(PersonIdentifier value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the request value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets a value indicating whether this request has a value.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue => Value is not null;

    /// <summary>
    /// Tries to get the organization identifier value.
    /// </summary>
    /// <param name="value">The organization identifier.</param>
    /// <returns><see langword="true"/> if this request contains an organization identifier.</returns>
    public bool TryGetValue(out OrganizationIdentifier value)
    {
        if (Value is OrganizationIdentifier organizationIdentifier)
        {
            value = organizationIdentifier;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Tries to get the person identifier value.
    /// </summary>
    /// <param name="value">The person identifier.</param>
    /// <returns><see langword="true"/> if this request contains a person identifier.</returns>
    public bool TryGetValue(out PersonIdentifier value)
    {
        if (Value is PersonIdentifier personIdentifier)
        {
            value = personIdentifier;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Converts this request to v1 lookup criteria.
    /// </summary>
    /// <returns>The v1 lookup criteria.</returns>
    public V1Models.PartyLookup ToPartyLookup()
    {
        if (TryGetValue(out PersonIdentifier personIdentifier))
        {
            return new V1Models.PartyLookup
            {
                Ssn = personIdentifier.ToString(),
            };
        }

        if (TryGetValue(out OrganizationIdentifier organizationIdentifier))
        {
            return new V1Models.PartyLookup
            {
                OrgNo = organizationIdentifier.ToString(),
            };
        }

        return ThrowHelper.ThrowArgumentException<V1Models.PartyLookup>(nameof(Value), "Request must contain either an organization identifier or a person identifier.");
    }
}

/// <summary>
/// Lookup v1 party information from A2.
/// </summary>
internal sealed class LookupV1PartyFromA2RequestHandler(IV1PartyService partyService)
    : IRequestHandler<LookupV1PartyRequest, V1Models.Party>
{
    /// <inheritdoc/>
    public async ValueTask<Result<V1Models.Party>> Handle(LookupV1PartyRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasValue)
        {
            return ThrowHelper.ThrowArgumentException<V1Models.Party>(nameof(request), "Request must contain either an organization identifier or a person identifier.");
        }

        var lookupValue = request.Value.ToString();
        Debug.Assert(!string.IsNullOrEmpty(lookupValue));
        var party = await partyService.LookupPartyBySSNOrOrgNo(lookupValue, cancellationToken);
        if (party is null)
        {
            if (request.TryGetValue(out OrganizationIdentifier organizationIdentifier))
            {
                return Problems.PartyNotFound.Create([new("organizationIdentifier", organizationIdentifier.ToString())]);
            }

            if (request.TryGetValue(out PersonIdentifier personIdentifier))
            {
                return Problems.PartyNotFound.Create([new("personIdentifier", personIdentifier.ToString())]);
            }

            return Problems.PartyNotFound.Create([new("identifier", lookupValue)]);
        }

        return party;
    }
}

/// <summary>
/// Lookup v1 party information from the local A3 database.
/// </summary>
internal sealed class LookupV1PartyFromDBRequestHandler(IUnitOfWorkManager manager)
    : IRequestHandler<LookupV1PartyRequest, V1Models.Party>
{
    /// <inheritdoc/>
    public async ValueTask<Result<V1Models.Party>> Handle(LookupV1PartyRequest request, CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(cancellationToken, activityName: "lookup v1 party");
        var persistence = uow.GetPartyPersistence();

        if (request.TryGetValue(out OrganizationIdentifier organizationIdentifier))
        {
            var org = await persistence
                .GetOrganizationByIdentifier(organizationIdentifier, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
                .FirstOrDefaultAsync(cancellationToken);

            if (org is null)
            {
                return Problems.PartyNotFound.Create([new("organizationIdentifier", organizationIdentifier.ToString())]);
            }

            return V1PartyMapper.ToV1Party(org);
        }

        if (request.TryGetValue(out PersonIdentifier personIdentifier))
        {
            var person = await persistence
                .GetPersonByIdentifier(personIdentifier, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken)
                .FirstOrDefaultAsync(cancellationToken);

            if (person is null)
            {
                return Problems.PartyNotFound.Create([new("personIdentifier", personIdentifier.ToString())]);
            }

            return V1PartyMapper.ToV1Party(person);
        }

        return ThrowHelper.ThrowArgumentException<V1Models.Party>(nameof(request), "Request must contain either an organization identifier or a person identifier.");
    }
}
