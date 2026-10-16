using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Register.Persistence.Tests;

public class PostgreSqlFunctionsTests(ITestOutputHelper output)
    : DatabaseTestBase
{
    private const string SequenceName = "register.user_id_seq";

    protected override ITestOutputHelper? TestOutputHelper => output;

    private IUnitOfWorkManager UnitOfWorkManager => GetRequiredService<IUnitOfWorkManager>();

    [Fact]
    public async Task Function_tx_max_safeval_Returns_Max_When_No_Concurrent_Transactions()
    {
        var value = await TxMaxSafeval();
        value.ShouldBe(long.MaxValue);
    }

    [Fact]
    public async Task Function_tx_nextval_Returns_New_SequenceNumber()
    {
        List<long> values = new();

        {
            await using var uow1 = await UnitOfWorkManager.CreateAsync(CancellationToken);
            await using var uow2 = await UnitOfWorkManager.CreateAsync(CancellationToken);
            await using var uow3 = await UnitOfWorkManager.CreateAsync(CancellationToken);
            await using var uow4 = await UnitOfWorkManager.CreateAsync(CancellationToken);
            await using var uow5 = await UnitOfWorkManager.CreateAsync(CancellationToken);

            values.AddRange(await Task.WhenAll([
                TxNextval(uow1),
                TxNextval(uow2),
                TxNextval(uow3),
                TxNextval(uow4),
                TxNextval(uow5),
            ]));

            values.AddRange(await Task.WhenAll([
                TxNextval(uow1),
                TxNextval(uow2),
                TxNextval(uow3),
                TxNextval(uow4),
                TxNextval(uow5),
            ]));

            values.AddRange(await Task.WhenAll([
                TxNextval(uow1),
                TxNextval(uow2),
                TxNextval(uow3),
                TxNextval(uow4),
                TxNextval(uow5),
            ]));
        }

        values.Distinct().Count().ShouldBe(values.Count);
    }

    [Fact]
    public async Task Function_tx_max_safeval_Returns_Number_Before_Concurrent_Transactions()
    {
        await using var uow1 = await UnitOfWorkManager.CreateAsync(CancellationToken);
        await using var uow2 = await UnitOfWorkManager.CreateAsync(CancellationToken);
        await using var uow3 = await UnitOfWorkManager.CreateAsync(CancellationToken);
        await using var uow4 = await UnitOfWorkManager.CreateAsync(CancellationToken);
        await using var uow5 = await UnitOfWorkManager.CreateAsync(CancellationToken);

        var nv1 = await TxNextval(uow1);
        var nv2 = await TxNextval(uow2);
        var nv3 = await TxNextval(uow3);
        var nv4 = await TxNextval(uow4);
        var nv5 = await TxNextval(uow5);

        nv1.ShouldBeLessThan(nv2);
        nv2.ShouldBeLessThan(nv3);
        nv3.ShouldBeLessThan(nv4);
        nv4.ShouldBeLessThan(nv5);

        var maxSafeval = await TxMaxSafeval();
        maxSafeval.ShouldBe(nv1 - 1);

        await uow2.CommitAsync(CancellationToken);

        maxSafeval = await TxMaxSafeval();
        maxSafeval.ShouldBe(nv1 - 1);

        await uow1.CommitAsync(CancellationToken);

        maxSafeval = await TxMaxSafeval();
        maxSafeval.ShouldBe(nv3 - 1);

        await uow5.CommitAsync(CancellationToken);
        await uow4.CommitAsync(CancellationToken);
        await uow3.CommitAsync(CancellationToken);

        maxSafeval = await TxMaxSafeval();
        maxSafeval.ShouldBe(long.MaxValue);
    }

    [Fact]
    public async Task Function_tx_max_safeval_Returns_Number_Before_Concurrent_Transactions_When_Sequence_Is_Past_32_Bits()
    {
        const long firstValuePast32Bits = 4294967297L;

        await SetNextSequenceValue(firstValuePast32Bits);

        await using var uow = await UnitOfWorkManager.CreateAsync(CancellationToken);
        var nv = await TxNextval(uow);

        nv.ShouldBe(firstValuePast32Bits);

        var maxSafeval = await TxMaxSafeval();
        maxSafeval.ShouldBe(nv - 1);
    }

    private async Task<long> TxMaxSafeval()
    {
        await using var uow = await UnitOfWorkManager.CreateAsync(CancellationToken);

        return await GetSingleValue(uow, /*strpsql*/$"""SELECT register.tx_max_safeval('{SequenceName}') AS max_safeval""");
    }

    private Task<long> TxNextval(IUnitOfWork uow)
        => GetSingleValue(uow, /*strpsql*/$"""SELECT register.tx_nextval('{SequenceName}') AS nextval""");

    private async Task SetNextSequenceValue(long value)
    {
        await using var uow = await UnitOfWorkManager.CreateAsync(CancellationToken);

        await GetSingleValue(uow, /*strpsql*/$"""SELECT setval('{SequenceName}', {value}, false)""");
        await uow.CommitAsync(CancellationToken);
    }

    private async Task<long> GetSingleValue(IUnitOfWork uow, string query)
    {
        var conn = uow.GetRequiredService<NpgsqlConnection>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = query;

        await cmd.PrepareAsync(CancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken);

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        var value = await reader.GetFieldValueAsync<long>(0, CancellationToken);
        (await reader.ReadAsync(CancellationToken)).ShouldBeFalse();

        return value;
    }
}
