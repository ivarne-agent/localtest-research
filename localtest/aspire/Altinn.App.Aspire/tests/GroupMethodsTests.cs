using System.Reflection;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The typed group aliases (<c>WithDialogporten()</c>, ...) on <see cref="AltinnLocaltestExtensions" /> are exactly
/// the manifest's groups, so adding, renaming or removing a group in the manifest fails here until the methods match.
/// </summary>
public sealed class GroupMethodsTests
{
    [Fact]
    public void EveryManifestGroupHasATypedMethodAndNoMethodLacksAGroup()
    {
        var expected = ManifestLoader.Default.Groups.Keys.ToDictionary(
            MethodName,
            group => group,
            StringComparer.Ordinal
        );
        var actual = typeof(AltinnLocaltestExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m =>
                m.Name.StartsWith("With", StringComparison.Ordinal)
                && m.Name != nameof(AltinnLocaltestExtensions.WithGroup)
                && m.ReturnType == typeof(IResourceBuilder<AltinnLocaltestResource>)
                && m.GetParameters() is [var only]
                && only.ParameterType == typeof(IResourceBuilder<AltinnLocaltestResource>)
            )
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = expected.Keys.Where(name => !actual.Contains(name)).Order(StringComparer.Ordinal).ToList();
        var extra = actual.Where(name => !expected.ContainsKey(name)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            $"AltinnLocaltestExtensions does not match the manifest's groups. Missing: "
                + $"{(missing.Count == 0 ? "none" : string.Join(", ", missing.Select(m => $"{m}() for group '{expected[m]}'")))}. "
                + $"Extra (no such group): {(extra.Count == 0 ? "none" : string.Join(", ", extra.Select(m => $"{m}()")))}."
        );
    }

    [Fact]
    public void TheTypedMethodsEnableTheirGroup()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true }
        );
        var localtest = builder
            .AddAltinnLocaltest(configure: o => o.DataDirectory = SampleModel.DataDirectory)
            .WithCorrespondence();

        var names = builder.Resources.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("correspondence", names);
        Assert.Contains("dialogporten-webapi", names);
        Assert.DoesNotContain("receipt", names);
        Assert.Same(localtest, localtest.WithReceipt());
        Assert.Contains("receipt", builder.Resources.Select(r => r.Name));
    }

    private static string MethodName(string group) =>
        "With"
        + string.Concat(
            group
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );
}
