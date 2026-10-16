using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Register.Persistence.Tests;

public class NpgsqlUnitOfWorkTests
    : DatabaseTestBase
{
    private IUnitOfWorkManager Manager => GetRequiredService<IUnitOfWorkManager>();

    [Fact]
    public async Task UnitOfWorkManagesDatabaseTransaction()
    {
        await using var connOwner = await Database.OwnerDataSource.OpenConnectionAsync(CancellationToken);

        // create a new table and add some data
        await using var cmdOwner = connOwner.CreateCommand();
        cmdOwner.CommandText =
            /*strpsql*/$"""
            CREATE TABLE public.test(id int PRIMARY KEY NOT NULL);
            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.test TO "{Database.UserName}";
            INSERT INTO public.test(id) VALUES (1), (2);
            """;

        await cmdOwner.ExecuteNonQueryAsync(CancellationToken);

        // start unit of work
        await using var uow = await Manager.CreateAsync(CancellationToken);
        var connUow = uow.GetRequiredService<NpgsqlConnection>();

        // read data from the table
        await using var cmdUow = connUow.CreateCommand();
        cmdUow.CommandText = /*strpsql*/"SELECT MAX(id) FROM public.test";
        await cmdUow.PrepareAsync(CancellationToken);
        (await cmdUow.ExecuteScalarAsync(CancellationToken)).ShouldBe(2);

        // add data outside of unit of work
        cmdOwner.CommandText = /*strpsql*/"INSERT INTO public.test(id) VALUES (3)";
        await cmdUow.PrepareAsync(CancellationToken);
        await cmdOwner.ExecuteNonQueryAsync(CancellationToken);

        // re-read data inside of unit of work
        (await cmdUow.ExecuteScalarAsync(CancellationToken)).ShouldBe(2);

        // add data inside of unit of work
        cmdUow.CommandText = /*strpsql*/"INSERT INTO public.test(id) VALUES (4)";
        await cmdUow.PrepareAsync(CancellationToken);
        await cmdUow.ExecuteNonQueryAsync(CancellationToken);

        // re-read data insideo
        cmdUow.CommandText = /*strpsql*/"SELECT MAX(id) FROM public.test";
        await cmdUow.PrepareAsync(CancellationToken);
        (await cmdUow.ExecuteScalarAsync(CancellationToken)).ShouldBe(4);

        // read data outside of unit of work
        cmdOwner.CommandText = /*strpsql*/"SELECT MAX(id) FROM public.test";
        await cmdOwner.PrepareAsync(CancellationToken);
        (await cmdOwner.ExecuteScalarAsync(CancellationToken)).ShouldBe(3);

        // commit unit of work
        await uow.CommitAsync(CancellationToken);

        // read data outside of unit of work
        (await cmdOwner.ExecuteScalarAsync(CancellationToken)).ShouldBe(4);
    }
}
