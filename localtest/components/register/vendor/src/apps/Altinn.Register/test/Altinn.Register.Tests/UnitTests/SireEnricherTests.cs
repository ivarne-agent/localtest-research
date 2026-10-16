using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Sire;
using Altinn.Register.PartyImport;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.PartyImport.A2.Enrichers;
using Moq;

namespace Altinn.Register.Tests.UnitTests;

public class SireEnricherTests
{
    private static readonly OrganizationIdentifier TestOrgId
        = OrganizationIdentifier.Parse("090090003");

    private static CancellationToken CancellationToken
        => TestContext.Current.CancellationToken;

    private static OrganizationRecord MinimalSireOrg(OrganizationIdentifier orgId)
        => new()
        {
            OwnerUuid = Guid.NewGuid(),
            PartyUuid = Guid.NewGuid(),
            PartyId = FieldValue.Unset,
            ExternalUrn = FieldValue.Unset,
            DisplayName = FieldValue.Unset,
            OrganizationIdentifier = orgId,
            PersonIdentifier = FieldValue.Unset,
            CreatedAt = FieldValue.Unset,
            ModifiedAt = FieldValue.Unset,
            IsDeleted = false,
            DeletedAt = FieldValue.Unset,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            Source = OrganizationSource.RegisteredWithSkatteetaten,
            UnitType = FieldValue.Unset,
            UnitStatus = FieldValue.Unset,
            TelephoneNumber = FieldValue.Unset,
            MobileNumber = FieldValue.Unset,
            FaxNumber = FieldValue.Unset,
            EmailAddress = FieldValue.Unset,
            InternetAddress = FieldValue.Unset,
            MailingAddress = FieldValue.Unset,
            BusinessAddress = FieldValue.Unset,
        };

    [Fact]
    public async Task Run_MapsOrganizationFields()
    {
        var sireOrg = new SireOrganization
        {
            OrganizationIdentifier = TestOrgId,
            Name = "Test AS",
            UnitType = "AS",
            UnitStatus = null,
            DeletedAt = null,
            MailingAddress = new() { Address = "Testgata 1", PostalCode = "0001", City = "OSLO" },
            LastUpdated = DateTimeOffset.UtcNow,
            BusinessRelationships = [],
        };

        var sireClient = new Mock<ISireClient>();
        sireClient
            .Setup(c => c.GetOrganization(TestOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<SireOrganization>)sireOrg);

        var enricher = new SireEnricher(sireClient.Object);

        var party = MinimalSireOrg(TestOrgId);
        var context = new A2PartyImportSagaEnrichmentRunContext
        {
            PartyIdentifier = new ImportPartyIdentifier(TestOrgId),
            Party = party,
            RoleAssignments = [],
        };

        await enricher.Run(context, CancellationToken);

        var result = Assert.IsType<OrganizationRecord>(context.Party);
        Assert.Equal("Test AS", result.DisplayName.Value);
        Assert.Equal("AS", result.UnitType.Value);
        Assert.False(result.IsDeleted.Value);
        Assert.Equal("Testgata 1", result.MailingAddress.Value!.Address);
    }

