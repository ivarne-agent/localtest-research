namespace Altinn.App.Aspire;

/// <summary>
/// The localtest platform's full stack: the manifest's <c>default</c> group and the optional groups enabled on it,
/// persistent containers with the package's named volumes.
/// </summary>
public sealed class AltinnLocaltestResource : AltinnLocaltestResourceBase
{
    internal AltinnLocaltestResource(string name, LocaltestStack stack)
        : base(name, stack) { }

    /// <summary>The enabled optional groups, sorted.</summary>
    public IReadOnlyCollection<string> Groups => Stack.EnabledGroups;
}
