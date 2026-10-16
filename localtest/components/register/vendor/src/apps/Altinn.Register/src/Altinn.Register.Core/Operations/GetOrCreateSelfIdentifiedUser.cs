using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts;
using Altinn.Register.Core.A2.SblProfile;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Mediator;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Core.Utils;
using Altinn.Register.PartyImport.A2;
using Altinn.Urn;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.Core.Operations;

#pragma warning disable SA1649 // File name should match first type name

/// <summary>
/// Request to get an existing self-identified user, or create one if none exists.
/// </summary>
/// <remarks>
/// This is a union type representing either a <see cref="GetOrCreateSelfIdentifiedEmailUserRequest"/>,
/// or a <see cref="GetOrCreateSelfIdentifiedEduUserRequest"/>.
/// </remarks>
public readonly record struct GetOrCreateSelfIdentifiedUserRequest
    : IRequest<NewOrExisting<SelfIdentifiedUserRecord>>
{
    /// <summary>
    /// Creates a request to get or create a self-identified idporten-email user.
    /// </summary>
    /// <param name="email">The user email.</param>
    /// <returns>A request to get or create a self-identified idporten-email user.</returns>
    public static GetOrCreateSelfIdentifiedUserRequest Email(string email)
        => new(email: new GetOrCreateSelfIdentifiedEmailUserRequest(email));

    /// <summary>
    /// Creates a request to get or create a self-identified educational user.
    /// </summary>
    /// <param name="extRef">The external reference for the educational user.</param>
    /// <param name="username">The username for the educational user.</param>
    /// <returns>A request to get or create a self-identified educational user.</returns>
    public static GetOrCreateSelfIdentifiedUserRequest Educational(string extRef, string username)
        => new(eduUser: new GetOrCreateSelfIdentifiedEduUserRequest(extRef, username));

    private readonly SelfIdentifiedUserType _type;

    private readonly GetOrCreateSelfIdentifiedEmailUserRequest _emailUser;

    private readonly GetOrCreateSelfIdentifiedEduUserRequest _eduUser;

    /// <summary>
    /// Creates a new instance of <see cref="GetOrCreateSelfIdentifiedUserRequest"/> based on the provided email user request.
    /// </summary>
    /// <param name="email">The email user request.</param>
    internal GetOrCreateSelfIdentifiedUserRequest(GetOrCreateSelfIdentifiedEmailUserRequest email)
    {
        _type = SelfIdentifiedUserType.IdPortenEmail;
        _emailUser = email;
    }

    /// <summary>
    /// Creates a new instance of <see cref="GetOrCreateSelfIdentifiedUserRequest"/> based on the provided educational user request.
    /// </summary>
    /// <param name="eduUser">The educational user request.</param>
    internal GetOrCreateSelfIdentifiedUserRequest(GetOrCreateSelfIdentifiedEduUserRequest eduUser)
    {
        _type = SelfIdentifiedUserType.Educational;
        _eduUser = eduUser;
    }

    /// <summary>
    /// Implementation of the union type pattern.
    /// </summary>
    public object? Value => _type switch
    {
        SelfIdentifiedUserType.IdPortenEmail => _emailUser,
        SelfIdentifiedUserType.Educational => _eduUser,
        _ => null,
    };

    /// <summary>
    /// Indicates whether this request has a value (i.e. is a valid request).
    /// This will be <see langword="false"/> if the request was default-constructed or constructed with an unsupported type.
    /// </summary>
    public bool HasValue => _type switch
    {
        SelfIdentifiedUserType.IdPortenEmail => true,
        SelfIdentifiedUserType.Educational => true,
        _ => false,
    };

    /// <summary>
    /// Tries to get the email user request if this is an email user request.
    /// </summary>
    /// <param name="emailUser">The email user request if this is an email user request.</param>
    /// <returns><see langword="true"/> if this is an email user request, <see langword="false"/> otherwise.</returns>
    public bool TryGetValue(out GetOrCreateSelfIdentifiedEmailUserRequest emailUser)
    {
        if (_type == SelfIdentifiedUserType.IdPortenEmail)
        {
            emailUser = _emailUser;
            return true;
        }

        emailUser = default;
        return false;
    }

    /// <summary>
    /// Tries to get the educational user request if this is an educational user request.
    /// </summary>
    /// <param name="eduUser">The educational user request if this is an educational user request.</param>
    /// <returns><see langword="true"/> if this is an educational user request, <see langword="false"/> otherwise.</returns>
    public bool TryGetValue(out GetOrCreateSelfIdentifiedEduUserRequest eduUser)
    {
        if (_type == SelfIdentifiedUserType.Educational)
        {
            eduUser = _eduUser;
            return true;
        }

        eduUser = default;
        return false;
    }
}