    [Fact]
    public async Task Run_OrganizationNotFound_LeavesPartyUnchanged()
    {
        var sireClient = new Mock<ISireClient>();
        sireClient
            .Setup(c => c.GetOrganization(TestOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Problems.OrganizationNotFound.Create());

        var enricher = new SireEnricher(sireClient.Object);

        var party = MinimalSireOrg(TestOrgId);
        var context = new A2PartyImportSagaEnrichmentRunContext
        {
            PartyIdentifier = new ImportPartyIdentifier(TestOrgId),
            Party = party,
            RoleAssignments = [],
        };

        await enricher.Run(context, CancellationToken);

        // The party reference should be exactly the same instance we started with —
        // the enricher must not synthesise a deletion state from a 404.
        Assert.Same(party, context.Party);
    }

    [Fact]
    public async Task Run_RoleMapping_PersonIdentifier_MapsToAssignment()
    {
        var personId = PersonIdentifier.Parse("25871999336");

        var sireOrg = new SireOrganization
        {
            OrganizationIdentifier = TestOrgId,
            Name = "Test AS",
            UnitType = "AS",
            UnitStatus = null,
            DeletedAt = null,
            MailingAddress = null,
            LastUpdated = null,
            BusinessRelationships =
            [
                new SireBusinessRelationship
                {
                    RoleIdentifier = "styreleder",
                    RelatedPersonIdentifier = personId,
                    RelatedOrganizationIdentifier = null,
                }
            ],
        };

        var sireClient = new Mock<ISireClient>();
        sireClient
            .Setup(c => c.GetOrganization(TestOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<SireOrganization>)sireOrg);

        var enricher = new SireEnricher(sireClient.Object);

        var party = MinimalSireOrg(TestOrgId);
        var context = new A2PartyImportSagaEnrichmentRunContext
        {
            PartyIdentifier = new ImportPartyIdentifier(TestOrgId),
            Party = party,
            RoleAssignments = [],
        };

        await enricher.Run(context, CancellationToken);

        Assert.True(context.RoleAssignments.ContainsKey(ExternalRoleSource.CentralCoordinatingRegister));
        var update = Assert.IsType<PartyExternalRoleAssignmentsUpdate.Full>(
            context.RoleAssignments[ExternalRoleSource.CentralCoordinatingRegister]);
        Assert.Single(update.Assignments);
    }

    /// <summary>
    /// Covers the <c>RelatedOrganizationIdentifier</c> branch in MapRoleAssignments — when SIRE
    /// reports a relationship to another organization rather than a person.
    /// </summary>
    [Fact]
    public async Task Run_RoleMapping_OrganizationIdentifier_MapsToAssignment()
    {
        var relatedOrgId = OrganizationIdentifier.Parse("090090011");

        var sireOrg = new SireOrganization
        {
            OrganizationIdentifier = TestOrgId,
            Name = "Test AS",
            UnitType = "KS",
            UnitStatus = null,
            DeletedAt = null,
            MailingAddress = null,
            LastUpdated = null,
            BusinessRelationships =
            [
                new SireBusinessRelationship
                {
                    RoleIdentifier = "komplementar",
                    RelatedPersonIdentifier = null,
                    RelatedOrganizationIdentifier = relatedOrgId,
                }
            ],
        };

        var sireClient = new Mock<ISireClient>();
        sireClient
            .Setup(c => c.GetOrganization(TestOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<SireOrganization>)sireOrg);

        var enricher = new SireEnricher(sireClient.Object);

        var party = MinimalSireOrg(TestOrgId);
        var context = new A2PartyImportSagaEnrichmentRunContext
        {
            PartyIdentifier = new ImportPartyIdentifier(TestOrgId),
            Party = party,
            RoleAssignments = [],
        };

        await enricher.Run(context, CancellationToken);

        Assert.True(context.RoleAssignments.ContainsKey(ExternalRoleSource.CentralCoordinatingRegister));
        var update = Assert.IsType<PartyExternalRoleAssignmentsUpdate.Full>(
            context.RoleAssignments[ExternalRoleSource.CentralCoordinatingRegister]);
        var assignment = Assert.Single(update.Assignments);
        Assert.Equal("komplementar", assignment.ExternalRoleIdentifier);
        var orgRef = Assert.IsType<PartyExternalRoleAssignmentPartyRef.Organization>(assignment.ToParty);
        Assert.Equal(relatedOrgId, orgRef.OrganizationIdentifier);
    }

    /// <summary>
    /// OrganizationDocumentValidator rejects relationships that have neither a person nor an
    /// org identifier, so this state never reaches the enricher in production. If it ever
    /// does (bug or test plumbing bypassing the validator), the enricher must fail loudly
    /// rather than silently dropping data.
    /// </summary>
    [Fact]
    public async Task Run_RoleMapping_NoIdentifier_ThrowsInvariantViolation()
    {
        var sireOrg = new SireOrganization
        {
            OrganizationIdentifier = TestOrgId,
            Name = "Test AS",
            UnitType = "KS",
            UnitStatus = null,
            DeletedAt = null,
            MailingAddress = null,
            LastUpdated = null,
            BusinessRelationships =
            [
                new SireBusinessRelationship
                {
                    RoleIdentifier = "komplementar",
                    RelatedPersonIdentifier = null,
                    RelatedOrganizationIdentifier = null,
                }
            ],
        };

        var sireClient = new Mock<ISireClient>();
        sireClient
            .Setup(c => c.GetOrganization(TestOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<SireOrganization>)sireOrg);

        var enricher = new SireEnricher(sireClient.Object);

        var party = MinimalSireOrg(TestOrgId);
        var context = new A2PartyImportSagaEnrichmentRunContext
        {
            PartyIdentifier = new ImportPartyIdentifier(TestOrgId),
            Party = party,
            RoleAssignments = [],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => enricher.Run(context, CancellationToken));
    }
}
