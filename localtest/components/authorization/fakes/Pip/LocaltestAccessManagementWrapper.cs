#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.Api.Contracts.Authorization;
using Altinn.Authorization.Enums;
using Altinn.Platform.Authorization.Localtest.Register;
using Altinn.Platform.Authorization.Models.AccessManagement;
using Altinn.Platform.Authorization.Services.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>
/// Access Management PIP over the register seed: roles and access packages per (subject, party) pair, authorized
/// parties for the logged-in user, and no delegations.
/// </summary>
public sealed class LocaltestAccessManagementWrapper(
    RegisterLocaltestClient register,
    IHttpContextAccessor httpContextAccessor,
    ILogger<LocaltestAccessManagementWrapper> logger)
    : IAccessManagementWrapper
{
    public Task<IEnumerable<DelegationChangeDto>> GetAllDelegationChanges(DelegationChangeInputDto input, CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<DelegationChangeDto>());

    public Task<IEnumerable<DelegationChangeDto>> GetAllDelegationChanges(CancellationToken cancellationToken = default, params Action<DelegationChangeInputDto>[] actions)
        => Task.FromResult(Enumerable.Empty<DelegationChangeDto>());

    public async Task<PipResponseDto> GetRolesAndAccessPackages(Guid to, Guid from, AccessRestriction accessRestriction = AccessRestriction.None, string? viaPartyOrganizationNumber = null, CancellationToken cancellationToken = default)
    {
        var response = new PipResponseDto();

        // Client delegations do not exist locally.
        if (accessRestriction == AccessRestriction.ClientDelegation)
        {
            return response;
        }

        var snapshot = await register.Get(cancellationToken);
        if (snapshot.UserByPartyUuid(to) is not { } user)
        {
            return response;
        }

        var grants = LocaltestRoleMapping.For(snapshot, user, from);
        foreach (var urn in grants.RoleUrns())
        {
            if (RoleUrn.TryParse(urn, out var role))
            {
                response.Roles.Add(role);
            }
            else
            {
                logger.LogWarning("Localtest PIP: skipping role {Urn}, not a valid role urn", urn);
            }
        }

        foreach (var urn in grants.AccessPackageUrns())
        {
            if (AccessPackageUrn.TryParse(urn, out var package))
            {
                response.AccessPackages.Add(package);
            }
            else
            {
                logger.LogWarning("Localtest PIP: skipping access package {Urn}, not a valid access package urn", urn);
            }
        }

        return response;
    }

    public async Task<IEnumerable<AuthorizedPartyDto>> GetAuthorizedParties(CancellationToken cancellationToken = default)
    {
        var snapshot = await register.Get(cancellationToken);
        return CurrentUser(snapshot) is { } user
            ? LocaltestAuthorizedParties.For(snapshot, user).Select(ToHostModel).ToList()
            : [];
    }

    public async Task<AuthorizedPartyDto?> GetAuthorizedParty(int partyId, CancellationToken cancellationToken = default)
    {
        var parties = await GetAuthorizedParties(cancellationToken);
        return parties.Concat(parties.SelectMany(p => p.Subunits)).FirstOrDefault(p => p.PartyId == partyId);
    }

    private static AuthorizedPartyDto ToHostModel(Altinn.Authorization.Api.Contracts.AccessManagement.AuthorizedPartyDto party)
        => new()
        {
            PartyUuid = party.PartyUuid,
            PartyId = party.PartyId,
            Name = party.Name,
            OrganizationNumber = party.OrganizationNumber,
            PersonId = party.PersonId,
            Type = (AuthorizedPartyType)(int)party.Type,
            UnitType = party.UnitType,
            IsDeleted = party.IsDeleted,
            OnlyHierarchyElementWithNoAccess = party.OnlyHierarchyElementWithNoAccess,
            AuthorizedRoles = party.AuthorizedRoles,
            Subunits = party.Subunits.Select(ToHostModel).ToList(),
        };

    private RegisterLocaltestClient.TestUser? CurrentUser(RegisterLocaltestClient.Snapshot snapshot)
    {
        var claim = httpContextAccessor.HttpContext?.User.FindFirstValue("urn:altinn:userid");
        return int.TryParse(claim, out var userId) ? snapshot.UserById(userId) : null;
    }
}
