using System.Net;
using Altinn.Authorization.ProblemDetails;

namespace Altinn.Register.Core.Errors;

/// <summary>
/// Problem descriptors for Register.
/// </summary>
public static class Problems
{
    private static readonly ProblemDescriptorFactory _factory
        = ProblemDescriptorFactory.New("REG");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PartyConflict { get; }
        = _factory.Create(0, HttpStatusCode.Conflict, "Party could not be inserted due to one or more uniqueness constraint violation");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor InvalidPartyUpdate { get; }
        = _factory.Create(1, HttpStatusCode.BadRequest, "Invalid party update");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PartyGone { get; }
        = _factory.Create(2, HttpStatusCode.Gone, "Party is gone");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    /// <remarks>This is for when a party that is *referenced* is not found.</remarks>
    public static ProblemDescriptor ReferencedPartyNotFound { get; }
        = _factory.Create(3, HttpStatusCode.BadRequest, "Party not found");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PartyFetchFailed { get; }
        = _factory.Create(4, HttpStatusCode.InternalServerError, "Failed to fetch party");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PartyAuthorizeFailed { get; }
        = _factory.Create(5, HttpStatusCode.InternalServerError, "Failed to authorize party");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor OrganizationNotFound { get; }
        = _factory.Create(6, HttpStatusCode.NotFound, "Organization not found");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PartyNotFound { get; }
        = _factory.Create(7, HttpStatusCode.NotFound, "Party not found");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PartyLookupTooManyFailedAttempts { get; }
        = _factory.Create(8, HttpStatusCode.TooManyRequests, "Too many failed attempts to lookup person");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor PersonNotFound { get; }
        = _factory.Create(9, HttpStatusCode.NotFound, "Person not found");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor AccessManagementSelfIdentifiedUserPushFailed { get; }
        = _factory.Create(10, HttpStatusCode.InternalServerError, "Failed to push self-identified user to access management");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor UsernameInUse { get; }
        = _factory.Create(11, HttpStatusCode.Conflict, "The specified username is already in use by another party");

    /// <summary>Gets a <see cref="ProblemDescriptor"/>.</summary>
    public static ProblemDescriptor FeatureDisabled { get; }
        = _factory.Create(12, (HttpStatusCode)418 /* I'm a teapot */, "The requested operation is disabled");
}
