using Altinn.Studio.EnvTopology;
using Altinn.Studio.StudioctlServer.Provisioning;

namespace Studioctl.Tests.Provisioning;

public sealed class AppProvisionerTests
{
    [Fact]
    public void DialogportenRunsWhenTheRouteTableHasTheWebApiRoute()
    {
        var topology = Topology(Route("localtest", "/"), Route("dialogporten-webapi", "/dialogporten"));

        Assert.True(AppProvisioner.DialogportenRunning(topology));
    }

    [Fact]
    public void DialogportenDoesNotRunWithoutTheGroupOrWithItsRouteDisabled()
    {
        Assert.False(AppProvisioner.DialogportenRunning(Topology(Route("localtest", "/"))));
        Assert.False(
            AppProvisioner.DialogportenRunning(Topology(Route("dialogporten-webapi", "/dialogporten", enabled: false)))
        );
        Assert.False(AppProvisioner.DialogportenRunning(new BoundTopologyIndex(null)));
    }

    private static BoundTopologyIndex Topology(params BoundTopologyRoute[] routes) =>
        new(new BoundTopologyConfig { Version = 1, Routes = [.. routes] });

    private static BoundTopologyRoute Route(string component, string prefix, bool enabled = true) =>
        new()
        {
            Component = component,
            Enabled = enabled,
            Destination = new BoundTopologyDestination
            {
                Kind = "http",
                Location = "host",
                Url = "http://127.0.0.1:8030",
            },
            Match = new BoundTopologyRouteMatch { Host = "local.altinn.cloud", PathPrefix = prefix },
        };
}