/// <summary>
/// Request to get or create a self-identified user with an email (IdPorten) identity. The email is used as the unique identifier for the user.
/// </summary>
/// <param name="Email">The email of the user.</param>
public readonly record struct GetOrCreateSelfIdentifiedEmailUserRequest(string Email)
    : IRequest<NewOrExisting<SelfIdentifiedUserRecord>>;

/// <summary>
/// Request to get or create a self-identified educational user. The external reference is used as the unique identifier for the user.
/// </summary>
/// <param name="ExtRef">The external reference for the educational user.</param>
/// <param name="UserName">The username for the educational user (only used for creation if the user does not already exist).</param>
public readonly record struct GetOrCreateSelfIdentifiedEduUserRequest(string ExtRef, string UserName)
    : IRequest<NewOrExisting<SelfIdentifiedUserRecord>>;

/// <summary>
/// Get-or-create self-identified user via SBL Bridge proxy (iteration 1).
/// </summary>
/// <remarks>
/// Two-call flow: lookup by external identity first, create if not found. The bridge owns
/// UserId / PartyId / PartyUuid allocation during the proxy phase.
/// </remarks>
internal sealed partial class GetOrCreateSelfIdentifiedUserFromBridgeHandler(
    ISblProfileBridgeClient bridgeClient,
    ICommandSender commandSender,
    TimeProvider timeProvider,
    ILogger<GetOrCreateSelfIdentifiedUserFromBridgeHandler> logger)
    : IRequestHandler<GetOrCreateSelfIdentifiedUserRequest, NewOrExisting<SelfIdentifiedUserRecord>>
    , IRequestHandler<GetOrCreateSelfIdentifiedEmailUserRequest, NewOrExisting<SelfIdentifiedUserRecord>>
    , IRequestHandler<GetOrCreateSelfIdentifiedEduUserRequest, NewOrExisting<SelfIdentifiedUserRecord>>
{
    private const int SbiUserTypeSelfIdentified = 2;

    /// <inheritdoc/>
    public async ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>> Handle(
        GetOrCreateSelfIdentifiedUserRequest request,
        CancellationToken cancellationToken)
    {
        string externalIdentity, userName;
        string? email;

        if (request.TryGetValue(out GetOrCreateSelfIdentifiedEmailUserRequest emailRequest))
        {
            externalIdentity = PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(emailRequest.Email)).ToString();
            userName = $"email:{emailRequest.Email}";
            email = emailRequest.Email.ToLowerInvariant();
        }
        else if (request.TryGetValue(out GetOrCreateSelfIdentifiedEduUserRequest eduRequest))
        {
            externalIdentity = eduRequest.ExtRef;
            userName = eduRequest.UserName;
            email = null;
        }
        else
        {
            return ThrowHelper.ThrowArgumentException<NewOrExisting<SelfIdentifiedUserRecord>>(
                "Request contained no value");
        }

        var lookupResult = await bridgeClient.LookupUser(externalIdentity, cancellationToken);
        if (lookupResult.IsProblem)
        {
            return lookupResult.Problem;
        }

        if (lookupResult.Value.Found)
        {
            return MapToRecord(lookupResult.Value.Profile, externalIdentity, email)
                .Select(NewOrExisting.Existing);
        }

        var createRequest = new SblUserProfile
        {
            ExternalIdentity = externalIdentity,
            UserName = userName,
            UserType = SbiUserTypeSelfIdentified,
        };

        var createResult = await bridgeClient.CreateUser(createRequest, cancellationToken);
        if (createResult.IsProblem)
        {
            return createResult.Problem;
        }

        var mapped = MapToRecord(createResult.Value, externalIdentity, email);
        if (mapped.IsProblem)
        {
            return mapped.Problem;
        }

        await EnqueueLocalImport(mapped.Value.PartyUuid.Value, cancellationToken);
        return mapped.Select(NewOrExisting.New);
    }

    /// <inheritdoc/>
    public ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>> Handle(
        GetOrCreateSelfIdentifiedEmailUserRequest request,
        CancellationToken cancellationToken)
        => Handle(new GetOrCreateSelfIdentifiedUserRequest(request), cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>> Handle(
        GetOrCreateSelfIdentifiedEduUserRequest request,
        CancellationToken cancellationToken)
        => Handle(new GetOrCreateSelfIdentifiedUserRequest(request), cancellationToken);

    // The polling A2PartyImportJob will eventually pick up this party as a change
    // from SBL, but we enqueue the same command eagerly so authentication can read
    // the party from local register on the immediate next request. ChangeId is 0 and
    // Tracking is left default — the polling job will track ProcessedMax when it
    // re-processes the same change.
    private async ValueTask EnqueueLocalImport(Guid partyUuid, CancellationToken cancellationToken)
    {
        try
        {
            await commandSender.Send(
                new ImportA2PartyCommand
                {
                    PartyUuid = partyUuid,
                    ChangeId = 0,
                    ChangedTime = timeProvider.GetUtcNow(),
                    Tracking = default,
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't fail the create on a bus hiccup — polling job will reconcile.
            Log.EnqueueImportFailed(logger, ex, partyUuid);
        }
    }

    private Result<SelfIdentifiedUserRecord> MapToRecord(
        SblUserProfile profile,
        string externalIdentity,
        string? email)
    {
        if (profile.UserUuid is null || profile.UserId <= 0 || profile.PartyId <= 0 || string.IsNullOrEmpty(profile.UserName))
        {
            Log.IncompleteBridgeResponse(logger);
            return Problems.PartyFetchFailed.Create([
                new("reason", "bridge response missing required identifiers"),
            ]);
        }

        FieldValue<PartyExternalRefUrn> externalUrn = email switch
        {
            null => FieldValue.Null,
            _ => PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(email)),
        };

        FieldValue<string> extRef = email switch
        {
            null => FieldValue.Null,
            _ => externalIdentity,
        };

        var userId = checked((uint)profile.UserId);

        var displayName = email switch
        {
            null => profile.UserName,
            _ => email,
        };

        var type = email switch
        {
            null => SelfIdentifiedUserType.Educational,
            _ => SelfIdentifiedUserType.IdPortenEmail,
        };

        return new SelfIdentifiedUserRecord
        {
            // Iteration-1 (bridge proxy) does not have a real version id; the polling
            // A2PartyImportJob will write a real version when it imports this party.
            PartyUuid = profile.UserUuid.Value,
            VersionId = 0UL,
            PartyId = checked((uint)profile.PartyId),
            OwnerUuid = FieldValue.Null,
            ExternalUrn = externalUrn,
            DisplayName = displayName,
            PersonIdentifier = FieldValue.Null,
            OrganizationIdentifier = FieldValue.Null,
            CreatedAt = FieldValue.Unset, // will be imported later
            ModifiedAt = FieldValue.Unset, // will be imported later
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(userId),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent(profile.UserName),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            SelfIdentifiedUserType = type,
            Email = email,
            ExtRef = extRef,
        };
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "SBL Bridge response missing one or more required identifiers (UserUuid, UserId, PartyId, UserName)")]
        public static partial void IncompleteBridgeResponse(ILogger logger);

        [LoggerMessage(1, LogLevel.Warning, "Failed to enqueue local profile import for newly created self-identified user {PartyUuid}.")]
        public static partial void EnqueueImportFailed(ILogger logger, Exception exception, Guid partyUuid);
    }
}

