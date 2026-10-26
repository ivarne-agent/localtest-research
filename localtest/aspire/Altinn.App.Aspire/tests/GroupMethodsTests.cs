using System.Reflection;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The typed group aliases (<c>WithDialogporten()</c>, ...) on <see cref="AltinnLocaltestExtensions" /> are exactly
/// the manifest's optional groups (less the ignored <c>monitoring</c>) and <c>everything</c>, so adding, renaming or removing a group in the manifest fails
/// here until the methods match. None of them, and no other method that enables a service, takes minimal mode's
/// builder.
/// </summary>
public sealed class GroupMethodsTests
{
    [Fact]
    public void EveryManifestGroupHasATypedMethodAndNoMethodLacksAGroup()
    {
        var expected = RequiredGroups(LocaltestManifest.Default)
            .Append(LocaltestStack.Everything)
            .ToDictionary(MethodName, group => group, StringComparer.Ordinal);
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
        localtest.WithDialogporten().WithArbeidsflate();
        Assert.Equal(["arbeidsflate", "correspondence", "dialogporten", "receipt"], localtest.Resource.Groups);
        var count = builder.Resources.Count;
        localtest.WithEverything();
        Assert.Equal(count, builder.Resources.Count);
    }

    /// <summary>
    /// The methods that take minimal mode's builder are exactly those for what both forms support: no group method,
    /// nothing else that enables a service. A method takes it when one of its parameters is an
    /// <c>IResourceBuilder&lt;T&gt;</c> that <c>IResourceBuilder&lt;AltinnLocaltestMinimalResource&gt;</c> converts to
    /// (by <c>IResourceBuilder&lt;out T&gt;</c>'s covariance) or binds to (a type parameter whose constraints it meets).
    /// </summary>
    [Fact]
    public void OnlyWhatBothFormsSupportTakesTheMinimalBuilder()
    {
        var takingMinimal = typeof(AltinnLocaltestMinimalResource)
            .Assembly.GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetParameters().Any(p => TakesMinimal(p.ParameterType)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["AltinnAppExtensions.AddAltinnApp", "AltinnLocaltestExtensions.WithFrontendDevServer"],
            takingMinimal
        );
        // The group methods take the full stack's builder only.
        Assert.False(typeof(AltinnLocaltestResource).IsAssignableFrom(typeof(AltinnLocaltestMinimalResource)));
        Assert.False(typeof(AltinnLocaltestMinimalResource).IsAssignableFrom(typeof(AltinnLocaltestResource)));
        Assert.True(typeof(AltinnLocaltestResource).IsSealed);
        Assert.True(typeof(AltinnLocaltestMinimalResource).IsSealed);
        Assert.False(
            typeof(IResourceBuilder<AltinnLocaltestResource>).IsAssignableFrom(
                typeof(IResourceBuilder<AltinnLocaltestMinimalResource>)
            )
        );
        Assert.True(
            typeof(IResourceBuilder<AltinnLocaltestResourceBase>).IsAssignableFrom(
                typeof(IResourceBuilder<AltinnLocaltestMinimalResource>)
            )
        );
    }

    [Fact]
    public void WhatBothFormsSupportKeepsTheBuildersType()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true }
        );
        var minimal = builder.AddAltinnLocaltestMinimal().WithFrontendDevServer(5173);

        Assert.IsType<AltinnLocaltestMinimalResource>(minimal.Resource);
        Assert.Equal(5173, minimal.Resource.Stack.FrontendDevServerPort);
        Assert.Throws<InvalidOperationException>(() => minimal.Resource.Stack.AddGroups(["dialogporten"]));
    }

    private static bool TakesMinimal(Type parameter)
    {
        var minimal = typeof(AltinnLocaltestMinimalResource);
        if (!parameter.IsGenericType || parameter.GetGenericTypeDefinition() != typeof(IResourceBuilder<>))
        {
            return parameter != typeof(object)
                && parameter.IsAssignableFrom(typeof(IResourceBuilder<>).MakeGenericType(minimal));
        }
        var argument = parameter.GetGenericArguments()[0];
        return argument.IsGenericParameter
            ? argument.GetGenericParameterConstraints().All(c => c.IsAssignableFrom(minimal))
            : argument.IsAssignableFrom(minimal);
    }

    /// <summary>
    /// The groups that need a typed method: every group of the manifest but the default and the minimal one and the one
    /// the package ignores (<see cref="LocaltestManifest.MonitoringGroup" />). A new group the package does not know
    /// fails the test until it gets a method or is ignored on purpose.
    /// </summary>
    internal static IReadOnlyList<string> RequiredGroups(LocaltestManifest manifest) =>
        [
            .. manifest.GroupNames.Where(g =>
                g
                    is not (
                        LocaltestManifest.DefaultGroup
                        or LocaltestManifest.MinimalGroup
                        or LocaltestManifest.MonitoringGroup
                    )
            ),
        ];

    private static string MethodName(string group) =>
        "With"
        + string.Concat(
            group
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );
}
