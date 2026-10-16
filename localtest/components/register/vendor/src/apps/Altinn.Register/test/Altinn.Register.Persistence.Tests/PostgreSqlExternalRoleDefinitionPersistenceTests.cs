using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.TestUtils;

namespace Altinn.Register.Persistence.Tests;

public class PostgreSqlExternalRoleDefinitionPersistenceTests(ITestOutputHelper output)
    : DatabaseTestBase
{
    protected override bool SeedData => false;

    protected override ITestOutputHelper? TestOutputHelper => output;

    private PostgreSqlExternalRoleDefinitionPersistence? _persistence;

    private PostgreSqlExternalRoleDefinitionPersistence Persistence => _persistence!;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _persistence = GetRequiredService<PostgreSqlExternalRoleDefinitionPersistence>();
    }

    [Theory]
    [InlineData(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet")]
    [InlineData(ExternalRoleSource.CentralCoordinatingRegister, "ikke-naeringsdrivende-hovedenhet")]
    public async Task GetsRoleDefinition(ExternalRoleSource source, string identifier)
    {
        // cold
        var def = await Persistence.TryGetRoleDefinition(source, identifier, CancellationToken);
        Assert.NotNull(def);
        Assert.Equal(source, def.Source);
        Assert.Equal(identifier, def.Identifier);

        // from cache
        def = await Persistence.TryGetRoleDefinition(source, identifier, CancellationToken);
        Assert.NotNull(def);
        Assert.Equal(source, def.Source);
        Assert.Equal(identifier, def.Identifier);
    }

    [Theory]
    [InlineData("BEDR", ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet")]
    [InlineData("AAFY", ExternalRoleSource.CentralCoordinatingRegister, "ikke-naeringsdrivende-hovedenhet")]
    public async Task GetsRoleDefinition_ByRoleCode(string roleCode, ExternalRoleSource source, string identifier)
    {
        // cold
        var def = await Persistence.TryGetRoleDefinitionByRoleCode(roleCode, CancellationToken);
        Assert.NotNull(def);
        Assert.Equal(source, def.Source);
        Assert.Equal(identifier, def.Identifier);

        // from cache
        def = await Persistence.TryGetRoleDefinitionByRoleCode(roleCode, CancellationToken);
        Assert.NotNull(def);
        Assert.Equal(source, def.Source);
        Assert.Equal(identifier, def.Identifier);
    }

    [Fact]
    public async Task HandlesThreadSpam()
    {
        var evt = new ManualResetEventSlim(initialState: false);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ =>
            {
                var tcs = new TaskCompletionSource();
                var thread = new Thread(() =>
                {
                    try
                    {
                        evt.Wait();
                        var vtask = Persistence.TryGetRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet");
                        CompleteVTask(tcs, vtask);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });

                thread.Start();
                return tcs.Task;
            })
            .ToArray();

        evt.Set();
        await Task.WhenAll(tasks);

        static void CompleteVTask(TaskCompletionSource tcs, ValueTask<ExternalRoleDefinition?> vtask)
        {
            if (vtask.IsCompleted)
            {
                CompleteCompletedVTask(tcs, vtask);
                return;
            }

            var awaiter = vtask.GetAwaiter();
            awaiter.UnsafeOnCompleted(() =>
            {
                CompleteVTask(tcs, vtask);
            });
        }

        static void CompleteCompletedVTask(TaskCompletionSource tsc, ValueTask<ExternalRoleDefinition?> vtask)
        {
            try
            {
                var result = vtask.GetAwaiter().GetResult();
                Assert.NotNull(result);
                Assert.Equal(ExternalRoleSource.CentralCoordinatingRegister, result.Source);
                Assert.Equal("hovedenhet", result.Identifier);
            }
            catch (Exception ex)
            {
                tsc.TrySetException(ex);
            }
            finally
            {
                tsc.TrySetResult();
            }
        }
    }
}