/// <summary>
/// Direct-DB implementation of get-or-create self-identified user (iteration 2).
/// </summary>
/// <remarks>
/// Stubbed for now; switching to this handler is the iteration-2 cutover. See issue #863.
/// </remarks>
internal sealed class GetOrCreateSelfIdentifiedUserFromDBHandler(IUnitOfWorkManager manager)
    : IRequestHandler<GetOrCreateSelfIdentifiedUserRequest, NewOrExisting<SelfIdentifiedUserRecord>>
    , IRequestHandler<GetOrCreateSelfIdentifiedEmailUserRequest, NewOrExisting<SelfIdentifiedUserRecord>>
    , IRequestHandler<GetOrCreateSelfIdentifiedEduUserRequest, NewOrExisting<SelfIdentifiedUserRecord>>
{
    /// <inheritdoc/>
    public ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>> Handle(
        GetOrCreateSelfIdentifiedUserRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TryGetValue(out GetOrCreateSelfIdentifiedEmailUserRequest emailRequest))
        {
            return Handle(emailRequest, cancellationToken);
        }
        else if (request.TryGetValue(out GetOrCreateSelfIdentifiedEduUserRequest eduRequest))
        {
            return Handle(eduRequest, cancellationToken);
        }
        else
        {
            return ThrowHelper.ThrowArgumentException<ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>>>(
                "Request contained no value");
        }
    }

    /// <inheritdoc/>
    public async ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>> Handle(
        GetOrCreateSelfIdentifiedEmailUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(
            activityName: "get or create email user",
            cancellationToken: cancellationToken);

        var parties = uow.GetPartyPersistence();
        var result = await parties.GetOrCreateSelfIdentifiedEmailUser(request.Email.ToLowerInvariant(), cancellationToken);
        if (result.IsProblem)
        {
            return result.Problem;
        }

        await uow.CommitAsync(cancellationToken);
        return result.Value;
    }

    /// <inheritdoc/>
    public async ValueTask<Result<NewOrExisting<SelfIdentifiedUserRecord>>> Handle(
        GetOrCreateSelfIdentifiedEduUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var uow = await manager.CreateAsync(
            activityName: "get or create edu user",
            cancellationToken: cancellationToken);

        var parties = uow.GetPartyPersistence();
        var result = await parties.GetOrCreateSelfIdentifiedEduUser(
            request.ExtRef,
            request.UserName,
            cancellationToken);

        if (result.IsProblem)
        {
            return result.Problem;
        }

        await uow.CommitAsync(cancellationToken);
        return result.Value;
    }
}
