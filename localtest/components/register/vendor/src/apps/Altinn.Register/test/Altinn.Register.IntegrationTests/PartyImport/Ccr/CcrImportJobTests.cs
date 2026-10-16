using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.PartyImport.Ccr;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.MassTransit;
using Altinn.Register.TestUtils.Sftp;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;

namespace Altinn.Register.IntegrationTests.PartyImport.Ccr;

/// <summary>
/// End-to-end test for <see cref="CcrImportJob"/>: configured through the production
/// <c>Altinn:register:PartyImport:Ccr:Sftp</c> settings, it should fetch a CCR flat file from a
/// real SFTP server (running in a test-container) and run to completion.
/// </summary>
public class CcrImportJobTests
    : IntegrationTestBase
{
    private static readonly TestDataFileProvider _ccrFiles = TestDataFileProvider.For("Ccr/FlatFile");

    private SftpServerInfo? _server;

    protected override async ValueTask InitializeAsync()
    {
        // Allocate a unique SFTP upload directory and stage the CCR file before the host is built,
        // so ConfigureConfiguration can point the SFTP settings at this test's isolated directory.
        var sftp = await TestContext.Current.GetRequiredFixture<SftpServerFixture>();
        _server = await sftp.CreateTestServer();

        using (var stream = _ccrFiles.GetFileInfo("baj00001.txt").CreateReadStream())
        {
            await _server.UploadFileAsync("baj00001.txt", stream, CancellationToken);
        }

        await base.InitializeAsync();
    }

    protected override void ConfigureConfiguration(IConfigurationBuilder configuration)
    {
        // Point the production-registered SftpClientSettings at this test's SFTP container dir.
        _server!.Configure(configuration, "Altinn:register:PartyImport:Ccr:Sftp");
    }

    [Fact]
    public async Task RunAsync_PersistsExactlyOneDaglRoleAssignmentPerOrganization()
    {
        var job = GetRequiredService<CcrImportJob>();

        var tracker = GetRequiredService<IImportJobTracker>();

        var before = await tracker.GetStatus(CcrImportJob.JobName, CancellationToken);
        before.EnqueuedMax.ShouldBe(0UL);

        await ((IJob<Unit>)job).RunAsync(CancellationToken);

        // The tracker only advances after the file is fetched and the parser has actually
        // produced (and the job has enqueued) at least one organization update. A silent
        // early-exit path (no file / parse incomplete) would leave EnqueuedMax at 0.
        var after = await tracker.GetStatus(CcrImportJob.JobName, CancellationToken);
        after.EnqueuedMax.ShouldBe(1UL);

        // Every ENH record in test1.txt has exactly one DAGL ("daglig-leder") sub-record.
        // After the consumer commits, each org should hold exactly one CCR-source
        // "daglig-leder" assignment in the DB.
        string[] orgIdentifiers = [
            "210690182", "213167022", "310146420", "311196073", "311258524",
            "311952153", "312105802", "312955385", "313316661", "313351033",
            "313704688", "313887162", "313993108", "315268729", "315676002",
        ];

        foreach (var orgNumber in orgIdentifiers)
        {
            var conversation = await TestHarness.Conversation<ImportCcrXmlCommand>(cmd => cmd.OrganizationIdentifier == OrganizationIdentifier.Parse(orgNumber), CancellationToken);
            conversation.ShouldNotBeNull(customMessage: $"there should be a conversation for organization {orgNumber}");

            var completedEvent = await conversation.Events.OfType<CcrXmlImportCompletedEvent>().FirstOrDefaultAsync(CancellationToken);
            var command = await conversation.Commands.OfType<ImportCcrXmlCommand>().FirstOrDefaultAsync(CancellationToken);
            command.ShouldNotBeNull();
            command.BatchId.ShouldBe(1U);

            var orgUpdatedEvent = await conversation.Events.Completed.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(CancellationToken);
            orgUpdatedEvent.ShouldNotBeNull();

            var roleAddedEvents = await conversation.Events.Completed.OfType<ExternalRoleAssignmentAddedEvent>().ToListAsync(CancellationToken);
            roleAddedEvents.Count.ShouldBe(1);

            var roleRemovedEvents = await conversation.Events.Completed.OfType<ExternalRoleAssignmentRemovedEvent>().ToListAsync(CancellationToken);
            roleRemovedEvents.Count.ShouldBe(0);
        }

        await Check(async (uow, ct) =>
        {
            var parties = uow.GetPartyPersistence();
            var roles = uow.GetPartyExternalRolePersistence();

            foreach (var orgNumber in orgIdentifiers)
            {
                var orgIdentifier = OrganizationIdentifier.Parse(orgNumber);

                var org = await parties
                    .GetOrganizationByIdentifier(orgIdentifier, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, ct)
                    .FirstOrDefaultAsync(ct);

                org.ShouldNotBeNull($"organization {orgNumber} should be persisted");

                var assignments = await roles
                    .GetExternalRoleAssignmentsFromParty(partyUuid: org.PartyUuid.Value, cancellationToken: ct)
                    .ToListAsync(ct);

                var daglCount = assignments.Count(a => a.Identifier == "daglig-leder");
                daglCount.ShouldBe(1, customMessage: $"organization {orgNumber} should have exactly one daglig-leder role assignment, but had {daglCount}");
            }
        });
    }
}
