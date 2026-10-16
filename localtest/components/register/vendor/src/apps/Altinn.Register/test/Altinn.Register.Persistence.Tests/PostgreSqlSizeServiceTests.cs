using Altinn.Register.Core.Utils;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Register.Persistence.Tests;

public class PostgreSqlSizeServiceTests
    : DatabaseTestBase
{
    private PostgreSqlSizeService Service => GetRequiredService<PostgreSqlSizeService>();

    protected override ValueTask ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<PostgreSqlSizeService>();

        return base.ConfigureServices(services);
    }

    [Fact]
    public async Task DatabaseHasSize()
    {
        var isEmpty = await Service.IsDatabaseSmallerThan(ByteSize.Byte, CancellationToken);
        isEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task DatabaseIsNotHuge()
    {
        var smallerThan = await Service.IsDatabaseSmallerThan(ByteSize.FromGibibytes(1), CancellationToken);
        smallerThan.ShouldBeTrue();
    }

    [Fact]
    public async Task DoesNotThrowOnNonExistingTable()
    {
        var isZero = await Service.IsTableSmallerThan("fake_schema", "no_table", ByteSize.Byte, CancellationToken);
        isZero.ShouldBeTrue();
    }
}
