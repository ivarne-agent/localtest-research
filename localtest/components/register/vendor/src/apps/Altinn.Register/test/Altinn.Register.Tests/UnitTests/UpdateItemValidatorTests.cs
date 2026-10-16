using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Sire;
using Altinn.Register.Integrations.Sire.Feed;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Unit tests for <see cref="UpdateItemValidator"/>.
/// </summary>
public class UpdateItemValidatorTests
{
    private const string ValidIdentifier = "090090003";

    private static readonly DateTimeOffset ValidRegisteredAt
        = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static UpdateItem? ValidItem(
        uint sequenceNumber = 1,
        string? identifier = ValidIdentifier,
        DateTimeOffset? registeredAt = null,
        NonExhaustiveEnum<SireUpdateType>? updateType = null)
        => new()
        {
            SequenceNumber = sequenceNumber,
            Identifier = identifier,
            RegisteredAt = registeredAt ?? ValidRegisteredAt,
            UpdateType = updateType ?? SireUpdateType.New,
        };

    /// <summary>
    /// A complete, well-formed UpdateItem parses into a SireUpdate whose fields mirror
    /// the wire shape — sekvensnummer, identifikator, registreringstidspunkt, hendelsetype.
    /// </summary>
    [Fact]
    public void TryValidate_ValidInput_ParsesToSireUpdate()
    {
        ValidationProblemBuilder builder = default;

        var ok = builder.TryValidate(
            path: "/",
            ValidItem(sequenceNumber: 42, identifier: ValidIdentifier, updateType: SireUpdateType.Changed),
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Equal(42u, result.SequenceNumber);
        Assert.Equal(ValidIdentifier, result.OrganizationIdentifier.ToString());
        Assert.Equal(ValidRegisteredAt, result.RegisteredAt);
        Assert.Equal(SireUpdateType.Changed, result.UpdateType.Value);
        Assert.False(builder.TryBuild(out _));
    }

    /// <summary>
    /// A <see langword="null"/> UpdateItem (e.g. a malformed array element) is rejected
    /// with a top-level Required error and produces no SireUpdate.
    /// </summary>
    [Fact]
    public void TryValidate_NullInput_AddsRequiredError()
    {
        ValidationProblemBuilder builder = default;

        var ok = builder.TryValidate(
            path: "/",
            (UpdateItem?)null,
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Missing / empty / whitespace identifikator is rejected as Required at
    /// <c>/identifikator</c>.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_MissingIdentifier_AddsRequiredError(string? identifier)
    {
        ValidationProblemBuilder builder = default;

        var ok = builder.TryValidate(
            path: "/",
            ValidItem(identifier: identifier),
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// A non-9-digit identifikator is rejected via the shared
    /// <c>OrganizationIdentifierValidator</c>, which emits InvalidOrganizationNumber.
    /// </summary>
    [Theory]
    [InlineData("12345")] // too short
    [InlineData("1234567890")] // too long
    [InlineData("abcdefghi")] // non-numeric
    [InlineData("090090003 ")] // trailing whitespace
    public void TryValidate_InvalidIdentifier_AddsValidationError(string identifier)
    {
        ValidationProblemBuilder builder = default;

        var ok = builder.TryValidate(
            path: "/",
            ValidItem(identifier: identifier),
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Missing registreringstidspunkt → Required at <c>/registreringstidspunkt</c>.
    /// </summary>
    [Fact]
    public void TryValidate_MissingRegisteredAt_AddsRequiredError()
    {
        ValidationProblemBuilder builder = default;

        UpdateItem? input = new UpdateItem
        {
            SequenceNumber = 1,
            Identifier = ValidIdentifier,
            RegisteredAt = null,
            UpdateType = SireUpdateType.New,
        };

        var ok = builder.TryValidate(
            path: "/",
            (UpdateItem?)input,
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Missing hendelsetype → Required at <c>/hendelsetype</c>.
    /// </summary>
    [Fact]
    public void TryValidate_MissingUpdateType_AddsRequiredError()
    {
        ValidationProblemBuilder builder = default;

        UpdateItem? input = new UpdateItem
        {
            SequenceNumber = 1,
            Identifier = ValidIdentifier,
            RegisteredAt = ValidRegisteredAt,
            UpdateType = null,
        };

        var ok = builder.TryValidate(
            path: "/",
            (UpdateItem?)input,
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Multiple field problems aggregate into a single failed validation result, so a
    /// single bad item surfaces every issue at once.
    /// </summary>
    [Fact]
    public void TryValidate_MultipleFieldErrors_Aggregate()
    {
        ValidationProblemBuilder builder = default;

        UpdateItem? input = new UpdateItem
        {
            SequenceNumber = 1,
            Identifier = null,
            RegisteredAt = null,
            UpdateType = null,
        };

        var ok = builder.TryValidate(
            path: "/",
            (UpdateItem?)input,
            default(UpdateItemValidator),
            out SireUpdate? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }
}
